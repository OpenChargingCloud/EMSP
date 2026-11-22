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

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.WebSocket;

using cloud.charging.open.EMSP.Configuration;

using cloud.charging.open.protocols.WWCP.Node.Logins;
using cloud.charging.open.protocols.WWCP.Node.TestKit;
using cloud.charging.open.protocols.WWCP.Node.Connections;

using cloud.charging.open.protocols.OCPI.Transport;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// The WebSocket server of OCPI of an EMSP: its section of the file, and a
    /// CPO that connects to it - let in where it is written down as a
    /// connection the other end makes, and not otherwise.
    /// </summary>
    public class OCPIServerTests
    {

        #region Data

        private String  directory  = default!;
        private EMSP?   emsp;

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void MakeADirectory()
        {
            directory = TestEMSPs.TemporaryDirectory("ocpi-server");
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


        #region TheSection_IsOffAndAtItsOwnPort_WhereItSaysNothing()

        /// <summary>
        /// Where the file says nothing, the server is off, at a port of its own
        /// beside the web interface's, and lets a client authenticate in every way.
        /// </summary>
        [Test]
        public void TheSection_IsOffAndAtItsOwnPort_WhereItSaysNothing()
        {

            var effective = new OCPIServerConfiguration().Effective();

            Assert.Multiple(() => {
                Assert.That(effective.IsEnabled,             Is.False);
                Assert.That(effective.TakesSSH,              Is.False);
                Assert.That(effective.TCPPort,               Is.EqualTo(IPPort.Parse(2359)));
                Assert.That(effective.TCPPort,               Is.Not.EqualTo(EMSP.DefaultHTTPPort));
                Assert.That(effective.ClientAuthentication,  Is.EqualTo(WebSocketClientAuthentications.All));
            });

        }

        #endregion

        #region TheSection_ComesBackAsItWasWritten()

        /// <summary>
        /// The section, read from the file and written again, is what was written.
        /// </summary>
        [Test]
        public void TheSection_ComesBackAsItWasWritten()
        {

            var written = new JObject(
                              new JProperty("webSocketServer", new JObject(
                                  new JProperty("enabled",               true),
                                  new JProperty("address",               "127.0.0.1"),
                                  new JProperty("port",                  4711),
                                  new JProperty("clientAuthentication",  new JArray("httpAuthOverTLS", "tlsClientCertificate")),
                                  new JProperty("ssh",                   new JObject(new JProperty("enabled", true)))
                              ))
                          );

            Assert.That(EMSPConfiguration.TryParse(written, out var configuration, out var error), Is.True, error);

            var section = configuration!.OCPIServer!;

            Assert.Multiple(() => {
                Assert.That(section.IsEnabled,              Is.True);
                Assert.That(section.TakesSSH,               Is.True);
                Assert.That(section.TCPPort,                Is.EqualTo(IPPort.Parse(4711)));
                Assert.That(section.Address?.ToString(),    Is.EqualTo("127.0.0.1"));
                Assert.That(section.ClientAuthentication,   Is.EqualTo(new[] { WebSocketClientAuthentication.HTTPAuthOverTLS, WebSocketClientAuthentication.TLSClientCertificate }));
                Assert.That(configuration.ToJSON().ToString(), Is.EqualTo(written.ToString()));
            });

        }

        #endregion

        #region TheSection_IsRefused_WhereItIsWrong(Section)

        /// <summary>
        /// A section that is wrong is refused, and says what is wrong with it.
        /// </summary>
        [TestCase("""{ "webSocketServer": "on" }""",                                   "must be a JSON object")]
        [TestCase("""{ "webSocketServer": { "clientAuthentication": [ "ssh" ] } }""",  "no way a client may authenticate")]
        [TestCase("""{ "webSocketServer": { "clientAuthentication": [ ] } }""",        "at least one way")]
        [TestCase("""{ "webSocketServer": { "address": "here" } }""",                  "must be an IP address")]
        [TestCase("""{ "webSocketServer": { "ssh": true } }""",                        "must be a JSON object")]
        public void TheSection_IsRefused_WhereItIsWrong(String  Section,
                                                        String  Says)
        {

            Assert.That(EMSPConfiguration.TryParse(JObject.Parse(Section), out _, out var error), Is.False);
            Assert.That(error, Does.Contain(Says));

        }

        #endregion


        #region (private) StartAnEMSPThatLetsCPOsIn()

        /// <summary>
        /// An EMSP of OCPI 2.2.1 whose WebSocket server of OCPI listens on
        /// 127.0.0.1, at a port of its own, letting a client in by HTTP
        /// authentication without TLS.
        /// </summary>
        private async Task<EMSP> StartAnEMSPThatLetsCPOsIn()
        {

            var configuration = TestEMSPs.Offline;

            configuration.Add(new JProperty("ocpi", new JObject(
                                  new JProperty("countryCode",  "DE"),
                                  new JProperty("partyId",      "GDF"),
                                  new JProperty("versions",     new JArray("2.2.1"))
                              )));

            configuration.Add(new JProperty("webSocketServer", new JObject(
                                  new JProperty("enabled",               true),
                                  new JProperty("address",               "127.0.0.1"),
                                  new JProperty("port",                  TestPorts.Free().ToUInt16()),
                                  new JProperty("clientAuthentication",  new JArray("httpAuthWithoutTLS"))
                              )));

            emsp = TestEMSPs.New(directory, configuration);

            await emsp.Start();

            Assert.That(emsp.OCPIServerRunning, Is.True, "The WebSocket server of OCPI is switched on in the file, and not running.");

            return emsp;

        }

        #endregion

        #region (private static) ConnectAs(EMSP, Login, Password, Subprotocol)

        /// <summary>
        /// Connect to the WebSocket port of OCPI of the given EMSP under the given
        /// login, offering the given subprotocol: the answer to the handshake.
        /// </summary>
        private static async Task<(WebSocketClient Client, HTTPResponse Response)> ConnectAs(EMSP    EMSP,
                                                                                             String  Login,
                                                                                             String  Password,
                                                                                             String  Subprotocol)
        {

            var client = new WebSocketClient(
                             URL.Parse($"ws://127.0.0.1:{EMSP.OCPIServerSettings.TCPPort}/{Login}"),
                             HTTPAuthentication:     HTTPBasicAuthentication.Create(Login, Password),
                             SecWebSocketProtocols:  [ Subprotocol ]
                         );

            var (_, response) = await client.Connect(MaxNumberOfRetries: 0);

            return (client, response);

        }

        #endregion


        #region ACPO_ThatIsWrittenDown_IsLetIn_OverTheSubprotocolOfItsVersion()

        /// <summary>
        /// A CPO written down as one that connects, with a password of its
        /// WebSocket login, connects over the subprotocol of its version of OCPI,
        /// and the EMSP sees the connection as up.
        /// </summary>
        [Test]
        public async Task ACPO_ThatIsWrittenDown_IsLetIn_OverTheSubprotocolOfItsVersion()
        {

            var emsp     = await StartAnEMSPThatLetsCPOsIn();
            var password = RandomExtensions.RandomString(32);

            Assert.That(emsp.WebSocketLogins.TrySetPasswordAuthentication(User_Id.Parse("cpo01"), password, null, I18NString.Create("tests"), out var error),
                        Is.True, error);

            Assert.That(emsp.Connections.TryAddConnection(I18NString.Create("The CPO, connecting"),
                                                          URL.Parse($"ws://127.0.0.1:{emsp.OCPIServerSettings.TCPPort}/cpo01"),
                                                          "CPO", true, null, null,
                                                          out var inbound, out error,
                                                          Inbound:      true,
                                                          OCPIVersion:  "2.2.1"),
                        Is.True, error);

            var (client, response) = await ConnectAs(emsp, "cpo01", password, OCPIWireNames.SubprotocolOf("2.2.1"));

            try
            {

                Assert.That(response.HTTPStatusCode, Is.EqualTo(HTTPStatusCode.SwitchingProtocols));

                var until = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

                while (emsp.ConnectionStates.GetValueOrDefault(inbound!)?.Status != ConnectionStatus.Connected && DateTimeOffset.UtcNow < until)
                    await Task.Delay(25);

                Assert.Multiple(() => {
                    Assert.That(emsp.ConnectionStates[inbound!].Status,  Is.EqualTo(ConnectionStatus.Connected));
                    Assert.That(emsp.OutboundConnections[inbound!],      Does.Contain("(ocpi2.2.1)"));
                });

            }
            finally
            {
                await client.Close();
            }

        }

        #endregion

        #region ACPO_DoesNotSignIn_OverOCPP()

        /// <summary>
        /// A CPO written down as one that connects, with the right password, does
        /// not get in over a subprotocol of OCPP: the port speaks OCPI, and
        /// nothing else.
        /// </summary>
        [Test]
        public async Task ACPO_DoesNotSignIn_OverOCPP()
        {

            var emsp     = await StartAnEMSPThatLetsCPOsIn();
            var password = RandomExtensions.RandomString(32);

            Assert.That(emsp.WebSocketLogins.TrySetPasswordAuthentication(User_Id.Parse("cpo03"), password, null, I18NString.Create("tests"), out var error),
                        Is.True, error);

            Assert.That(emsp.Connections.TryAddConnection(I18NString.Create("The CPO, connecting"),
                                                          URL.Parse($"ws://127.0.0.1:{emsp.OCPIServerSettings.TCPPort}/cpo03"),
                                                          "CPO", true, null, null,
                                                          out _, out error,
                                                          Inbound:      true,
                                                          OCPIVersion:  "2.2.1"),
                        Is.True, error);

            foreach (var subprotocol in new[] { "ocpp2.1", "ocpp1.6" })
            {

                var (client, response) = await ConnectAs(emsp, "cpo03", password, subprotocol);

                try
                {
                    Assert.That(response.HTTPStatusCode, Is.Not.EqualTo(HTTPStatusCode.SwitchingProtocols), $"The CPO got in over {subprotocol}.");
                }
                finally
                {
                    await client.Close();
                }

            }

        }

        #endregion

        #region ALogin_ThatIsNoCPOThatConnects_IsTurnedAway()

        /// <summary>
        /// A WebSocket login under whose name no connection is written down is
        /// turned away, however right its password is: nothing that is not
        /// written down gets in.
        /// </summary>
        [Test]
        public async Task ALogin_ThatIsNoCPOThatConnects_IsTurnedAway()
        {

            var emsp     = await StartAnEMSPThatLetsCPOsIn();
            var password = RandomExtensions.RandomString(32);

            Assert.That(emsp.WebSocketLogins.TrySetPasswordAuthentication(User_Id.Parse("cpo02"), password, null, I18NString.Create("tests"), out var error),
                        Is.True, error);

            var (client, response) = await ConnectAs(emsp, "cpo02", password, OCPIWireNames.SubprotocolOf("2.2.1"));

            try
            {
                Assert.That(response.HTTPStatusCode, Is.Not.EqualTo(HTTPStatusCode.SwitchingProtocols));
            }
            finally
            {
                await client.Close();
            }

        }

        #endregion

    }

}
