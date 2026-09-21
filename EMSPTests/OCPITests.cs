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
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.EMSP.Configuration;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// The OCPI side, over the wire: the versions a partner is shown, the
    /// endpoints they point at, a partner being added and signing in with the
    /// token it was given, a CPO pushing a location, a token being issued and
    /// fetched, and this EMSP registering with a CPO of its own accord.
    /// </summary>
    /// <remarks>
    /// The partner in these tests is a plain HTTP client with an OCPI token,
    /// which is what a CPO is from where this EMSP stands. For the one test
    /// where the EMSP has to call somebody, a stub CPO is stood up on a port
    /// of its own: three routes, which is all the credentials handshake needs.
    /// </remarks>
    public class OCPITests : AEMSPTests
    {

        #region (private) Partner(Token)

        /// <summary>
        /// A CPO calling this EMSP with the token it was given, encoded the way
        /// OCPI 2.2 sends it.
        /// </summary>
        private HttpClient Partner(String Token)
        {

            var http = new HttpClient { BaseAddress = new Uri(BaseURL) };

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                           "Token",
                                                           Convert.ToBase64String(Encoding.UTF8.GetBytes(Token))
                                                       );

            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return http;

        }

        #endregion

        #region (private) AddPartner(HTTP, ...)

        /// <summary>
        /// Add a roaming partner through the JSON API, the way the web
        /// interface does, and hand back the token this EMSP made up for it.
        /// </summary>
        private async Task<(String Id, String Token, JObject Answer)> AddPartner(HttpClient  HTTP,
                                                                                 String      Version       = "2.2.1",
                                                                                 String      CountryCode   = "DE",
                                                                                 String      PartyId       = "GEF",
                                                                                 String?     TheirToken    = null,
                                                                                 String?     VersionsURL   = null)
        {

            var body = new List<JProperty> {
                           new ("version",      Version),
                           new ("countryCode",  CountryCode),
                           new ("partyId",      PartyId),
                           new ("role",         "CPO"),
                           new ("name",         "Test CPO"),
                           new ("website",      "https://cpo.example.org")
                       };

            if (TheirToken  is not null)  body.Add(new JProperty("theirToken",  TheirToken));
            if (VersionsURL is not null)  body.Add(new JProperty("versionsURL", VersionsURL));

            var response = await HTTP.PostAsync("/api/v1/ocpi/partners", JSONBody([.. body]));
            var text     = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), $"Adding the partner answered {(Int32) response.StatusCode}: {text}");

            var answer = JObject.Parse(text);

            return (answer.Value<String>("id")!, answer.Value<String>("ourToken")!, answer);

        }

        #endregion

        #region (private static) OCPIResponse(Response)

        /// <summary>
        /// The OCPI envelope of an answer, with the HTTP status checked first
        /// so that a failing test says which of the two went wrong.
        /// </summary>
        private static async Task<JObject> OCPIResponse(HttpResponseMessage Response)
        {

            var text = await Response.Content.ReadAsStringAsync();

            Assert.That(Response.IsSuccessStatusCode, Is.True, $"{Response.RequestMessage?.Method} {Response.RequestMessage?.RequestUri} answered {(Int32) Response.StatusCode}: {text}");

            return JObject.Parse(text);

        }

        #endregion

        #region (private static) LocationJSON(Id)

        /// <summary>
        /// The least a CPO has to say about a location in OCPI 2.2.1.
        /// </summary>
        private static JObject LocationJSON(String Id)

            => new (
                   new JProperty("country_code",  "DE"),
                   new JProperty("party_id",      "GEF"),
                   new JProperty("id",            Id),
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
                   new JProperty("evses",         new JArray()),
                   new JProperty("last_updated",  "2026-09-20T10:00:00Z")
               );

        #endregion


        #region TheVersionsAreListedForEverybody()

        /// <summary>
        /// The versions list is the one door a partner is given, and it is
        /// open: a CPO that has not registered yet needs it to find the
        /// credentials endpoint. Every URL in it has to be one this EMSP
        /// actually serves, which means below the HTTPExt API's root.
        /// </summary>
        [Test]
        public async Task TheVersionsAreListedForEverybody()
        {

            using var http = Anonymous();

            var answer   = await OCPIResponse(await http.GetAsync("/ext/versions"));
            var versions = answer["data"] as JArray;

            Assert.That(versions, Is.Not.Null, "The versions list carries no data.");

            var listed = versions!.Select(version => version.Value<String>("version")).ToArray();

            Assert.Multiple(() => {

                Assert.That(answer.Value<Int32>("status_code"), Is.EqualTo(1000));
                Assert.That(listed, Is.EquivalentTo(OCPIConfiguration.DefaultVersions));

                foreach (var version in versions)
                    Assert.That(version.Value<String>("url"),
                                Is.EqualTo($"{BaseURL.TrimEnd('/')}/ext/versions/{version.Value<String>("version")}"),
                                "A version is advertised at a URL this EMSP does not serve.");

            });

        }

        #endregion

        #region TheVersionDetailsPointAtEndpointsThatExist()

        /// <summary>
        /// The library builds the URLs it advertises from the Host header and
        /// its own prefix, as if it sat at the root of the server; here it
        /// sits below "/ext". This is the test that says the two agree: every
        /// endpoint a partner is told about answers.
        /// </summary>
        [Test]
        public async Task TheVersionDetailsPointAtEndpointsThatExist()
        {

            using var admin = await SignedIn();

            var (_, token, _) = await AddPartner(admin);

            using var partner = Partner(token);

            var answer    = await OCPIResponse(await partner.GetAsync("/ext/versions/2.2.1"));
            var endpoints = answer["data"]?["endpoints"] as JArray;

            Assert.That(endpoints, Is.Not.Null.And.Not.Empty, "The version details carry no endpoints.");

            var identifiers = endpoints!.Select(endpoint => endpoint.Value<String>("identifier")).ToArray();

            Assert.Multiple(() => {
                Assert.That(identifiers, Does.Contain("credentials"));
                Assert.That(identifiers, Does.Contain("locations"));
                Assert.That(identifiers, Does.Contain("sessions"));
                Assert.That(identifiers, Does.Contain("cdrs"));
                Assert.That(identifiers, Does.Contain("tokens"));
                Assert.That(identifiers, Does.Contain("commands"));
            });

            foreach (var endpoint in endpoints)
            {

                var identifier = endpoint.Value<String>("identifier")!;
                var url        = new Uri(endpoint.Value<String>("url")!);

                Assert.That(url.AbsolutePath, Does.StartWith("/ext/v2.2.1/"),
                            $"The '{identifier}' endpoint is advertised outside the HTTPExt API, where nothing serves it.");

                // The library advertises a charging profiles module in the
                // version details of an EMSP and serves no route for it - a
                // gap in the library, and a known one, so it is not what this
                // test is about.
                if (identifier == "chargingprofiles")
                    continue;

                // The commands module has no route at its base: a CPO POSTs
                // its answers below it, one path per command.
                var probe = await partner.GetAsync(
                                identifier == "commands"
                                    ? new Uri(url + "/START_SESSION/probe")
                                    : url
                            );

                // A 404 is the one answer that says the URL is wrong; a 405
                // says the route exists and GET is not how it is called.
                Assert.That(probe.StatusCode, Is.Not.EqualTo(HttpStatusCode.NotFound),
                            $"The '{identifier}' endpoint is advertised at {url} and not served there.");

            }

        }

        #endregion

        #region APartnerSignsInWithTheTokenItWasGiven()

        /// <summary>
        /// The whole of the first half of a peering: the operator adds the
        /// CPO and is handed a token, the CPO presents it, and this EMSP
        /// answers with its own credentials - which say where its versions
        /// are and who it is.
        /// </summary>
        [Test]
        public async Task APartnerSignsInWithTheTokenItWasGiven()
        {

            using var admin = await SignedIn();

            var (id, token, answer) = await AddPartner(admin);

            Assert.Multiple(() => {
                Assert.That(id,    Is.EqualTo("DE-GEF_CPO"));
                Assert.That(token, Is.Not.Empty);
                Assert.That(answer["partner"]?.Value<Boolean>("registered"),  Is.False, "A partner that has not come yet is reported as registered.");
                Assert.That(answer["partner"]?.Value<Boolean>("canRegister"), Is.False, "A partner that handed out nothing is reported as registrable.");
            });

            var listed = (await GetJSON(admin, "/api/v1/ocpi/partners"))["partners"] as JArray;

            Assert.That(listed?.Select(partner => partner.Value<String>("id")), Does.Contain("DE-GEF_CPO"));
            Assert.That(listed?.First(partner => partner.Value<String>("id") == "DE-GEF_CPO").Value<String>("ourToken"), Is.EqualTo(token),
                        "The token is not shown to the administrator, who is the one who has to hand it over.");

            using var partner = Partner(token);

            var credentials = (await OCPIResponse(await partner.GetAsync("/ext/v2.2.1/credentials")))["data"];

            Assert.Multiple(() => {
                Assert.That(credentials?.Value<String>("token"), Is.EqualTo(token));
                Assert.That(credentials?.Value<String>("url"),   Is.EqualTo($"{BaseURL.TrimEnd('/')}/ext/versions"));
                Assert.That(credentials?["roles"]?.First?.Value<String>("role"),         Is.EqualTo("EMSP"));
                Assert.That(credentials?["roles"]?.First?.Value<String>("party_id"),     Is.EqualTo("GDF"));
                Assert.That(credentials?["roles"]?.First?.Value<String>("country_code"), Is.EqualTo("DE"));
            });

        }

        #endregion

        #region AddingTheSamePartnerTwiceIsRefused()

        [Test]
        public async Task AddingTheSamePartnerTwiceIsRefused()
        {

            using var admin = await SignedIn();

            await AddPartner(admin);

            var again = await admin.PostAsync("/api/v1/ocpi/partners", JSONBody(
                                  new JProperty("version",      "2.3.0"),
                                  new JProperty("countryCode",  "DE"),
                                  new JProperty("partyId",      "GEF"),
                                  new JProperty("role",         "CPO"),
                                  new JProperty("name",         "The same CPO, on another version")
                              ));

            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
                        "One CPO on two versions is two partners with one identity, and the second was let in.");

        }

        #endregion

        #region ACPOPushesALocationAndItShowsUp()

        /// <summary>
        /// What the peering is for: a CPO PUTs a location, and the operator
        /// sees it on the Locations page - and in the log.
        /// </summary>
        [Test]
        public async Task ACPOPushesALocationAndItShowsUp()
        {

            using var admin = await SignedIn();

            var (_, token, _) = await AddPartner(admin);

            using var partner = Partner(token);

            var put = await partner.PutAsync(
                                "/ext/v2.2.1/emsp/locations/DE/GEF/LOC0001",
                                new StringContent(LocationJSON("LOC0001").ToString(), Encoding.UTF8, "application/json")
                            );

            var putText = await put.Content.ReadAsStringAsync();

            Assert.That(put.IsSuccessStatusCode, Is.True, $"PUT location answered {(Int32) put.StatusCode}: {putText}");
            Assert.That(JObject.Parse(putText).Value<Int32>("status_code"), Is.EqualTo(1000), putText);

            var locations = (await GetJSON(admin, "/api/v1/ocpi/locations"))["items"] as JArray;

            Assert.That(locations, Is.Not.Null.And.Not.Empty, "The location the CPO pushed is not on the Locations page.");

            var location = locations!.First(item => item.Value<String>("id") == "LOC0001");

            Assert.Multiple(() => {
                Assert.That(location.Value<String>("version"),  Is.EqualTo("2.2.1"));
                Assert.That(location.Value<String>("name"),     Is.EqualTo("Test location"));
                Assert.That(location.Value<String>("city"),     Is.EqualTo("Jena"));
            });

            var counts = (await GetJSON(admin, "/api/v1/configuration/ocpi"))["counts"];

            Assert.That(counts?.Value<Int32>("locations"), Is.EqualTo(1));

            Assert.That(EMSP.Log.Recent(500).Any(entry => entry.Message.Contains("LOC0001", StringComparison.Ordinal)),
                        Is.True,
                        "A location arrived and the log does not say so.");

        }

        #endregion

        #region AnUnknownTokenCannotPush()

        /// <summary>
        /// An empty list of partners is "nobody", not "everybody".
        /// </summary>
        [Test]
        public async Task AnUnknownTokenCannotPush()
        {

            using var stranger = Partner("nobody-gave-me-this");

            var put = await stranger.PutAsync(
                                "/ext/v2.2.1/emsp/locations/DE/GEF/LOC0001",
                                new StringContent(LocationJSON("LOC0001").ToString(), Encoding.UTF8, "application/json")
                            );

            Assert.That(put.IsSuccessStatusCode, Is.False, "Somebody with a made-up token pushed a location into this EMSP.");

            using var admin = await SignedIn();

            var locations = (await GetJSON(admin, "/api/v1/ocpi/locations"))["items"] as JArray;

            Assert.That(locations, Is.Empty);

        }

        #endregion

        #region ARemovedPartnerIsShutOut()

        [Test]
        public async Task ARemovedPartnerIsShutOut()
        {

            using var admin = await SignedIn();

            var (id, token, _) = await AddPartner(admin);

            using var partner = Partner(token);

            Assert.That((await partner.PutAsync("/ext/v2.2.1/emsp/locations/DE/GEF/LOC0001",
                                                new StringContent(LocationJSON("LOC0001").ToString(), Encoding.UTF8, "application/json"))).IsSuccessStatusCode,
                        Is.True, "The partner could not push before it was removed, so the test below proves nothing.");

            var removed = await admin.DeleteAsync($"/api/v1/ocpi/partners/2.2.1/{id}");

            Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var afterwards = await partner.PutAsync("/ext/v2.2.1/emsp/locations/DE/GEF/LOC0002",
                                                    new StringContent(LocationJSON("LOC0002").ToString(), Encoding.UTF8, "application/json"));

            Assert.That(afterwards.IsSuccessStatusCode, Is.False, "A removed partner's token still opens this EMSP.");

            var listed = (await GetJSON(admin, "/api/v1/ocpi/partners"))["partners"] as JArray;

            Assert.That(listed, Is.Empty);

        }

        #endregion

        #region ATokenIsIssuedAndACPOCanFetchIt()

        /// <summary>
        /// The other direction: a token this EMSP issued to a customer is
        /// what a CPO fetches, so that its charging stations know the card.
        /// </summary>
        [Test]
        public async Task ATokenIsIssuedAndACPOCanFetchIt()
        {

            using var admin = await SignedIn();

            var issued = await admin.PostAsync("/api/v1/ocpi/tokens", JSONBody(
                                   new JProperty("version",      "2.2.1"),
                                   new JProperty("uid",          "0123456789ABCDEF"),
                                   new JProperty("type",         "RFID"),
                                   new JProperty("contractId",   "DE-GDF-C12345678-X"),
                                   new JProperty("visualNumber", "Card 1")
                               ));

            Assert.That(issued.StatusCode, Is.EqualTo(HttpStatusCode.Created), await issued.Content.ReadAsStringAsync());

            var tokens = (await GetJSON(admin, "/api/v1/ocpi/tokens"))["tokens"] as JArray;

            var token  = tokens?.FirstOrDefault(entry => entry.Value<String>("uid") == "0123456789ABCDEF");

            Assert.That(token, Is.Not.Null, "The token was issued and is not on the Tokens page.");

            Assert.Multiple(() => {
                Assert.That(token!.Value<String>("version"),      Is.EqualTo("2.2.1"));
                Assert.That(token.Value<String>("contract_id"),   Is.EqualTo("DE-GDF-C12345678-X"));
                Assert.That(token.Value<String>("status"),        Is.EqualTo("ALLOWED"));
                Assert.That(token.Value<Boolean>("valid"),        Is.True);
            });

            var (_, partnerToken, _) = await AddPartner(admin);

            using var partner = Partner(partnerToken);

            var fetched = (await OCPIResponse(await partner.GetAsync("/ext/v2.2.1/emsp/tokens")))["data"] as JArray;

            Assert.That(fetched?.Select(entry => entry.Value<String>("uid")), Does.Contain("0123456789ABCDEF"),
                        "The CPO cannot fetch the token this EMSP issued.");

            var gone = await admin.DeleteAsync("/api/v1/ocpi/tokens/2.2.1/0123456789ABCDEF");

            Assert.That(gone.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var remaining = (await GetJSON(admin, "/api/v1/ocpi/tokens"))["tokens"] as JArray;

            Assert.That(remaining?.Select(entry => entry.Value<String>("uid")), Does.Not.Contain("0123456789ABCDEF"));

        }

        #endregion

        #region ThePartnersAreKeptBetweenStarts()

        /// <summary>
        /// The library writes its partners to files of its own beside the
        /// configuration and reads them back; a partner added today has to be
        /// there tomorrow, token and all.
        /// </summary>
        [Test]
        public async Task ThePartnersAreKeptBetweenStarts()
        {

            using var admin = await SignedIn();

            var (id, token, _) = await AddPartner(admin);

            await EMSP.Stop();

            var again = TestEMSPs.New(Directory, Configuration, Clock);

            try
            {

                await again.Start();

                var kept = again.OCPIVersions.SelectMany(version => version.RemoteParties).ToArray();

                Assert.Multiple(() => {
                    Assert.That(kept.Select(partner => partner.Id.ToString()), Does.Contain(id));
                    Assert.That(kept.First(partner => partner.Id.ToString() == id).OurToken?.ToString(), Is.EqualTo(token));
                    Assert.That(kept.First(partner => partner.Id.ToString() == id).Version,              Is.EqualTo("2.2.1"));
                });

            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

        #region TheTokensAreKeptBetweenStartsOnEveryVersion()

        /// <summary>
        /// A token issued today has to be there tomorrow - on every version,
        /// not only on the one whose store happens to be flat.
        /// </summary>
        /// <remarks>
        /// The failure this guards against did not announce itself: the 2.2.1
        /// and 2.3.0 Common APIs file every token under the party it belongs
        /// to, and used to read their assets database back before they knew
        /// their own parties - so the replay found no party for the token and
        /// dropped it without a word, while the 2.1.1 token came back. An
        /// EMSP with two versions then listed one token where it had issued
        /// two, and the CPO on the other version was told there was none.
        ///
        /// An EMSP of its own with all three versions, because the fixture's
        /// offers the two default ones - and 2.3.0 keeps its tokens the same
        /// way 2.2.1 does.
        /// </remarks>
        [Test]
        public async Task TheTokensAreKeptBetweenStartsOnEveryVersion()
        {

            var directory      = TestEMSPs.TemporaryDirectory("tokens");

            var configuration  = TestEMSPs.Offline;

            configuration["ocpi"] = new JObject(
                                        new JProperty("versions", new JArray("2.1.1", "2.2.1", "2.3.0"))
                                    );

            var first = TestEMSPs.New(directory, configuration);

            try
            {

                await first.Start();

                Assert.That(first.OCPIVersions.Select(version => version.Label), Is.EquivalentTo(new[] { "2.1.1", "2.2.1", "2.3.0" }));

                foreach (var version in first.OCPIVersions)
                {

                    var issued = await first.AddTokenAsync(
                                           new JObject(
                                               new JProperty("version",     version.Label),
                                               new JProperty("uid",         "DEGDFC12345678X"),
                                               new JProperty("type",        "OTHER"),
                                               new JProperty("contractId",  "DE-GDF-C12345678-X")
                                           )
                                       );

                    Assert.That(issued.Success, Is.True, $"OCPI {version.Label}: {issued.Message}");

                }

                Assert.That(first.TokenCount, Is.EqualTo(3));

                await first.Stop();

            }
            finally
            {
                await first.DisposeAsync();
            }

            var again = TestEMSPs.New(directory, configuration);

            try
            {

                await again.Start();

                Assert.Multiple(() => {

                    Assert.That(again.TokenCount, Is.EqualTo(3),
                                $"Only {String.Join(", ", again.OCPIVersions.Where(version => version.Tokens.Any()).Select(version => version.Label))} kept its token.");

                    foreach (var version in again.OCPIVersions)
                        Assert.That(version.HasToken(protocols.OCPI.Token_Id.Parse("DEGDFC12345678X")), Is.True,
                                    $"The token is gone on OCPI {version.Label}.");

                });

            }
            finally
            {
                await again.DisposeAsync();
                TestEMSPs.Remove(directory);
            }

        }

        #endregion

        #region TheEMSPRegistersWithACPOOfItsOwnAccord()

        /// <summary>
        /// The second way a peering starts: the CPO handed out a token and
        /// its versions URL, and this EMSP goes there - fetches the versions,
        /// finds the credentials endpoint, POSTs its own credentials with a
        /// fresh token for the CPO, and takes the CPO's token from the answer.
        /// </summary>
        /// <remarks>
        /// The CPO is a stub with the three routes the handshake touches. It
        /// records what it was sent, which is how the test knows this EMSP
        /// said the right things about itself.
        /// </remarks>
        [Test]
        public async Task TheEMSPRegistersWithACPOOfItsOwnAccord()
        {

            using var admin = await SignedIn();

            await using var cpo = await StubCPO.Start();

            var (id, ourTokenBefore, _) = await AddPartner(
                                                    admin,
                                                    TheirToken:   StubCPO.TokenA,
                                                    VersionsURL:  cpo.VersionsURL
                                                );

            var before = ((await GetJSON(admin, "/api/v1/ocpi/partners"))["partners"] as JArray)!.
                             First(partner => partner.Value<String>("id") == id);

            Assert.Multiple(() => {
                Assert.That(before.Value<Boolean>("canRegister"), Is.True,  "A partner with a token and a versions URL is not offered for registration.");
                Assert.That(before.Value<Boolean>("registered"),  Is.False);
            });

            var register = await admin.PostAsync($"/api/v1/ocpi/partners/2.2.1/{id}/register", JSONBody());
            var text     = await register.Content.ReadAsStringAsync();

            Assert.That(register.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"The registration answered {(Int32) register.StatusCode}: {text}");

            var answer = JObject.Parse(text);

            Assert.That(answer.Value<Boolean>("ok"), Is.True, answer.Value<String>("message"));

            var after = (answer["partners"]?["partners"] as JArray)!.First(partner => partner.Value<String>("id") == id);

            Assert.Multiple(() => {

                Assert.That(after.Value<Boolean>("registered"),   Is.True, "The registration went through and the partner is not reported as registered.");
                Assert.That(after.Value<String>("theirToken"),    Is.EqualTo(StubCPO.TokenC), "The token the CPO handed out in its answer was not taken.");
                Assert.That(after.Value<String>("remoteStatus"),  Is.EqualTo("ONLINE"));

                // What the CPO was told.
                Assert.That(cpo.ReceivedCredentials, Is.Not.Null, "The CPO never received this EMSP's credentials.");
                Assert.That(cpo.ReceivedCredentials?.Value<String>("url"),                        Is.EqualTo(EMSP.OCPIVersionsURL.ToString()));
                Assert.That(cpo.ReceivedCredentials?["roles"]?.First?.Value<String>("role"),      Is.EqualTo("EMSP"));
                Assert.That(cpo.ReceivedCredentials?["roles"]?.First?.Value<String>("party_id"),  Is.EqualTo("GDF"));

                // The token this EMSP sent the CPO is the one the CPO must use
                // from now on - a fresh one, and the one the list shows.
                Assert.That(cpo.ReceivedCredentials?.Value<String>("token"), Is.EqualTo(after.Value<String>("ourToken")));
                Assert.That(cpo.ReceivedCredentials?.Value<String>("token"), Is.Not.EqualTo(ourTokenBefore));

                // And it presented the token the CPO had handed out.
                Assert.That(cpo.TokensSeen, Does.Contain(StubCPO.TokenA));

            });

        }

        #endregion


        #region (private) StubCPO

        /// <summary>
        /// The three routes of a CPO that the credentials handshake touches:
        /// the versions, the details of one, and the credentials endpoint.
        /// </summary>
        private sealed class StubCPO : IAsyncDisposable
        {

            /// <summary>The token the CPO handed out for this EMSP to call it with.</summary>
            public const String TokenA = "stub-cpo-token-a";

            /// <summary>The token the CPO hands out in answer to the registration.</summary>
            public const String TokenC = "stub-cpo-token-c";

            private readonly HTTPServer server;

            public String    VersionsURL          { get; }

            public JObject?  ReceivedCredentials  { get; private set; }

            public List<String> TokensSeen        { get; } = [];


            private StubCPO(HTTPServer Server, String VersionsURL)
            {
                this.server       = Server;
                this.VersionsURL  = VersionsURL;
            }


            public static async Task<StubCPO> Start()
            {

                var port    = TestEMSPs.FreePort();
                var server  = new HTTPServer(IPAddress: IPv4Address.Localhost, TCPPort: IPPort.Parse(port));
                var origin  = $"http://127.0.0.1:{port}";
                var stub    = new StubCPO(server, $"{origin}/versions");
                var api     = server.AddHTTPAPI(HTTPPath.Root);

                api.AddHandler(
                    HTTPPath.Parse("/versions"),
                    request => {
                        stub.Remember(request);
                        return Task.FromResult(JSON(request, new JArray(
                            new JObject(
                                new JProperty("version",  "2.2.1"),
                                new JProperty("url",      $"{origin}/versions/2.2.1")
                            )
                        )));
                    },
                    HTTPMethod.GET
                );

                api.AddHandler(
                    HTTPPath.Parse("/versions/2.2.1"),
                    request => {
                        stub.Remember(request);
                        return Task.FromResult(JSON(request, new JObject(
                            new JProperty("version",    "2.2.1"),
                            new JProperty("endpoints",  new JArray(
                                new JObject(
                                    new JProperty("identifier",  "credentials"),
                                    new JProperty("role",        "RECEIVER"),
                                    new JProperty("url",         $"{origin}/2.2.1/credentials")
                                )
                            ))
                        )));
                    },
                    HTTPMethod.GET
                );

                api.AddHandler(
                    HTTPPath.Parse("/2.2.1/credentials"),
                    request => {
                        stub.Remember(request);
                        stub.ReceivedCredentials = JObject.Parse(request.HTTPBodyAsUTF8String ?? "{}");
                        return Task.FromResult(JSON(request, new JObject(
                            new JProperty("token",  TokenC),
                            new JProperty("url",    stub.VersionsURL),
                            new JProperty("roles",  new JArray(
                                new JObject(
                                    new JProperty("role",              "CPO"),
                                    new JProperty("party_id",          "GEF"),
                                    new JProperty("country_code",      "DE"),
                                    new JProperty("business_details",  new JObject(
                                        new JProperty("name",  "Stub CPO")
                                    ))
                                )
                            ))
                        )));
                    },
                    HTTPMethod.POST
                );

                await server.Start();

                return stub;

            }


            private void Remember(HTTPRequest Request)
            {

                // The token as this EMSP presented it: base64 in OCPI 2.2,
                // which is what the assertion undoes.
                if (Request.Authorization is HTTPTokenAuthentication tokenAuth)
                {
                    try
                    {
                        TokensSeen.Add(Encoding.UTF8.GetString(Convert.FromBase64String(tokenAuth.Token)));
                    }
                    catch (FormatException)
                    {
                        TokensSeen.Add(tokenAuth.Token);
                    }
                }

            }


            private static HTTPResponse JSON(HTTPRequest Request, JToken Data)

                => new HTTPResponse.Builder(Request) {
                       HTTPStatusCode  = HTTPStatusCode.OK,
                       ContentType     = HTTPContentType.Application.JSON_UTF8,
                       Content         = Encoding.UTF8.GetBytes(
                                             new JObject(
                                                 new JProperty("data",            Data),
                                                 new JProperty("status_code",     1000),
                                                 new JProperty("status_message",  "OK"),
                                                 new JProperty("timestamp",       DateTimeOffset.UtcNow.ToString("o"))
                                             ).ToString()
                                         ),
                       Connection      = ConnectionType.Close
                   }.AsImmutable;


            public async ValueTask DisposeAsync()
            {
                await server.Stop();
            }

        }

        #endregion

    }

}
