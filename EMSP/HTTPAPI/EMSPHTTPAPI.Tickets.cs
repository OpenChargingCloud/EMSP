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

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// The part of the JSON API that is a driver's long-term keys and the
    /// charging tickets signed with them.
    /// </summary>
    /// <remarks>
    /// Running is for one's own keys and tickets, editing for everybody's -
    /// as the contracts have it. A ticket request is a COSE_Sign: sent as
    /// CBOR, or in JSON as {"request": "&lt;base64&gt;"}; the ticket comes
    /// back the same way it was asked for.
    /// </remarks>
    public partial class EMSPHTTPAPI
    {

        #region (private) RegisterTicketRoutes()

        /// <summary>
        /// Everything under /v1/account-keys and /v1/tickets.
        /// </summary>
        private void RegisterTicketRoutes()
        {

            AddHandler(HTTPPath.Root + "v1/account-keys",               GetAccountKeys,     HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/account-keys",               PostAccountKey,     HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/account-keys/{id}/revoke",   PostRevokeKey,      HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/account-keys/ca.pem",        GetAccountCA,       HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "v1/tickets",                    GetTickets,         HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/tickets",                    PostTicket,         HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/tickets/issuer.pem",         GetTicketIssuer,    HTTPMethod.GET);

        }

        #endregion


        #region (private) GetAccountKeys(Request)

        /// <summary>
        /// GET /api/v1/account-keys: the account certificates of whoever asks,
        /// or everybody's for whoever may manage them - with the account CA.
        /// </summary>
        private Task<HTTPResponse> GetAccountKeys(HTTPRequest Request)
        {

            if (!TryGetUser(Request, out var user, out var unauthorized))
                return Task.FromResult(unauthorized);

            var own         = EMSP.IsAllowed(user, [ Permission.Run (EMSPAccess.Tickets) ]);
            var everybodys  = EMSP.IsAllowed(user, [ Permission.Edit(EMSPAccess.Tickets) ]);

            if (!own && !everybodys)
                return Task.FromResult(RefusePermission(Request, user, [ Permission.Run(EMSPAccess.Tickets) ], null));

            return Task.FromResult(JSONResponse(Request, HTTPStatusCode.OK, EMSP.AccountCertificatesJSON(user.Id.ToString(), everybodys)));

        }

        #endregion

        #region (private) PostAccountKey(Request)

        /// <summary>
        /// POST /api/v1/account-keys with {"csr", "label"}: an account
        /// certificate for the key in the request.
        /// </summary>
        private async Task<HTTPResponse> PostAccountKey(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(EMSPAccess.Tickets), true, out var user, out var refused))
                return refused;

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return errorResponse;

            var result = await EMSP.IssueAccountCertificateAsync(user, json.Value<String>("csr"), json.Value<String>("label"));

            if (!result.Success)
                return NotChanged(Request, HTTPStatusCode.BadRequest, result.Message, result.NotSaved);

            var response = new JObject(new JProperty("message", result.Message));

            foreach (var property in result.Data!.Properties())
                response[property.Name] = property.Value;

            response["accountKeys"] = EMSP.AccountCertificatesJSON(user.Id.ToString(), EMSP.IsAllowed(user, [ Permission.Edit(EMSPAccess.Tickets) ]));

            return JSONResponse(Request, HTTPStatusCode.Created, response);

        }

        #endregion

        #region (private) PostRevokeKey(Request)

        /// <summary>
        /// POST /api/v1/account-keys/{id}/revoke: an account certificate taken
        /// back - one's own, or anybody's for whoever may manage them.
        /// </summary>
        private async Task<HTTPResponse> PostRevokeKey(HTTPRequest Request)
        {

            if (RefuseCrossSite(Request) is HTTPResponse crossSite)
                return crossSite;

            if (!TryGetUser(Request, out var user, out var unauthorized))
                return unauthorized;

            var own         = EMSP.IsAllowed(user, [ Permission.Run (EMSPAccess.Tickets) ]);
            var everybodys  = EMSP.IsAllowed(user, [ Permission.Edit(EMSPAccess.Tickets) ]);

            if (!own && !everybodys)
                return RefusePermission(Request, user, [ Permission.Run(EMSPAccess.Tickets) ], null);

            var id = Request.ParsedURLParameters.Length > 0 ? Request.ParsedURLParameters[0] : "";

            // Somebody else's is none of theirs: the same 404 as one that does
            // not exist.
            if (!EMSP.AccountCertificates.TryGet(id, out var certificate) ||
                (!everybodys && !String.Equals(certificate.Owner, user.Id.ToString(), StringComparison.OrdinalIgnoreCase)))
                return ErrorJSON(Request, HTTPStatusCode.NotFound, $"You have no account certificate {id}.");

            var result = await EMSP.RevokeAccountCertificateAsync(certificate.Id, user);

            if (!result.Success)
                return NotChanged(Request, HTTPStatusCode.Conflict, result.Message, result.NotSaved);

            return JSONResponse(Request, HTTPStatusCode.OK, new JObject(
                       new JProperty("message",      result.Message),
                       new JProperty("accountKeys",  EMSP.AccountCertificatesJSON(user.Id.ToString(), everybodys))
                   ));

        }

        #endregion


        #region (private) GetTickets(Request)

        /// <summary>
        /// GET /api/v1/tickets: the charging tickets of whoever asks, or
        /// everybody's for whoever may manage them - with the ticket issuer.
        /// </summary>
        private Task<HTTPResponse> GetTickets(HTTPRequest Request)
        {

            if (!TryGetUser(Request, out var user, out var unauthorized))
                return Task.FromResult(unauthorized);

            var own         = EMSP.IsAllowed(user, [ Permission.Run (EMSPAccess.Tickets) ]);
            var everybodys  = EMSP.IsAllowed(user, [ Permission.Edit(EMSPAccess.Tickets) ]);

            if (!own && !everybodys)
                return Task.FromResult(RefusePermission(Request, user, [ Permission.Run(EMSPAccess.Tickets) ], null));

            return Task.FromResult(JSONResponse(Request, HTTPStatusCode.OK, EMSP.TicketsJSON(user.Id.ToString(), everybodys)));

        }

        #endregion

        #region (private) PostTicket(Request)

        /// <summary>
        /// POST /api/v1/tickets: a ticket request, a COSE_Sign - as CBOR
        /// (application/cose or application/cbor), or in JSON as
        /// {"request": "&lt;base64&gt;"} - answered with the ticket signed by
        /// this EMSP, the same way.
        /// </summary>
        private async Task<HTTPResponse> PostTicket(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(EMSPAccess.Tickets), true, out var user, out var refused))
                return refused;

            var asCBOR   = Request.ContentType?.MediaType is "application/cose" or "application/cbor";
            Byte[] bytes;

            if (asCBOR)
                bytes = Request.HTTPBody?.ToArray() ?? [];

            else
            {

                if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                    return errorResponse;

                try
                {
                    bytes = Convert.FromBase64String(json.Value<String>("request") ?? "");
                }
                catch (FormatException)
                {
                    return ErrorJSON(Request, HTTPStatusCode.BadRequest, "A 'request' is required: the COSE_Sign of the ticket, in Base64.");
                }

            }

            if (bytes.Length == 0)
                return ErrorJSON(Request, HTTPStatusCode.BadRequest, "A ticket request is required: the COSE_Sign of the ticket.");

            if (bytes.Length > 4096)
                return ErrorJSON(Request, HTTPStatusCode.BadRequest, "A ticket request is a few hundred bytes, and this is more than four thousand.");

            var result = await EMSP.IssueTicketAsync(user, bytes);

            if (!result.Success)
                return NotChanged(Request, HTTPStatusCode.BadRequest, result.Message, result.NotSaved);

            if (asCBOR)
                return new HTTPResponse.Builder(Request) {
                           HTTPStatusCode  = HTTPStatusCode.Created,
                           ContentType     = HTTPContentType.TryParse("application/cose", out var cose) ? cose : HTTPContentType.Application.OCTETSTREAM,
                           Content         = Convert.FromBase64String(result.Data!.Value<String>("ticket")!),
                           CacheControl    = "no-store"
                       }.WithCommonSecurityHeaders().AsImmutable;

            var response = new JObject(new JProperty("message", result.Message));

            foreach (var property in result.Data!.Properties())
                response[property.Name] = property.Value;

            return JSONResponse(Request, HTTPStatusCode.Created, response);

        }

        #endregion


        #region (private) GetAccountCA(Request) / GetTicketIssuer(Request)

        /// <summary>
        /// GET /api/v1/account-keys/ca.pem: what the account certificates are
        /// signed by.
        /// </summary>
        private Task<HTTPResponse> GetAccountCA(HTTPRequest Request)
            => PEMFile(Request, EMSP.AccountCA.CertificatePEM);

        /// <summary>
        /// GET /api/v1/tickets/issuer.pem: what a charge point operator
        /// believes this EMSP's charging tickets by.
        /// </summary>
        private Task<HTTPResponse> GetTicketIssuer(HTTPRequest Request)
            => PEMFile(Request, EMSP.TicketIssuer.CertificatePEM);

        /// <summary>
        /// A certificate as a file, behind the sign-in like everything below
        /// /api - as the MO root is.
        /// </summary>
        private Task<HTTPResponse> PEMFile(HTTPRequest Request, String PEM)
        {

            if (!TryGetUser(Request, out _, out var unauthorized))
                return Task.FromResult(unauthorized);

            return Task.FromResult(
                       new HTTPResponse.Builder(Request) {
                           HTTPStatusCode  = HTTPStatusCode.OK,
                           ContentType     = HTTPContentType.Text.PLAIN,
                           Content         = Encoding.UTF8.GetBytes(PEM),
                           CacheControl    = "no-store"
                       }.WithCommonSecurityHeaders().AsImmutable
                   );

        }

        #endregion

    }

}
