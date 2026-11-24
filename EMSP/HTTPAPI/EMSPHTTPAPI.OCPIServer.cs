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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// The part of the JSON API about the CPOs that connect over a WebSocket
    /// or SSH: their connections, their logins and what they sign in with,
    /// the chains a client certificate may lead to, and the server they
    /// connect to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The routes of the connections and of the logins are every node's - see
    /// <see cref="NodeHTTPAPI.RegisterConnectionRoutes"/> and
    /// <see cref="NodeHTTPAPI.RegisterWebSocketLoginRoutes"/> in WWCP_Node -,
    /// so the EMSP's pages and a CSMS's are the same pages. All of them at the
    /// resource <see cref="EMSPAccess.Partners"/>: a CPO let in is a CPO this
    /// EMSP believes, as a partner with an access token is.
    /// </para>
    /// <para>
    /// A CPO is written down twice, as everybody who connects to a node is: as
    /// an inbound connection of the type "CPO" - which says that it may
    /// connect, and which is switched off to shut it out - and as a login,
    /// which says what it signs in with. A CPO this EMSP connects to is a
    /// connection of the same type, made by this EMSP.
    /// </para>
    /// </remarks>
    public partial class EMSPHTTPAPI
    {

        #region Data

        /// <summary>
        /// Where the routes of the WebSocket server of OCPI and its logins are.
        /// </summary>
        public static readonly HTTPPath OCPIServerPath = HTTPPath.Root + "v1/configuration/ocpi-server";

        #endregion


        #region (private) RegisterOCPIServerRoutes()

        private void RegisterOCPIServerRoutes()
        {

            RegisterConnectionRoutes(EMSPAccess.Partners);

            AddHandler(OCPIServerPath, GetOCPIServer, HTTPMethod.GET);

            RegisterWebSocketLoginRoutes(
                new WebSocketLoginRoutes(
                    OCPIServerPath,
                    () => EMSP.WebSocketLogins,
                    () => EMSP.ClientTrust,
                    EMSPAccess.Partners,
                    "charge point operator",
                    [ "ocpi", "cpo" ]
                )
            );

        }

        #endregion

        #region (private) GetOCPIServer(Request)

        /// <summary>
        /// GET /api/v1/configuration/ocpi-server: the server the CPOs connect
        /// to - as the file says it, and as it is -, and who is connected now.
        /// </summary>
        /// <remarks>
        /// The server is changed in the "webSocketServer" section of the file,
        /// and the EMSP started again.
        /// </remarks>
        private Task<HTTPResponse> GetOCPIServer(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(EMSPAccess.Partners), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, new JObject(
                           new JProperty("configuration",  EMSP.OCPIServerSettings.ToJSON()),
                           new JProperty("state",          new JObject(
                               new JProperty("running",    EMSP.OCPIServerRunning),
                               new JProperty("tls",        EMSP.OCPIServerTLS),
                               new JProperty("ssh",        EMSP.OCPIOverSSHServed),
                               new JProperty("sshPort",    EMSP.SSHPort?.ToUInt16())
                           )),
                           new JProperty("connected",      new JArray(EMSP.CPOsIn))
                       ))
                   );

        }

        #endregion

    }

}
