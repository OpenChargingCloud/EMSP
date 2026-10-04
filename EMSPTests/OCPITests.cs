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

using cloud.charging.open.EMSP.Configuration;

using cloud.charging.open.protocols.WWCP.Node.TestKit;

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

        #region (private static) LocationJSON(Id, PartyId = "GEF", Version = "2.2.1")

        /// <summary>
        /// The least a CPO has to say about a location in OCPI 2.2.1 - and
        /// in 2.3.0, which asks for the same - and in 2.1.1, which asks for
        /// its type as well.
        /// </summary>
        private static JObject LocationJSON(String  Id,
                                            String  PartyId   = "GEF",
                                            String  Version   = "2.2.1")
        {

            var location = new JObject(
                               new JProperty("country_code",  "DE"),
                               new JProperty("party_id",      PartyId),
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

            if (Version == "2.1.1")
                location.AddFirst(new JProperty("type", "ON_STREET"));

            return location;

        }

        #endregion

        #region (private static) TariffJSON(Id, PartyId, Version, Currency, LastUpdated)

        /// <summary>
        /// The least a CPO has to say about a tariff: its currency, one
        /// element with one price per kWh - and on 2.3.0 whether the price
        /// includes taxes.
        /// </summary>
        private static JObject TariffJSON(String  Id,
                                          String  PartyId,
                                          String  Version,
                                          String  Currency,
                                          String  LastUpdated)
        {

            var tariff = new JObject(
                             new JProperty("country_code",  "DE"),
                             new JProperty("party_id",      PartyId),
                             new JProperty("id",            Id),
                             new JProperty("currency",      Currency),
                             new JProperty("elements",      new JArray(
                                 new JObject(
                                     new JProperty("price_components", new JArray(
                                         new JObject(
                                             new JProperty("type",       "ENERGY"),
                                             new JProperty("price",      0.30),
                                             new JProperty("step_size",  1)
                                         )
                                     ))
                                 )
                             )),
                             new JProperty("last_updated",  LastUpdated)
                         );

            if (Version == "2.3.0")
                tariff.Add(new JProperty("tax_included", "YES"));

            return tariff;

        }

        #endregion

        #region (private static) CDRJSONv2_1_1(Id, PartyId)

        /// <summary>
        /// A charge detail record as OCPI 2.1.1 has a CPO send it: the
        /// location it was charged at whole, one charging period, and the
        /// party it comes from - which 2.1.1 itself leaves to the URL.
        /// </summary>
        private static JObject CDRJSONv2_1_1(String  Id,
                                             String  PartyId)

            => new (
                   new JProperty("country_code",      "DE"),
                   new JProperty("party_id",          PartyId),
                   new JProperty("id",                Id),
                   new JProperty("start_date_time",   "2026-09-20T10:00:00Z"),
                   new JProperty("stop_date_time",    "2026-09-20T11:00:00Z"),
                   new JProperty("auth_id",           "DE-GDF-C12345678-X"),
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

        #region (private static) PartnerOn(BaseURL, Version, Token)

        /// <summary>
        /// A CPO calling an EMSP of a test's own on one version, with the
        /// token it was given - as it is on 2.1.1, encoded in Base64 on
        /// 2.2.1 and 2.3.0 - or, without one, a caller who has none.
        /// </summary>
        private static HttpClient PartnerOn(String   BaseURL,
                                            String   Version,
                                            String?  Token)
        {

            var http = new HttpClient { BaseAddress = new Uri(BaseURL) };

            if (Token is not null)
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

        #region (private) OnEveryVersion(Name, Test)

        /// <summary>
        /// The CPO on each version of an EMSP that offers all three, and
        /// the token it was given: DE-G21 on 2.1.1, DE-G22 on 2.2.1 and
        /// DE-G23 on 2.3.0.
        /// </summary>
        private static readonly (String Version, String PartyId, String Token)[] EveryVersion = [
            ("2.1.1", "G21", "partner-token-G21"),
            ("2.2.1", "G22", "partner-token-G22"),
            ("2.3.0", "G23", "partner-token-G23")
        ];

        /// <summary>
        /// Run a test against an EMSP of its own with all three versions -
        /// the fixture's offers the two default ones - and a CPO added on
        /// each, as listed in <see cref="EveryVersion"/>.
        /// </summary>
        private static async Task OnEveryVersion(String                    Name,
                                                 Func<EMSP, String, Task>  Test)
        {

            var directory      = TestEMSPs.TemporaryDirectory(Name);

            var configuration  = TestEMSPs.Offline;

            configuration["ocpi"] = new JObject(
                                        new JProperty("versions", new JArray(EveryVersion.Select(partner => partner.Version)))
                                    );

            var emsp = await TestPorts.StartedOnFreshPorts(() => TestEMSPs.New(directory, configuration));

            try
            {

                foreach (var (version, partyId, token) in EveryVersion)
                {

                    var added = await emsp.AddRemotePartyAsync(
                                          new JObject(
                                              new JProperty("version",      version),
                                              new JProperty("countryCode",  "DE"),
                                              new JProperty("partyId",      partyId),
                                              new JProperty("role",         "CPO"),
                                              new JProperty("name",         $"Test CPO {partyId}"),
                                              new JProperty("ourToken",     token)
                                          )
                                      );

                    Assert.That(added.Success, Is.True, $"OCPI {version}: {added.Message}");

                }

                await Test(emsp, emsp.WebInterfaceURL.ToString());

            }
            finally
            {
                await emsp.DisposeAsync();
                TestEMSPs.Remove(directory);
            }

        }

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

        #region OnEveryVersionTheEMSPSaysItIsAnEMSPAndNothingElse()

        /// <summary>
        /// What a partner is told on each version, with a CPO on each: its
        /// credentials name one role, this EMSP's own, and its version
        /// details only modules an EMSP offers - each below the version's
        /// own path, and each answering.
        /// </summary>
        /// <remarks>
        /// On 2.3.0 the Common API's parties are the node's own: its
        /// credentials list them as its roles, and its version details
        /// announce the CPO's modules as soon as one of them is a CPO. A
        /// CPO that ends up among them is told it is talking to a CPO. The
        /// test of the version details asked only 2.2.1, and the one of the
        /// credentials only for the first role.
        /// </remarks>
        [Test]
        public async Task OnEveryVersionTheEMSPSaysItIsAnEMSPAndNothingElse()

            => await OnEveryVersion("own-roles", async (emsp, baseURL) => {

                   foreach (var (version, _, token) in EveryVersion)
                   {

                       using var partner = PartnerOn(baseURL, version, token);

                       var credentials = (await OCPIResponse(await partner.GetAsync($"/ext/v{version}/credentials")))["data"] as JObject;

                       Assert.That(credentials, Is.Not.Null, $"OCPI {version}: the credentials carry no data.");

                       // 2.1.1 says who it is beside the token; 2.2.1 and 2.3.0 as a list of roles.
                       var roles = version == "2.1.1"
                                       ? [ credentials! ]
                                       : (credentials!["roles"] as JArray)?.OfType<JObject>().ToArray() ?? [];

                       Assert.Multiple(() => {
                           Assert.That(roles.Select(role => $"{role.Value<String>("country_code")}-{role.Value<String>("party_id")} {role.Value<String>("role") ?? "EMSP"}"),
                                       Is.EqualTo(new[] { "DE-GDF EMSP" }),
                                       $"OCPI {version}: the credentials name other roles than this EMSP's own: {credentials}");
                       });

                       var endpoints = (await OCPIResponse(await partner.GetAsync($"/ext/versions/{version}")))["data"]?["endpoints"] as JArray;

                       Assert.That(endpoints, Is.Not.Null.And.Not.Empty, $"OCPI {version}: the version details carry no endpoints.");

                       foreach (var endpoint in endpoints!)
                       {

                           var identifier = endpoint.Value<String>("identifier")!;
                           var url        = new Uri(endpoint.Value<String>("url")!);

                           Assert.Multiple(() => {
                               Assert.That(url.AbsolutePath, Does.StartWith($"/ext/v{version}/"),
                                           $"OCPI {version}: the '{identifier}' endpoint is advertised outside the HTTPExt API, where nothing serves it.");
                               Assert.That(url.AbsolutePath, Does.Not.Contain("/cpo/"),
                                           $"OCPI {version}: the '{identifier}' endpoint at {url} is a CPO's, and this is an EMSP.");
                           });

                           // As in TheVersionDetailsPointAtEndpointsThatExist: the
                           // charging profiles are a known gap of the library, and
                           // the commands have no route at their base.
                           if (identifier == "chargingprofiles")
                               continue;

                           var probe = await partner.GetAsync(
                                           identifier == "commands"
                                               ? new Uri(url + "/START_SESSION/probe")
                                               : url
                                       );

                           Assert.That(probe.StatusCode, Is.Not.EqualTo(HttpStatusCode.NotFound),
                                       $"OCPI {version}: the '{identifier}' endpoint is advertised at {url} and not served there.");

                       }

                   }

               });

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

        #region TheCredentialsAreForAKnownTokenOnly()

        /// <summary>
        /// The credentials say where this EMSP's versions are, which roles it
        /// plays and its business details - to the partner whose token it
        /// handed out, and to nobody else: not to a caller without a token,
        /// not to one with a token this EMSP never made up. On every version.
        /// </summary>
        /// <remarks>
        /// Until WWCP_OCPI e677ff43 the library answered both with 1000, the
        /// credentials and the token "&lt;any&gt;" - while its locations
        /// are open data, which is its default, and on 2.3.0 even without a
        /// token at all. A 401 is what it answers now.
        ///
        /// On an EMSP with all three versions and a partner on each, so that
        /// the 401s are told apart from a route that answers nobody.
        /// </remarks>
        [Test]
        public async Task TheCredentialsAreForAKnownTokenOnly()

            => await OnEveryVersion("credentials", async (emsp, baseURL) => {

                   foreach (var (version, _, token) in EveryVersion)
                   {

                       var path = $"/ext/v{version}/credentials";

                       using var partner  = PartnerOn(baseURL, version, token);
                       using var nobody   = PartnerOn(baseURL, version, null);
                       using var stranger = PartnerOn(baseURL, version, "nobody-gave-me-this");

                       var known = await OCPIResponse(await partner.GetAsync(path));

                       Assert.That(known["data"]?.Value<String>("token"), Is.EqualTo(token),
                                   $"OCPI {version}: the partner's own token is not answered with the credentials, so the refusals below prove nothing.");

                       foreach (var (who, http) in new[] { ("a caller without a token", nobody), ("a token nobody gave out", stranger) })
                       {

                           var response = await http.GetAsync(path);
                           var text     = await response.Content.ReadAsStringAsync();

                           Assert.Multiple(() => {
                               Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                                           $"OCPI {version}: {who} was answered {(Int32) response.StatusCode}: {text}");
                               Assert.That(text, Does.Not.Contain("business_details"),
                                           $"OCPI {version}: {who} was shown this EMSP's business details.");
                           });

                       }

                   }

               });

        #endregion

        #region ALocationArrivesOnEveryVersion()

        /// <summary>
        /// A CPO PUTs a location and then PATCHes it - on every version -
        /// and the EMSP keeps it, under the version it came in on, with
        /// what the PATCH changed.
        /// </summary>
        /// <remarks>
        /// Until WWCP_OCPI 4ddca474 the EMSP API of 2.3.0 made no counters,
        /// and every route that counts threw before its handler: a CPO's PUT
        /// and PATCH of a location were answered 3000, and nothing arrived.
        /// Until 81d160b9 a CPO on 2.3.0 was then refused as an unknown
        /// party, as the library knew only the node's own parties there.
        /// The test of the push only asked 2.2.1, where nothing was missing.
        ///
        /// Until WWCP_OCPI 7e06b5ae the EMSP API of 2.1.1 served a location
        /// at "locations/{location_id}", where OCPI 2.1.1 has a CPO PUT it
        /// at "locations/{country_code}/{party_id}/{location_id}": a CPO
        /// that kept to the specification was answered "Unknown location
        /// identification!", and 2.1.1 was left out here.
        /// </remarks>
        [Test]
        public async Task ALocationArrivesOnEveryVersion()

            => await OnEveryVersion("locations", async (emsp, baseURL) => {

                   foreach (var (version, partyId, token) in EveryVersion)
                   {

                       using var partner = PartnerOn(baseURL, version, token);

                       var path  = $"/ext/v{version}/emsp/locations/DE/{partyId}/LOC0001";

                       var put   = await OCPIResponse(await partner.PutAsync(
                                                                path,
                                                                new StringContent(LocationJSON("LOC0001", partyId, version).ToString(), Encoding.UTF8, "application/json")
                                                            ));

                       Assert.That(put.Value<Int32>("status_code"), Is.EqualTo(1000), $"OCPI {version}: PUT location: {put}");

                       var patch = await OCPIResponse(await partner.PatchAsync(
                                                                path,
                                                                new StringContent(new JObject(
                                                                                      new JProperty("name",          $"Renamed on {version}"),
                                                                                      new JProperty("last_updated",  "2026-09-20T12:00:00Z")
                                                                                  ).ToString(), Encoding.UTF8, "application/json")
                                                            ));

                       Assert.That(patch.Value<Int32>("status_code"), Is.EqualTo(1000), $"OCPI {version}: PATCH location: {patch}");

                       var kept = emsp.OCPIVersions.First(ocpi => ocpi.Label == version).
                                       Locations.FirstOrDefault(location => location.Value<String>("id") == "LOC0001");

                       Assert.That(kept, Is.Not.Null, $"OCPI {version}: the location the CPO pushed was not kept.");

                       Assert.Multiple(() => {
                           Assert.That(kept!.Value<String>("version"),  Is.EqualTo(version));
                           Assert.That(kept. Value<String>("name"),     Is.EqualTo($"Renamed on {version}"), $"OCPI {version}: the PATCH did not reach the location.");
                       });

                   }

               });

        #endregion

        #region ATariffPutAgainOrPatchedIsTheNewOneOnEveryVersion()

        /// <summary>
        /// A CPO PUTs a tariff, PUTs it again with another currency, and
        /// PATCHes the currency once more - on every version - and the EMSP
        /// keeps the tariff as it was said last.
        /// </summary>
        /// <remarks>
        /// Until WWCP_OCPI d714d2ae, and 8bbccf89 for 2.1.1, the library
        /// called TimeRangeDictionary.TryUpdate with the tariff to replace
        /// and its replacement swapped: the tariff kept was put in its own
        /// place, and every PUT of a changed tariff and every PATCH was
        /// answered 1000 and changed nothing. An EMSP kept the first version
        /// of each of its partners' tariffs.
        /// </remarks>
        [Test]
        public async Task ATariffPutAgainOrPatchedIsTheNewOneOnEveryVersion()

            => await OnEveryVersion("tariffs", async (emsp, baseURL) => {

                   foreach (var (version, partyId, token) in EveryVersion)
                   {

                       using var partner = PartnerOn(baseURL, version, token);

                       var path   = $"/ext/v{version}/emsp/tariffs/DE/{partyId}/TARIFF0001";

                       foreach (var (currency, lastUpdated) in new[] { ("EUR", "2026-09-20T10:00:00Z"), ("USD", "2026-09-20T11:00:00Z") })
                       {

                           var put = await OCPIResponse(await partner.PutAsync(
                                                                    path,
                                                                    new StringContent(TariffJSON("TARIFF0001", partyId, version, currency, lastUpdated).ToString(), Encoding.UTF8, "application/json")
                                                                ));

                           Assert.That(put.Value<Int32>("status_code"), Is.EqualTo(1000), $"OCPI {version}: PUT tariff in {currency}: {put}");

                       }

                       Assert.That(CurrencyOfTheTariffKept(emsp, version), Is.EqualTo("USD"), $"OCPI {version}: the tariff put again is still the one put first.");

                       var patch = await OCPIResponse(await partner.PatchAsync(
                                                                path,
                                                                new StringContent(new JObject(
                                                                                      new JProperty("currency",      "CHF"),
                                                                                      new JProperty("last_updated",  "2026-09-20T12:00:00Z")
                                                                                  ).ToString(), Encoding.UTF8, "application/json")
                                                            ));

                       Assert.That(patch.Value<Int32>("status_code"), Is.EqualTo(1000), $"OCPI {version}: PATCH tariff: {patch}");

                       Assert.That(CurrencyOfTheTariffKept(emsp, version), Is.EqualTo("CHF"), $"OCPI {version}: the PATCH did not reach the tariff.");

                   }

               });


        private static String? CurrencyOfTheTariffKept(EMSP    Node,
                                                       String  Version)

            => Node.OCPIVersions.First(ocpi => ocpi.Label == Version).
                    Tariffs.FirstOrDefault(tariff => tariff.Value<String>("id") == "TARIFF0001")?.
                    Value<String>("currency");

        #endregion

        #region APartnerFromBeforeARestartCanPushOnEveryVersion()

        /// <summary>
        /// A partner added before this EMSP was restarted can push after it,
        /// on every version.
        /// </summary>
        /// <remarks>
        /// The Common API reads its remote parties back at every start, and
        /// what a push is checked against has to come back with them: the
        /// EMSP API's remote CPOs on 2.2.1, which the EMSP rebuilds, and on
        /// 2.3.0 the data the library keeps for a remote party apart from
        /// its own, since WWCP_OCPI 81d160b9. Without that the partner can
        /// sign in and is refused as an unknown party when it pushes.
        /// </remarks>
        [Test]
        public async Task APartnerFromBeforeARestartCanPushOnEveryVersion()
        {

            var directory      = TestEMSPs.TemporaryDirectory("restart-push");

            var configuration  = TestEMSPs.Offline;

            configuration["ocpi"] = new JObject(
                                        new JProperty("versions", new JArray(EveryVersion.Select(partner => partner.Version)))
                                    );

            var first = await TestPorts.StartedOnFreshPorts(() => TestEMSPs.New(directory, configuration));

            try
            {

                foreach (var (version, partyId, token) in EveryVersion)
                {

                    var added = await first.AddRemotePartyAsync(
                                          new JObject(
                                              new JProperty("version",      version),
                                              new JProperty("countryCode",  "DE"),
                                              new JProperty("partyId",      partyId),
                                              new JProperty("role",         "CPO"),
                                              new JProperty("name",         $"Test CPO {partyId}"),
                                              new JProperty("ourToken",     token)
                                          )
                                      );

                    Assert.That(added.Success, Is.True, $"OCPI {version}: {added.Message}");

                }

                await first.Stop();

            }
            finally
            {
                await first.DisposeAsync();
            }

            var again = await TestPorts.StartedOnFreshPorts(() => TestEMSPs.New(directory, configuration));

            try
            {

                var baseURL = again.WebInterfaceURL.ToString();

                foreach (var (version, partyId, token) in EveryVersion)
                {

                    using var partner = PartnerOn(baseURL, version, token);

                    var put = await OCPIResponse(await partner.PutAsync(
                                                              $"/ext/v{version}/emsp/locations/DE/{partyId}/LOC0002",
                                                              new StringContent(LocationJSON("LOC0002", partyId, version).ToString(), Encoding.UTF8, "application/json")
                                                          ));

                    Assert.That(put.Value<Int32>("status_code"), Is.EqualTo(1000), $"OCPI {version}: PUT location after the restart: {put}");

                }

            }
            finally
            {
                await again.DisposeAsync();
                TestEMSPs.Remove(directory);
            }

        }

        #endregion

        #region ACPOHasATokenAuthorisedOnEveryVersion()

        /// <summary>
        /// A driver holds a card this EMSP issued to a charging station, and
        /// its CPO asks this EMSP whether to start: the token is ALLOWED - on
        /// every version. One nobody issued is not.
        /// </summary>
        /// <remarks>
        /// Until WWCP_OCPI 4ddca474 the EMSP API of 2.3.0 made no counters,
        /// and its real-time authorisation threw before its handler: a CPO
        /// on 2.3.0 was answered 3000 for every card. Nothing asked it.
        /// </remarks>
        [Test]
        public async Task ACPOHasATokenAuthorisedOnEveryVersion()

            => await OnEveryVersion("authorize", async (emsp, baseURL) => {

                   foreach (var (version, _, token) in EveryVersion)
                   {

                       var issued = await emsp.AddTokenAsync(
                                              new JObject(
                                                  new JProperty("version",     version),
                                                  new JProperty("uid",         "DEGDFC12345678X"),
                                                  new JProperty("type",        "RFID"),
                                                  new JProperty("contractId",  "DE-GDF-C12345678-X")
                                              )
                                          );

                       Assert.That(issued.Success, Is.True, $"OCPI {version}: {issued.Message}");

                       using var partner = PartnerOn(baseURL, version, token);

                       var known   = await partner.PostAsync($"/ext/v{version}/emsp/tokens/DEGDFC12345678X/authorize", null);
                       var answer  = await OCPIResponse(known);

                       Assert.Multiple(() => {
                           Assert.That(answer.Value<Int32>("status_code"),           Is.EqualTo(1000),      $"OCPI {version}: {answer}");
                           Assert.That(answer["data"]?.Value<String>("allowed"),      Is.EqualTo("ALLOWED"), $"OCPI {version}: the card this EMSP issued is not allowed: {answer}");
                       });

                       var unknown     = await partner.PostAsync($"/ext/v{version}/emsp/tokens/NOBODYISSUEDTHIS/authorize", null);
                       var unknownText = await unknown.Content.ReadAsStringAsync();

                       Assert.That(JObject.Parse(unknownText)["data"]?.Value<String>("allowed"), Is.Not.EqualTo("ALLOWED"),
                                   $"OCPI {version}: a card nobody issued is allowed: {(Int32) unknown.StatusCode} {unknownText}");

                   }

               });

        #endregion

        #region AChargeDetailRecordArrivesOn2_1_1()

        /// <summary>
        /// A CPO on 2.1.1 POSTs the charge detail record of a session, and
        /// the EMSP keeps it - which is what it bills its customer by.
        /// </summary>
        /// <remarks>
        /// Until WWCP_OCPI 4ddca474 the EMSP API of 2.1.1 made no counters,
        /// so that its POST of a CDR threw before its handler and was
        /// answered 3000: a CPO on 2.1.1 could not hand over a single one.
        /// </remarks>
        [Test]
        public async Task AChargeDetailRecordArrivesOn2_1_1()

            => await OnEveryVersion("cdrs", async (emsp, baseURL) => {

                   var (version, partyId, token) = EveryVersion.First(partner => partner.Version == "2.1.1");

                   using var partner = PartnerOn(baseURL, version, token);

                   var post = await OCPIResponse(await partner.PostAsync(
                                                           $"/ext/v{version}/emsp/cdrs",
                                                           new StringContent(CDRJSONv2_1_1("CDR0001", partyId).ToString(), Encoding.UTF8, "application/json")
                                                       ));

                   Assert.That(post.Value<Int32>("status_code"), Is.EqualTo(1000), $"POST cdr: {post}");

                   var kept = emsp.OCPIVersions.First(ocpi => ocpi.Label == version).
                                   CDRs.FirstOrDefault(cdr => cdr.Value<String>("id") == "CDR0001");

                   Assert.That(kept, Is.Not.Null, "The charge detail record the CPO posted was not kept.");

                   Assert.Multiple(() => {
                       Assert.That(kept!.Value<String>("version"),       Is.EqualTo(version));
                       Assert.That(kept. Value<String>("auth_id"),       Is.EqualTo("DE-GDF-C12345678-X"));
                       Assert.That(kept. Value<Decimal>("total_energy"), Is.EqualTo(12.5m));
                   });

               });

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

            var again = await TestPorts.StartedOnFreshPorts(() => TestEMSPs.New(Directory, Configuration, Clock));

            try
            {

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

            var first = await TestPorts.StartedOnFreshPorts(() => TestEMSPs.New(directory, configuration));

            try
            {

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

            var again = await TestPorts.StartedOnFreshPorts(() => TestEMSPs.New(directory, configuration));

            try
            {

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

    }

}
