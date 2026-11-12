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

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.OCPI;
using cloud.charging.open.protocols.WWCP.Node.Logging;

using V = cloud.charging.open.protocols.OCPIv2_1_1;

#endregion

namespace cloud.charging.open.EMSP.OCPI
{

    /// <summary>
    /// OCPI 2.1.1, as this EMSP speaks it: the classic one, with one party
    /// per peer and a contract written as an "auth id".
    /// </summary>
    /// <remarks>
    /// This version has no notion of several parties behind one endpoint, so
    /// the library's Common API for it is built for exactly one - this EMSP -
    /// and a remote party is a country code, a party identification and a
    /// role rather than a list of credentials roles. Tokens are sent to the
    /// partner unencoded, which is why the remote token is not base64 encoded
    /// here where it is in the versions after it.
    /// </remarks>
    public sealed class OCPIv2_1_1 : OCPIVersion
    {

        #region Data

        private readonly V.CommonAPI     commonAPI;
        private readonly V.EMSP_HTTPAPI  emspAPI;

        #endregion

        #region Properties

        public override Version_Id  Id
            => V.Version.Id;

        /// <summary>
        /// The library's Common API for this version.
        /// </summary>
        public V.CommonAPI     CommonAPI
            => commonAPI;

        /// <summary>
        /// The library's EMSP API for this version.
        /// </summary>
        public V.EMSP_HTTPAPI  EMSPAPI
            => emspAPI;

        #endregion

        #region Constructor(s)

        public OCPIv2_1_1(EMSP           EMSP,
                          CommonHTTPAPI  BaseAPI,
                          String         Directory)

            : base(EMSP)

        {

            commonAPI = new V.CommonAPI(

                            OurBusinessDetails:        EMSP.BusinessDetails,
                            OurCountryCode:            EMSP.PartyId.CountryCode,
                            OurPartyId:                EMSP.PartyId.PartyId,
                            OurRole:                   Role.EMSP,

                            BaseAPI:                   BaseAPI,
                            AdditionalURLPathPrefix:   EMSP.ExtAPI.RootPath,

                            HTTPServerName:            $"OpenChargingCloud EMSP v{EMSP.Version}",
                            HTTPServiceName:           $"OpenChargingCloud EMSP v{EMSP.Version}",

                            LoggingPath:               Directory,
                            DisableLogging:            true

                        );

            emspAPI   = new V.EMSP_HTTPAPI(

                            CommonAPI:                 commonAPI,
                            AllowDowngrades:           EMSP.OCPI.AllowDowngrades,

                            HTTPServerName:            $"OpenChargingCloud EMSP v{EMSP.Version}",
                            HTTPServiceName:           $"OpenChargingCloud EMSP v{EMSP.Version}",

                            LoggingPath:               Directory,
                            DisableLogging:            true

                        );

            WireEvents();

        }

        #endregion


        #region (private) WireEvents()

        private void WireEvents()
        {

            var log = EMSP.Log;

            commonAPI.OnPostCredentialsResponse   += new V.OCPIResponseLogHandler((timestamp, api, request, response, cancellationToken) => {
                LogHandshake("registered with", request, response);
                return Task.CompletedTask;
            });

            commonAPI.OnPutCredentialsResponse    += new V.OCPIResponseLogHandler((timestamp, api, request, response, cancellationToken) => {
                LogHandshake("renewed its registration with", request, response);
                return Task.CompletedTask;
            });

            commonAPI.OnDeleteCredentialsResponse += new V.OCPIResponseLogHandler((timestamp, api, request, response, cancellationToken) => {
                LogHandshake("unregistered from", request, response);
                return Task.CompletedTask;
            });

            // A line the file of the partners refused: the change is taken
            // back - or, a registration a partner accepted, kept and written
            // down later. The file, why and the command; not the line, which
            // holds tokens. Told under the library's lock, so it only logs.
            commonAPI.OnRemotePartyNotSaved += (timestamp, command, fileName, exception) => {
                log.Exception(exception, $"OCPI {Label}: '{fileName}' could not be written ({command})", "ocpi", "files");
                return Task.CompletedTask;
            };

            commonAPI.OnLocationAdded           += location => { LogReceived("location",             location.Id.ToString(), location.CountryCode, location.PartyId, "added");   return Task.CompletedTask; };
            commonAPI.OnLocationChanged          += location => { LogReceived("location",             location.Id.ToString(), location.CountryCode, location.PartyId, "changed"); return Task.CompletedTask; };
            commonAPI.OnTariffAdded              += tariff   => { LogReceived("tariff",               tariff.  Id.ToString(), tariff.  CountryCode, tariff.  PartyId, "added");   return Task.CompletedTask; };
            commonAPI.OnTariffChanged            += tariff   => { LogReceived("tariff",               tariff.  Id.ToString(), tariff.  CountryCode, tariff.  PartyId, "changed"); return Task.CompletedTask; };
            commonAPI.OnSessionAdded             += session  => { LogReceived("charging session",     session. Id.ToString(), session. CountryCode, session. PartyId, "added");   return Task.CompletedTask; };
            commonAPI.OnChargeDetailRecordAdded  += cdr      => { LogReceived("charge detail record", cdr.     Id.ToString(), cdr.     CountryCode, cdr.     PartyId, "added");   return Task.CompletedTask; };

            emspAPI.OnPostTokenRequest += (timestamp, sender, eventTrackingId, fromCountryCode, fromPartyId, tokenId, requestedTokenType, locationReference, cancellationToken) => {

                log.Info(
                    $"'{fromCountryCode}-{fromPartyId}' asked whether the token '{tokenId}' may charge" +
                    (locationReference.HasValue ? $" at the location '{locationReference.Value.LocationId}'" : "") +
                    " (OCPI 2.1.1).",
                    "ocpi", "tokens", "partner"
                );

                return Task.CompletedTask;

            };


            void LogHandshake(String What, V.OCPIRequest Request, V.OCPIResponse Response)
            {

                var who     = Request.RemoteParty?.Id.ToString() ?? $"somebody at {Request.HTTPRequest.RemoteSocket}";
                var worked  = Response.StatusCode == StatusCode.Success;

                if (EMSP.OCPI.Logging?.Requests == false && worked)
                    return;

                log.Log(
                    worked ? LogLevel.Notice : LogLevel.Warning,
                    worked
                        ? $"The roaming partner {who} {What} this EMSP (OCPI 2.1.1)."
                        : $"The roaming partner {who} tried to {What.Split(' ')[0]} this EMSP and was answered {Response.StatusCode}: {Response.StatusMessage} (OCPI 2.1.1).",
                    "ocpi", "credentials", "partner"
                );

            }

            void LogReceived(String What, String Id, CountryCode CountryCode, Party_Id PartyId, String How)
            {

                if (EMSP.OCPI.Logging?.Requests == false)
                    return;

                log.Info(
                    $"The {What} '{Id}' of {CountryCode}-{PartyId} was {How} (OCPI 2.1.1).",
                    "ocpi", What.Contains("session") ? "sessions" : What.Contains("record") ? "cdrs" : What.Contains("tariff") ? "tariffs" : "locations", "partner"
                );

            }

        }

        #endregion


        #region Roaming partners

        public override IEnumerable<RemotePartySummary> RemoteParties

            => commonAPI.RemoteParties.Select(party => Summarize(
                                                           party,
                                                           party.CountryCode,
                                                           party.PartyId,
                                                           party.Role,
                                                           party.BusinessDetails
                                                       ));


        public override IEnumerable<RemoteParty_Id> UnsavedRemoteParties
            => commonAPI.UnsavedRemoteParties;


        public override async Task<OCPIOperationResult> AddRemoteParty(RemotePartySpec Spec)
        {

            var businessDetails = new BusinessDetails(Spec.Name, Spec.Website);

            var result = Spec.CanRegister

                             ? await commonAPI.AddRemoteParty(
                                         CountryCode:                       Spec.CountryCode,
                                         PartyId:                           Spec.PartyId,
                                         Role:                              Spec.Role,
                                         BusinessDetails:                   businessDetails,
                                         LocalAccessToken:                  Spec.OurToken,
                                         RemoteVersionsURL:                 Spec.TheirVersionsURL!.Value,
                                         RemoteAccessToken:                 Spec.TheirToken!.Value,

                                         // OCPI 2.1.1 sends the token as it is;
                                         // the base64 encoding arrived with 2.2.
                                         RemoteAccessTokenBase64Encoding:   false,
                                         LocalAccessTokenBase64Encoding:    false,
                                         LocalAccessStatus:                 AccessStatus.ALLOWED,
                                         RemoteStatus:                      RemoteAccessStatus.ONLINE
                                     )

                             : await commonAPI.AddRemoteParty(
                                         CountryCode:                       Spec.CountryCode,
                                         PartyId:                           Spec.PartyId,
                                         Role:                              Spec.Role,
                                         BusinessDetails:                   businessDetails,
                                         LocalAccessToken:                  Spec.OurToken,
                                         LocalAccessTokenBase64Encoding:    false,
                                         LocalAccessStatus:                 AccessStatus.ALLOWED,
                                         Status:                            PartyStatus.ENABLED
                                     );

            return result.IsSuccess
                       ? OCPIOperationResult.Ok($"The roaming partner '{Spec.Id}' was added on OCPI {Label}.")
                       : OCPIOperationResult.Failed(
                             result.ErrorResponse ?? "The library declined to add the roaming partner and did not say why.",
                             result.NotSaved
                         );

        }


        public override async Task<OCPIOperationResult> RemoveRemoteParty(RemoteParty_Id Id)
        {

            var result = await commonAPI.TryRemoveRemoteParty(Id);

            return result.IsSuccess
                       ? OCPIOperationResult.Ok($"The roaming partner '{Id}' was removed from OCPI {Label}.")
                       : OCPIOperationResult.Failed(
                             result.ErrorResponse ?? "The library declined to remove the roaming partner and did not say why.",
                             result.NotSaved
                         );

        }


        public override async Task<OCPIOperationResult> Register(RemoteParty_Id Id)
        {

            if (!commonAPI.TryGetRemoteParty(Id, out var party))
                return OCPIOperationResult.Failed($"There is no roaming partner '{Id}' on OCPI 2.1.1.");

            var client = emspAPI.GetCPOClient(party, AllowCachedClients: false);

            if (client is null)
                return OCPIOperationResult.Failed($"'{Id}' has not handed out a token and a versions URL, so there is nowhere to send this EMSP's credentials.");

            var result = await client.TryRegister();

            var outcome = DescribeRegistration(
                       Id,
                       result.Response.StatusCode,
                       result.Response.StatusMessage,
                       result.Response.Data is not null,
                       result.NotSaved,
                       result.Reason
                          );

            // Registered: now the partner can be told this EMSP's tokens.
            if (outcome.Success)
                await PushAllTokens();

            return outcome;

        }

        #endregion

        #region Tokens

        public override IEnumerable<JObject> Tokens

            => commonAPI.GetTokenStatus().
                         Select(tokenStatus => WithVersion(
                                                   tokenStatus.Token.ToJSON(),
                                                   new JProperty("status", tokenStatus.Status.ToString())
                                               ));


        public override Boolean HasToken(Token_Id Id)
            => commonAPI.GetTokenStatus().Any(tokenStatus => tokenStatus.Token.Id == Id);


        public override async Task<String?> AddToken(TokenSpec Spec)
        {

            if (!Auth_Id.TryParse(Spec.ContractId, out var authId))
                return $"'{Spec.ContractId}' is not an authentication identification.";

            var result = await commonAPI.AddToken(
                                   new V.Token(
                                       EMSP.PartyId.CountryCode,
                                       EMSP.PartyId.PartyId,
                                       Spec.Id,
                                       Spec.Type,
                                       authId,
                                       Spec.Issuer,
                                       Spec.IsValid,
                                       Spec.Whitelist,
                                       Spec.VisualNumber,
                                       Spec.Language
                                   ),
                                   AllowedType.ALLOWED
                               );

            if (!result.IsSuccess)
                return result.ErrorResponse ?? "The library declined to add the token and did not say why.";

            await PushToPartners(Spec.Id);

            return null;

        }


        public override async Task<Boolean> RemoveToken(Token_Id Id)
        {

            // OCPI has no way to take a token back from a partner: they are
            // told that it is not valid any more, before it is gone here.
            await SetTokenValid(Id, false);

            var result = await commonAPI.RemoveToken(Id);

            return result.IsSuccess;

        }


        public override async Task<String?> SetTokenValid(Token_Id Id, Boolean IsValid)
        {

            if (commonAPI.GetTokenStatus().FirstOrDefault(tokenStatus => tokenStatus.Token.Id == Id) is not { } tokenStatus)
                return $"There is no token '{Id}' on OCPI {Label}.";

            var token  = tokenStatus.Token;

            // A new LastUpdated, because the library takes an update that is
            // not newer than what it holds for a downgrade and declines it.
            var result = await commonAPI.UpdateToken(
                                   new V.Token(
                                       token.CountryCode,
                                       token.PartyId,
                                       token.Id,
                                       token.Type,
                                       token.AuthId,
                                       token.Issuer,
                                       IsValid,
                                       token.WhitelistType,
                                       token.VisualNumber,
                                       token.UILanguage,
                                       token.Created,
                                       Timestamp.Now
                                   ),
                                   IsValid ? AllowedType.ALLOWED : AllowedType.BLOCKED
                               );

            if (!result.IsSuccess)
                return result.ErrorResponse ?? "The library declined to change the token and did not say why.";

            await PushToPartners(Id);

            return null;

        }

        #endregion

        #region (private) PushToPartners(Id) / PushAllTokens()

        /// <summary>
        /// Send a token of this EMSP to every roaming partner there is a way
        /// to: a CPO has to know a card before a driver holds it against one
        /// of its charging stations.
        /// </summary>
        /// <remarks>
        /// Tokens were issued, blocked and taken away here, and no partner
        /// ever heard of it: the library has the PUT of the OCPI tokens module
        /// for a CPO, and nothing called it. A partner that cannot be reached,
        /// or declines the token, is said in the log and does not undo what
        /// was done here: it is sent everything again when it is registered
        /// with, and each token again whenever it changes.
        /// </remarks>
        private async Task PushToPartners(Token_Id Id)
        {

            if (commonAPI.GetTokenStatus().FirstOrDefault(tokenStatus => tokenStatus.Token.Id == Id) is not { } status)
                return;

            foreach (var party in commonAPI.RemoteParties.ToArray())
            {

                var client = emspAPI.GetCPOClient(party);

                // No token and no versions URL of theirs yet: nowhere to send it.
                if (client is null)
                    continue;

                try
                {

                    var response = await client.PutToken(status.Token);

                    if (response.StatusCode == protocols.OCPI.StatusCode.Success)
                        EMSP.Log.Info   ($"The token '{Id}' was sent to '{party.Id}' on OCPI {Label}.", "ocpi", "tokens", "partner");
                    else
                        EMSP.Log.Warning($"The token '{Id}' was not taken by '{party.Id}' on OCPI {Label}: {response.StatusCode} {response.StatusMessage}", "ocpi", "tokens", "partner");

                }
                catch (Exception e)
                {
                    EMSP.Log.Warning($"The token '{Id}' could not be sent to '{party.Id}' on OCPI {Label}: {e.Message}", "ocpi", "tokens", "partner");
                }

            }

        }

        /// <summary>
        /// Send every token of this EMSP to its roaming partners: after a
        /// registration, which is when a partner first can be told.
        /// </summary>
        private async Task PushAllTokens()
        {
            foreach (var id in commonAPI.GetTokenStatus().Select(tokenStatus => tokenStatus.Token.Id).ToArray())
                await PushToPartners(id);
        }

        #endregion

        #region What the partners sent

        // Whose a location is: a 2.1.1 location does not say in its JSON,
        // the path it was pushed to does, and the library keeps it.
        public override IEnumerable<JObject> Locations
            => commonAPI.GetLocations().Select(location => WithVersion(
                                                               location.ToJSON(),
                                                               new JProperty("country_code",  location.CountryCode.ToString()),
                                                               new JProperty("party_id",      location.PartyId.    ToString())
                                                           ));

        public override IEnumerable<JObject> Tariffs
            => commonAPI.GetTariffs().  Select(tariff   => WithVersion(tariff.  ToJSON()));

        public override IEnumerable<JObject> Sessions
            => commonAPI.GetSessions(). Select(session  => WithVersion(session. ToJSON()));

        public override IEnumerable<JObject> CDRs
            => commonAPI.GetCDRs().     Select(cdr      => WithVersion(cdr.     ToJSON()));

        #endregion

        #region Modules

        protected override IEnumerable<String> Modules
            => [ "locations", "tariffs", "sessions", "cdrs", "tokens", "commands" ];

        #endregion

    }

}
