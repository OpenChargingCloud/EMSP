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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.EMSP.Contracts;

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// The part of the JSON API that is about contract certificates: what a
    /// driver holds and asks for, what the operator sees of all of them, and
    /// the MO root everybody needs.
    /// </summary>
    /// <remarks>
    /// Two permissions on the contracts, asked about separately: running is
    /// for one's own and editing is for everybody's. A route that either one
    /// opens checks for either one rather than for both, which is what a
    /// single question to the node would ask for.
    /// </remarks>
    public partial class EMSPHTTPAPI
    {

        #region (private) RegisterContractRoutes()

        /// <summary>
        /// Everything under /v1/contracts.
        /// </summary>
        private void RegisterContractRoutes()
        {

            AddHandler(HTTPPath.Root + "v1/contracts",                  GetContracts,   HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/contracts",                  PostContract,   HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/contracts/mo-root.pem",      GetMORoot,      HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/contracts/{emaid}/revoke",   PostRevoke,     HTTPMethod.POST);

        }

        #endregion


        #region (private) GetContracts(Request)

        /// <summary>
        /// GET /api/v1/contracts: the contracts of whoever asks, or every
        /// contract for whoever may manage them - with the MO root and the
        /// sub-CAs.
        /// </summary>
        private Task<HTTPResponse> GetContracts(HTTPRequest Request)
        {

            if (!TryGetUser(Request, out var user, out var unauthorized))
                return Task.FromResult(unauthorized);

            var ownContracts   = EMSP.IsAllowed(user, [ Permission.Run (EMSPAccess.Contracts) ]);
            var everybodys     = EMSP.IsAllowed(user, [ Permission.Edit(EMSPAccess.Contracts) ]);

            if (!ownContracts && !everybodys)
                return Task.FromResult(RefusePermission(Request, user, [ Permission.Run(EMSPAccess.Contracts) ], null));

            return Task.FromResult(
                       JSONResponse(
                           Request,
                           HTTPStatusCode.OK,
                           EMSP.ContractsJSON(user.Id.ToString(), Everyone: everybodys)
                       )
                   );

        }

        #endregion

        #region (private) PostContract(Request)

        /// <summary>
        /// POST /api/v1/contracts with {"csr"}: a contract certificate for
        /// the key in the request, made out to a fresh eMAID.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The answer carries the certificate, the two sub-CAs and the MO
        /// root, because the browser bundles the first two with the key it
        /// kept and hands the driver the third for the vehicle's trust store.
        /// </para>
        /// <para>
        /// A request refused for what it is gets 400; one the registry could
        /// not write down gets 500, and nothing was issued - as the node
        /// answers a change its files cannot take.
        /// </para>
        /// </remarks>
        private async Task<HTTPResponse> PostContract(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(EMSPAccess.Contracts), true, out var user, out var refused))
                return refused;

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return errorResponse;

            var csr = json.Value<String>("csr");

            if (String.IsNullOrWhiteSpace(csr))
                return ErrorJSON(Request, HTTPStatusCode.BadRequest, "A 'csr' is required: a PKCS#10 certificate signing request in PEM, signed with the key it carries.");

            var result = await EMSP.IssueContractAsync(user, csr);

            if (!result.Success)
                return NotChanged(Request, HTTPStatusCode.BadRequest, result.Message, result.NotSaved);

            var response = new JObject(
                               new JProperty("message",  result.Message)
                           );

            if (result.Data is not null)
                foreach (var property in result.Data.Properties())
                    response[property.Name] = property.Value;

            response["contracts"] = EMSP.ContractsJSON(user.Id.ToString(), Everyone: EMSP.IsAllowed(user, [ Permission.Edit(EMSPAccess.Contracts) ]));

            return JSONResponse(Request, HTTPStatusCode.Created, response);

        }

        #endregion

        #region (private) PostRevoke(Request)

        /// <summary>
        /// POST /api/v1/contracts/{emaid}/revoke: take a contract back - one's
        /// own, or anybody's for whoever may manage them.
        /// </summary>
        /// <remarks>
        /// One taken back already gets 409; one the registry could not write
        /// down as taken back gets 500, and is still good, its token too.
        /// </remarks>
        private async Task<HTTPResponse> PostRevoke(HTTPRequest Request)
        {

            if (RefuseCrossSite(Request) is HTTPResponse crossSite)
                return crossSite;

            if (!TryGetUser(Request, out var user, out var unauthorized))
                return unauthorized;

            var parameters = Request.ParsedURLParameters;

            if (parameters.Length == 0)
                return ErrorJSON(Request, HTTPStatusCode.BadRequest, "An eMAID is required.");

            if (!EMAId.TryParse(parameters[0], out var emaId, out var parseError))
                return ErrorJSON(Request, HTTPStatusCode.BadRequest, parseError);

            if (!EMSP.Contracts.TryGet(emaId, out var contract))
                return ErrorJSON(Request, HTTPStatusCode.NotFound, $"There is no contract {emaId}.");

            var everybodys   = EMSP.IsAllowed(user, [ Permission.Edit(EMSPAccess.Contracts) ]);
            var ownContracts = EMSP.IsAllowed(user, [ Permission.Run (EMSPAccess.Contracts) ]);
            var itsOwner     = String.Equals(contract.Owner, user.Id.ToString(), StringComparison.OrdinalIgnoreCase);

            if (!everybodys && !(ownContracts && itsOwner))
                return RefusePermission(Request, user, [ Permission.Edit(EMSPAccess.Contracts) ], $"The contract {emaId} belongs to somebody else.");

            var result = await EMSP.RevokeContractAsync(emaId, user);

            if (!result.Success)
                return NotChanged(Request, HTTPStatusCode.Conflict, result.Message, result.NotSaved);

            var response = new JObject(
                               new JProperty("message",    result.Message),
                               new JProperty("contracts",  EMSP.ContractsJSON(user.Id.ToString(), Everyone: everybodys))
                           );

            if (result.Data is not null)
                foreach (var property in result.Data.Properties())
                    response[property.Name] = property.Value;

            return JSONResponse(Request, HTTPStatusCode.OK, response);

        }

        #endregion

        #region (private) GetMORoot(Request)

        /// <summary>
        /// GET /api/v1/contracts/mo-root.pem: the MO root certificate as a
        /// file - what a vehicle, a station or a CSMS is pointed at.
        /// </summary>
        /// <remarks>
        /// Behind the sign-in like everything else below /api, although the
        /// root is public by nature: what it guards is not the certificate but
        /// the convention that nothing here answers strangers.
        /// </remarks>
        private Task<HTTPResponse> GetMORoot(HTTPRequest Request)
        {

            if (!TryGetUser(Request, out _, out var unauthorized))
                return Task.FromResult(unauthorized);

            return Task.FromResult(
                       new HTTPResponse.Builder(Request) {
                           HTTPStatusCode  = HTTPStatusCode.OK,
                           ContentType     = HTTPContentType.Text.PLAIN,
                           Content         = Encoding.UTF8.GetBytes(EMSP.ContractCA.RootPEM),
                           CacheControl    = "no-store"
                       }.WithCommonSecurityHeaders().AsImmutable
                   );

        }

        #endregion

    }

}
