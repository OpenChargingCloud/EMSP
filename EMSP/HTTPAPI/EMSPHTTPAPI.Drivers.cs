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

using System.Diagnostics.CodeAnalysis;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.EMSP.Drivers;
using cloud.charging.open.EMSP.OCPI;

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// The part of the JSON API that is the drivers': the RFID cards they
    /// bring and the operator lets in, what they charged, and leaving.
    /// </summary>
    /// <remarks>
    /// The cards ride on the tokens' permissions, as the contracts ride on
    /// their own: running is for one's own cards - bringing one, blocking
    /// it, taking it away - and editing is for everybody's, letting a card in
    /// or turning it down among them. A route that either one opens checks
    /// for either one.
    /// </remarks>
    public partial class EMSPHTTPAPI
    {

        #region (private) RegisterDriverRoutes()

        /// <summary>
        /// Everything under /v1/cards, /v1/charging and /v1/me.
        /// </summary>
        private void RegisterDriverRoutes()
        {

            AddHandler(HTTPPath.Root + "v1/cards",                 GetCards,      HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/cards",                 PostCard,      HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/cards/{uid}",           DeleteCard,    HTTPMethod.DELETE);
            AddHandler(HTTPPath.Root + "v1/cards/{uid}/approve",   PostApprove,   HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/cards/{uid}/reject",    PostReject,    HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/cards/{uid}/block",     PostBlock,     HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/cards/{uid}/unblock",   PostUnblock,   HTTPMethod.POST);

            AddHandler(HTTPPath.Root + "v1/charging",              GetCharging,   HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "v1/me/delete",             PostDeleteMe,  HTTPMethod.POST);

        }

        #endregion


        #region (private) GetCards(Request)

        /// <summary>
        /// GET /api/v1/cards: the cards of whoever asks, or every driver's for
        /// whoever may manage the tokens.
        /// </summary>
        private Task<HTTPResponse> GetCards(HTTPRequest Request)
        {

            if (!TryGetUser(Request, out var user, out var unauthorized))
                return Task.FromResult(unauthorized);

            var ownCards    = EMSP.IsAllowed(user, [ Permission.Run (EMSPAccess.Tokens) ]);
            var everybodys  = EMSP.IsAllowed(user, [ Permission.Edit(EMSPAccess.Tokens) ]);

            if (!ownCards && !everybodys)
                return Task.FromResult(RefusePermission(Request, user, [ Permission.Run(EMSPAccess.Tokens) ], null));

            return Task.FromResult(
                       JSONResponse(
                           Request,
                           HTTPStatusCode.OK,
                           EMSP.CardsJSON(user.Id.ToString(), Everyone: everybodys)
                       )
                   );

        }

        #endregion

        #region (private) PostCard(Request)

        /// <summary>
        /// POST /api/v1/cards with {"uid", "label"}: a card of one's own,
        /// entered, waiting to be let in.
        /// </summary>
        private async Task<HTTPResponse> PostCard(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(EMSPAccess.Tokens), true, out var user, out var refused))
                return refused;

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return errorResponse;

            var result = await EMSP.RequestCardAsync(user, json.Value<String>("uid"), json.Value<String>("label"));

            return Answer(Request, user, result, HTTPStatusCode.Created, HTTPStatusCode.BadRequest);

        }

        #endregion

        #region (private) PostApprove(Request) / PostReject(Request)

        /// <summary>
        /// POST /api/v1/cards/{uid}/approve, optionally with {"whitelist"}: the
        /// operator letting a card in.
        /// </summary>
        private async Task<HTTPResponse> PostApprove(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(EMSPAccess.Tokens), true, out var user, out var refused))
                return refused;

            if (!TryParseUID(Request, out var uid, out var badRequest))
                return badRequest;

            var whitelist = Request.HTTPBody?.Length > 0 && TryParseJSONObject(Request, out var json, out _)
                                ? json.Value<String>("whitelist")
                                : null;

            var result = await EMSP.ApproveCardAsync(uid, user, whitelist);

            return Answer(Request, user, result, HTTPStatusCode.OK, HTTPStatusCode.Conflict);

        }

        /// <summary>
        /// POST /api/v1/cards/{uid}/reject, optionally with {"reason"}: the
        /// operator turning a card down.
        /// </summary>
        private async Task<HTTPResponse> PostReject(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(EMSPAccess.Tokens), true, out var user, out var refused))
                return refused;

            if (!TryParseUID(Request, out var uid, out var badRequest))
                return badRequest;

            var reason = Request.HTTPBody?.Length > 0 && TryParseJSONObject(Request, out var json, out _)
                             ? json.Value<String>("reason")
                             : null;

            var result = await EMSP.RejectCardAsync(uid, user, reason);

            return Answer(Request, user, result, HTTPStatusCode.OK, HTTPStatusCode.Conflict);

        }

        #endregion

        #region (private) PostBlock(Request) / PostUnblock(Request)

        /// <summary>
        /// POST /api/v1/cards/{uid}/block: a card that charges nowhere until it
        /// is let charge again - one's own, or anybody's for whoever may manage
        /// the tokens.
        /// </summary>
        private Task<HTTPResponse> PostBlock(HTTPRequest Request)
            => SetBlocked(Request, true);

        /// <summary>
        /// POST /api/v1/cards/{uid}/unblock: a blocked card that charges again.
        /// </summary>
        private Task<HTTPResponse> PostUnblock(HTTPRequest Request)
            => SetBlocked(Request, false);

        private async Task<HTTPResponse> SetBlocked(HTTPRequest Request, Boolean Blocked)
        {

            if (!TryOwnCard(Request, out var user, out var card, out var refused))
                return refused;

            var result = await EMSP.SetCardBlockedAsync(card, Blocked, user);

            return Answer(Request, user, result, HTTPStatusCode.OK, HTTPStatusCode.Conflict);

        }

        #endregion

        #region (private) DeleteCard(Request)

        /// <summary>
        /// DELETE /api/v1/cards/{uid}: a card taken away, its token with it -
        /// one's own, or anybody's for whoever may manage the tokens.
        /// </summary>
        private async Task<HTTPResponse> DeleteCard(HTTPRequest Request)
        {

            if (!TryOwnCard(Request, out var user, out var card, out var refused))
                return refused;

            var result = await EMSP.RemoveCardAsync(card, user);

            return Answer(Request, user, result, HTTPStatusCode.OK, HTTPStatusCode.Conflict);

        }

        #endregion


        #region (private) GetCharging(Request)

        /// <summary>
        /// GET /api/v1/charging: what whoever asks charged with their cards and
        /// contracts - the sessions and the charge detail records the partners
        /// pushed.
        /// </summary>
        private Task<HTTPResponse> GetCharging(HTTPRequest Request)
        {

            if (!TryGetUser(Request, out var user, out var unauthorized))
                return Task.FromResult(unauthorized);

            if (!EMSP.IsAllowed(user, [ Permission.Run(EMSPAccess.Tokens) ]) &&
                !EMSP.IsAllowed(user, [ Permission.Run(EMSPAccess.Contracts) ]))
                return Task.FromResult(RefusePermission(Request, user, [ Permission.Run(EMSPAccess.Tokens) ], null));

            return Task.FromResult(JSONResponse(Request, HTTPStatusCode.OK, EMSP.ChargingJSON(user)));

        }

        #endregion

        #region (private) PostDeleteMe(Request)

        /// <summary>
        /// POST /api/v1/me/delete with {"username"}: a driver deleting their
        /// own account - the username typed again, so that a stray click does
        /// not.
        /// </summary>
        private async Task<HTTPResponse> PostDeleteMe(HTTPRequest Request)
        {

            if (RefuseCrossSite(Request) is HTTPResponse crossSite)
                return crossSite;

            if (!TryGetUser(Request, out var user, out var unauthorized))
                return unauthorized;

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return errorResponse;

            if (!String.Equals(json.Value<String>("username")?.Trim(), user.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                return ErrorJSON(Request, HTTPStatusCode.BadRequest, "Type your username to confirm that your account is to be deleted.");

            var result = await EMSP.DeleteDriverAsync(user);

            if (!result.Success)
                return NotChanged(Request, result.NotSaved ? HTTPStatusCode.InternalServerError : HTTPStatusCode.Forbidden, result.Message, result.NotSaved);

            return JSONResponse(Request, HTTPStatusCode.OK, new JObject(new JProperty("message", result.Message)));

        }

        #endregion


        #region (private) TryParseUID(Request, out UID, out BadRequest)

        private static Boolean TryParseUID(HTTPRequest                             Request,
                                           [NotNullWhen(true)]  out String?        UID,
                                           [NotNullWhen(false)] out HTTPResponse?  BadRequest)
        {

            UID         = null;
            BadRequest  = null;

            var parameters = Request.ParsedURLParameters;

            if (!DriverCard.TryParseUID(parameters.Length > 0 ? parameters[0] : null, out UID, out var error))
            {
                BadRequest = ErrorJSON(Request, HTTPStatusCode.BadRequest, error);
                return false;
            }

            return true;

        }

        #endregion

        #region (private) TryOwnCard(Request, out User, out Card, out Refused)

        /// <summary>
        /// The card a state-changing request names, when whoever sends it may
        /// change it: its driver, or whoever may manage the tokens.
        /// </summary>
        private Boolean TryOwnCard(HTTPRequest                             Request,
                                   [NotNullWhen(true)]  out IUser?         User,
                                   [NotNullWhen(true)]  out DriverCard?    Card,
                                   [NotNullWhen(false)] out HTTPResponse?  Refused)
        {

            User  = null;
            Card  = null;

            if (RefuseCrossSite(Request) is HTTPResponse crossSite)
            {
                Refused = crossSite;
                return false;
            }

            if (!TryGetUser(Request, out var user, out Refused))
                return false;

            if (!TryParseUID(Request, out var uid, out Refused))
                return false;

            var everybodys  = EMSP.IsAllowed(user, [ Permission.Edit(EMSPAccess.Tokens) ]);
            var ownCards    = EMSP.IsAllowed(user, [ Permission.Run (EMSPAccess.Tokens) ]);

            if (!everybodys && !ownCards)
            {
                Refused = RefusePermission(Request, user, [ Permission.Run(EMSPAccess.Tokens) ], null);
                return false;
            }

            // Somebody else's card is no card of theirs: the same 404 as a card
            // that does not exist, so that nobody learns whose UIDs are here.
            if (!EMSP.Cards.TryGet(uid, out var card) ||
                (!everybodys && !String.Equals(card.Owner, user.Id.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                Refused = ErrorJSON(Request, HTTPStatusCode.NotFound, $"You have no card {uid}.");
                return false;
            }

            User     = user;
            Card     = card;
            Refused  = null;
            return true;

        }

        #endregion

        #region (private) Answer(Request, User, Result, Success, Failure)

        /// <summary>
        /// What came of a change to a card, with the cards as they are now - the
        /// account's own, or everybody's for whoever may manage the tokens.
        /// </summary>
        private HTTPResponse Answer(HTTPRequest          Request,
                                    IUser                User,
                                    OCPIOperationResult  Result,
                                    HTTPStatusCode       Success,
                                    HTTPStatusCode       Failure)
        {

            if (!Result.Success)
                return NotChanged(Request, Failure, Result.Message, Result.NotSaved);

            var response = new JObject(
                               new JProperty("message", Result.Message)
                           );

            if (Result.Data is not null)
                foreach (var property in Result.Data.Properties())
                    response[property.Name] = property.Value;

            response["cards"] = EMSP.CardsJSON(User.Id.ToString(), Everyone: EMSP.IsAllowed(User, [ Permission.Edit(EMSPAccess.Tokens) ]));

            return JSONResponse(Request, Success, response);

        }

        #endregion

    }

}
