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
using System.Security.Cryptography;
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.Mail;
using org.GraphDefined.Vanaheimr.Hermod.WebSocket;

using cloud.charging.open.EMSP.CommandLine;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.TestKit;
using cloud.charging.open.protocols.WWCP.Node.Connections;

using cloud.charging.open.protocols.OCPI.Transport;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// The CPOs that connect to an EMSP, over its JSON API and at its command
    /// line: their connections, their logins and what they sign in with, at
    /// the resource "partners" - which an administrator may change and the
    /// operator of the EMSP only read.
    /// </summary>
    public class OCPIServerAPITests
    {

        #region Data

        private String   directory  = default!;
        private EMSP?    emsp;
        private Uri?     address;

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void MakeADirectory()
        {
            directory = TestEMSPs.TemporaryDirectory("ocpi-server-api");
        }

        [TearDown]
        public async Task StopAndRemove()
        {

            if (emsp is not null)
                await emsp.DisposeAsync();

            emsp = null;

            TestEMSPs.Remove(directory);

        }

        #endregion


        #region (helpers)

        /// <summary>
        /// An EMSP of OCPI 2.2.1, made and not started - its WebSocket server of
        /// OCPI on 127.0.0.1 at a port of its own, letting a client in by HTTP
        /// authentication without TLS, where it should listen.
        /// </summary>
        private EMSP NewEMSP(Boolean Listens)
        {

            var configuration = TestEMSPs.Offline;

            configuration.Add(new JProperty("ocpi", new JObject(
                                  new JProperty("countryCode",  "DE"),
                                  new JProperty("partyId",      "GDF"),
                                  new JProperty("versions",     new JArray("2.2.1"))
                              )));

            if (Listens)
                configuration.Add(new JProperty("webSocketServer", new JObject(
                                      new JProperty("enabled",               true),
                                      new JProperty("address",               "127.0.0.1"),
                                      new JProperty("port",                  TestPorts.Free().ToUInt16()),
                                      new JProperty("clientAuthentication",  new JArray("httpAuthWithoutTLS"))
                                  )));

            emsp     = TestEMSPs.New(directory, configuration);
            address  = new Uri(emsp.WebInterfaceURL.ToString());

            return emsp;

        }

        private async Task<HttpClient> SignedInAs(User_Id Name, String Role)
        {

            var password = "correct-horse-battery-" + Guid.NewGuid().ToString("N")[..8];

            Assert.That(emsp!.ExtAPI.TryGetOrganization(Organization_Id.Parse(EMSP.DefaultOrganization), out var organization) &&
                        organization is Organization, Is.True, "the EMSP's organization is not there");

            var account = await emsp.ExtAPI.CreateUser(
                                    Name,
                                    I18NString.Create(Languages.en, Name.ToString()),
                                    SimpleEMailAddress.Parse($"{Name}@localhost"),
                                    User2OrganizationEdgeLabel.IsMember,
                                    (Organization) organization!,
                                    Password:                  password,
                                    SkipDefaultNotifications:  true,
                                    SkipNewUserEMail:          true,
                                    SkipNewUserNotifications:  true,
                                    AcceptedEULA:              DateTimeOffset.UtcNow.AddSeconds(-1),
                                    IsAuthenticated:           true
                                );

            Assert.That(account, Is.Not.Null);
            Assert.That(emsp.ExtAPI.TryGetUser(Name, out var stored), Is.True);
            Assert.That(emsp.ExtAPI.TryGetUserGroup(UserGroup_Id.Parse(Role), out var group), Is.True, $"the EMSP has no group '{Role}'");
            Assert.That((await emsp.ExtAPI.AddUserToUserGroup((User) stored!, User2UserGroupEdgeLabel.IsMember, (UserGroup) group!)).IsSuccess, Is.True);

            var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                             "Basic",
                                                             Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Name}:{password}"))
                                                         );

            return client;

        }

        private static async Task<(HttpStatusCode Status, JObject JSON)> Post(HttpClient Client, String Path, Object JSON)
        {
            var response = await Client.PostAsync(Path, new StringContent(JObject.FromObject(JSON).ToString(), Encoding.UTF8, "application/json"));
            return (response.StatusCode, JObject.Parse(await response.Content.ReadAsStringAsync()));
        }

        private static async Task<(HttpStatusCode Status, JObject JSON)> Get(HttpClient Client, String Path)
        {
            var response = await Client.GetAsync(Path);
            return (response.StatusCode, JObject.Parse(await response.Content.ReadAsStringAsync()));
        }

        private static async Task<(HttpStatusCode Status, JObject JSON)> Put(HttpClient Client, String Path, Object JSON)
        {
            var response = await Client.PutAsync(Path, new StringContent(JObject.FromObject(JSON).ToString(), Encoding.UTF8, "application/json"));
            return (response.StatusCode, JObject.Parse(await response.Content.ReadAsStringAsync()));
        }

        private static async Task<(HttpStatusCode Status, JObject JSON)> Delete(HttpClient Client, String Path)
        {
            var response = await Client.DeleteAsync(Path);
            return (response.StatusCode, JObject.Parse(await response.Content.ReadAsStringAsync()));
        }

        private static JObject? LoginIn(JObject Logins, String Login)

            => (Logins["logins"] as JArray)?.OfType<JObject>().FirstOrDefault(login => login.Value<String>("login") == Login);

        /// <summary>
        /// The CPO of the given name written down by an administrator as one
        /// that connects, over OCPI 2.2.1, at the shared routes of the
        /// connections - which carry the version of OCPI, and give it back.
        /// </summary>
        private static async Task<String> CPOWrittenDown(HttpClient Admin, EMSP EMSP, String Name)
        {

            var (status, added) = await Post(Admin, "api/v1/configuration/connections", new {
                                                        description     = new { en = "The CPO, connecting" },
                                                        url             = $"ws://127.0.0.1:{EMSP.OCPIServerSettings.TCPPort}/{Name}",
                                                        connectionType  = "CPO",
                                                        autoConnect     = true,
                                                        inbound         = true,
                                                        ocpiVersion     = "2.2.1"
                                                    });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), added.ToString());

            var id     = added.Value<String>("id")!;
            var entry  = (added["connections"]?["connections"] as JArray)?.OfType<JObject>().FirstOrDefault(one => one.Value<String>("id") == id);

            Assert.That(entry?.Value<String>("ocpiVersion"), Is.EqualTo("2.2.1"), added.ToString());

            return id;

        }

        /// <summary>
        /// The CPO of the given name written down as one that connects, over
        /// OCPI 2.2.1 - in the store, where nobody signed in to the JSON API.
        /// </summary>
        private static String CPOWrittenDown(EMSP EMSP, String Name)
        {

            Assert.That(EMSP.Connections.TryAddConnection(I18NString.Create("The CPO, connecting"),
                                                          URL.Parse($"ws://127.0.0.1:{EMSP.OCPIServerSettings.TCPPort}/{Name}"),
                                                          "CPO", true, null, null,
                                                          out var id, out var error,
                                                          Inbound:      true,
                                                          OCPIVersion:  "2.2.1"),
                        Is.True, error);

            return id!;

        }

        #endregion


        #region AnAdministratorLetsACPOIn()

        /// <summary>
        /// An administrator gives a CPO a login with a password and a key pin,
        /// switches it off and on, and forgets it again; the server it connects
        /// to and the CPO's connection are read beside.
        /// </summary>
        [Test]
        public async Task AnAdministratorLetsACPOIn()
        {

            await TestPorts.StartedOnFreshPorts(() => NewEMSP(false));

            using var admin = await SignedInAs(User_Id.Parse("admin1"), WWCPNode.AdminRole.ToString());

            #region The server, with nobody in yet

            var (status, server) = await Get(admin, "api/v1/configuration/ocpi-server");

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), server.ToString());

            Assert.Multiple(() => {
                Assert.That(server["configuration"],                        Is.InstanceOf<JObject>());
                Assert.That(server["state"]?["running"]?.Value<Boolean>(),  Is.False, "the server is switched off where the file says nothing");
                Assert.That(server["connected"] as JArray,                  Is.Empty);
            });

            #endregion

            #region Its connection, as the connections list it

            var connectionId = await CPOWrittenDown(admin, emsp!, "cpo-01");

            (status, var connections) = await Get(admin, "api/v1/configuration/connections");

            Assert.That(status,                     Is.EqualTo(HttpStatusCode.OK), connections.ToString());
            Assert.That(connections.ToString(),     Does.Contain(connectionId));

            #endregion

            #region Its login, with a password and a key pin

            (status, var logins) = await Post(admin, "api/v1/configuration/ocpi-server/logins", new {
                                       login     = "cpo-01",
                                       password  = "a-long-password-" + Guid.NewGuid().ToString("N")
                                   });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), logins.ToString());
            Assert.That(LoginIn((JObject) logins["logins"]!, "cpo-01"), Is.Not.Null, logins.ToString());

            var pin = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("the public key of the CPO")));

            (status, var pinned) = await Post(admin, "api/v1/configuration/ocpi-server/logins/cpo-01/keyPins", new { pin, label = "the CPO's TLS key" });

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.OK), pinned.ToString());
            Assert.That(pinned["keyPin"]?.Value<String>(),  Is.EqualTo(pin).IgnoreCase);

            Assert.That(emsp!.WebSocketLogins.TryGet(User_Id.Parse("cpo-01"), out var login), Is.True);

            Assert.Multiple(() => {
                Assert.That(login!.HasPassword,  Is.True);
                Assert.That(login.HasKeyPin,     Is.True);
                Assert.That(login.Enabled,       Is.True);
            });

            #endregion

            #region Switched off, and on again - and no mark of OCPP

            (status, var off) = await Put(admin, "api/v1/configuration/ocpi-server/logins/cpo-01", new { enabled = false });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), off.ToString());
            Assert.That(emsp.WebSocketLogins.TryGet(User_Id.Parse("cpo-01"), out login) && !login.Enabled, Is.True);

            (status, var marked) = await Put(admin, "api/v1/configuration/ocpi-server/logins/cpo-01", new { gateway = true });

            Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest), marked.ToString());

            (status, var on) = await Put(admin, "api/v1/configuration/ocpi-server/logins/cpo-01", new { enabled = true });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), on.ToString());

            #endregion

            #region The chains a client certificate may lead to

            (status, var trust) = await Get(admin, "api/v1/configuration/ocpi-server/trust");

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), trust.ToString());

            #endregion

            #region Forgotten again

            (status, var forgotten) = await Delete(admin, "api/v1/configuration/ocpi-server/logins/cpo-01");

            Assert.That(status,                                                       Is.EqualTo(HttpStatusCode.OK), forgotten.ToString());
            Assert.That(emsp.WebSocketLogins.TryGet(User_Id.Parse("cpo-01"), out _),  Is.False);

            (status, var removed) = await Post(admin, "api/v1/configuration/connections/remove", new { id = connectionId });

            Assert.That(status,                                                        Is.EqualTo(HttpStatusCode.OK), removed.ToString());
            Assert.That(emsp.Connections.Connections.Any(entry => entry.Id == connectionId), Is.False);

            #endregion

        }

        #endregion

        #region TheOperatorOnlyReads()

        /// <summary>
        /// Who may let a CPO in hands a foreign system the right to push into
        /// this EMSP: the operator of the EMSP reads the CPOs, and changes
        /// nothing of them.
        /// </summary>
        [Test]
        public async Task TheOperatorOnlyReads()
        {

            await TestPorts.StartedOnFreshPorts(() => NewEMSP(false));

            using var user = await SignedInAs(User_Id.Parse("operator1"), EMSPAccess.Operator.Name);

            var (status, server) = await Get(user, "api/v1/configuration/ocpi-server");
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), server.ToString());

            (status, var logins) = await Get(user, "api/v1/configuration/ocpi-server/logins");
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), logins.ToString());

            (status, var connections) = await Get(user, "api/v1/configuration/connections");
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), connections.ToString());

            (status, var login) = await Post(user, "api/v1/configuration/ocpi-server/logins", new {
                                      login     = "cpo-01",
                                      password  = "a-long-password-" + Guid.NewGuid().ToString("N")
                                  });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), login.ToString());

            (status, var connection) = await Post(user, "api/v1/configuration/connections", new JObject(
                                           new JProperty("url",             "ws://127.0.0.1:2359/cpo-01"),
                                           new JProperty("connectionType",  "CPO"),
                                           new JProperty("inbound",         true)
                                       ));

            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), connection.ToString());

            Assert.Multiple(() => {
                Assert.That(emsp!.WebSocketLogins.TryGet(User_Id.Parse("cpo-01"), out _), Is.False);
                Assert.That(emsp.Connections.Connections,                                Is.Empty);
            });

        }

        #endregion

        #region ACPOWithALoginGivenOverTheAPI_SignsIn()

        /// <summary>
        /// A CPO written down as one that connects, given a password by an
        /// administrator over the JSON API, connects - and the server says so.
        /// </summary>
        [Test]
        public async Task ACPOWithALoginGivenOverTheAPI_SignsIn()
        {

            var emsp = NewEMSP(true);

            await emsp.Start();

            Assert.That(emsp.OCPIServerRunning, Is.True);

            using var admin    = await SignedInAs(User_Id.Parse("admin1"), WWCPNode.AdminRole.ToString());

            var connectionId   = await CPOWrittenDown(admin, emsp, "cpo01");
            var password       = "a-long-password-" + Guid.NewGuid().ToString("N");

            var (status, logins) = await Post(admin, "api/v1/configuration/ocpi-server/logins", new { login = "cpo01", password });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), logins.ToString());

            var client = new WebSocketClient(
                             URL.Parse($"ws://127.0.0.1:{emsp.OCPIServerSettings.TCPPort}/cpo01"),
                             HTTPAuthentication:     HTTPBasicAuthentication.Create("cpo01", password),
                             SecWebSocketProtocols:  [ OCPIWireNames.SubprotocolOf("2.2.1") ]
                         );

            try
            {

                var (_, response) = await client.Connect(MaxNumberOfRetries: 0);

                Assert.That(response.HTTPStatusCode, Is.EqualTo(HTTPStatusCode.SwitchingProtocols));

                var until = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

                while (emsp.ConnectionStates.GetValueOrDefault(connectionId)?.Status != ConnectionStatus.Connected && DateTimeOffset.UtcNow < until)
                    await Task.Delay(25);

                (status, var server) = await Get(admin, "api/v1/configuration/ocpi-server");

                Assert.Multiple(() => {
                    Assert.That(status,                                          Is.EqualTo(HttpStatusCode.OK), server.ToString());
                    Assert.That(server["state"]?["running"]?.Value<Boolean>(),   Is.True);
                    Assert.That(server["connected"]?.ToObject<String[]>(),       Is.EqualTo(new[] { "cpo01" }));
                });

            }
            finally
            {
                await client.Close();
            }

        }

        #endregion

        #region AtTheConsoleCPOsListsTheCPOs()

        /// <summary>
        /// 'cpos' says where the server is and lists every CPO that connects
        /// with its connection and what it signs in with; 'cpoPins' and
        /// 'cpoKeys' give and list its key pins and keys, and say what keeps
        /// it out.
        /// </summary>
        [Test]
        public async Task AtTheConsoleCPOsListsTheCPOs()
        {

            NewEMSP(false);

            using var cli = new EMSPCLI(emsp!);

            var none = await cli.Execute("cpos");

            Assert.Multiple(() => {
                Assert.That(none, Has.Some.EqualTo("The WebSocket server of OCPI is switched off."));
                Assert.That(none, Has.Some.EqualTo("No CPO that connects is written down: none may connect."));
            });

            CPOWrittenDown(emsp!, "cpo-01");

            var pin    = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("the public key of the CPO")));
            var given  = await cli.Execute($"cpoPins cpo-01 add {pin} the-CPO");

            Assert.That(String.Join(" | ", given), Does.Contain("key pin"));

            var listed = await cli.Execute("cpos");

            Assert.Multiple(() => {
                Assert.That(listed, Has.Some.StartsWith("cpo-01  on  not connected"));
                Assert.That(listed, Has.Some.Contains("signs in with 1 key pin(s)"));
            });

            var pins   = await cli.Execute("cpoPins");

            Assert.Multiple(() => {
                Assert.That(pins, Has.Some.EqualTo("cpo-01:"));
                Assert.That(pins, Has.Some.Contains("WebSocket server of OCPI is switched off"));
            });

            var keys   = await cli.Execute("cpoKeys");

            Assert.That(keys, Has.Some.EqualTo("No CPO of this EMSP has an SSH key."));

        }

        #endregion

    }

}
