/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of EMSP <https://github.com/OpenChargingCloud/EMSP>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;

using org.GraphDefined.Vanaheimr.Hermod.PKI;

using cloud.charging.open.protocols.ISO15118.PKI;

using BCCertificate = Org.BouncyCastle.X509.X509Certificate;

#endregion

namespace cloud.charging.open.EMSP.Contracts
{

    /// <summary>
    /// The mobility operator's certificate authority: the MO root, the two
    /// sub-CAs below it, and the signing of contract certificates for the
    /// drivers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hierarchy is the one ISO 15118-2 Annex F draws for a mobility
    /// operator - a root of its own, a Sub-CA 1, a Sub-CA 2, and the contract
    /// certificates below that - built with the profiles of the ISO 15118 PKI
    /// builder, so that what a charging station and its CSMS see from this
    /// EMSP is what they see from the reference hierarchies of the test
    /// environment. Everything is on secp256r1 with SHA-256: that is what
    /// ISO 15118-2 mandates, and it is the one curve a vehicle signs its
    /// authorization with.
    /// </para>
    /// <para>
    /// This class is a certificate authority and not a key factory: a driver
    /// makes their key pair in their browser, sends a certificate signing
    /// request, and gets a certificate back. The only private keys kept here
    /// are the authority's own - below <see cref="Directory"/>, and never
    /// anywhere a web request could reach them.
    /// </para>
    /// <para>
    /// The three CAs are made once, at the first start, and read back at
    /// every start after it: a root made afresh would invalidate every
    /// contract this EMSP ever issued, and every CPO that was handed the old
    /// root would turn the drivers away with nothing anywhere saying why. So
    /// a directory that is there but cannot be read is an error, never a
    /// reason to make a new one.
    /// </para>
    /// </remarks>
    public sealed class ContractCertificateAuthority
    {

        #region Data

        /// <summary>
        /// The directory below the PKI directory that holds the authority.
        /// </summary>
        public const String  DirectoryName       = "mo";

        /// <summary>
        /// The file the MO root certificate alone is written to: what a CPO,
        /// a CSMS or a vehicle is pointed at to believe the contracts.
        /// </summary>
        public const String  RootTrustFileName   = "mo_root_trust.pem";

        /// <summary>
        /// The file the two sub-CA certificates are written to, the one that
        /// signs contracts first: what a contract certificate travels with.
        /// </summary>
        public const String  ChainFileName       = "mo_chain.pem";

        /// <summary>
        /// The one curve ISO 15118-2 contract certificates are on.
        /// </summary>
        public const String  RequiredCurveOID    = "1.2.840.10045.3.1.7";

        /// <summary>
        /// The signature algorithm of everything this authority signs.
        /// </summary>
        public const String  SignatureAlgorithm  = "SHA256withECDSA";

        private static readonly V2GProfileOptions profileOptions
            = new (V2GProfileFlavor.Strict15118_2,
                   V2GAlgorithm.EcdsaP256,
                   V2GPolicySet.None,
                   V2GRootLayout.SeparateRoots);

        private readonly SecureRandom  random;

        #endregion

        #region Properties

        /// <summary>
        /// Where the certificates and the private keys of the authority live.
        /// </summary>
        public String          Directory        { get; }

        /// <summary>
        /// The MO root certificate.
        /// </summary>
        public BCCertificate   Root             { get; }

        /// <summary>
        /// The MO Sub-CA 1, signed by the root.
        /// </summary>
        public BCCertificate   SubCA1           { get; }

        /// <summary>
        /// The MO Sub-CA 2, signed by Sub-CA 1: what signs the contracts.
        /// </summary>
        public BCCertificate   SubCA2           { get; }

        /// <summary>
        /// The MO root certificate as PEM.
        /// </summary>
        public String          RootPEM          { get; }

        /// <summary>
        /// The two sub-CA certificates as PEM, Sub-CA 2 first: what a
        /// contract certificate is bundled with, so that whoever verifies
        /// it can walk up to the root.
        /// </summary>
        public String          ChainPEM         { get; }

        /// <summary>
        /// The SHA-256 fingerprint of the MO root, as it is compared over a
        /// telephone.
        /// </summary>
        public String          RootFingerprint  { get; }

        /// <summary>
        /// The subject of the MO root.
        /// </summary>
        public String          RootSubject
            => Root.SubjectDN.ToString();

        /// <summary>
        /// When the MO root expires.
        /// </summary>
        public DateTimeOffset  RootNotAfter
            => new (Root.NotAfter, TimeSpan.Zero);

        /// <summary>
        /// Where the MO root certificate alone lies.
        /// </summary>
        public String          RootTrustPath
            => Path.Combine(Directory, RootTrustFileName);

        /// <summary>
        /// Whether the three CAs were made just now, at this start.
        /// </summary>
        public Boolean         WasCreated       { get; }

        private readonly AsymmetricCipherKeyPair  rootKeyPair;
        private readonly AsymmetricCipherKeyPair  subCA1KeyPair;
        private readonly AsymmetricCipherKeyPair  subCA2KeyPair;

        #endregion

        #region Constructor(s)

        private ContractCertificateAuthority(String                   Directory,
                                             AsymmetricCipherKeyPair  RootKeyPair,
                                             BCCertificate            Root,
                                             AsymmetricCipherKeyPair  SubCA1KeyPair,
                                             BCCertificate            SubCA1,
                                             AsymmetricCipherKeyPair  SubCA2KeyPair,
                                             BCCertificate            SubCA2,
                                             Boolean                  WasCreated,
                                             SecureRandom             Random)
        {

            this.Directory        = Directory;
            this.rootKeyPair      = RootKeyPair;
            this.Root             = Root;
            this.subCA1KeyPair    = SubCA1KeyPair;
            this.SubCA1           = SubCA1;
            this.subCA2KeyPair    = SubCA2KeyPair;
            this.SubCA2           = SubCA2;
            this.WasCreated       = WasCreated;
            this.random           = Random;

            this.RootPEM          = Root.ToPEM();
            this.ChainPEM         = SubCA2.ToPEM() + SubCA1.ToPEM();
            this.RootFingerprint  = FingerprintOf(Root);

        }

        #endregion


        #region (static) OpenOrCreate(Directory, CountryCode, ProviderId, Organization, Random = null)

        /// <summary>
        /// The authority that lies in the given directory, or - when the
        /// directory is empty - a new one, written there.
        /// </summary>
        /// <param name="Directory">Where the certificates and keys live; created when it does not exist.</param>
        /// <param name="CountryCode">The country of the mobility operator, e.g. "DE": the C of every subject.</param>
        /// <param name="ProviderId">The provider identification, e.g. "GDF": written into the common names, so that two operators' roots are told apart.</param>
        /// <param name="Organization">The name of the mobility operator: the O of every subject.</param>
        /// <param name="Random">Where the serial numbers and the keys come from.</param>
        /// <exception cref="InvalidOperationException">When the directory holds something, but not a whole and valid authority.</exception>
        public static ContractCertificateAuthority OpenOrCreate(String         Directory,
                                                                String         CountryCode,
                                                                String         ProviderId,
                                                                String         Organization,
                                                                SecureRandom?  Random   = null)
        {

            var random = Random ?? new SecureRandom();

            System.IO.Directory.CreateDirectory(Directory);

            var files = new[] {
                            Path.Combine(Directory, "mo_root_ca.cert.pem"),
                            Path.Combine(Directory, "mo_root_ca.key.pem"),
                            Path.Combine(Directory, "mo_sub_ca_1.cert.pem"),
                            Path.Combine(Directory, "mo_sub_ca_1.key.pem"),
                            Path.Combine(Directory, "mo_sub_ca_2.cert.pem"),
                            Path.Combine(Directory, "mo_sub_ca_2.key.pem")
                        };

            var present = files.Count(File.Exists);

            #region Nothing there: make it

            if (present == 0)
            {

                var suffix   = $"{CountryCode}-{ProviderId}";

                var root     = V2GCertificateBuilder.Issue(
                                   Profile(V2GRole.MORootCA, suffix, CountryCode, Organization),
                                   V2GAlgorithm.EcdsaP256,
                                   random,
                                   issuer: null
                               );

                var subCA1   = V2GCertificateBuilder.Issue(
                                   Profile(V2GRole.MOSubCA1, suffix, CountryCode, Organization),
                                   V2GAlgorithm.EcdsaP256,
                                   random,
                                   issuer: root
                               );

                var subCA2   = V2GCertificateBuilder.Issue(
                                   Profile(V2GRole.MOSubCA2, suffix, CountryCode, Organization),
                                   V2GAlgorithm.EcdsaP256,
                                   random,
                                   issuer: subCA1
                               );

                var created  = new ContractCertificateAuthority(
                                   Directory,
                                   root.  KeyPair, root.  Certificate,
                                   subCA1.KeyPair, subCA1.Certificate,
                                   subCA2.KeyPair, subCA2.Certificate,
                                   WasCreated:  true,
                                   random
                               );

                created.Write();

                return created;

            }

            #endregion

            #region Something there: all of it, or an error

            if (present != files.Length)
                throw new InvalidOperationException(
                          $"The mobility operator's certificate authority in '{Directory}' is incomplete: " +
                          $"{present} of {files.Length} files are there. A root made afresh would invalidate every " +
                          "contract certificate this EMSP issued, so nothing is made here - restore the directory, " +
                          "or remove it to start with a new root."
                      );

            try
            {

                var rootKeyPair    = ReadKeyPair    (files[1]);
                var root           = ReadCertificate(files[0]);
                var subCA1KeyPair  = ReadKeyPair    (files[3]);
                var subCA1         = ReadCertificate(files[2]);
                var subCA2KeyPair  = ReadKeyPair    (files[5]);
                var subCA2         = ReadCertificate(files[4]);

                // A root whose time is up is worse than no root: every
                // contract it vouches for is refused, and the refusal names
                // the driver.
                root.  CheckValidity();
                subCA1.CheckValidity();
                subCA2.CheckValidity();

                subCA1.Verify(root.  GetPublicKey());
                subCA2.Verify(subCA1.GetPublicKey());

                var opened = new ContractCertificateAuthority(
                                 Directory,
                                 rootKeyPair,   root,
                                 subCA1KeyPair, subCA1,
                                 subCA2KeyPair, subCA2,
                                 WasCreated:  false,
                                 random
                             );

                // The public files are rewritten at every start, so that a
                // trust file somebody deleted comes back.
                opened.WritePublicFiles();

                return opened;

            }
            catch (Exception e) when (e is not InvalidOperationException)
            {
                throw new InvalidOperationException(
                          $"The mobility operator's certificate authority in '{Directory}' could not be read: {e.Message} " +
                          "Restore the directory, or remove it to start with a new root - which invalidates every " +
                          "contract certificate this EMSP issued.",
                          e
                      );
            }

            #endregion

        }

        #endregion

        #region TryIssue(CSR, EMAId, Validity, Now, out Certificate, out Error)

        /// <summary>
        /// A contract certificate for the key in the given signing request,
        /// made out to the given eMAID - or the sentence that says why not.
        /// </summary>
        /// <remarks>
        /// The request has to be signed with the key it carries: that is the
        /// proof that whoever sent it holds the private half, and it is the
        /// one thing about the request that is believed. Its subject is not -
        /// the eMAID is this EMSP's to hand out, and it is what the common
        /// name is set to, and nothing else.
        /// </remarks>
        /// <param name="CSR">The PKCS#10 certificate signing request, as PEM.</param>
        /// <param name="EMAId">The e-mobility account identifier to make the certificate out to.</param>
        /// <param name="Validity">How long the certificate is good for.</param>
        /// <param name="Now">What time it is.</param>
        /// <param name="Certificate">The certificate.</param>
        /// <param name="Error">What is wrong with the request.</param>
        public Boolean TryIssue(String?                                  CSR,
                                EMAId                                    EMAId,
                                TimeSpan                                 Validity,
                                DateTimeOffset                           Now,
                                [NotNullWhen(true)]  out BCCertificate?  Certificate,
                                [NotNullWhen(false)] out String?         Error)
        {

            Certificate  = null;
            Error        = null;

            #region The request, and the proof that its key is held

            Pkcs10CertificationRequest request;

            try
            {

                using var reader = new PemReader(new StringReader(CSR ?? ""));

                if (reader.ReadObject() is not Pkcs10CertificationRequest parsed)
                {
                    Error = "This is not a PKCS#10 certificate signing request in PEM.";
                    return false;
                }

                request = parsed;

            }
            catch (Exception e)
            {
                Error = $"The certificate signing request could not be read: {e.Message}";
                return false;
            }

            if (!request.Verify())
            {
                Error = "The certificate signing request is not signed by the key it carries, so nobody has shown they hold that key.";
                return false;
            }

            #endregion

            #region The key: secp256r1, and nothing else

            AsymmetricKeyParameter publicKey;

            try
            {
                publicKey = request.GetPublicKey();
            }
            catch (Exception e)
            {
                Error = $"The public key of the request could not be read: {e.Message}";
                return false;
            }

            if (publicKey is not ECPublicKeyParameters ecKey)
            {
                Error = "A contract certificate needs an elliptic curve key on secp256r1; this request carries another kind of key.";
                return false;
            }

            if (ecKey.PublicKeyParamSet?.Id != RequiredCurveOID)
            {
                Error = "A contract certificate needs a key on secp256r1 (NIST P-256), the one curve a vehicle signs its authorization with; this request carries a key on another curve.";
                return false;
            }

            #endregion

            #region The certificate

            var profile    = V2GCertProfile.ForRole(V2GRole.ContractCertLeaf, profileOptions);

            var subjectDN  = new X509Name(
                                 [ X509Name.C,          X509Name.O,           X509Name.DC, X509Name.DC, X509Name.CN ],
                                 [ EMAId.CountryCode,   SubjectOrganization,  "V2G",       "MO",        EMAId.Compact ]
                             );

            var notBefore  = Now.UtcDateTime.AddMinutes(-5);

            var generator  = new X509V3CertificateGenerator();

            generator.SetSerialNumber(new BigInteger(159, random).Abs().Add(BigInteger.One));
            generator.SetIssuerDN    (SubCA2.SubjectDN);
            generator.SetSubjectDN   (subjectDN);
            generator.SetNotBefore   (notBefore);
            generator.SetNotAfter    (notBefore.Add(Validity));
            generator.SetPublicKey   (publicKey);

            generator.AddExtension(X509Extensions.BasicConstraints,        critical: true,  new BasicConstraints(cA: false));
            generator.AddExtension(X509Extensions.KeyUsage,                critical: true,  new KeyUsage(profile.KeyUsageBits));

            if (profile.ExtendedKeyUsages.Length > 0)
                generator.AddExtension(X509Extensions.ExtendedKeyUsage,    critical: false, new ExtendedKeyUsage(profile.ExtendedKeyUsages));

            generator.AddExtension(X509Extensions.SubjectKeyIdentifier,    critical: false,
                                   X509ExtensionUtilities.CreateSubjectKeyIdentifier  (SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(publicKey)));

            generator.AddExtension(X509Extensions.AuthorityKeyIdentifier,  critical: false,
                                   X509ExtensionUtilities.CreateAuthorityKeyIdentifier(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(subCA2KeyPair.Public)));

            Certificate = generator.Generate(
                              new Asn1SignatureFactory(SignatureAlgorithm, subCA2KeyPair.Private, random)
                          );

            return true;

            #endregion

        }

        #endregion

        #region Verify(Certificate)

        /// <summary>
        /// Whether the given certificate was signed by this authority's
        /// Sub-CA 2 and is within its time.
        /// </summary>
        public Boolean Verify(BCCertificate Certificate)
        {

            try
            {
                Certificate.CheckValidity();
                Certificate.Verify(subCA2KeyPair.Public);
                return true;
            }
            catch
            {
                return false;
            }

        }

        #endregion


        #region (private) Write()

        /// <summary>
        /// Write the three CAs where <see cref="OpenOrCreate"/> will find
        /// them again, the keys readable by nobody else.
        /// </summary>
        private void Write()
        {

            Save("mo_root_ca",  rootKeyPair,   Root);
            Save("mo_sub_ca_1", subCA1KeyPair, SubCA1);
            Save("mo_sub_ca_2", subCA2KeyPair, SubCA2);

            WritePublicFiles();

            void Save(String Name, AsymmetricCipherKeyPair KeyPair, BCCertificate Certificate)
            {

                File.WriteAllText(
                    Path.Combine(Directory, $"{Name}.cert.pem"),
                    Certificate.ToPEM()
                );

                OwnerOnlyFile.Write(
                    Path.Combine(Directory, $"{Name}.key.pem"),
                    PemEncoding.WriteString(
                        "PRIVATE KEY",
                        PrivateKeyInfoFactory.CreatePrivateKeyInfo(KeyPair.Private).GetDerEncoded()
                    ) + Environment.NewLine
                );

            }

        }

        #endregion

        #region (private) WritePublicFiles()

        /// <summary>
        /// The root alone, and the two sub-CAs together: what somebody else
        /// is pointed at.
        /// </summary>
        private void WritePublicFiles()
        {
            File.WriteAllText(RootTrustPath,                              RootPEM);
            File.WriteAllText(Path.Combine(Directory, ChainFileName),     ChainPEM);
        }

        #endregion


        #region (private) SubjectOrganization

        /// <summary>
        /// The O of every subject this authority writes: the root's own.
        /// </summary>
        private String SubjectOrganization
            => Root.SubjectDN.GetValueList(X509Name.O).OfType<String>().FirstOrDefault() ?? "EMSP";

        #endregion

        #region (private static) Profile(Role, Suffix, CountryCode, Organization)

        /// <summary>
        /// The ISO 15118 profile of a CA, with this operator's name and
        /// country in its subject instead of the builder's placeholders.
        /// </summary>
        private static V2GCertProfile Profile(V2GRole  Role,
                                              String   Suffix,
                                              String   CountryCode,
                                              String   Organization)

            => V2GCertProfile.ForRole(Role, profileOptions, Suffix) with {
                   Organization  = Organization,
                   Country       = CountryCode
               };

        #endregion

        #region (private static) ReadKeyPair(Path) / ReadCertificate(Path)

        private static AsymmetricCipherKeyPair ReadKeyPair(String Path)
        {

            // PKCS#8 comes back as a private key rather than as a pair, which
            // is what was written: the public half is derived rather than
            // stored, and BouncyCastle is content to do that for EC keys.
            using var reader = new PemReader(File.OpenText(Path));

            return reader.ReadObject() switch {
                       AsymmetricCipherKeyPair pair  => pair,
                       AsymmetricKeyParameter  key   => new AsymmetricCipherKeyPair(PKIFactory.PublicKeyOf(key), key),
                       _                             => throw new InvalidOperationException($"'{Path}' holds no private key.")
                   };

        }

        private static BCCertificate ReadCertificate(String Path)
        {

            using var reader = new PemReader(File.OpenText(Path));

            return reader.ReadObject() as BCCertificate
                       ?? throw new InvalidOperationException($"'{Path}' holds no certificate.");

        }

        #endregion

        #region (static) FingerprintOf(Certificate)

        /// <summary>
        /// The SHA-256 fingerprint of a certificate, as colon-separated hex.
        /// </summary>
        public static String FingerprintOf(BCCertificate Certificate)

            => String.Join(":", SHA256.HashData(Certificate.GetEncoded()).Select(b => b.ToString("X2")));

        #endregion

    }

}
