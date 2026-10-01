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

using System.Text;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// The three routes of a CPO that the credentials handshake touches: the
    /// versions, the details of one, and the credentials endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both directions of the peering need it. When this EMSP is the one that
    /// walks, all three are called; when the CPO walks, only the first two
    /// are, because it is this EMSP that calls back to see whether the URL in
    /// the credentials it was sent answers at all.
    /// </para>
    /// <para>
    /// One version, in the shape of that version: OCPI 2.1.1 lists its
    /// endpoints without a role, and its credentials name one party rather
    /// than roles. It records what it was sent rather than asserting anything
    /// itself.
    /// </para>
    /// </remarks>
    internal sealed class StubCPO : IAsyncDisposable
    {

        #region Data

        /// <summary>The token the CPO handed out for this EMSP to call it with.</summary>
        public const String TokenA = "stub-cpo-token-a";

        /// <summary>The token the CPO hands out in its credentials.</summary>
        public const String TokenC = "stub-cpo-token-c";

        private readonly HTTPServer server;

        #endregion

        #region Properties

        /// <summary>The single OCPI version it offers.</summary>
        public String        Version                { get; }

        /// <summary>Where the stub is: on the port the server was given as it bound it.</summary>
        public String        Origin
            => $"http://127.0.0.1:{server.TCPPort}";

        /// <summary>Where it says its versions are.</summary>
        public String        VersionsURL
            => $"{Origin}/versions";

        /// <summary>What this EMSP POSTed to its credentials endpoint, if anything.</summary>
        public JObject?      ReceivedCredentials    { get; private set; }

        /// <summary>Every token it was presented with, decoded.</summary>
        public List<String>  TokensSeen             { get; } = [];

        /// <summary>
        /// Something to do once this EMSP's credentials have arrived, before
        /// the answer goes back: a test making a file unwritable there, say.
        /// </summary>
        public Action?       WhenCredentialsArrive  { get; set; }

        #endregion

        #region Constructor(s)

        private StubCPO(HTTPServer  Server,
                        String      Version)
        {
            this.server   = Server;
            this.Version  = Version;
        }

        #endregion


        #region (static) Start(Version = "2.2.1")

        /// <summary>
        /// Start one that offers the given version.
        /// </summary>
        public static async Task<StubCPO> Start(String Version = "2.2.1")
        {

            // On port 0: the operating system picks the port as the server
            // binds it, so that no other test run on this machine can take
            // it in between, and the EMSP is told the port the stub got.
            // No gap, and so nothing for the kit's StartedOnFreshPorts to
            // close - which starts nodes, and this is none.
            var server  = new HTTPServer(IPAddress: IPv4Address.Localhost, TCPPort: IPPort.Zero);
            var stub    = new StubCPO(server, Version);
            var api     = server.AddHTTPAPI(HTTPPath.Root);

            api.AddHandler(
                HTTPPath.Parse("/versions"),
                request => {
                    stub.Remember(request);
                    return Task.FromResult(JSON(request, new JArray(
                        new JObject(
                            new JProperty("version",  Version),
                            new JProperty("url",      $"{stub.Origin}/versions/{Version}")
                        )
                    )));
                },
                HTTPMethod.GET
            );

            api.AddHandler(
                HTTPPath.Parse($"/versions/{Version}"),
                request => {
                    stub.Remember(request);
                    return Task.FromResult(JSON(request, new JObject(
                        new JProperty("version",    Version),
                        new JProperty("endpoints",  new JArray(
                            Version == "2.1.1"
                                ? new JObject(
                                      new JProperty("identifier",  "credentials"),
                                      new JProperty("url",         $"{stub.Origin}/{Version}/credentials")
                                  )
                                : new JObject(
                                      new JProperty("identifier",  "credentials"),
                                      new JProperty("role",        "RECEIVER"),
                                      new JProperty("url",         $"{stub.Origin}/{Version}/credentials")
                                  )
                        ))
                    )));
                },
                HTTPMethod.GET
            );

            api.AddHandler(
                HTTPPath.Parse($"/{Version}/credentials"),
                request => {
                    stub.Remember(request);
                    stub.ReceivedCredentials = JObject.Parse(request.HTTPBodyAsUTF8String ?? "{}");
                    stub.WhenCredentialsArrive?.Invoke();
                    return Task.FromResult(JSON(request, stub.Credentials));
                },
                HTTPMethod.POST
            );

            await server.Start();

            return stub;

        }

        #endregion

        #region Credentials / CredentialsBody()

        /// <summary>
        /// Its credentials, in the shape of its version: its token, where its
        /// versions are, and who it is.
        /// </summary>
        public JObject Credentials

            => Version == "2.1.1"
                   ? new (
                         new JProperty("token",             TokenC),
                         new JProperty("url",               VersionsURL),
                         new JProperty("business_details",  new JObject(
                             new JProperty("name",  "Stub CPO")
                         )),
                         new JProperty("party_id",          "GEF"),
                         new JProperty("country_code",      "DE")
                     )
                   : new (
                         new JProperty("token",  TokenC),
                         new JProperty("url",    VersionsURL),
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
                     );

        /// <summary>
        /// Its credentials as the body of the POST with which it registers at
        /// this EMSP.
        /// </summary>
        public StringContent CredentialsBody()

            => new (Credentials.ToString(),
                    Encoding.UTF8,
                    "application/json");

        #endregion


        #region (private) Remember(Request)

        private void Remember(HTTPRequest Request)
        {

            // The token as this EMSP presented it: base64 in OCPI 2.2 and
            // later, which is what this undoes so that an assertion can read
            // it.
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

        #endregion

        #region (private static) JSON(Request, Data)

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

        #endregion

        #region DisposeAsync()

        public async ValueTask DisposeAsync()
        {
            await server.Stop();
        }

        #endregion

    }

}
