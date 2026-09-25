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

using V = cloud.charging.open.protocols.OCPIv2_3_0;

#endregion

namespace cloud.charging.open.EMSP.OCPI
{

    /// <summary>
    /// OCPI 2.3.0, as this EMSP speaks it.
    /// </summary>
    /// <remarks>
    /// The same registration and token handling as 2.2.1; what the partners
    /// push is kept in the Common API in this version, which is also where
    /// the events about it come from.
    ///
    /// <b>Offered on request only.</b> The library's Common API for 2.3.0
    /// files a pushed location under the party that owns it, and knows only
    /// this EMSP's own parties - a CPO's push is therefore turned away as
    /// coming from an unknown party. Making the CPO one of this EMSP's parties
    /// would let the push through and, in the same breath, make the version
    /// details advertise CPO modules this EMSP does not serve. Until the
    /// library keeps remote CPOs apart for this version as it does for 2.2.1,
    /// 2.3.0 is a version a partner can register on and fetch tokens from,
    /// and nothing more; it is not in the default list for that reason.
    /// </remarks>
    public sealed class OCPIv2_3_0 : OCPIVersion
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

        public OCPIv2_3_0(EMSP           EMSP,
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

            commonAPI.OnLocationAdded            += location => { LogReceived("location",             location.Id.ToString(), location.CountryCode, location.PartyId, "added");   return Task.CompletedTask; };
            commonAPI.OnLocationChanged          += location => { LogReceived("location",             location.Id.ToString(), location.CountryCode, location.PartyId, "changed"); return Task.CompletedTask; };
            commonAPI.OnTariffAdded              += tariff   => { LogReceived("tariff",               tariff.  Id.ToString(), tariff.  CountryCode, tariff.  PartyId, "added");   return Task.CompletedTask; };
            commonAPI.OnTariffChanged            += tariff   => { LogReceived("tariff",               tariff.  Id.ToString(), tariff.  CountryCode, tariff.  PartyId, "changed"); return Task.CompletedTask; };
            commonAPI.OnSessionAdded             += session  => { LogReceived("charging session",     session. Id.ToString(), session. CountryCode, session. PartyId, "added");   return Task.CompletedTask; };
            commonAPI.OnChargeDetailRecordAdded  += cdr      => { LogReceived("charge detail record", cdr.     Id.ToString(), cdr.     CountryCode, cdr.     PartyId, "added");   return Task.CompletedTask; };


            void LogHandshake(String What, V.OCPIRequest Request, V.OCPIResponse Response)
            {

                var who     = Request.RemoteParty?.Id.ToString() ?? $"somebody at {Request.HTTPRequest.RemoteSocket}";
                var worked  = Response.StatusCode == StatusCode.Success;

                if (EMSP.OCPI.Logging?.Requests == false && worked)
                    return;

                log.Log(
                    worked ? LogLevel.Notice : LogLevel.Warning,
                    worked
                        ? $"The roaming partner {who} {What} this EMSP (OCPI 2.3.0)."
                        : $"The roaming partner {who} tried to {What.Split(' ')[0]} this EMSP and was answered {Response.StatusCode}: {Response.StatusMessage} (OCPI 2.3.0).",
                    "ocpi", "credentials", "partner"
                );

            }

            void LogReceived(String What, String Id, CountryCode CountryCode, Party_Id PartyId, String How)
            {

                if (EMSP.OCPI.Logging?.Requests == false)
                    return;

                log.Info(
                    $"The {What} '{Id}' of {CountryCode}-{PartyId} was {How} (OCPI 2.3.0).",
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

            return result.IsSuccess
                       ? null
                       : result.ErrorResponse ?? "The library declined to add the roaming partner and did not say why.";

        }


        public override Task<Boolean> RemoveRemoteParty(RemoteParty_Id Id)
            => commonAPI.RemoveRemoteParty(Id);


        public override async Task<OCPIOperationResult> Register(RemoteParty_Id Id)
        {

            if (!commonAPI.TryGetRemoteParty(Id, out var party))
                return OCPIOperationResult.Failed($"There is no roaming partner '{Id}' on OCPI 2.3.0.");

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
            => commonAPI.GetLocations().Select(location => WithVersion(location.ToJSON()));

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
