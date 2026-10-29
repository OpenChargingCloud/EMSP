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
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.EMSP.Tickets;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// Starts and stops with a charging ticket, the way the driver's phone
    /// asks: a start request signed by the ticket's key, countersigned by
    /// this EMSP with a token of this start alone and sent to a stub CPO as
    /// OCPI START_SESSION; the token ALLOWED there until the stop, and
    /// BLOCKED after it - and every request it refuses.
    /// </summary>
    public class TicketStartTests : AEMSPTests
    {

        #region Data

        private const String  EVSEId    = "DE*GEF*E1*1";
        private const String  EVSEUId   = "E1";
        private const String  OtherUId  = "E2";
        private const String  Location  = "LOC1";

        #endregion

        #region Configuration

        /// <summary>All three versions: 2.3.0 is no default one.</summary>
        protected override JObject Configuration
        {
            get
            {
                var configuration = TestEMSPs.Offline;
                configuration["ocpi"] = new JObject(new JProperty("versions", new JArray("2.1.1", "2.2.1", "2.3.0")));
                return configuration;
            }
        }

        #endregion


        #region AStartWithATicketIsCountersignedSentAndStopped(Version)

        /// <summary>
        /// A start request with its ticket: countersigned - the phone's bytes
        /// and signature as they came, this EMSP's signature by the ticket
        /// issuer with the token, the reference and the time in its protected
        /// header - and sent as START_SESSION with a token of this start
        /// alone, the ticket and the countersigned request beside it. The
        /// token is ALLOWED until the stop, which goes out as STOP_SESSION
        /// of the session the CPO reported, countersigned with the same
        /// token and reference; BLOCKED after it.
        /// </summary>
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task AStartWithATicketIsCountersignedSentAndStopped(String Version)
        {

            await using var cpo          = await StubCPO.Start(Version);
            using var partner            = await PeeredWith(cpo, Version);

            using var alice              = await SignedUp("alice");
            var (ticketKey, ticket, id)  = await IssuedTicket(alice);
            using var _                  = ticketKey;

            var request                  = StartRequest(ticket, ticketKey, EVSEId);
            var answer                   = await alice.PostAsync($"/api/v1/tickets/{id}/start", JSONBody(
                                                                     new JProperty("ticket",   Convert.ToBase64String(ticket)),
                                                                     new JProperty("request",  Convert.ToBase64String(request))
                                                                 ));
            var body                     = JObject.Parse(await answer.Content.ReadAsStringAsync());

            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), body.ToString());

            var countersigned            = Convert.FromBase64String(body.Value<String>("request")!);
            var token                    = body.Value<String>("token")!;
            var authRef                  = body.Value<String>("authRef")!;

            using var issuer             = X509Certificate2.CreateFromPem(await alice.GetStringAsync("/api/v1/tickets/issuer.pem"));

            AssertCountersigned(request, countersigned, issuer, token, authRef);

            var started                  = cpo.Commands.Single(command => command.Name == "START_SESSION").JSON;

            Assert.Multiple(() => {
                Assert.That(token,                                                Does.Match("^T-[0-9A-F]{16}$"));
                Assert.That(started["token"]?.Value<String>("uid"),               Is.EqualTo(token));
                Assert.That(started["token"]?.Value<String>("contract_id"),       Is.EqualTo(token), "the contract id says something of the account");
                Assert.That(started["token"]?.Value<String>("type"),              Is.EqualTo("APP_USER"));
                Assert.That(started["token"]?.Value<String>("whitelist"),         Is.EqualTo("NEVER"));
                Assert.That(started.Value<String>("authorization_reference"),     Is.EqualTo(authRef));
                Assert.That(started.Value<String>("location_id"),                 Is.EqualTo(Location));
                Assert.That(started.Value<String>("evse_uid"),                    Is.EqualTo(EVSEUId));
                Assert.That(started.Value<String>("charging_ticket"),             Is.EqualTo(Convert.ToBase64String(ticket)));
                Assert.That(started.Value<String>("start_request"),               Is.EqualTo(Convert.ToBase64String(countersigned)));
            });

            Assert.Multiple(async () => {
                Assert.That(await Authorized(partner, Version, token, Location, EVSEUId),  Is.EqualTo("ALLOWED"),     "the token of the start is not allowed at its EVSE");
                Assert.That(await Authorized(partner, Version, token, Location, OtherUId), Is.EqualTo("NOT_ALLOWED"), "the token of the start is allowed at another EVSE");
                Assert.That(await Authorized(partner, Version, token),                     Is.EqualTo("NOT_ALLOWED"), "the token of the start is allowed where the CPO says nothing of where");
            });

            var asStarted                = await StartOf(alice, id);

            Assert.Multiple(() => {
                Assert.That(asStarted.Value<String>("start"),                     Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(countersigned))));
                Assert.That(asStarted.Value<String>("token"),                     Is.EqualTo(token));
                Assert.That(asStarted.Value<String>("authRef"),                   Is.EqualTo(authRef));
                Assert.That(asStarted.Value<String>("evse"),                      Is.EqualTo(EVSEId));
                Assert.That(asStarted.Value<String>("state"),                     Is.EqualTo("started"));
                Assert.That(asStarted["result"],                                  Is.Null, "a CommandResult before the CPO sent one");
                Assert.That(asStarted["session"],                                 Is.Null, "a session before the CPO pushed one");
            });

            await SendCommandResult(partner, started, "ACCEPTED");
            await PushSession(partner, Version, "S1", token, authRef);

            var asCharging               = await StartOf(alice, id);

            Assert.Multiple(() => {
                Assert.That(asCharging["result"]?.Value<String>("result"),                  Is.EqualTo("ACCEPTED"), "the CPO's CommandResult was not taken");
                Assert.That(asCharging["result"]?.Value<String>("receivedAt"),              Is.Not.Null);
                Assert.That(asCharging["session"]?.Value<String>("id"),                     Is.EqualTo("S1"));
                Assert.That(asCharging["session"]?.Value<String>("authorization_reference"), Is.EqualTo(authRef));
                Assert.That(asCharging["session"]?["cdr_token"]?.Value<String>("uid"),      Is.EqualTo(token));
                Assert.That(asCharging["session"]?["version"],                              Is.Null, "the session is not as OCPI writes it");
                Assert.That(asCharging.Value<String>("state"),                              Is.EqualTo("started"));
            });

            var stop                     = StopRequest(ticket, ticketKey, countersigned);
            var stopped                  = await alice.PostAsync($"/api/v1/tickets/{id}/stop", JSONBody(
                                                                     new JProperty("ticket",   Convert.ToBase64String(ticket)),
                                                                     new JProperty("start",    Convert.ToBase64String(countersigned)),
                                                                     new JProperty("request",  Convert.ToBase64String(stop))
                                                                 ));
            var stopBody                 = JObject.Parse(await stopped.Content.ReadAsStringAsync());

            Assert.That(stopped.StatusCode, Is.EqualTo(HttpStatusCode.OK), stopBody.ToString());

            var stopCountersigned        = Convert.FromBase64String(stopBody.Value<String>("request")!);

            AssertCountersigned(stop, stopCountersigned, issuer, token, authRef);

            var stopSession              = cpo.Commands.Single(command => command.Name == "STOP_SESSION").JSON;

            Assert.Multiple(async () => {
                Assert.That(stopSession.Value<String>("session_id"),              Is.EqualTo("S1"));
                Assert.That(stopSession.Value<String>("stop_request"),            Is.EqualTo(Convert.ToBase64String(stopCountersigned)));
                Assert.That(stopSession.Value<String>("charging_ticket"),         Is.EqualTo(Convert.ToBase64String(ticket)));
                Assert.That(await Authorized(partner, Version, token, Location, EVSEUId), Is.Not.EqualTo("ALLOWED"), "the token of a stopped start is still allowed");
                Assert.That(EMSP.TicketStarts.All.Single().State,                 Is.EqualTo("stopped"));
            });

            await SendCommandResult(partner, stopSession, "ACCEPTED");
            await PostCDR(partner, Version, "CDR1", "S1", token, authRef);

            var asStopped                = await StartOf(alice, id);

            Assert.Multiple(() => {
                Assert.That(asStopped.Value<String>("state"),                     Is.EqualTo("stopped"));
                Assert.That(asStopped.Value<String>("endedAt"),                   Is.Not.Null);
                Assert.That(asStopped["result"]?.Value<String>("result"),         Is.EqualTo("ACCEPTED"));
                Assert.That(asStopped["stopResult"]?.Value<String>("result"),     Is.EqualTo("ACCEPTED"), "the CPO's CommandResult of the stop was not taken");
                Assert.That(asStopped["cdr"]?.Value<String>("id"),                Is.EqualTo("CDR1"));
                Assert.That(asStopped["cdr"]?.Value<Decimal>("total_energy"),     Is.EqualTo(4.2m));
            });

            var again                    = await alice.PostAsync($"/api/v1/tickets/{id}/stop", JSONBody(
                                                                     new JProperty("ticket",   Convert.ToBase64String(ticket)),
                                                                     new JProperty("start",    Convert.ToBase64String(countersigned)),
                                                                     new JProperty("request",  Convert.ToBase64String(StopRequest(ticket, ticketKey, countersigned)))
                                                                 ));

            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "a start was stopped twice");

        }

        #endregion

        #region AStartTheCPOLaterAnswersRejectedFails()

        /// <summary>
        /// A CPO that took the START_SESSION and then sends a CommandResult of
        /// REJECTED: the start failed - so GET says, with the result - and its
        /// token is not allowed any more.
        /// </summary>
        [Test]
        public async Task AStartTheCPOLaterAnswersRejectedFails()
        {

            await using var cpo          = await StubCPO.Start("2.2.1");
            using var partner            = await PeeredWith(cpo, "2.2.1");

            using var alice              = await SignedUp("alice");
            var (ticketKey, ticket, id)  = await IssuedTicket(alice);
            using var _                  = ticketKey;

            var answer                   = await alice.PostAsync($"/api/v1/tickets/{id}/start", JSONBody(
                                                                     new JProperty("ticket",   Convert.ToBase64String(ticket)),
                                                                     new JProperty("request",  Convert.ToBase64String(StartRequest(ticket, ticketKey, EVSEId)))
                                                                 ));

            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());

            var token                    = JObject.Parse(await answer.Content.ReadAsStringAsync()).Value<String>("token")!;

            await SendCommandResult(partner, cpo.Commands.Single(command => command.Name == "START_SESSION").JSON, "REJECTED");

            var start                    = await StartOf(alice, id);

            Assert.Multiple(async () => {
                Assert.That(start.Value<String>("state"),                                    Is.EqualTo("failed"));
                Assert.That(start.Value<String>("endedAt"),                                  Is.Not.Null);
                Assert.That(start["result"]?.Value<String>("result"),                        Is.EqualTo("REJECTED"));
                Assert.That(await Authorized(partner, "2.2.1", token, Location, EVSEUId),   Is.Not.EqualTo("ALLOWED"), "the token of a failed start is still allowed");
            });

        }

        #endregion

        #region ATicketIsTheAccountsItWasIssuedToAlone()

        /// <summary>
        /// GET /api/v1/tickets/{id}: one's own ticket, with no start made yet -
        /// and 404, the same, for somebody else's and for one never issued.
        /// </summary>
        [Test]
        public async Task ATicketIsTheAccountsItWasIssuedToAlone()
        {

            using var alice              = await SignedUp("alice");
            using var bob                = await SignedUp("bobby");
            var (ticketKey, _, id)       = await IssuedTicket(alice);
            using var __                 = ticketKey;

            var own                      = await alice.GetAsync($"/api/v1/tickets/{id.ToLowerInvariant()}");
            var body                     = JObject.Parse(await own.Content.ReadAsStringAsync());

            var bobs                     = await bob.  GetAsync($"/api/v1/tickets/{id}");
            var none                     = await alice.GetAsync($"/api/v1/tickets/{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}");

            Assert.Multiple(() => {
                Assert.That(own.StatusCode,                     Is.EqualTo(HttpStatusCode.OK), body.ToString());
                Assert.That(body.Value<String>("id"),           Is.EqualTo(id.ToLowerInvariant()));
                Assert.That(body["notBefore"],                  Is.Not.Null);
                Assert.That(body["notAfter"],                   Is.Not.Null);
                Assert.That(body["starts"] as JArray,           Is.Empty);
                Assert.That(bobs.StatusCode,                    Is.EqualTo(HttpStatusCode.NotFound), "somebody else's ticket");
                Assert.That(none.StatusCode,                    Is.EqualTo(HttpStatusCode.NotFound), "a ticket never issued");
            });

        }

        #endregion

        #region AStartTheCPODoesNotAcceptStartsNothing()

        /// <summary>
        /// A CPO that answers REJECTED: no countersigned request comes back,
        /// and the token of the start is not allowed.
        /// </summary>
        [Test]
        public async Task AStartTheCPODoesNotAcceptStartsNothing()
        {

            await using var cpo          = await StubCPO.Start("2.2.1");
            using var partner            = await PeeredWith(cpo, "2.2.1");

            cpo.CommandResult            = "REJECTED";

            using var alice              = await SignedUp("alice");
            var (ticketKey, ticket, id)  = await IssuedTicket(alice);
            using var _                  = ticketKey;

            var answer                   = await alice.PostAsync($"/api/v1/tickets/{id}/start", JSONBody(
                                                                     new JProperty("ticket",   Convert.ToBase64String(ticket)),
                                                                     new JProperty("request",  Convert.ToBase64String(StartRequest(ticket, ticketKey, EVSEId)))
                                                                 ));
            var body                     = JObject.Parse(await answer.Content.ReadAsStringAsync());
            var start                    = EMSP.TicketStarts.All.Single();

            Assert.Multiple(async () => {
                Assert.That(answer.StatusCode,                         Is.EqualTo(HttpStatusCode.BadGateway), body.ToString());
                Assert.That(body["request"],                           Is.Null, "a request the CPO did not accept came back countersigned");
                Assert.That(body.Value<String>("error"),               Does.Contain("REJECTED"));
                Assert.That(start.State,                               Is.EqualTo("refused"));
                Assert.That(await Authorized(partner, "2.2.1", start.Token, Location, EVSEUId), Is.Not.EqualTo("ALLOWED"));
            });

        }

        #endregion

        #region AStartRequestIsRefused(How)

        /// <summary>
        /// Taken again, naming another ticket, an hour off, signed by another
        /// key, with somebody else's ticket, at an EVSE nobody pushed - each
        /// refused, and nothing sent.
        /// </summary>
        [TestCase("taken again",            HttpStatusCode.BadRequest)]
        [TestCase("another ticket",         HttpStatusCode.BadRequest)]
        [TestCase("an hour off",            HttpStatusCode.BadRequest)]
        [TestCase("another key",            HttpStatusCode.BadRequest)]
        [TestCase("somebody else's ticket", HttpStatusCode.BadRequest)]
        [TestCase("an unknown EVSE",        HttpStatusCode.Conflict)]
        public async Task AStartRequestIsRefused(String How, HttpStatusCode Expected)
        {

            await using var cpo          = await StubCPO.Start("2.2.1");
            using var partner            = await PeeredWith(cpo, "2.2.1");

            using var alice              = await SignedUp("alice");
            using var bob                = await SignedUp("bobby");
            var (ticketKey, ticket, id)  = await IssuedTicket(alice);
            var (otherKey,  other,  _)   = await IssuedTicket(alice);
            using var k1                 = ticketKey;
            using var k2                 = otherKey;

            var request                  = How switch {
                                               "another ticket"   => StartRequest(other,  ticketKey, EVSEId),
                                               "an hour off"      => StartRequest(ticket, ticketKey, EVSEId, DateTimeOffset.UtcNow.AddHours(-1)),
                                               "another key"      => StartRequest(ticket, otherKey,  EVSEId),
                                               "an unknown EVSE"  => StartRequest(ticket, ticketKey, "DE*GEF*E9*9"),
                                               _                  => StartRequest(ticket, ticketKey, EVSEId)
                                           };

            var asks                     = How == "somebody else's ticket" ? bob : alice;

            HttpResponseMessage Post()
                => asks.PostAsync($"/api/v1/tickets/{id}/start", JSONBody(
                       new JProperty("ticket",   Convert.ToBase64String(ticket)),
                       new JProperty("request",  Convert.ToBase64String(request))
                   )).GetAwaiter().GetResult();

            if (How == "taken again")
                Assert.That(Post().StatusCode, Is.EqualTo(HttpStatusCode.OK), "the first time");

            var answer                   = Post();

            Assert.Multiple(async () => {
                Assert.That(answer.StatusCode,  Is.EqualTo(Expected), await answer.Content.ReadAsStringAsync());
                Assert.That(cpo.Commands,       Has.Count.EqualTo(How == "taken again" ? 1 : 0), "a command went out");
            });

        }

        #endregion

        #region AStartAtAPartnerOn2_1_1IsRefused()

        /// <summary>
        /// An EVSE at a CPO on OCPI 2.1.1, which has no authorization
        /// reference for the countersignature to name: refused, saying so.
        /// </summary>
        [Test]
        public async Task AStartAtAPartnerOn2_1_1IsRefused()
        {

            var added = await EMSP.AddRemotePartyAsync(
                                  new JObject(
                                      new JProperty("version",      "2.1.1"),
                                      new JProperty("countryCode",  "DE"),
                                      new JProperty("partyId",      "GEF"),
                                      new JProperty("role",         "CPO"),
                                      new JProperty("name",         "Test CPO"),
                                      new JProperty("ourToken",     "partner-token-211")
                                  )
                              );

            Assert.That(added.Success, Is.True, added.Message);

            using var partner            = PartnerOn("2.1.1", "partner-token-211");

            await PushLocation(partner, "2.1.1");

            using var alice              = await SignedUp("alice");
            var (ticketKey, ticket, id)  = await IssuedTicket(alice);
            using var _                  = ticketKey;

            var answer                   = await alice.PostAsync($"/api/v1/tickets/{id}/start", JSONBody(
                                                                     new JProperty("ticket",   Convert.ToBase64String(ticket)),
                                                                     new JProperty("request",  Convert.ToBase64String(StartRequest(ticket, ticketKey, EVSEId)))
                                                                 ));
            var text                     = await answer.Content.ReadAsStringAsync();

            Assert.Multiple(() => {
                Assert.That(answer.StatusCode,  Is.EqualTo(HttpStatusCode.Conflict), text);
                Assert.That(text,               Does.Contain("OCPI 2.2.1 or later"));
            });

        }

        #endregion


        #region (private static) AssertCountersigned(Sent, Countersigned, Issuer, Token, AuthRef)

        /// <summary>
        /// What the phone checks: its own bytes and signature as they were,
        /// two signatures, the second by the ticket issuer, the token, the
        /// reference and the time in its protected header.
        /// </summary>
        private static void AssertCountersigned(Byte[]            Sent,
                                                Byte[]            Countersigned,
                                                X509Certificate2  Issuer,
                                                String            Token,
                                                String            AuthRef)
        {

            Assert.That(COSESign.TryParse(Sent,          out var sent,   out var error), Is.True, error);
            Assert.That(COSESign.TryParse(Countersigned, out var signed, out error),     Is.True, error);

            var theirs   = signed!.Signatures.Count == 2 ? signed.Signatures[1] : null;
            var issuer   = PublicKeyFactory.CreateKey(Issuer.PublicKey.ExportSubjectPublicKeyInfo());

            String? Text(String Label)
                => theirs?.ProtectedHeader.TryGet(CBORValue.FromText(Label), out var value) == true && value.TryGetText(out var text) ? text : null;

            Assert.Multiple(() => {
                Assert.That(signed.Signatures,                                         Has.Count.EqualTo(2));
                Assert.That(signed.ProtectedHeaderBytes,                               Is.EqualTo(sent!.ProtectedHeaderBytes));
                Assert.That(signed.Payload,                                            Is.EqualTo(sent.Payload));
                Assert.That(signed.Signatures[0].ProtectedHeaderBytes,                 Is.EqualTo(sent.Signatures[0].ProtectedHeaderBytes));
                Assert.That(signed.Signatures[0].Signature,                            Is.EqualTo(sent.Signatures[0].Signature), "the phone's signature was changed");
                Assert.That(ChargingTicket.KeyIdOf(theirs!),                           Is.EqualTo(SHA256.HashData(Issuer.RawData)));
                Assert.That(ChargingTicket.Verifies(issuer, signed, theirs!),          Is.True, "not signed by the ticket issuer");
                Assert.That(Text(TicketRequest.TokenLabel),                            Is.EqualTo(Token),   "the token is not in the protected header");
                Assert.That(Text(TicketRequest.AuthRefLabel),                          Is.EqualTo(AuthRef), "the reference is not in the protected header");
                Assert.That(theirs!.ProtectedHeader.TryGet(CBORValue.FromText(TicketRequest.SignedAtLabel), out var iat) &&
                            Math.Abs(iat.AsInt64() - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) < 300, Is.True, "no time it signed at, or not now");
            });

        }

        #endregion


        #region (private) PeeredWith(CPO, Version)

        /// <summary>
        /// The stub CPO added as a roaming partner and registered with, its
        /// location with one EVSE pushed - and the CPO as a client with the
        /// token this EMSP gave it.
        /// </summary>
        private async Task<HttpClient> PeeredWith(StubCPO  CPO,
                                                  String   Version)
        {

            using var admin  = await SignedIn();

            var added        = await admin.PostAsync("/api/v1/ocpi/partners", JSONBody(
                                                         new JProperty("version",      Version),
                                                         new JProperty("countryCode",  "DE"),
                                                         new JProperty("partyId",      "GEF"),
                                                         new JProperty("role",         "CPO"),
                                                         new JProperty("name",         "Stub CPO"),
                                                         new JProperty("theirToken",   StubCPO.TokenA),
                                                         new JProperty("versionsURL",  CPO.VersionsURL)
                                                     ));
            var addedText    = await added.Content.ReadAsStringAsync();

            Assert.That(added.StatusCode, Is.EqualTo(HttpStatusCode.Created), addedText);

            var register     = await admin.PostAsync($"/api/v1/ocpi/partners/{Version}/{JObject.Parse(addedText).Value<String>("id")}/register", JSONBody());

            Assert.That(register.StatusCode, Is.EqualTo(HttpStatusCode.OK), await register.Content.ReadAsStringAsync());

            var partner      = PartnerOn(Version, CPO.ReceivedCredentials!.Value<String>("token")!);

            await PushLocation(partner, Version);

            return partner;

        }

        #endregion

        #region (private) PartnerOn(Version, Token)

        /// <summary>
        /// The CPO calling this EMSP with the token it was given - as it is on
        /// 2.1.1, in Base64 on 2.2.1 and 2.3.0.
        /// </summary>
        private HttpClient PartnerOn(String Version, String Token)
        {

            var http = new HttpClient { BaseAddress = new Uri(BaseURL) };

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                           "Token",
                                                           Version == "2.1.1" ? Token : Convert.ToBase64String(Encoding.UTF8.GetBytes(Token))
                                                       );

            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Who asks whom, as OCPI 2.2 has a CPO say: without them, the
            // library looks for a location of 2.2.1 among the wrong ones.
            if (Version != "2.1.1")
            {
                http.DefaultRequestHeaders.Add("OCPI-from-country-code",  "DE");
                http.DefaultRequestHeaders.Add("OCPI-from-party-id",      "GEF");
                http.DefaultRequestHeaders.Add("OCPI-to-country-code",    "DE");
                http.DefaultRequestHeaders.Add("OCPI-to-party-id",        "GDF");
            }

            return http;

        }

        #endregion

        #region (private static) PushLocation(Partner, Version) / PushSession(Partner, Version, Id, Token, AuthRef)

        /// <summary>A location of DE*GEF with two EVSEs, PUT by the CPO.</summary>
        private static async Task PushLocation(HttpClient Partner, String Version)
        {

            static JObject EVSE(String UId, String Id)

                => new (
                       new JProperty("uid",           UId),
                       new JProperty("evse_id",       Id),
                       new JProperty("status",        "AVAILABLE"),
                       new JProperty("connectors",    new JArray(
                           new JObject(
                               new JProperty("id",            "1"),
                               new JProperty("standard",      "IEC_62196_T2"),
                               new JProperty("format",        "SOCKET"),
                               new JProperty("power_type",    "AC_3_PHASE"),
                               new JProperty("max_voltage",   230),
                               new JProperty("max_amperage",  16),
                               new JProperty("last_updated",  "2026-09-20T10:00:00Z")
                           )
                       )),
                       new JProperty("last_updated",  "2026-09-20T10:00:00Z")
                   );

            var location = new JObject(
                               new JProperty("country_code",  "DE"),
                               new JProperty("party_id",      "GEF"),
                               new JProperty("id",            Location),
                               new JProperty("publish",       true),
                               new JProperty("name",          "Test location"),
                               new JProperty("address",       "Biberweg 18"),
                               new JProperty("city",          "Jena"),
                               new JProperty("postal_code",   "07749"),
                               new JProperty("country",       "DEU"),
                               new JProperty("coordinates",   new JObject(
                                   new JProperty("latitude",   "50.927"),
                                   new JProperty("longitude",  "11.587")
                               )),
                               new JProperty("time_zone",     "Europe/Berlin"),
                               new JProperty("evses",         new JArray(EVSE(EVSEUId, EVSEId), EVSE(OtherUId, "DE*GEF*E1*2"))),
                               new JProperty("last_updated",  "2026-09-20T10:00:00Z")
                           );

            // 2.1.1 asks for the location's type, and its connectors name
            // their voltage and amperage without the "max_".
            if (Version == "2.1.1")
            {

                location.AddFirst(new JProperty("type", "ON_STREET"));

                foreach (var connector in location["evses"]!.Select(evse => (JObject) evse["connectors"]![0]!))
                {
                    connector.Remove("max_voltage");
                    connector.Remove("max_amperage");
                    connector["voltage"]   = 230;
                    connector["amperage"]  = 16;
                }

            }

            var answer = await Partner.PutAsync($"/ext/v{Version}/emsp/locations/DE/GEF/{Location}", new StringContent(location.ToString(), Encoding.UTF8, "application/json"));
            var text   = await answer.Content.ReadAsStringAsync();

            Assert.That(answer.IsSuccessStatusCode && JObject.Parse(text).Value<Int32>("status_code") == 1000, Is.True, $"OCPI {Version}: PUT location: {(Int32) answer.StatusCode} {text}");

        }

        /// <summary>The session of a start, PUT by the CPO, ACTIVE.</summary>
        private static async Task PushSession(HttpClient Partner, String Version, String Id, String Token, String AuthRef)
        {

            var session = new JObject(
                              new JProperty("country_code",             "DE"),
                              new JProperty("party_id",                 "GEF"),
                              new JProperty("id",                       Id),
                              new JProperty("start_date_time",          DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")),
                              new JProperty("kwh",                      0),
                              new JProperty("cdr_token",                new JObject(
                                  new JProperty("country_code",  "DE"),
                                  new JProperty("party_id",      "GDF"),
                                  new JProperty("uid",           Token),
                                  new JProperty("type",          "APP_USER"),
                                  new JProperty("contract_id",   Token)
                              )),
                              new JProperty("auth_method",              "COMMAND"),
                              new JProperty("authorization_reference",  AuthRef),
                              new JProperty("location_id",              Location),
                              new JProperty("evse_uid",                 EVSEUId),
                              new JProperty("connector_id",             "1"),
                              new JProperty("currency",                 "EUR"),
                              new JProperty("status",                   "ACTIVE"),
                              new JProperty("last_updated",             DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                          );

            var answer  = await Partner.PutAsync($"/ext/v{Version}/emsp/sessions/DE/GEF/{Id}", new StringContent(session.ToString(), Encoding.UTF8, "application/json"));
            var text    = await answer.Content.ReadAsStringAsync();

            Assert.That(answer.IsSuccessStatusCode && JObject.Parse(text).Value<Int32>("status_code") == 1000, Is.True, $"OCPI {Version}: PUT session: {(Int32) answer.StatusCode} {text}");

        }

        #endregion

        #region (private static) StartOf(Driver, TicketId) / SendCommandResult(Partner, Command, Result) / PostCDR(...)

        /// <summary>The one start GET /api/v1/tickets/{id} tells of.</summary>
        private static async Task<JObject> StartOf(HttpClient Driver, String TicketId)
        {

            var answer = await Driver.GetAsync($"/api/v1/tickets/{TicketId}");
            var text   = await answer.Content.ReadAsStringAsync();

            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);

            var starts = JObject.Parse(text)["starts"] as JArray;

            Assert.That(starts, Has.Count.EqualTo(1), text);

            return (JObject) starts![0];

        }

        /// <summary>The CPO's CommandResult of a command, POSTed to its response_url.</summary>
        private static async Task SendCommandResult(HttpClient Partner, JObject Command, String Result)
        {

            var answer = await Partner.PostAsync(
                                   new Uri(Command.Value<String>("response_url")!),
                                   new StringContent(new JObject(new JProperty("result", Result)).ToString(), Encoding.UTF8, "application/json")
                               );
            var text   = await answer.Content.ReadAsStringAsync();

            Assert.That(answer.IsSuccessStatusCode, Is.True, $"POST CommandResult: {(Int32) answer.StatusCode} {text}");

        }

        /// <summary>The charge detail record of a session, POSTed by the CPO.</summary>
        private static async Task PostCDR(HttpClient Partner, String Version, String Id, String SessionId, String Token, String AuthRef)
        {

            var from  = DateTimeOffset.UtcNow.AddMinutes(-30).ToString("yyyy-MM-ddTHH:mm:ssZ");
            var to    = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            var cdr   = new JObject(
                            new JProperty("country_code",             "DE"),
                            new JProperty("party_id",                 "GEF"),
                            new JProperty("id",                       Id),
                            new JProperty("start_date_time",          from),
                            new JProperty("end_date_time",            to),
                            new JProperty("session_id",               SessionId),
                            new JProperty("cdr_token",                new JObject(
                                new JProperty("country_code",  "DE"),
                                new JProperty("party_id",      "GDF"),
                                new JProperty("uid",           Token),
                                new JProperty("type",          "APP_USER"),
                                new JProperty("contract_id",   Token)
                            )),
                            new JProperty("auth_method",              "COMMAND"),
                            new JProperty("authorization_reference",  AuthRef),
                            new JProperty("cdr_location",             new JObject(
                                new JProperty("id",                    Location),
                                new JProperty("name",                  "Test location"),
                                new JProperty("address",               "Biberweg 18"),
                                new JProperty("city",                  "Jena"),
                                new JProperty("postal_code",           "07749"),
                                new JProperty("country",               "DEU"),
                                new JProperty("coordinates",           new JObject(
                                    new JProperty("latitude",   "50.927"),
                                    new JProperty("longitude",  "11.587")
                                )),
                                new JProperty("evse_uid",              EVSEUId),
                                new JProperty("evse_id",               EVSEId),
                                new JProperty("connector_id",          "1"),
                                new JProperty("connector_standard",    "IEC_62196_T2"),
                                new JProperty("connector_format",      "SOCKET"),
                                new JProperty("connector_power_type",  "AC_3_PHASE")
                            )),
                            new JProperty("currency",                 "EUR"),
                            new JProperty("charging_periods",         new JArray(
                                new JObject(
                                    new JProperty("start_date_time",  from),
                                    new JProperty("dimensions",       new JArray(
                                        new JObject(
                                            new JProperty("type",    "ENERGY"),
                                            new JProperty("volume",  4.2)
                                        )
                                    ))
                                )
                            )),
                            // 2.3.0 says what a price is before taxes, 2.2.1 without VAT.
                            new JProperty("total_cost",               new JObject(
                                new JProperty(Version == "2.3.0" ? "before_taxes" : "excl_vat",  1.5)
                            )),
                            new JProperty("total_energy",             4.2),
                            new JProperty("total_time",               0.5),
                            new JProperty("last_updated",             to)
                        );

            var answer = await Partner.PostAsync($"/ext/v{Version}/emsp/cdrs", new StringContent(cdr.ToString(), Encoding.UTF8, "application/json"));
            var text   = await answer.Content.ReadAsStringAsync();

            Assert.That(answer.IsSuccessStatusCode && JObject.Parse(text).Value<Int32>("status_code") == 1000, Is.True, $"OCPI {Version}: POST cdr: {(Int32) answer.StatusCode} {text}");

        }

        #endregion

        #region (private static) Authorized(Partner, Version, Token, Location = null, EVSE = null)

        /// <summary>
        /// What this EMSP answers the CPO's real-time authorisation of a token -
        /// ALLOWED, BLOCKED, ... - at the given location and EVSE, or where the
        /// CPO says nothing of where.
        /// </summary>
        private static async Task<String?> Authorized(HttpClient  Partner,
                                                      String      Version,
                                                      String      Token,
                                                      String?     Location   = null,
                                                      String?     EVSE       = null)
        {

            var where  = Location is null
                             ? null
                             : new StringContent(new JObject(
                                                     new JProperty("location_id",  Location),
                                                     new JProperty("evse_uids",    EVSE is null ? new JArray() : new JArray(EVSE))
                                                 ).ToString(), Encoding.UTF8, "application/json");

            var answer = await Partner.PostAsync($"/ext/v{Version}/emsp/tokens/{Token}/authorize?type=APP_USER", where);
            var text   = await answer.Content.ReadAsStringAsync();

            try
            {
                return JObject.Parse(text)["data"]?.Value<String>("allowed") ?? $"{(Int32) answer.StatusCode} {text}";
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return $"{(Int32) answer.StatusCode} {text}";
            }

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

        #region (private static) IssuedTicket(HTTP)

        /// <summary>
        /// A ticket for this EMSP from now for a day, made with a fresh ticket
        /// key and an account key of the driver's, and signed by this EMSP:
        /// the ticket key, the ticket as it came back, its id.
        /// </summary>
        private static async Task<(ECDsa Key, Byte[] Ticket, String Id)> IssuedTicket(HttpClient HTTP)
        {

            using var accountKey  = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var csr               = new CertificateRequest("CN=whoever", accountKey, HashAlgorithmName.SHA256).CreateSigningRequestPem();
            var certified         = JObject.Parse(await (await HTTP.PostAsync("/api/v1/account-keys", JSONBody(new JProperty("csr", csr)))).Content.ReadAsStringAsync());
            var accountKeyId      = Convert.FromHexString(certified["accountCertificate"]!.Value<String>("id")!);

            var ticketKey         = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var now               = DateTimeOffset.UtcNow;

            var ticket            = new ChargingTicket(
                                        RandomNumberGenerator.GetBytes(16),
                                        "DE*GDF",
                                        COSEKey.From((ECPublicKeyParameters) Public(ticketKey)),
                                        DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds()),
                                        DateTimeOffset.FromUnixTimeSeconds(now.AddDays(1).ToUnixTimeSeconds())
                                    );

            var request           = COSESign.Sign(ticket.Encode(),
                                                  Private(ticketKey),
                                                  ChargingTicket.SignatureHeader(ChargingTicket.TicketKeyId),
                                                  BodyProtectedHeader: ChargingTicket.BodyHeader).
                                        AddSignature(Private(accountKey), ChargingTicket.SignatureHeader(accountKeyId));

            var answer            = await HTTP.PostAsync("/api/v1/tickets", JSONBody(new JProperty("request", Convert.ToBase64String(request.ToByteArray()))));
            var body              = JObject.Parse(await answer.Content.ReadAsStringAsync());

            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.Created), body.ToString());

            return (ticketKey, Convert.FromBase64String(body.Value<String>("ticket")!), ticket.IdText);

        }

        #endregion

        #region (private static) StartRequest(Ticket, TicketKey, EVSE, At = null) / StopRequest(Ticket, TicketKey, Start)

        /// <summary>A start request as the phone makes one, signed by the ticket's key.</summary>
        private static Byte[] StartRequest(Byte[]           Ticket,
                                           ECDsa            TicketKey,
                                           String           EVSE,
                                           DateTimeOffset?  At   = null)

            => Signed(TicketKey, TicketRequest.StartContentType, [
                   new ("typ",     TicketRequest.StartType),
                   new ("v",       TicketRequest.Version),
                   new ("ticket",  SHA256.HashData(Ticket)),
                   new ("evse",    EVSE),
                   new ("at",      (At ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds()),
                   new ("nonce",   RandomNumberGenerator.GetBytes(16))
               ]);

        /// <summary>A stop request as the phone makes one, naming the countersigned start.</summary>
        private static Byte[] StopRequest(Byte[]  Ticket,
                                          ECDsa   TicketKey,
                                          Byte[]  Start)

            => Signed(TicketKey, TicketRequest.StopContentType, [
                   new ("typ",     TicketRequest.StopType),
                   new ("v",       TicketRequest.Version),
                   new ("ticket",  SHA256.HashData(Ticket)),
                   new ("start",   SHA256.HashData(Start)),
                   new ("at",      DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                   new ("nonce",   RandomNumberGenerator.GetBytes(16))
               ]);

        private static Byte[] Signed(ECDsa                                        TicketKey,
                                     String                                       ContentType,
                                     List<KeyValuePair<CBORValue, CBORValue>>     Payload)

            => COSESign.Sign(CBORValue.FromMap(Payload).ToByteArray(new CBORWriterOptions { Deterministic = true }),
                             Private(TicketKey),
                             ChargingTicket.SignatureHeader(ChargingTicket.TicketKeyId),
                             BodyProtectedHeader: new COSEHeaders((COSEHeaderLabel.ContentType, CBORValue.FromText(ContentType)))).
                   ToByteArray();

        #endregion

        #region (private static) Public(Key) / Private(Key)

        /// <summary>A key made here, the way Styx's COSE takes it.</summary>
        private static AsymmetricKeyParameter Public(ECDsa Key)
            => PublicKeyFactory.CreateKey(Key.ExportSubjectPublicKeyInfo());

        private static AsymmetricKeyParameter Private(ECDsa Key)
            => PrivateKeyFactory.CreateKey(Key.ExportPkcs8PrivateKey());

        #endregion

    }

}
