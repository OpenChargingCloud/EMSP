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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using cloud.charging.open.EMSP.Contracts;

#endregion

namespace cloud.charging.open.EMSP.Tickets
{

    /// <summary>
    /// A key of this EMSP's own on P-256 and the self-signed certificate that
    /// says whose it is: made at the first start, and read at every start
    /// after - a key made afresh would make everything it signed worthless.
    /// </summary>
    /// <remarks>
    /// Two of them: the authority the drivers' account certificates are
    /// signed by, and the key that signs the charging tickets, whose
    /// certificate a charge point operator holds to believe them. In a
    /// directory below "pki", as "&lt;name&gt;.key.pem" - PKCS#8, readable by
    /// its owner alone - and "&lt;name&gt;.cert.pem".
    /// </remarks>
    public sealed class SigningKey : IDisposable
    {

        #region Properties

        /// <summary>The private key.</summary>
        public ECDsa             Key            { get; }

        /// <summary>The certificate, self-signed.</summary>
        public X509Certificate2  Certificate    { get; }

        /// <summary>The certificate as PEM.</summary>
        public String            CertificatePEM { get; }

        /// <summary>The SHA-256 of the certificate: how a signature names this key.</summary>
        public Byte[]            KeyId          { get; }

        /// <summary>The same, as hex in pairs: what a person compares.</summary>
        public String            Fingerprint
            => Convert.ToHexString(KeyId);

        /// <summary>Where the certificate lies.</summary>
        public String            CertificatePath { get; }

        /// <summary>Whether it was made at this start.</summary>
        public Boolean           WasCreated     { get; }

        #endregion

        #region Constructor(s)

        private SigningKey(ECDsa             Key,
                           X509Certificate2  Certificate,
                           String            CertificatePath,
                           Boolean           WasCreated)
        {

            this.Key              = Key;
            this.Certificate      = Certificate;
            this.CertificatePEM   = Certificate.ExportCertificatePem() + "\n";
            this.KeyId            = SHA256.HashData(Certificate.RawData);
            this.CertificatePath  = CertificatePath;
            this.WasCreated       = WasCreated;

        }

        #endregion


        #region (static) OpenOrCreate(Directory, Name, Subject, IsAuthority, Validity, Now)

        /// <summary>
        /// The key and its certificate in the given directory, or - where
        /// neither is there - made and written.
        /// </summary>
        /// <param name="Directory">Where they lie.</param>
        /// <param name="Name">What the two files are called before ".key.pem" and ".cert.pem".</param>
        /// <param name="Subject">Whose they are, as a distinguished name.</param>
        /// <param name="IsAuthority">Whether it signs certificates, or only data.</param>
        /// <param name="Validity">How long the certificate is good for.</param>
        /// <param name="Now">What time it is.</param>
        /// <exception cref="InvalidOperationException">Where one of the two files is there and the other is not, or one cannot be read.</exception>
        public static SigningKey OpenOrCreate(String                  Directory,
                                              String                  Name,
                                              X500DistinguishedName   Subject,
                                              Boolean                 IsAuthority,
                                              TimeSpan                Validity,
                                              DateTimeOffset          Now)
        {

            System.IO.Directory.CreateDirectory(Directory);

            var keyPath   = Path.Combine(Directory, $"{Name}.key.pem");
            var certPath  = Path.Combine(Directory, $"{Name}.cert.pem");

            var present   = (File.Exists(keyPath) ? 1 : 0) + (File.Exists(certPath) ? 1 : 0);

            if (present == 1)
                throw new InvalidOperationException($"Of '{keyPath}' and '{certPath}' one is there and the other is not. Restore the missing one, or remove both to have a new key made - which makes everything the old one signed worthless.");

            if (present == 2)
            {

                try
                {

                    var key          = ECDsa.Create();
                    key.ImportFromPem(File.ReadAllText(keyPath));

                    var certificate  = X509Certificate2.CreateFromPem(File.ReadAllText(certPath));

                    if (!certificate.GetECDsaPublicKey()!.ExportSubjectPublicKeyInfo().SequenceEqual(key.ExportSubjectPublicKeyInfo()))
                        throw new InvalidOperationException("the key is not the certificate's");

                    return new SigningKey(key, certificate, certPath, WasCreated: false);

                }
                catch (Exception e) when (e is not InvalidOperationException || e.Message == "the key is not the certificate's")
                {
                    throw new InvalidOperationException($"'{keyPath}' and '{certPath}' could not be read as a key and its certificate: {e.Message}", e);
                }

            }

            var made     = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request  = new CertificateRequest(Subject, made, HashAlgorithmName.SHA256);

            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(IsAuthority, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(IsAuthority ? X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature
                                                                                    : X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            var created  = request.CreateSelfSigned(Now.AddMinutes(-1), Now.Add(Validity));

            OwnerOnlyFile.Write(keyPath,  made.ExportPkcs8PrivateKeyPem() + "\n");
            File.WriteAllText  (certPath, created.ExportCertificatePem() + "\n");

            return new SigningKey(made, X509CertificateLoader.LoadCertificate(created.RawData), certPath, WasCreated: true);

        }

        #endregion


        #region Dispose()

        public void Dispose()
        {
            Key.Dispose();
            Certificate.Dispose();
        }

        #endregion

    }

}
