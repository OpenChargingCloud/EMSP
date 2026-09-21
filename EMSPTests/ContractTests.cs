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

using System.Net;
using System.Net.Http.Json;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

using org.GraphDefined.Vanaheimr.Hermod.PKI;

using cloud.charging.open.protocols.ISO15118.PKI;

using cloud.charging.open.EMSP.Contracts;

using BCCertificate = Org.BouncyCastle.X509.X509Certificate;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// Drivers signing up, asking for contract certificates, and what those
    /// certificates are worth.
    /// </summary>
    /// <remarks>
    /// Over HTTP, the way a browser does it: the sign-up at Hermod's own
    /// route, the request with a PKCS#10 made here with a key of its own,
    /// and the answer checked against the MO root the EMSP hands out.
    /// </remarks>
    public class ContractTests : AEMSPTests
    {

        #region ADriverSignsUpAndGetsAContract()

        [Test]
        public async Task ADriverSignsUpAndGetsAContract()
        {

            using var driver = await SignedUp("alice");

            #region Signed up means signed in, as a driver and nothing else

            var me = await GetJSON(driver, "/api/v1/auth/me");

            Assert.Multiple(() => {
                Assert.That(me.Value<String>("username"),     Is.EqualTo("alice"));
                Assert.That(me["roles"]?.Values<String>(),    Is.EquivalentTo(new[] { "driver" }));
                Assert.That(me["permissions"]?.Values<String>(), Is.EquivalentTo(new[] { "issueContracts" }));
            });

            #endregion

            #region The contract

            var keyPair   = V2GCertificateBuilder.GenerateKeyPair(V2GAlgorithm.EcdsaP256, new SecureRandom());
            var response  = await driver.PostAsync("/api/v1/contracts", JSONBody(new JProperty("csr", CSR(keyPair))));
            var body      = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), body.ToString());

            var contract  = body["contract"] as JObject;

            Assert.That(contract, Is.Not.Null);
            Assert.That(EMAId.TryParse(contract!.Value<String>("emaId"), out var emaId, out var error), Is.True, error);

            var certificate = ReadCertificate(body.Value<String>("certificate"));
            var chain       = ReadCertificates(body.Value<String>("chain"));
            var moRoot      = ReadCertificate(body.Value<String>("moRoot"));

            Assert.Multiple(() => {

                Assert.That(emaId!.CountryCode,   Is.EqualTo("DE"));
                Assert.That(emaId.ProviderId,     Is.EqualTo("GDF"));
                Assert.That(contract.Value<String>("owner"),   Is.EqualTo("alice"));
                Assert.That(contract.Value<String>("status"),  Is.EqualTo("valid"));

                // The common name is the eMAID, without separators and with
                // its check digit: what a vehicle reads out of it.
                Assert.That(certificate.SubjectDN.GetValueList(X509Name.CN).OfType<String>().Single(), Is.EqualTo(emaId.Compact));

                // The key in the certificate is the key of the request.
                Assert.That(certificate.GetPublicKey(), Is.EqualTo(keyPair.Public));

                // And it chains up to the MO root through the two sub-CAs.
                Assert.That(chain, Has.Length.EqualTo(2));
                Assert.That(() => certificate.Verify(chain[0].GetPublicKey()), Throws.Nothing, "not signed by MO Sub-CA 2");
                Assert.That(() => chain[0].   Verify(chain[1].GetPublicKey()), Throws.Nothing, "Sub-CA 2 not signed by Sub-CA 1");
                Assert.That(() => chain[1].   Verify(moRoot.  GetPublicKey()), Throws.Nothing, "Sub-CA 1 not signed by the MO root");
                Assert.That(() => moRoot.     Verify(moRoot.  GetPublicKey()), Throws.Nothing, "the MO root is not self-signed");

                Assert.That(moRoot.SubjectDN.ToString(), Does.Contain("GraphDefined EMSP"));

                // Two years, as the ISO 15118-2 profile has it.
                Assert.That((certificate.NotAfter - certificate.NotBefore).TotalDays, Is.EqualTo(730).Within(1));

            });

            #endregion

            #region Listed, and a token on every OCPI version

            var listed = await GetJSON(driver, "/api/v1/contracts");

            Assert.Multiple(() => {
                Assert.That(listed.Value<Boolean>("everyone"),                          Is.False);
                Assert.That(listed["contracts"]?.Count(),                              Is.EqualTo(1));
                Assert.That(listed["contracts"]?[0]?.Value<String>("emaId"),           Is.EqualTo(emaId!.ToString()));
                Assert.That(listed["contracts"]?[0]?.Value<String>("certificate"),     Does.StartWith("-----BEGIN CERTIFICATE-----"));
                Assert.That(listed["moRoot"]?.Value<String>("fingerprint"),            Is.EqualTo(EMSP.ContractCA.RootFingerprint));
                Assert.That(EMSP.TokenCount,                                           Is.EqualTo(EMSP.OCPIVersions.Count));
            });

            using var root   = await SignedIn();
            var       tokens = await GetJSON(root, "/api/v1/ocpi/tokens");

            Assert.That(tokens["tokens"]?.Select(token => token.Value<String>("uid")), Has.All.EqualTo(emaId!.Compact));

            #endregion

        }

        #endregion

        #region ADriverSeesOnlyTheirOwnContracts()

        [Test]
        public async Task ADriverSeesOnlyTheirOwnContracts()
        {

            using var alice  = await SignedUp("alice");
            using var bob    = await SignedUp("bobby");

            await IssueContract(alice);
            await IssueContract(bob);
            await IssueContract(bob);

            var aliceSees  = await GetJSON(alice, "/api/v1/contracts");
            var bobSees    = await GetJSON(bob,   "/api/v1/contracts");

            using var root = await SignedIn();
            var rootSees   = await GetJSON(root,  "/api/v1/contracts");

            Assert.Multiple(() => {

                Assert.That(aliceSees["contracts"]?.Count(),                            Is.EqualTo(1));
                Assert.That(aliceSees["contracts"]?.Select(c => c.Value<String>("owner")), Has.All.EqualTo("alice"));

                Assert.That(bobSees["contracts"]?.Count(),                              Is.EqualTo(2));
                Assert.That(bobSees["contracts"]?.Select(c => c.Value<String>("owner")),   Has.All.EqualTo("bobby"));

                Assert.That(rootSees.Value<Boolean>("everyone"),                          Is.True);
                Assert.That(rootSees["contracts"]?.Count(),                             Is.EqualTo(3));

            });

        }

        #endregion

        #region ADriverMayLookAtNothingElse()

        /// <summary>
        /// A driver is signed in and may look at their contracts - and at
        /// nothing of the EMSP: not its configuration, not its log, not the
        /// stream that carries the log.
        /// </summary>
        [Test]
        public async Task ADriverMayLookAtNothingElse()
        {

            using var driver = await SignedUp("alice");

            var configuration  = await driver.GetAsync("/api/v1/configuration");
            var logs           = await driver.GetAsync("/api/v1/logs");
            var partners       = await driver.GetAsync("/api/v1/ocpi/partners");
            var tokens         = await driver.GetAsync("/api/v1/ocpi/tokens");
            var events         = await driver.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead);

            Assert.Multiple(() => {
                Assert.That(configuration.StatusCode,  Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(logs.         StatusCode,  Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(partners.     StatusCode,  Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(tokens.       StatusCode,  Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(events.       StatusCode,  Is.EqualTo(HttpStatusCode.Forbidden));
            });

        }

        #endregion

        #region ARequestWithTheWrongKeyIsRefused()

        [Test]
        public async Task ARequestWithTheWrongKeyIsRefused()
        {

            using var driver = await SignedUp("alice");

            var p384      = V2GCertificateBuilder.GenerateKeyPair(V2GAlgorithm.EcdsaP384, new SecureRandom());
            var response  = await driver.PostAsync("/api/v1/contracts", JSONBody(new JProperty("csr", CSR(p384))));
            var body      = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,           Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(body.Value<String>("error"),   Does.Contain("secp256r1"));
                Assert.That(EMSP.ContractCount,            Is.EqualTo(0));
            });

        }

        #endregion

        #region ARequestThatIsNotSignedWithItsKeyIsRefused()

        /// <summary>
        /// The signature on the request is the proof that whoever sent it
        /// holds the key; a request whose signature does not fit is somebody
        /// asking for a certificate to a key that is not theirs.
        /// </summary>
        [Test]
        public async Task ARequestThatIsNotSignedWithItsKeyIsRefused()
        {

            using var driver = await SignedUp("alice");

            var random    = new SecureRandom();
            var keyPair   = V2GCertificateBuilder.GenerateKeyPair(V2GAlgorithm.EcdsaP256, random);
            var other     = V2GCertificateBuilder.GenerateKeyPair(V2GAlgorithm.EcdsaP256, random);

            // The public key of one pair, signed with the private key of the other.
            var forged    = new Pkcs10CertificationRequest(
                                new Asn1SignatureFactory("SHA256withECDSA", other.Private, random),
                                new X509Name("CN=somebody"),
                                keyPair.Public,
                                null
                            ).ToPEM();

            var response  = await driver.PostAsync("/api/v1/contracts", JSONBody(new JProperty("csr", forged)));
            var body      = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,           Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(body.Value<String>("error"),   Does.Contain("not signed"));
                Assert.That(EMSP.ContractCount,            Is.EqualTo(0));
            });

            var nonsense  = await driver.PostAsync("/api/v1/contracts", JSONBody(new JProperty("csr", "-----BEGIN CERTIFICATE REQUEST-----\nAAAA\n-----END CERTIFICATE REQUEST-----")));

            Assert.That(nonsense.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        }

        #endregion

        #region ARevokedContractIsOverAndItsTokenGone()

        [Test]
        public async Task ARevokedContractIsOverAndItsTokenGone()
        {

            using var alice  = await SignedUp("alice");
            using var bob    = await SignedUp("bobby");

            var emaId        = await IssueContract(alice);

            Assert.That(EMSP.TokenCount, Is.EqualTo(EMSP.OCPIVersions.Count));

            #region Not by somebody else

            var byBob = await bob.PostAsync($"/api/v1/contracts/{emaId}/revoke", JSONBody());

            Assert.That(byBob.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

            #endregion

            #region By its owner

            var byAlice  = await alice.PostAsync($"/api/v1/contracts/{emaId}/revoke", JSONBody());
            var body     = JObject.Parse(await byAlice.Content.ReadAsStringAsync());

            Assert.Multiple(() => {
                Assert.That(byAlice.StatusCode,                              Is.EqualTo(HttpStatusCode.OK), body.ToString());
                Assert.That(body["contract"]?.Value<String>("status"),       Is.EqualTo("revoked"));
                Assert.That(body["contract"]?.Value<String>("revokedBy"),    Is.EqualTo("alice"));
                Assert.That(EMSP.TokenCount,                                 Is.EqualTo(0));
                Assert.That(EMSP.Contracts.TryGet(emaId, out var stored),    Is.True);
                Assert.That(stored!.IsRevoked,                               Is.True);
            });

            #endregion

            #region Only once

            var again = await alice.PostAsync($"/api/v1/contracts/{emaId}/revoke", JSONBody());

            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

            #endregion

            #region And the operator may take anybody's

            var bobs     = await IssueContract(bob);

            using var root = await SignedIn();

            var byRoot   = await root.PostAsync($"/api/v1/contracts/{bobs}/revoke", JSONBody());

            Assert.That(byRoot.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            #endregion

        }

        #endregion

        #region TheMORootAndTheContractsSurviveARestart()

        /// <summary>
        /// The one thing that must not happen at a start is a new MO root:
        /// it would invalidate every contract ever issued, and every CPO
        /// handed the old root would turn the drivers away.
        /// </summary>
        [Test]
        public async Task TheMORootAndTheContractsSurviveARestart()
        {

            using var alice  = await SignedUp("alice");

            var emaId        = await IssueContract(alice);
            var fingerprint  = EMSP.ContractCA.RootFingerprint;

            await EMSP.Stop();

            var again = TestEMSPs.New(Directory, Configuration, Clock);

            try
            {

                await again.Start();

                Assert.Multiple(() => {
                    Assert.That(again.ContractCA.WasCreated,           Is.False, "A second MO root was made.");
                    Assert.That(again.ContractCA.RootFingerprint,      Is.EqualTo(fingerprint));
                    Assert.That(again.ContractCount,                   Is.EqualTo(1));
                    Assert.That(again.Contracts.TryGet(emaId, out var stored), Is.True);
                    Assert.That(stored!.Owner,                         Is.EqualTo("alice"));
                    Assert.That(again.ContractCA.Verify(ReadCertificate(again.Contracts.TryReadPEM(stored, out var pem) ? pem : null)), Is.True,
                                "The certificate on disk is not one the reloaded authority signed.");
                });

                // The tokens are the OCPI library's to keep between starts, and
                // it keeps the 2.1.1 one; whether every version's comes back is
                // its business and not this feature's, so only the contract's
                // own records are asserted here.

            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

        #region AnIncompleteAuthorityIsAnErrorAndNotANewRoot()

        [Test]
        public async Task AnIncompleteAuthorityIsAnErrorAndNotANewRoot()
        {

            await EMSP.Stop();

            File.Delete(Path.Combine(EMSP.ContractCA.Directory, "mo_sub_ca_2.key.pem"));

            Assert.That(() => TestEMSPs.New(Directory, Configuration, Clock),
                        Throws.InvalidOperationException.With.Message.Contains("incomplete"));

        }

        #endregion

        #region TheSignedUpAccountSignsInAtTheDoor()

        /// <summary>
        /// Hermod's sign-up makes an account in no organization, and the
        /// sign-in door refuses those; this EMSP puts every sign-up into its
        /// organization, so that the driver can come back tomorrow.
        /// </summary>
        [Test]
        public async Task TheSignedUpAccountSignsInAtTheDoor()
        {

            using var signedUp = await SignedUp("alice", "correct horse battery staple");

            using var tomorrow = await SignedInAs("alice", "correct horse battery staple");

            var me = await GetJSON(tomorrow, "/api/v1/auth/me");

            Assert.That(me["roles"]?.Values<String>(), Is.EquivalentTo(new[] { "driver" }));

        }

        #endregion

        #region ASignUpNeedsAFreeUsernameAndAPassword()

        [Test]
        public async Task ASignUpNeedsAFreeUsernameAndAPassword()
        {

            using var first  = await SignedUp("alice");

            using var http   = Anonymous();

            var taken     = await http.PostAsJsonAsync("/ext/auth/signup", new { username = "alice",  email = "alice2@example.org", password = "correct horse battery staple" });
            var asRoot    = await http.PostAsJsonAsync("/ext/auth/signup", new { username = "root",   email = "root2@example.org",  password = "correct horse battery staple" });
            var weak      = await http.PostAsJsonAsync("/ext/auth/signup", new { username = "carol",  email = "carol@example.org",  password = "x" });

            Assert.Multiple(() => {
                Assert.That(taken. StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
                Assert.That(asRoot.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
                Assert.That(weak.  StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(EMSP.ExtAPI.Users.Count(), Is.EqualTo(2), "root and alice, and nobody else");
            });

        }

        #endregion


        #region (protected) SignedUp(Username, Password = null)

        /// <summary>
        /// A browser that signed up as somebody, carrying the session the
        /// sign-up handed out.
        /// </summary>
        protected async Task<HttpClient> SignedUp(String   Username,
                                                  String?  Password   = null)
        {

            var http      = Anonymous();

            var response  = await http.PostAsJsonAsync(
                                      "/ext/auth/signup",
                                      new {
                                          username     = Username,
                                          email        = $"{Username}@example.org",
                                          password     = Password ?? "correct horse battery staple",
                                          displayName  = Username
                                      }
                                  );

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                        $"Signing up as '{Username}' failed: {await response.Content.ReadAsStringAsync()}");

            return http;

        }

        #endregion

        #region (protected) IssueContract(HTTP)

        /// <summary>
        /// One contract, with a key made here, for whoever the browser is.
        /// </summary>
        protected async Task<EMAId> IssueContract(HttpClient HTTP)
        {

            var keyPair   = V2GCertificateBuilder.GenerateKeyPair(V2GAlgorithm.EcdsaP256, new SecureRandom());
            var response  = await HTTP.PostAsync("/api/v1/contracts", JSONBody(new JProperty("csr", CSR(keyPair))));
            var body      = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), body.ToString());
            Assert.That(EMAId.TryParse(body["contract"]?.Value<String>("emaId"), out var emaId, out var error), Is.True, error);

            return emaId!;

        }

        #endregion

        #region (protected static) CSR(KeyPair)

        /// <summary>
        /// A PKCS#10 request for the given key, signed with it, as a browser
        /// would send one.
        /// </summary>
        protected static String CSR(AsymmetricCipherKeyPair KeyPair)

            => new Pkcs10CertificationRequest(
                   new Asn1SignatureFactory("SHA256withECDSA", KeyPair.Private, new SecureRandom()),
                   new X509Name("CN=a driver"),
                   KeyPair.Public,
                   null
               ).ToPEM();

        #endregion

        #region (protected static) ReadCertificate(PEM) / ReadCertificates(PEM)

        protected static BCCertificate ReadCertificate(String? PEM)
            => ReadCertificates(PEM).Single();

        protected static BCCertificate[] ReadCertificates(String? PEM)
        {

            using var reader = new PemReader(new StringReader(PEM ?? ""));

            var certificates = new List<BCCertificate>();

            while (reader.ReadObject() is Object item)
                if (item is BCCertificate certificate)
                    certificates.Add(certificate);

            return [.. certificates];

        }

        #endregion

    }

}
