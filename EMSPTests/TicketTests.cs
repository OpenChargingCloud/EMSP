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
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using cloud.charging.open.EMSP.Tickets;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// A driver's long-term account keys and the charging tickets signed with
    /// them, over HTTP, the way a browser asks: the account certificate, the
    /// ticket the EMSP signs with the account's signature taken off, and every
    /// ticket it refuses.
    /// </summary>
    public class TicketTests : AEMSPTests
    {

        #region AnAccountCertificateIsGoodForTwoYearsFromAMinuteAgo()

        [Test]
        public async Task AnAccountCertificateIsGoodForTwoYearsFromAMinuteAgo()
        {

            using var alice    = await SignedUp("alice");
            using var bob      = await SignedUp("bobby");

            var before         = DateTimeOffset.UtcNow;
            var (key, id, pem) = await AccountKey(alice, "Laptop");
            using var _        = key;

            using var certificate = X509Certificate2.CreateFromPem(pem);
            using var ca          = X509Certificate2.CreateFromPem(await alice.GetStringAsync("/api/v1/account-keys/ca.pem"));

            var listed         = await GetJSON(alice, "/api/v1/account-keys");
            var bobs           = await GetJSON(bob,   "/api/v1/account-keys");

            var keyUsage       = certificate.Extensions.OfType<X509KeyUsageExtension>().Single();

            Assert.Multiple(() => {

                Assert.That(certificate.GetNameInfo(X509NameType.SimpleName, false),  Is.EqualTo("alice"));
                Assert.That(certificate.Issuer,                                       Is.EqualTo(ca.Subject));
                Assert.That(new DateTimeOffset(certificate.NotBefore.ToUniversalTime()), Is.EqualTo(before.AddMinutes(-1)).Within(TimeSpan.FromSeconds(10)));
                Assert.That(certificate.NotAfter - certificate.NotBefore,             Is.EqualTo(TimeSpan.FromDays(730) + TimeSpan.FromMinutes(1)).Within(TimeSpan.FromSeconds(2)));
                Assert.That(keyUsage.KeyUsages,                                       Is.EqualTo(X509KeyUsageFlags.DigitalSignature), "for signing and nothing else");
                Assert.That(certificate.GetECDsaPublicKey()!.ExportSubjectPublicKeyInfo(), Is.EqualTo(key.ExportSubjectPublicKeyInfo()));
                Assert.That(id,                                                       Is.EqualTo(Convert.ToHexString(SHA256.HashData(certificate.RawData))));

                Assert.That(listed["certificates"]?.Count(),                          Is.EqualTo(1));
                Assert.That(listed["certificates"]?[0]?.Value<String>("label"),       Is.EqualTo("Laptop"));
                Assert.That(listed["certificates"]?[0]?.Value<String>("status"),      Is.EqualTo("valid"));
                Assert.That(bobs["certificates"]?.Count(),                            Is.Zero, "another driver sees it");

            });

            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(ca);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

            Assert.That(chain.Build(certificate), Is.True, "it does not chain up to the account CA");

        }

        #endregion

        #region ASigningRequestThatIsNoneOrNotOnP256IsRefused(What)

        [TestCase("none")]
        [TestCase("rsa")]
        [TestCase("p384")]
        public async Task ASigningRequestThatIsNoneOrNotOnP256IsRefused(String What)
        {

            using var alice = await SignedUp("alice");

            var csr = What switch {
                          "rsa"   => new CertificateRequest("CN=x", RSA.Create(2048), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSigningRequestPem(),
                          "p384"  => new CertificateRequest("CN=x", ECDsa.Create(ECCurve.NamedCurves.nistP384), HashAlgorithmName.SHA256).CreateSigningRequestPem(),
                          _       => "-----BEGIN CERTIFICATE REQUEST-----\nAAAA\n-----END CERTIFICATE REQUEST-----\n"
                      };

            var answer = await alice.PostAsync("/api/v1/account-keys", JSONBody(new JProperty("csr", csr)));

            Assert.Multiple(() => {
                Assert.That(answer.StatusCode,               Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(EMSP.AccountCertificates.All,    Is.Empty);
            });

        }

        #endregion

        #region ADriverGetsATicketSignedWithoutTheirSignatureInIt()

        /// <summary>
        /// The ticket comes back signed by its own key and by the ticket
        /// issuer, the payload as it was, and with no signature by the
        /// account's key in it - while the EMSP keeps whose it is.
        /// </summary>
        [Test]
        public async Task ADriverGetsATicketSignedWithoutTheirSignatureInIt()
        {

            using var alice         = await SignedUp("alice");
            var (account, id, _)    = await AccountKey(alice);
            using var ticketKey     = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var ticket              = Ticket(ticketKey, MaxKW: 22, MaxMinutes: 120, MaxKWh: 40.5m);
            var request             = Request(ticket, ticketKey, account, Convert.FromHexString(id));

            var answer              = await alice.PostAsync("/api/v1/tickets", JSONBody(new JProperty("request", Convert.ToBase64String(request))));
            var body                = JObject.Parse(await answer.Content.ReadAsStringAsync());

            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.Created), body.ToString());

            var signed              = Convert.FromBase64String(body.Value<String>("ticket")!);
            using var issuer        = X509Certificate2.CreateFromPem(await alice.GetStringAsync("/api/v1/tickets/issuer.pem"));
            using var issuerKey     = issuer.GetECDsaPublicKey()!;

            Assert.That(COSE.TryDecodeSign(signed, out var message, out var error), Is.True, error);
            Assert.That(ChargingTicket.TryDecode(message!.Payload, out var decoded, out error), Is.True, error);

            var listed              = await GetJSON(alice, "/api/v1/tickets");

            Assert.Multiple(() => {

                Assert.That(signed[0],                                            Is.EqualTo(0xD8), "not tagged");
                Assert.That(signed[1],                                            Is.EqualTo(98),   "not tagged as a COSE_Sign");
                Assert.That(message.Payload,                                      Is.EqualTo(ticket.Encode()), "the payload was changed");
                Assert.That(message.Signatures,                                   Has.Count.EqualTo(2));
                Assert.That(message.Signatures[0].KeyId,                          Is.EqualTo(ChargingTicket.TicketKeyId));
                Assert.That(COSE.Verifies(ticketKey, message, message.Signatures[0]), Is.True, "the ticket's own signature is gone");
                Assert.That(message.Signatures[1].KeyId,                          Is.EqualTo(SHA256.HashData(issuer.RawData)));
                Assert.That(COSE.Verifies(issuerKey, message, message.Signatures[1]), Is.True, "not signed by the ticket issuer");
                Assert.That(message.Signatures.Any(signature => COSE.Verifies(account, message, signature)), Is.False, "the account's signature is still in it");
                Assert.That(message.Signatures.Any(signature => signature.KeyId!.SequenceEqual(Convert.FromHexString(id))), Is.False, "the account's key is still named in it");

                Assert.That(decoded!.MaxKW,                                       Is.EqualTo(22m));
                Assert.That(decoded. MaxMinutes,                                  Is.EqualTo(120u));
                Assert.That(decoded. MaxKWh,                                      Is.EqualTo(40.5m));
                Assert.That(decoded. EMSP,                                        Is.EqualTo("DE*GDF"));

                Assert.That(listed["tickets"]?.Count(),                           Is.EqualTo(1));
                Assert.That(listed["tickets"]?[0]?.Value<String>("id"),           Is.EqualTo(ticket.IdText));
                Assert.That(listed["tickets"]?[0]?.Value<String>("owner"),        Is.EqualTo("alice"), "the EMSP keeps whose it is");
                Assert.That(listed["tickets"]?[0]?.Value<String>("accountCertificate"), Is.EqualTo(id));

            });

        }

        #endregion

        #region ATicketComesBackAsCBORWhereItWasAskedForAsCBOR()

        [Test]
        public async Task ATicketComesBackAsCBORWhereItWasAskedForAsCBOR()
        {

            using var alice       = await SignedUp("alice");
            var (account, id, _)  = await AccountKey(alice);
            using var ticketKey   = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var content           = new ByteArrayContent(Request(Ticket(ticketKey), ticketKey, account, Convert.FromHexString(id)));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/cose");

            var answer            = await alice.PostAsync("/api/v1/tickets", content);
            var bytes             = await answer.Content.ReadAsByteArrayAsync();

            Assert.Multiple(() => {
                Assert.That(answer.StatusCode,                        Is.EqualTo(HttpStatusCode.Created), System.Text.Encoding.UTF8.GetString(bytes));
                Assert.That(answer.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/cose"));
                Assert.That(COSE.TryDecodeSign(bytes, out var message, out _) && message.Signatures.Count == 2, Is.True);
            });

        }

        #endregion

        #region ATicketNotSignedByAKeyOfTheAccountIsRefused(How)

        /// <summary>
        /// Signed by another driver's key, by no account key, by a ticket key
        /// that is not the one in it - or with a key of the account that was
        /// taken back: refused, and nothing signed.
        /// </summary>
        [TestCase("another driver's key")]
        [TestCase("no account key")]
        [TestCase("another ticket key")]
        [TestCase("a key taken back")]
        public async Task ATicketNotSignedByAKeyOfTheAccountIsRefused(String How)
        {

            using var alice          = await SignedUp("alice");
            using var bob            = await SignedUp("bobby");

            var (account,  id,  _)   = await AccountKey(alice);
            var (bobsKey,  bobs, _)  = await AccountKey(bob);
            using var ticketKey      = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var otherKey       = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var ticket               = Ticket(ticketKey);

            if (How == "a key taken back")
            {
                var revoked = await alice.PostAsync($"/api/v1/account-keys/{id}/revoke", JSONBody());
                Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            }

            var request              = How switch {
                                           "another driver's key"  => Request(ticket, ticketKey, bobsKey, Convert.FromHexString(bobs)),
                                           "no account key"        => Request(ticket, ticketKey, null,    null),
                                           "another ticket key"    => Request(ticket, otherKey,  account, Convert.FromHexString(id)),
                                           _                       => Request(ticket, ticketKey, account, Convert.FromHexString(id))
                                       };

            var answer               = await alice.PostAsync("/api/v1/tickets", JSONBody(new JProperty("request", Convert.ToBase64String(request))));

            Assert.Multiple(async () => {
                Assert.That(answer.StatusCode,       Is.EqualTo(HttpStatusCode.BadRequest), await answer.Content.ReadAsStringAsync());
                Assert.That(EMSP.IssuedTickets.All,  Is.Empty);
            });

        }

        #endregion

        #region ATicketOutsideTheRulesIsRefused(How)

        [TestCase("too long")]
        [TestCase("another EMSP")]
        [TestCase("begun an hour ago")]
        [TestCase("over")]
        [TestCase("seen before")]
        public async Task ATicketOutsideTheRulesIsRefused(String How)
        {

            using var alice       = await SignedUp("alice");
            var (account, id, _)  = await AccountKey(alice);
            using var ticketKey   = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            var now               = DateTimeOffset.UtcNow;
            var ticket            = Ticket(ticketKey);

            ticket = How switch {
                         "too long"           => ticket with { NotAfter  = ticket.NotBefore.AddDays(31) },
                         "another EMSP"       => ticket with { EMSP      = "DE*XYZ" },
                         "begun an hour ago"  => ticket with { NotBefore = now.AddHours(-1) },
                         "over"               => ticket with { NotBefore = now.AddDays(-2), NotAfter = now.AddDays(-1) },
                         _                    => ticket
                     };

            if (How == "seen before")
            {
                var first = await alice.PostAsync("/api/v1/tickets", JSONBody(new JProperty("request", Convert.ToBase64String(Request(ticket, ticketKey, account, Convert.FromHexString(id))))));
                Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            }

            var answer            = await alice.PostAsync("/api/v1/tickets", JSONBody(new JProperty("request", Convert.ToBase64String(Request(ticket, ticketKey, account, Convert.FromHexString(id))))));

            Assert.Multiple(async () => {
                Assert.That(answer.StatusCode,       Is.EqualTo(HttpStatusCode.BadRequest), await answer.Content.ReadAsStringAsync());
                Assert.That(EMSP.IssuedTickets.All,  Has.Count.EqualTo(How == "seen before" ? 1 : 0));
            });

        }

        #endregion

        #region NobodyElseTakesADriversKeyBack()

        [Test]
        public async Task NobodyElseTakesADriversKeyBack()
        {

            using var alice       = await SignedUp("alice");
            using var bob         = await SignedUp("bobby");
            var (_, id, _)        = await AccountKey(alice);

            var bobs              = await bob.PostAsync($"/api/v1/account-keys/{id}/revoke", JSONBody());
            var mine              = await GetJSON(alice, "/api/v1/account-keys");

            Assert.Multiple(() => {
                Assert.That(bobs.StatusCode,                                  Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(mine["certificates"]?[0]?.Value<String>("status"), Is.EqualTo("valid"));
            });

        }

        #endregion

        #region ADriverLeavingTakesTheirKeysBack()

        [Test]
        public async Task ADriverLeavingTakesTheirKeysBack()
        {

            using var alice  = await SignedUp("alice");
            await AccountKey(alice);
            await AccountKey(alice);

            var deleted      = await alice.PostAsync("/api/v1/me/delete", JSONBody(new JProperty("username", "alice")));

            Assert.Multiple(() => {
                Assert.That(deleted.StatusCode,                                                     Is.EqualTo(HttpStatusCode.OK));
                Assert.That(EMSP.AccountCertificates.All.Select(certificate => certificate.IsRevoked), Is.All.True);
            });

        }

        #endregion

        #region ATicketRequestTheBrowserMadeIsReadAsItWasWritten()

        /// <summary>
        /// A ticket request the web interface's own code made - src/crypto/cose.ts,
        /// run under Node - read and checked by this EMSP's: both signatures
        /// verify, and the payload is what this side writes for the same
        /// ticket, byte for byte, 22.5 kW as a half-precision float included.
        /// </summary>
        [Test]
        public void ATicketRequestTheBrowserMadeIsReadAsItWasWritten()
        {

            var request     = Convert.FromBase64String(
                                                     "2GKEWCShA3ggYXBwbGljYXRpb24vY2hhcmdpbmctdGlja2V0K2Nib3KgWLCoYXYBYmlkUCF2YqMHpjlFbmtM+GAih/VjZXhw" +
                                                     "GmrKhmdja2V5pAECIAEhWCCt6YzWY97StqY5pGAMEv+e4pzJvEKpLGoirc8j8tnO3CJYIHKNJKJjuYtdRZVPDo31MZv4Qp6Y" +
                                                     "73So+WmXvn7mSUclY25iZhpqyTTnY3R5cG5DaGFyZ2luZ1RpY2tldGRlbXNwZkRFKkdERmZsaW1pdHOiYmtX+U2gZ21pbnV0" +
                                                     "ZXMYWoKDS6IBJgRGdGlja2V0oFhAEzEJdpZ/AqY6tcBUQ4xIR47MAIyUtl4xjKH3mzSKXkr+/4g01BHlpXVxrSr+AVW1cSXe" +
                                                     "vaLS+hwy4BRnPRdCX4NYJqIBJgRYIAcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHBwcHoFhA4TCCYxg491/CKiy9N5TU" +
                                                     "QBgrkQRs/tZ0j9mw4YfHQb/rvifyD54QVsYU4NHP4rf2H1xRx8LojkVgUDj70q9Kxw==");

            using var accountKey = ECDsa.Create();
            accountKey.ImportSubjectPublicKeyInfo(Convert.FromBase64String("MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE0YwTTVq3iZW/f7yF/MbyXpeCsoLxFnd0lm4wp16tQ8c8FMe30qLCkeRLQI+/19mt9RO1oRsUbVI3Vj1U4o7sxw=="), out _);

            Assert.That(COSE.TryDecodeSign(request, out var message, out var error), Is.True, error);
            Assert.That(ChargingTicket.TryDecode(message!.Payload, out var ticket, out error), Is.True, error);

            using var ticketKey  = ECDsa.Create(ticket!.Key);

            Assert.Multiple(() => {
                Assert.That(ticket.EMSP,                                               Is.EqualTo("DE*GDF"));
                Assert.That(ticket.MaxKW,                                              Is.EqualTo(22.5m));
                Assert.That(ticket.MaxMinutes,                                         Is.EqualTo(90u));
                Assert.That(ticket.MaxKWh,                                             Is.Null);
                Assert.That(ticket.NotAfter - ticket.NotBefore,                        Is.EqualTo(TimeSpan.FromDays(1)));
                Assert.That(message.Signatures[0].KeyId,                               Is.EqualTo(ChargingTicket.TicketKeyId));
                Assert.That(COSE.Verifies(ticketKey,  message, message.Signatures[0]), Is.True, "the ticket key's signature");
                Assert.That(COSE.Verifies(accountKey, message, message.Signatures[1]), Is.True, "the account key's signature");
                Assert.That(ticket.Encode(),                                           Is.EqualTo(message.Payload), "written otherwise here");
            });

        }

        #endregion

        #region TheKeysAreTheSameAfterARestart()

        [Test]
        public void TheKeysAreTheSameAfterARestart()
        {

            var directory  = Path.Combine(Directory, "keys-again");
            var subject    = new X500DistinguishedName("CN=Test Issuer");
            var now        = DateTimeOffset.UtcNow;

            using var first  = SigningKey.OpenOrCreate(directory, "issuer", subject, false, TimeSpan.FromDays(10), now);
            using var again  = SigningKey.OpenOrCreate(directory, "issuer", subject, false, TimeSpan.FromDays(10), now);

            File.Delete(Path.Combine(directory, "issuer.cert.pem"));

            Assert.Multiple(() => {
                Assert.That(first.WasCreated,   Is.True);
                Assert.That(again.WasCreated,   Is.False);
                Assert.That(again.Fingerprint,  Is.EqualTo(first.Fingerprint));
                Assert.That(() => SigningKey.OpenOrCreate(directory, "issuer", subject, false, TimeSpan.FromDays(10), now),
                            Throws.InvalidOperationException, "a key without its certificate was made anew");
            });

        }

        #endregion


        #region (private) SignedUp(Username)

        private async Task<HttpClient> SignedUp(String Username)
        {

            var http      = Anonymous();

            var response  = await http.PostAsJsonAsync("/ext/auth/signup", new {
                                                                                username     = Username,
                                                                                email        = $"{Username}@example.org",
                                                                                password     = "correct horse battery staple",
                                                                                displayName  = Username
                                                                            });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());

            return http;

        }

        #endregion

        #region (private static) AccountKey(HTTP, Label = null)

        /// <summary>
        /// A key on P-256 made here, certified as the account's: the key, the
        /// certificate's id and the certificate.
        /// </summary>
        private static async Task<(ECDsa Key, String Id, String PEM)> AccountKey(HttpClient HTTP, String? Label = null)
        {

            var key       = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var csr       = new CertificateRequest("CN=whoever", key, HashAlgorithmName.SHA256).CreateSigningRequestPem();

            var answer    = await HTTP.PostAsync("/api/v1/account-keys", JSONBody(new JProperty("csr", csr), new JProperty("label", Label)));
            var body      = JObject.Parse(await answer.Content.ReadAsStringAsync());

            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.Created), body.ToString());

            return (key, body["accountCertificate"]!.Value<String>("id")!, body.Value<String>("certificate")!);

        }

        #endregion

        #region (private static) Ticket(Key, ...)

        /// <summary>A ticket for this EMSP from now for a day, with a fresh id.</summary>
        private static ChargingTicket Ticket(ECDsa     Key,
                                             Decimal?  MaxKW       = null,
                                             UInt32?   MaxMinutes  = null,
                                             Decimal?  MaxKWh      = null)
        {

            var now = DateTimeOffset.UtcNow;

            return new ChargingTicket(
                       RandomNumberGenerator.GetBytes(16),
                       "DE*GDF",
                       Key.ExportParameters(false),
                       DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds()),
                       DateTimeOffset.FromUnixTimeSeconds(now.AddDays(1).ToUnixTimeSeconds()),
                       MaxKW,
                       MaxMinutes,
                       MaxKWh
                   );

        }

        #endregion

        #region (private static) Request(Ticket, TicketKey, AccountKey, AccountKeyId)

        /// <summary>
        /// A ticket request as a driver's browser makes one: the ticket signed
        /// with the ticket key and - where given - with an account key.
        /// </summary>
        private static Byte[] Request(ChargingTicket  Ticket,
                                      ECDsa           TicketKey,
                                      ECDsa?          AccountKey,
                                      Byte[]?         AccountKeyId)
        {

            var body        = COSE.BodyHeader(ChargingTicket.ContentType);
            var payload     = Ticket.Encode();

            var signatures  = new List<COSESignature> {
                                  COSE.Sign(TicketKey, ChargingTicket.TicketKeyId, body, payload)
                              };

            if (AccountKey is not null && AccountKeyId is not null)
                signatures.Add(COSE.Sign(AccountKey, AccountKeyId, body, payload));

            return COSE.EncodeSign(new COSESign(body, payload, signatures));

        }

        #endregion

    }

}
