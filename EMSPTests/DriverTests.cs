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
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.PKI;

using cloud.charging.open.protocols.ISO15118.PKI;

using cloud.charging.open.EMSP.Drivers;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// The drivers' side: where a driver lands when signing up, the RFID
    /// cards a driver brings and the operator lets in, what a driver charged,
    /// and a driver leaving.
    /// </summary>
    /// <remarks>
    /// Over HTTP, the way a browser does it, with a CPO on 2.1.1 and one on
    /// 2.2.1 pushing what was charged the way a CPO does it.
    /// </remarks>
    public class DriverTests : AEMSPTests
    {

        #region Data

        /// <summary>
        /// A card's UID as a reader prints it, and as the EMSP keeps it.
        /// </summary>
        private const String  Typed  = "04:a2:b3:c4:d5:e6:f7";
        private const String  UID    = "04A2B3C4D5E6F7";

        #endregion


        #region ADriverSignsUpIntoTheDriversOrganization()

        /// <summary>
        /// A driver lands in EVDrivers, apart from the organization whoever
        /// runs this EMSP is in, and may ask for contracts and bring cards -
        /// and comes back through the sign-in tomorrow.
        /// </summary>
        [Test]
        public async Task ADriverSignsUpIntoTheDriversOrganization()
        {

            using var driver   = await SignedUp("alice");

            var me             = await GetJSON(driver, "/api/v1/auth/me");

            Assert.That(EMSP.ExtAPI.TryGetUser(User_Id.Parse("alice"), out var alice), Is.True);

            var organizations  = alice!.User2Organization_OutEdges.Select(edge => edge.Target.Id.ToString()).ToArray();

            using var tomorrow = await SignedInAs("alice", "correct horse battery staple");

            Assert.Multiple(() => {
                Assert.That(me["roles"]?.Values<String>(),        Is.EquivalentTo(new[] { "driver" }));
                Assert.That(me["permissions"]?.Values<String>(),  Is.EquivalentTo(new[] { "contracts:run", "tokens:run", "tickets:run" }));
                Assert.That(organizations,                        Is.EqualTo(new[] { EMSP.DriverOrganization }));
            });

        }

        #endregion

        #region ACardWaitsUntilTheOperatorLetsItIn()

        /// <summary>
        /// A card a driver brings opens nothing until the operator lets it in;
        /// then it is a token on every OCPI version, an RFID card, valid.
        /// </summary>
        [Test]
        public async Task ACardWaitsUntilTheOperatorLetsItIn()
        {

            using var driver  = await SignedUp("alice");
            using var root    = await SignedIn();

            var entered       = await driver.PostAsync("/api/v1/cards", JSONBody(new JProperty("uid", Typed), new JProperty("label", "Blue keyring")));
            var enteredBody   = JObject.Parse(await entered.Content.ReadAsStringAsync());

            var tokensBefore  = EMSP.TokenCount;
            var approvedByMe  = await driver.PostAsync($"/api/v1/cards/{UID}/approve", JSONBody());
            var waiting       = await GetJSON(root, "/api/v1/cards");

            var approved      = await root.PostAsync($"/api/v1/cards/{UID}/approve", JSONBody());
            var approvedBody  = JObject.Parse(await approved.Content.ReadAsStringAsync());

            var mine          = await GetJSON(driver, "/api/v1/cards");
            var tokens        = (await GetJSON(root, "/api/v1/ocpi/tokens"))["tokens"]!.OfType<JObject>().Where(token => token.Value<String>("uid") == UID).ToArray();

            Assert.Multiple(() => {

                Assert.That(entered.StatusCode,                                   Is.EqualTo(HttpStatusCode.Created), enteredBody.ToString());
                Assert.That(enteredBody["card"]?.Value<String>("uid"),            Is.EqualTo(UID), "kept as the card sends it, without the colons");
                Assert.That(enteredBody["card"]?.Value<String>("state"),          Is.EqualTo("requested"));
                Assert.That(enteredBody["card"]?.Value<String>("label"),          Is.EqualTo("Blue keyring"));
                Assert.That(tokensBefore,                                         Is.Zero, "a card asked for is no token yet");

                Assert.That(approvedByMe.StatusCode,                              Is.EqualTo(HttpStatusCode.Forbidden), "a driver lets in no card, their own neither");

                Assert.That(waiting.Value<Boolean>("everyone"),                   Is.True);
                Assert.That(waiting.Value<Int32>("waiting"),                      Is.EqualTo(1));
                Assert.That(waiting["cards"]?[0]?.Value<String>("owner"),         Is.EqualTo("alice"));

                Assert.That(approved.StatusCode,                                  Is.EqualTo(HttpStatusCode.OK), approvedBody.ToString());
                Assert.That(mine.Value<Boolean>("everyone"),                      Is.False);
                Assert.That(mine["cards"]?[0]?.Value<String>("state"),            Is.EqualTo("active"));
                Assert.That(mine["cards"]?[0]?.Value<String>("decidedBy"),        Is.EqualTo("root"));
                Assert.That(mine["cards"]?[0]?.Value<String>("contractId"),       Is.EqualTo($"DE-GDF-C{UID}"));

                Assert.That(tokens.Select(token => token.Value<String>("version")), Is.EquivalentTo(EMSP.OCPIVersions.Select(version => version.Label)));
                Assert.That(tokens.Select(token => token.Value<String>("type")),    Is.All.EqualTo("RFID"));
                Assert.That(tokens.Select(token => token.Value<Boolean>("valid")),  Is.All.True);

            });

        }

        #endregion

        #region ADriverBlocksTheirCardAndItsTokenWithIt()

        /// <summary>
        /// A card blocked is a token invalid and BLOCKED on every version - what
        /// a partner reads of it and what it is told when it asks - and let
        /// charge again, valid and ALLOWED.
        /// </summary>
        [Test]
        public async Task ADriverBlocksTheirCardAndItsTokenWithIt()
        {

            using var driver  = await SignedUp("alice");
            using var root    = await SignedIn();

            await ACardLetIn(driver, root);

            var blocked       = await driver.PostAsync($"/api/v1/cards/{UID}/block", JSONBody());
            var whileBlocked  = await TokensOf(root, UID);
            var again         = await driver.PostAsync($"/api/v1/cards/{UID}/block", JSONBody());

            var unblocked     = await driver.PostAsync($"/api/v1/cards/{UID}/unblock", JSONBody());
            var afterwards    = await TokensOf(root, UID);

            Assert.Multiple(() => {

                Assert.That(blocked.StatusCode,                                         Is.EqualTo(HttpStatusCode.OK));
                Assert.That(whileBlocked.Select(token => token.Value<Boolean>("valid")), Is.All.False);
                Assert.That(whileBlocked.Select(token => token.Value<String>("status")), Is.All.EqualTo("BLOCKED"));
                Assert.That(whileBlocked,                                               Has.Length.EqualTo(EMSP.OCPIVersions.Count));
                Assert.That(again.StatusCode,                                           Is.EqualTo(HttpStatusCode.Conflict), "blocked already");

                Assert.That(unblocked.StatusCode,                                       Is.EqualTo(HttpStatusCode.OK));
                Assert.That(afterwards.Select(token => token.Value<Boolean>("valid")),   Is.All.True);
                Assert.That(afterwards.Select(token => token.Value<String>("status")),   Is.All.EqualTo("ALLOWED"));

            });

        }

        #endregion

        #region NobodyElseSeesOrTouchesADriversCard()

        /// <summary>
        /// Another driver neither sees a card nor blocks it, takes it away or
        /// enters it again - and is told it is not theirs the way a card that
        /// does not exist is.
        /// </summary>
        [Test]
        public async Task NobodyElseSeesOrTouchesADriversCard()
        {

            using var alice   = await SignedUp("alice");
            using var bob     = await SignedUp("bobby");
            using var root    = await SignedIn();

            await ACardLetIn(alice, root);

            var bobs          = await GetJSON(bob, "/api/v1/cards");
            var blocked       = await bob.PostAsync($"/api/v1/cards/{UID}/block", JSONBody());
            var removed       = await bob.DeleteAsync($"/api/v1/cards/{UID}");
            var nowhere       = await bob.DeleteAsync("/api/v1/cards/0102030405060708");
            var enteredAgain  = await bob.PostAsync("/api/v1/cards", JSONBody(new JProperty("uid", UID)));
            var stillAlices   = await GetJSON(alice, "/api/v1/cards");

            Assert.Multiple(() => {
                Assert.That(bobs["cards"]?.Count(),                          Is.Zero);
                Assert.That(blocked.StatusCode,                              Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(removed.StatusCode,                              Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(nowhere.StatusCode,                              Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(enteredAgain.StatusCode,                         Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(stillAlices["cards"]?[0]?.Value<String>("state"), Is.EqualTo("active"));
            });

        }

        #endregion

        #region ATurnedDownCardGetsNoTokenAndItsDriverSeesWhy()

        [Test]
        public async Task ATurnedDownCardGetsNoTokenAndItsDriverSeesWhy()
        {

            using var driver  = await SignedUp("alice");
            using var root    = await SignedIn();

            await driver.PostAsync("/api/v1/cards", JSONBody(new JProperty("uid", UID)));

            var rejected      = await root.PostAsync($"/api/v1/cards/{UID}/reject", JSONBody(new JProperty("reason", "Not a card of ours.")));
            var mine          = await GetJSON(driver, "/api/v1/cards");
            var lateApproval  = await root.PostAsync($"/api/v1/cards/{UID}/approve", JSONBody());
            var removed       = await driver.DeleteAsync($"/api/v1/cards/{UID}");
            var gone          = await GetJSON(driver, "/api/v1/cards");

            Assert.Multiple(() => {
                Assert.That(rejected.StatusCode,                          Is.EqualTo(HttpStatusCode.OK));
                Assert.That(mine["cards"]?[0]?.Value<String>("state"),    Is.EqualTo("rejected"));
                Assert.That(mine["cards"]?[0]?.Value<String>("reason"),   Is.EqualTo("Not a card of ours."));
                Assert.That(lateApproval.StatusCode,                      Is.EqualTo(HttpStatusCode.Conflict), "decided already");
                Assert.That(EMSP.TokenCount,                              Is.Zero);
                Assert.That(removed.StatusCode,                           Is.EqualTo(HttpStatusCode.OK));
                Assert.That(gone["cards"]?.Count(),                       Is.Zero);
            });

        }

        #endregion

        #region ACardTakenAwayTakesItsTokenWithIt()

        [Test]
        public async Task ACardTakenAwayTakesItsTokenWithIt()
        {

            using var driver  = await SignedUp("alice");
            using var root    = await SignedIn();

            await ACardLetIn(driver, root);

            var tokensBefore  = EMSP.TokenCount;
            var removed       = await driver.DeleteAsync($"/api/v1/cards/{UID}");

            Assert.Multiple(() => {
                Assert.That(tokensBefore,           Is.EqualTo(EMSP.OCPIVersions.Count));
                Assert.That(removed.StatusCode,     Is.EqualTo(HttpStatusCode.OK));
                Assert.That(EMSP.TokenCount,        Is.Zero);
                Assert.That(EMSP.Cards.All,         Is.Empty);
            });

        }

        #endregion

        #region AUIDThatIsNoneIsRefused(Typed)

        [TestCase("")]
        [TestCase("xyz12345")]
        [TestCase("0102")]
        [TestCase("010203040")]
        [TestCase("0102030405060708091011")]
        public async Task AUIDThatIsNoneIsRefused(String Typed)
        {

            using var driver = await SignedUp("alice");

            var entered = await driver.PostAsync("/api/v1/cards", JSONBody(new JProperty("uid", Typed)));

            Assert.Multiple(() => {
                Assert.That(entered.StatusCode,  Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(EMSP.Cards.All,      Is.Empty);
            });

        }

        #endregion

        #region ADriverSeesWhatTheyChargedAndNothingElse()

        /// <summary>
        /// A session a CPO on 2.2.1 pushed with the driver's card in it, and a
        /// charge detail record a CPO on 2.1.1 pushed with the card's contract
        /// in it, are the driver's; a session of another card is not.
        /// </summary>
        [Test]
        public async Task ADriverSeesWhatTheyChargedAndNothingElse()
        {

            using var driver  = await SignedUp("alice");
            using var root    = await SignedIn();

            await ACardLetIn(driver, root);

            var nothingYet    = await GetJSON(driver, "/api/v1/charging");

            await PartnerAdded("2.1.1", "G21", "partner-token-G21");
            await PartnerAdded("2.2.1", "G22", "partner-token-G22");

            using (var cpo = PartnerOn("2.2.1", "partner-token-G22"))
            {
                await Pushed(cpo.PutAsync("/ext/v2.2.1/emsp/sessions/DE/G22/S0001", JSON(SessionJSON("S0001", UID,                $"DE-GDF-C{UID}"))));
                await Pushed(cpo.PutAsync("/ext/v2.2.1/emsp/sessions/DE/G22/S0002", JSON(SessionJSON("S0002", "0A0B0C0D0E0F0102", "DE-GDF-C0A0B0C0D0E0F0102"))));
            }

            using (var cpo = PartnerOn("2.1.1", "partner-token-G21"))
                await Pushed(cpo.PostAsync("/ext/v2.1.1/emsp/cdrs", JSON(CDRJSON("CDR0001", $"DE-GDF-C{UID}"))));

            var charged       = await GetJSON(driver, "/api/v1/charging");
            var somebodyElse  = await GetJSON(await SignedUp("bobby"), "/api/v1/charging");

            var session       = charged["sessions"]?[0] as JObject;
            var cdr           = charged["cdrs"]?[0] as JObject;

            Assert.Multiple(() => {

                Assert.That(nothingYet["sessions"]?.Count(),      Is.Zero);
                Assert.That(nothingYet.Value<Int32>("cards"),      Is.EqualTo(1));

                Assert.That(charged["sessions"]?.Count(),          Is.EqualTo(1), charged.ToString());
                Assert.That(session?.Value<String>("id"),          Is.EqualTo("S0001"));
                Assert.That(session?.Value<String>("version"),     Is.EqualTo("2.2.1"));
                Assert.That(session?.Value<String>("status"),      Is.EqualTo("ACTIVE"));
                Assert.That(session?.Value<Decimal>("kWh"),        Is.EqualTo(7.5m));
                Assert.That(session?.Value<String>("location"),    Is.EqualTo("LOC0001"));
                Assert.That(session?.Value<String>("evse"),        Is.EqualTo("DE*G22*E0001"));
                Assert.That(session?.Value<String>("token"),       Is.EqualTo(UID));

                Assert.That(charged["cdrs"]?.Count(),              Is.EqualTo(1), charged.ToString());
                Assert.That(cdr?.Value<String>("id"),              Is.EqualTo("CDR0001"));
                Assert.That(cdr?.Value<String>("version"),         Is.EqualTo("2.1.1"));
                Assert.That(cdr?.Value<Decimal>("kWh"),            Is.EqualTo(12.5m));
                Assert.That(cdr?.Value<Decimal>("cost"),           Is.EqualTo(4.00m));
                Assert.That(cdr?.Value<String>("currency"),        Is.EqualTo("EUR"));
                Assert.That(cdr?.Value<String>("address"),         Is.EqualTo("Biberweg 18, Jena"));
                Assert.That(cdr?["end"]?.ToObject<DateTimeOffset>(), Is.EqualTo(DateTimeOffset.Parse("2026-09-20T11:00:00Z")));

                Assert.That(somebodyElse["sessions"]?.Count(),     Is.Zero);
                Assert.That(somebodyElse["cdrs"]?.Count(),         Is.Zero);

            });

        }

        #endregion

        #region ADriverDeletesTheirAccount()

        /// <summary>
        /// A driver leaving takes their contracts and cards with them: the
        /// tokens are gone, the contract taken back, the account deleted - and
        /// only with the username typed again, and only for a driver.
        /// </summary>
        [Test]
        public async Task ADriverDeletesTheirAccount()
        {

            using var driver  = await SignedUp("alice");
            using var root    = await SignedIn();

            await ACardLetIn(driver, root);

            var contract      = await driver.PostAsync("/api/v1/contracts", JSONBody(new JProperty("csr", CSR())));
            Assert.That(contract.StatusCode, Is.EqualTo(HttpStatusCode.Created), await contract.Content.ReadAsStringAsync());

            var notConfirmed  = await driver.PostAsync("/api/v1/me/delete", JSONBody(new JProperty("username", "bob")));
            var rootLeaving   = await root.  PostAsync("/api/v1/me/delete", JSONBody(new JProperty("username", "root")));
            var deleted       = await driver.PostAsync("/api/v1/me/delete", JSONBody(new JProperty("username", "alice")));
            var afterwards    = await driver.GetAsync ("/api/v1/cards");

            Assert.Multiple(() => {
                Assert.That(notConfirmed.StatusCode,                                     Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(rootLeaving. StatusCode,                                     Is.EqualTo(HttpStatusCode.Forbidden), "whoever looks after the EMSP leaves by another hand");
                Assert.That(deleted.     StatusCode,                                     Is.EqualTo(HttpStatusCode.OK));
                Assert.That(afterwards.  StatusCode,                                     Is.EqualTo(HttpStatusCode.Unauthorized), "the session went with the account");
                Assert.That(EMSP.ExtAPI.Users.Select(user => user.Id.ToString()),        Is.EqualTo(new[] { "root" }));
                Assert.That(EMSP.TokenCount,                                             Is.Zero, "neither the card's nor the contract's token is left");
                Assert.That(EMSP.Cards.All,                                              Is.Empty);
                Assert.That(EMSP.Contracts.All.Select(contract => contract.IsRevoked),   Is.All.True);
            });

        }

        #endregion

        #region TheCardsAreThereAfterARestart()

        [Test]
        public void TheCardsAreThereAfterARestart()
        {

            var directory  = Path.Combine(Directory, "cards-again");
            var first      = new DriverCardRegistry(directory);
            var card       = new DriverCard(UID, "alice", "Blue keyring", DriverCardState.Requested, DateTimeOffset.Parse("2026-10-09T12:00:00Z"));

            Assert.That(first.TryAdd(card, out var error, out _), Is.True, error);
            Assert.That(first.TryReplace(card, card with { State = DriverCardState.Blocked, ContractId = $"DE-GDF-C{UID}" }, out error, out _), Is.True, error);

            var again      = new DriverCardRegistry(directory);

            Assert.Multiple(() => {
                Assert.That(again.All,                               Has.Count.EqualTo(1));
                Assert.That(again.All[0].State,                      Is.EqualTo(DriverCardState.Blocked));
                Assert.That(again.All[0].Label,                      Is.EqualTo("Blue keyring"));
                Assert.That(again.All[0].ContractId,                 Is.EqualTo($"DE-GDF-C{UID}"));
                Assert.That(again.All[0].RequestedAt,                Is.EqualTo(card.RequestedAt));
            });

        }

        #endregion


        #region (private) SignedUp(Username)

        /// <summary>
        /// A browser that signed up as somebody, carrying the session the
        /// sign-up handed out.
        /// </summary>
        private async Task<HttpClient> SignedUp(String Username)
        {

            var http      = Anonymous();

            var response  = await http.PostAsJsonAsync(
                                      "/ext/auth/signup",
                                      new {
                                          username     = Username,
                                          email        = $"{Username}@example.org",
                                          password     = "correct horse battery staple",
                                          displayName  = Username
                                      }
                                  );

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                        $"Signing up as '{Username}' failed: {await response.Content.ReadAsStringAsync()}");

            return http;

        }

        #endregion

        #region (private) ACardLetIn(Driver, Root)

        /// <summary>
        /// The card of this fixture, entered by the driver and let in by the
        /// operator.
        /// </summary>
        private static async Task ACardLetIn(HttpClient Driver, HttpClient Root)
        {

            var entered  = await Driver.PostAsync("/api/v1/cards", JSONBody(new JProperty("uid", Typed)));
            Assert.That(entered.StatusCode, Is.EqualTo(HttpStatusCode.Created), await entered.Content.ReadAsStringAsync());

            var approved = await Root.PostAsync($"/api/v1/cards/{UID}/approve", JSONBody());
            Assert.That(approved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await approved.Content.ReadAsStringAsync());

        }

        #endregion

        #region (private static) TokensOf(Root, UID)

        private static async Task<JObject[]> TokensOf(HttpClient Root, String UID)

            => [.. (await GetJSON(Root, "/api/v1/ocpi/tokens"))["tokens"]!.
                       OfType<JObject>().
                       Where(token => token.Value<String>("uid") == UID)];

        #endregion

        #region (private) PartnerAdded(Version, PartyId, Token) / PartnerOn(Version, Token)

        private async Task PartnerAdded(String Version, String PartyId, String Token)
        {

            var added = await EMSP.AddRemotePartyAsync(
                                  new JObject(
                                      new JProperty("version",      Version),
                                      new JProperty("countryCode",  "DE"),
                                      new JProperty("partyId",      PartyId),
                                      new JProperty("role",         "CPO"),
                                      new JProperty("name",         $"Test CPO {PartyId}"),
                                      new JProperty("ourToken",     Token)
                                  )
                              );

            Assert.That(added.Success, Is.True, $"OCPI {Version}: {added.Message}");

        }

        /// <summary>
        /// A CPO calling this EMSP with the token it was given - as it is on
        /// 2.1.1, in Base64 from 2.2 on.
        /// </summary>
        private HttpClient PartnerOn(String Version, String Token)
        {

            var http = new HttpClient { BaseAddress = new Uri(BaseURL) };

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                           "Token",
                                                           Version == "2.1.1"
                                                               ? Token
                                                               : Convert.ToBase64String(Encoding.UTF8.GetBytes(Token))
                                                       );

            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return http;

        }

        #endregion

        #region (private static) Pushed(Request)

        private static async Task Pushed(Task<HttpResponseMessage> Request)
        {

            var response  = await Request;
            var text      = await response.Content.ReadAsStringAsync();

            Assert.That(response.IsSuccessStatusCode, Is.True, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} answered {(Int32) response.StatusCode}: {text}");
            Assert.That(JObject.Parse(text).Value<Int32>("status_code"), Is.EqualTo(1000), text);

        }

        #endregion

        #region (private static) JSON(...) / SessionJSON(...) / CDRJSON(...)

        private static StringContent JSON(JObject JSON)
            => new (JSON.ToString(), Encoding.UTF8, "application/json");

        /// <summary>
        /// A session as OCPI 2.2.1 has a CPO push it: charging, 7.5 kWh so far,
        /// with the token it was started with.
        /// </summary>
        private static JObject SessionJSON(String Id, String TokenUID, String ContractId)

            => new (
                   new JProperty("country_code",     "DE"),
                   new JProperty("party_id",         "G22"),
                   new JProperty("id",               Id),
                   new JProperty("start_date_time",  "2026-10-09T08:00:00Z"),
                   new JProperty("kwh",              7.5),
                   new JProperty("cdr_token",        new JObject(
                       new JProperty("country_code",  "DE"),
                       new JProperty("party_id",      "GDF"),
                       new JProperty("uid",           TokenUID),
                       new JProperty("type",          "RFID"),
                       new JProperty("contract_id",   ContractId)
                   )),
                   new JProperty("auth_method",      "WHITELIST"),
                   new JProperty("location_id",      "LOC0001"),
                   new JProperty("evse_uid",         "DE*G22*E0001"),
                   new JProperty("connector_id",     "1"),
                   new JProperty("currency",         "EUR"),
                   new JProperty("status",           "ACTIVE"),
                   new JProperty("last_updated",     "2026-10-09T08:30:00Z")
               );

        /// <summary>
        /// A charge detail record as OCPI 2.1.1 has a CPO send it, with the
        /// contract it was charged under as its authorization.
        /// </summary>
        private static JObject CDRJSON(String Id, String AuthId)

            => new (
                   new JProperty("country_code",      "DE"),
                   new JProperty("party_id",          "G21"),
                   new JProperty("id",                Id),
                   new JProperty("start_date_time",   "2026-09-20T10:00:00Z"),
                   new JProperty("stop_date_time",    "2026-09-20T11:00:00Z"),
                   new JProperty("auth_id",           AuthId),
                   new JProperty("auth_method",       "WHITELIST"),
                   new JProperty("location",          new JObject(
                       new JProperty("id",            "LOC0001"),
                       new JProperty("type",          "ON_STREET"),
                       new JProperty("address",       "Biberweg 18"),
                       new JProperty("city",          "Jena"),
                       new JProperty("postal_code",   "07749"),
                       new JProperty("country",       "DEU"),
                       new JProperty("coordinates",   new JObject(
                           new JProperty("latitude",  "50.927"),
                           new JProperty("longitude", "11.587")
                       )),
                       new JProperty("evses",         new JArray()),
                       new JProperty("last_updated",  "2026-09-20T09:00:00Z")
                   )),
                   new JProperty("currency",          "EUR"),
                   new JProperty("charging_periods",  new JArray(
                       new JObject(
                           new JProperty("start_date_time", "2026-09-20T10:00:00Z"),
                           new JProperty("dimensions",      new JArray(
                               new JObject(
                                   new JProperty("type",    "ENERGY"),
                                   new JProperty("volume",  12.5)
                               )
                           ))
                       )
                   )),
                   new JProperty("total_cost",        4.00),
                   new JProperty("total_energy",      12.5),
                   new JProperty("total_time",        1.0),
                   new JProperty("last_updated",      "2026-09-20T11:05:00Z")
               );

        #endregion

        #region (private static) CSR()

        /// <summary>
        /// A PKCS#10 request for a key made here, signed with it, as a browser
        /// would send one.
        /// </summary>
        private static String CSR()
        {

            var keyPair = V2GCertificateBuilder.GenerateKeyPair(V2GAlgorithm.EcdsaP256, new SecureRandom());

            return new Pkcs10CertificationRequest(
                       new Asn1SignatureFactory("SHA256withECDSA", keyPair.Private, new SecureRandom()),
                       new X509Name("CN=a driver"),
                       keyPair.Public,
                       null
                   ).ToPEM();

        }

        #endregion

    }

}
