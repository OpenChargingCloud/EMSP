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

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Logging;
using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// The JSON API the browser talks to, registered at "/api": what every
    /// node has - see <see cref="NodeHTTPAPI"/> - and what only an EMSP has,
    /// its roaming partners, the tokens and what the partners pushed, and the
    /// contracts.
    /// </summary>
    /// <remarks>
    /// The sign-out and who is signed in, the status and the clock, the
    /// configuration, name resolution and the time servers, the certificate
    /// store, the log and the event stream are the node's, as they are the
    /// vehicle's and the local controller's; this class used to have its own
    /// copy of all of them. What is left here is registered on top - see
    /// EMSPHTTPAPI.OCPI.cs and EMSPHTTPAPI.Contracts.cs - and what an EMSP asks
    /// of the node's routes beyond a sign-in: the log, its event stream and the
    /// clock are the operator's, not the drivers'.
    /// </remarks>
    public partial class EMSPHTTPAPI : NodeHTTPAPI
    {

        #region Properties

        /// <summary>
        /// The EMSP this API speaks for.
        /// </summary>
        public EMSP  EMSP  { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create and register the JSON API within the given HTTP server.
        /// </summary>
        /// <param name="HTTPServer">The HTTP server.</param>
        /// <param name="EMSP">The EMSP this API speaks for.</param>
        /// <param name="ExtAPI">The accounts and the groups they are in.</param>
        /// <param name="Log">Everything that happens inside this EMSP.</param>
        /// <param name="APIPath">The root path of the API, the node's own by default.</param>
        /// <param name="Version">The version reported by the status resource.</param>
        public EMSPHTTPAPI(HTTPServer  HTTPServer,
                           EMSP        EMSP,
                           HTTPExtAPI  ExtAPI,
                           EventLog    Log,
                           HTTPPath?   APIPath   = null,
                           String?     Version   = null)

            : base(HTTPServer,
                   EMSP,
                   ExtAPI,
                   Log,
                   APIPath,
                   Version ?? typeof(EMSPHTTPAPI).Assembly.GetName().Version?.ToString(3) ?? "0.0.0")

        {

            this.EMSP = EMSP;

            // The OCPI side: the roaming partners, the tokens and what the
            // partners pushed; see EMSPHTTPAPI.OCPI.cs.
            RegisterOCPIRoutes();

            // The contracts: what a driver holds and asks for, and the MO
            // root; see EMSPHTTPAPI.Contracts.cs.
            RegisterContractRoutes();

        }

        #endregion


        #region (protected override) ProductStatus()

        /// <summary>
        /// Who this EMSP is in OCPI: its party, after the version.
        /// </summary>
        protected override IEnumerable<JProperty> ProductStatus()
        {
            yield return new JProperty("partyId", EMSP.PartyIdText);
        }

        #endregion

        #region (protected override) ToReadTheLog

        /// <summary>
        /// The log and its event stream need "configuration:read", which the
        /// viewer, the operator and the administrators have and a driver does
        /// not.
        /// </summary>
        /// <remarks>
        /// An EMSP's accounts include its customers, who sign themselves up,
        /// and the log is the operator's and not theirs. The event stream asks
        /// it again before every entry, as it asks the sign-in: an account
        /// taken out of the operator's group is not sent the log on the one
        /// request it would otherwise not be refused on, and the Logs page,
        /// told 403, says that this account may no longer read it rather than
        /// reconnecting every few seconds.
        /// </remarks>
        protected override Permission? ToReadTheLog

            => Permission.Read(NodeResources.Configuration);

        #endregion

        #region (protected override) ToReadTheClock

        /// <summary>
        /// The clock needs "configuration:read" as well: what the time is worth
        /// is the first card of the NTS page, an operator's page, and was
        /// read with the configuration when it was one of its routes.
        /// </summary>
        protected override Permission? ToReadTheClock

            => Permission.Read(NodeResources.Configuration);

        #endregion

    }

}
