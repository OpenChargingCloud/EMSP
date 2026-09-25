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

using V = cloud.charging.open.protocols.OCPIv2_2_1;

#endregion

namespace cloud.charging.open.EMSP.OCPI
{

    /// <summary>
    /// OCPI 2.2.1, as this EMSP speaks it.
    /// </summary>
    /// <remarks>
    /// In this version the library keeps what the partners pushed in the EMSP
    /// API rather than in the Common API - "remote" locations, sessions and
    /// so on - which is where the events about them come from as well.
    /// </remarks>
    public sealed class OCPIv2_2_1 : OCPIVersion
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

        public OCPIv2_2_1(EMSP           EMSP,
                          CommonHTTPAPI  BaseAPI,
                          String         Directory)

            : base(EMSP)

        {

            commonAPI = new V.CommonAPI(

                            OurPartyData:              [
                                                           new V.PartyData(
                                                               EMSP.PartyId,
                                                               Role.EMSP,
                                                               EMSP.BusinessDetails,
                                                               EMSP.OCPI.AllowDowngrades
                                                           )
                                                       ],
                            DefaultPartyId:            EMSP.PartyId,

                            BaseAPI:                   BaseAPI,

                            // Only in the URLs it advertises, never in the paths
                            // it serves: see EMSP.OCPI.cs for why.
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
            SyncRemoteCPOs();

        }

        #endregion


        #region (private) SyncRemoteCPOs()

        /// <summary>
        /// Every partner that may push - a CPO, or a hub standing in for
        /// several - has to be known to the EMSP API as a remote CPO as well,
        /// or what it pushes is refused as coming from "an unknown party".
        /// </summary>
        /// <remarks>
        /// The Common API reads its remote parties back from its own file at
        /// every start; the EMSP API's registry of remote CPOs is not read
        /// back, so it is rebuilt here from what the Common API remembers. A
        /// partner that was added and this EMSP restarted would otherwise be
        /// able to sign in and unable to push - and the refusal would name a
        /// party this EMSP plainly lists.
        /// </remarks>
        private void SyncRemoteCPOs()
        {

            foreach (var party in commonAPI.RemoteParties)
            {

                foreach (var role in party.Roles.Where(role => role.Role == Role.CPO || role.Role == Role.HUB))
                {

                    if (emspAPI.HasRemoteCPO(role.PartyId))
                        continue;

                    emspAPI.AddRemoteCPO(
                        role.PartyId,
                        role.Role,
                        role.BusinessDetails,
                        role.AllowDowngrades
                    ).GetAwaiter().GetResult();

                }

            }

        }

        #endregion


        #region (private) WireEvents()

        /// <summary>
        /// What of this version ends up in the event log.
        /// </summary>
        private void WireEvents()
        {

            var log = EMSP.Log;

            #region The credentials handshake

            commonAPI.OnPostCredentialsHTTPResponse   += new V.OCPIResponseLogHandler((timestamp, api, request, response, cancellationToken) => {
                LogHandshake("registered with", request, response);
                return Task.CompletedTask;
            });

            commonAPI.OnPutCredentialsHTTPResponse    += new V.OCPIResponseLogHandler((timestamp, api, request, response, cancellationToken) => {
                LogHandshake("renewed its registration with", request, response);
                return Task.CompletedTask;
            });

            commonAPI.OnDeleteCredentialsHTTPResponse += new V.OCPIResponseLogHandler((timestamp, api, request, response, cancellationToken) => {
                LogHandshake("unregistered from", request, response);
                return Task.CompletedTask;
            });

            #endregion

            #region What the partners push

            emspAPI.OnRemoteLocationAdded            += location => { LogReceived("location",             location.Id.ToString(), location.CountryCode, location.PartyId, "added");   return Task.CompletedTask; };
            emspAPI.OnRemoteLocationChanged          += location => { LogReceived("location",             location.Id.ToString(), location.CountryCode, location.PartyId, "changed"); return Task.CompletedTask; };
            emspAPI.OnRemoteTariffAdded              += tariff   => { LogReceived("tariff",               tariff.  Id.ToString(), tariff.  CountryCode, tariff.  PartyId, "added");   return Task.CompletedTask; };
            emspAPI.OnRemoteTariffChanged            += tariff   => { LogReceived("tariff",               tariff.  Id.ToString(), tariff.  CountryCode, tariff.  PartyId, "changed"); return Task.CompletedTask; };
            emspAPI.OnRemoteSessionAdded             += session  => { LogReceived("charging session",     session. Id.ToString(), session. CountryCode, session. PartyId, "added");   return Task.CompletedTask; };
            emspAPI.OnRemoteSessionChanged           += session  => { LogReceived("charging session",     session. Id.ToString(), session. CountryCode, session. PartyId, "changed"); return Task.CompletedTask; };
            emspAPI.OnRemoteChargeDetailRecordAdded  += cdr      => { LogReceived("charge detail record", cdr.     Id.ToString(), cdr.     CountryCode, cdr.     PartyId, "added");   return Task.CompletedTask; };

            #endregion

            #region A partner asking about a token

            emspAPI.OnPostTokenRequest += (timestamp, sender, eventTrackingId, remotePartyId, from, to, tokenId, requestedTokenType, locationReference, cancellationToken) => {

                log.Info(
                    $"'{remotePartyId}' asked whether the token '{tokenId}' may charge" +
                    (locationReference.HasValue ? $" at the location '{locationReference.Value.LocationId}'" : "") +
                    " (OCPI 2.2.1).",
                    "ocpi", "tokens", "partner"
                );

                return Task.CompletedTask;

            };

            #endregion


            void LogHandshake(String What, V.OCPIRequest Request, V.OCPIResponse Response)
            {

                var who     = Request.RemoteParty?.Id.ToString() ?? $"somebody at {Request.HTTPRequest.RemoteSocket}";
                var worked  = Response.StatusCode == StatusCode.Success;

                if (EMSP.OCPI.Logging?.Requests == false && worked)
                    return;

                log.Log(
                    worked ? LogLevel.Notice : LogLevel.Warning,
                    worked
                        ? $"The roaming partner {who} {What} this EMSP (OCPI 2.2.1)."
                        : $"The roaming partner {who} tried to {What.Split(' ')[0]} this EMSP and was answered {Response.StatusCode}: {Response.StatusMessage} (OCPI 2.2.1).",
                    "ocpi", "credentials", "partner"
                );

            }

            void LogReceived(String What, String Id, CountryCode CountryCode, Party_Id PartyId, String How)
            {

                if (EMSP.OCPI.Logging?.Requests == false)
                    return;

                log.Info(
                    $"The {What} '{Id}' of {CountryCode}-{PartyId} was {How} (OCPI 2.2.1).",
                    "ocpi", What.Contains("session") ? "sessions" : What.Contains("record") ? "cdrs" : What.Contains("tariff") ? "tariffs" : "locations", "partner"
                );

            }

        }

        #endregion


        #region Roaming partners

        public override IEnumerable<RemotePartySummary> RemoteParties

            => commonAPI.RemoteParties.Select(party => {

                   var roles = party.Roles.ToArray();
                   var role  = roles.Length > 0 ? roles[0] : (CredentialsRole?) null;

                   return Summarize(
                              party,
                              role?.PartyId.CountryCode ?? party.Id.CountryCode,
                              role?.PartyId.PartyId     ?? party.Id.PartyId,
                              role?.Role                ?? party.Id.Role,
                              role?.BusinessDetails     ?? new BusinessDetails(party.Id.ToString())
                          );

               });


        public override async Task<String?> AddRemoteParty(RemotePartySpec Spec)
        {

            var roles = new[] {
                            new CredentialsRole(
                                Spec.CountryCode,
                                Spec.PartyId,
                                Spec.Role,
                                new BusinessDetails(Spec.Name, Spec.Website),
                                AllowDowngrades: false
                            )
                        };

            var result = Spec.CanRegister

                             ? await commonAPI.AddRemoteParty(
                                         Id:                                Spec.Id,
                                         CredentialsRoles:                  roles,
                                         LocalAccessToken:                  Spec.OurToken,
                                         RemoteVersionsURL:                 Spec.TheirVersionsURL!.Value,
                                         RemoteAccessToken:                 Spec.TheirToken!.Value,
                                         RemoteAccessTokenBase64Encoding:   true,
                                         LocalAccessStatus:                 AccessStatus.ALLOWED,
                                         RemoteStatus:                      RemoteAccessStatus.ONLINE,
                                         Status:                            PartyStatus.ENABLED
                                     )

                             : await commonAPI.AddRemoteParty(
                                         Id:                                Spec.Id,
                                         CredentialsRoles:                  roles,
                                         LocalAccessToken:                  Spec.OurToken,
                                         LocalAccessStatus:                 AccessStatus.ALLOWED,
                                         Status:                            PartyStatus.ENABLED
                                     );

            if (!result.IsSuccess)
                return result.ErrorResponse ?? "The library declined to add the roaming partner and did not say why.";

            // Known to the EMSP API as well, or its pushes are refused: see
            // SyncRemoteCPOs.
            if ((Spec.Role == Role.CPO || Spec.Role == Role.HUB) &&
                !emspAPI.HasRemoteCPO(Party_Idv3.From(Spec.CountryCode, Spec.PartyId)))
            {
                await emspAPI.AddRemoteCPO(
                          Party_Idv3.From(Spec.CountryCode, Spec.PartyId),
                          Spec.Role,
                          new BusinessDetails(Spec.Name, Spec.Website),
                          false
                      );
            }

            return null;

        }


        public override Task<Boolean> RemoveRemoteParty(RemoteParty_Id Id)
            => commonAPI.RemoveRemoteParty(Id);


        public override async Task<OCPIOperationResult> Register(RemoteParty_Id Id)
        {

            if (!commonAPI.TryGetRemoteParty(Id, out var party))
                return OCPIOperationResult.Failed($"There is no roaming partner '{Id}' on OCPI 2.2.1.");

            var client = emspAPI.GetCPOClient(party, AllowCachedClients: false);

            if (client is null)
                return OCPIOperationResult.Failed($"'{Id}' has not handed out a token and a versions URL, so there is nowhere to send this EMSP's credentials.");

            var response = await client.Register();

            return DescribeRegistration(Id, response.StatusCode, response.StatusMessage, response.Data is not null);

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
            => commonAPI.TryGetTokenStatus(EMSP.PartyId, Id, out _);


        public override async Task<String?> AddToken(TokenSpec Spec)
        {

            if (!Contract_Id.TryParse(Spec.ContractId, out var contractId))
                return $"'{Spec.ContractId}' is not a contract identification.";

            var result = await commonAPI.AddToken(
                                   new V.Token(
                                       EMSP.PartyId.CountryCode,
                                       EMSP.PartyId.PartyId,
                                       Spec.Id,
                                       Spec.Type,
                                       contractId,
                                       Spec.Issuer,
                                       Spec.IsValid,
                                       Spec.Whitelist,
                                       Spec.VisualNumber,
                                       UILanguage:  Spec.Language
                                   ),
                                   AllowedType.ALLOWED
                               );

            return result.IsSuccess
                       ? null
                       : result.ErrorResponse ?? "The library declined to add the token and did not say why.";

        }


        public override async Task<Boolean> RemoveToken(Token_Id Id)
        {

            var result = await commonAPI.RemoveToken(EMSP.PartyId, Id);

            return result.IsSuccess;

        }

        #endregion

        #region What the partners sent

        public override IEnumerable<JObject> Locations
            => emspAPI.GetRemoteLocations().Select(location => WithVersion(location.ToJSON()));

        public override IEnumerable<JObject> Tariffs
            => emspAPI.GetRemoteTariffs().  Select(tariff   => WithVersion(tariff.  ToJSON()));

        public override IEnumerable<JObject> Sessions
            => emspAPI.GetRemoteSessions(). Select(session  => WithVersion(session. ToJSON()));

        public override IEnumerable<JObject> CDRs
            => emspAPI.GetRemoteCDRs().     Select(cdr      => WithVersion(cdr.     ToJSON()));

        #endregion

        #region Modules

        // No "chargingprofiles": the library advertises that module in the
        // version details of an EMSP, and its EMSP API serves no route for
        // it. What is listed here is what answers.
        protected override IEnumerable<String> Modules
            => [ "locations", "tariffs", "sessions", "cdrs", "tokens", "commands" ];

        #endregion

    }

}
