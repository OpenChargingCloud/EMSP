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

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.OCPI;

#endregion

namespace cloud.charging.open.EMSP.OCPI
{

    #region RemotePartySpec

    /// <summary>
    /// What it takes to add a roaming partner: who they are, the token they
    /// will use with this EMSP, and - when this EMSP is the one that starts
    /// the peering - the token and the versions URL they handed out.
    /// </summary>
    /// <remarks>
    /// Two ways to add one, and they are the two halves of the OCPI
    /// registration. With only <paramref name="OurToken"/> the partner is
    /// expected to come to this EMSP: they GET the versions with that token
    /// and POST their credentials, and the library fills in the rest. With
    /// <paramref name="TheirToken"/> and <paramref name="TheirVersionsURL"/>
    /// as well, this EMSP can go to them - see
    /// <see cref="OCPIVersion.Register"/>.
    /// </remarks>
    /// <param name="CountryCode">The partner's country code.</param>
    /// <param name="PartyId">The partner's party identification.</param>
    /// <param name="Role">What the partner is - a CPO, almost always.</param>
    /// <param name="Name">Its business name.</param>
    /// <param name="Website">Its website, or null.</param>
    /// <param name="OurToken">The token the partner presents to this EMSP.</param>
    /// <param name="TheirToken">The token this EMSP presents to the partner, or null while they have not handed one out.</param>
    /// <param name="TheirVersionsURL">Where the partner's versions endpoint is, or null.</param>
    public sealed record RemotePartySpec(CountryCode      CountryCode,
                                         Party_Id         PartyId,
                                         Role             Role,
                                         String           Name,
                                         URL?             Website,
                                         AccessToken      OurToken,
                                         AccessToken?     TheirToken,
                                         URL?             TheirVersionsURL)
    {

        /// <summary>
        /// The identification the library files the partner under.
        /// </summary>
        public RemoteParty_Id  Id
            => RemoteParty_Id.From(CountryCode, PartyId, Role);

        /// <summary>
        /// Whether this EMSP has what it needs to start the peering itself.
        /// </summary>
        public Boolean  CanRegister
            => TheirToken.HasValue && TheirVersionsURL.HasValue;

    }

    #endregion

    #region TokenSpec

    /// <summary>
    /// What it takes to issue a token to a customer.
    /// </summary>
    /// <param name="Id">The token's identification: what the card or the app sends.</param>
    /// <param name="Type">RFID, APP_USER, AD_HOC_USER or OTHER.</param>
    /// <param name="ContractId">The contract behind it, e.g. "DE-GDF-C12345678-X".</param>
    /// <param name="Issuer">Who issued it; this EMSP, unless said otherwise.</param>
    /// <param name="IsValid">Whether it may be used at all.</param>
    /// <param name="Whitelist">Whether a partner may authorise it without asking: ALWAYS, ALLOWED, ALLOWED_OFFLINE or NEVER.</param>
    /// <param name="VisualNumber">What is printed on the card, or null.</param>
    /// <param name="Language">The customer's language, or null.</param>
    public sealed record TokenSpec(Token_Id       Id,
                                   TokenType      Type,
                                   String         ContractId,
                                   String         Issuer,
                                   Boolean        IsValid,
                                   WhitelistType  Whitelist,
                                   String?        VisualNumber,
                                   Languages?     Language);

    #endregion

    #region RemotePartySummary

    /// <summary>
    /// A roaming partner as the web interface sees it: the same shape for
    /// every OCPI version, and never the whole of what the library keeps.
    /// </summary>
    public sealed record RemotePartySummary(String              Version,
                                            RemoteParty_Id      Id,
                                            CountryCode         CountryCode,
                                            Party_Id            PartyId,
                                            Role                Role,
                                            String              Name,
                                            URL?                Website,
                                            PartyStatus         Status,
                                            AccessToken?        OurToken,
                                            AccessStatus?       OurTokenStatus,
                                            AccessToken?        TheirToken,
                                            URL?                TheirVersionsURL,
                                            RemoteAccessStatus? RemoteStatus,
                                            Version_Id?         SelectedVersion,
                                            DateTimeOffset      Created,
                                            DateTimeOffset      LastUpdated)
    {

        /// <summary>
        /// Whether this EMSP holds what it needs to talk to the partner: a
        /// token of theirs and a place to send it.
        /// </summary>
        public Boolean  CanRegister
            => TheirToken.HasValue && TheirVersionsURL.HasValue;

        /// <summary>
        /// Whether the peering is complete in both directions.
        /// </summary>
        /// <remarks>
        /// A partner that only holds our token may still be on its way, and
        /// one whose token and versions URL were typed in by hand has not been
        /// spoken to yet. What the library writes down once the credentials
        /// have actually been exchanged - in either direction - is the version
        /// the two sides settled on, so that is what says "registered".
        /// </remarks>
        public Boolean  Registered
            => CanRegister && SelectedVersion.HasValue;

        /// <summary>
        /// The partner as the web interface reads it.
        /// </summary>
        /// <param name="IncludeSecrets">Whether the access tokens travel along. They are shown to whoever may manage partners, and to nobody else.</param>
        public JObject ToJSON(Boolean IncludeSecrets)

            => new (
                   new JProperty("version",            Version),
                   new JProperty("id",                 Id.ToString()),
                   new JProperty("countryCode",        CountryCode.ToString()),
                   new JProperty("partyId",            PartyId.ToString()),
                   new JProperty("role",               Role.ToString()),
                   new JProperty("name",               Name),
                   new JProperty("website",            Website?.ToString()),
                   new JProperty("status",             Status.ToString()),
                   new JProperty("ourToken",           IncludeSecrets ? OurToken?.ToString()   : null),
                   new JProperty("hasOurToken",        OurToken.HasValue),
                   new JProperty("ourTokenStatus",     OurTokenStatus?.ToString()),
                   new JProperty("theirToken",         IncludeSecrets ? TheirToken?.ToString() : null),
                   new JProperty("hasTheirToken",      TheirToken.HasValue),
                   new JProperty("theirVersionsURL",   TheirVersionsURL?.ToString()),
                   new JProperty("remoteStatus",       RemoteStatus?.ToString()),
                   new JProperty("selectedVersion",    SelectedVersion?.ToString()),
                   new JProperty("canRegister",        CanRegister),
                   new JProperty("registered",         Registered),
                   new JProperty("created",            Created.    ToString("o")),
                   new JProperty("lastUpdated",        LastUpdated.ToString("o"))
               );

    }

    #endregion

    #region OCPIOperationResult

    /// <summary>
    /// How something that was asked of a roaming partner, or of the store
    /// behind them, went.
    /// </summary>
    /// <param name="Success">Whether it worked.</param>
    /// <param name="Message">What happened, in a sentence the web interface can show.</param>
    /// <param name="Data">Whatever the operation has to hand back, or null.</param>
    /// <param name="NotSaved">True where nothing was wrong with what was asked, and the file it is kept in could not be written: nothing changed - but for a registration the partner accepted, which is in effect and written down later, see <see cref="OCPIVersion.UnsavedRemoteParties"/>.</param>
    public sealed record OCPIOperationResult(Boolean   Success,
                                             String    Message,
                                             JObject?  Data       = null,
                                             Boolean   NotSaved   = false)
    {

        public static OCPIOperationResult Ok    (String Message, JObject? Data = null)
            => new (true,  Message, Data);

        public static OCPIOperationResult Failed(String Message, Boolean NotSaved = false)
            => new (false, Message, NotSaved: NotSaved);

    }

    #endregion

    #region EVSEPlace / CommandAnswer

    /// <summary>
    /// Where an EVSE is, as far as a command to its CPO needs it: on which
    /// version and with which roaming partner, at which location, under
    /// which uid - and its EVSE id, as a driver names it.
    /// </summary>
    public sealed record EVSEPlace(OCPIVersion     Version,
                                   RemoteParty_Id  Partner,
                                   String          Location,
                                   String          EVSEUId,
                                   String          EVSEId);

    /// <summary>
    /// How a CPO answered a command: whether it accepted it, and in a
    /// sentence.
    /// </summary>
    public sealed record CommandAnswer(Boolean  Accepted,
                                       String   Message);

    #endregion


    /// <summary>
    /// One OCPI version this EMSP speaks: the library's Common API and EMSP
    /// API for that version, behind one shape the rest of this EMSP can talk
    /// to without knowing which version it is holding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library keeps a Common API per OCPI version, and each of those has
    /// its own roaming partners, its own tokens and its own store of what the
    /// partners sent - the data structures differ between the versions, so
    /// they cannot share one. The web interface, on the other hand, wants one
    /// list of partners and one list of tokens. This is where the two meet:
    /// every version answers the same questions, and the EMSP puts the answers
    /// side by side.
    /// </para>
    /// <para>
    /// What comes back is JSON, in the shape the library writes it (the OCPI
    /// shape, with snake_case names), with the version added. A page that
    /// shows a location reads the same fields whichever version it came in
    /// on, which is the point of OCPI having kept them.
    /// </para>
    /// </remarks>
    public abstract class OCPIVersion
    {

        #region Properties

        /// <summary>
        /// The EMSP this version belongs to.
        /// </summary>
        public EMSP        EMSP     { get; }

        /// <summary>
        /// The version identification, e.g. "2.2.1".
        /// </summary>
        public abstract Version_Id  Id  { get; }

        /// <summary>
        /// The version as the web interface writes it: "2.2.1".
        /// </summary>
        public String      Label
            => Id.ToString();

        #endregion

        #region Constructor(s)

        protected OCPIVersion(EMSP EMSP)
        {
            this.EMSP = EMSP;
        }

        #endregion


        #region Roaming partners

        /// <summary>
        /// Every roaming partner this version knows.
        /// </summary>
        public abstract IEnumerable<RemotePartySummary>  RemoteParties { get; }

        /// <summary>
        /// One roaming partner, or null.
        /// </summary>
        public RemotePartySummary? GetRemoteParty(RemoteParty_Id Id)
            => RemoteParties.FirstOrDefault(party => party.Id == Id);

        /// <summary>
        /// Add a roaming partner. Answers whether it was added, and what went
        /// wrong where it was not - with NotSaved where it was only the file of
        /// the partners that refused.
        /// </summary>
        public abstract Task<OCPIOperationResult>  AddRemoteParty(RemotePartySpec Spec);

        /// <summary>
        /// Forget a roaming partner: its tokens stop working the moment this
        /// returns having removed it. Answers what went wrong where it was not
        /// - with NotSaved where it was only the file of the partners that
        /// refused, and the partner is still there.
        /// </summary>
        public abstract Task<OCPIOperationResult>  RemoveRemoteParty(RemoteParty_Id Id);

        /// <summary>
        /// Start the peering with a partner that handed out its token and its
        /// versions URL: fetch their versions, and POST this EMSP's
        /// credentials to them. Answers NotSaved where the file of the
        /// partners refused - before anything was sent, and nothing changed;
        /// or after they accepted, and what they answered is kept, see
        /// UnsavedRemoteParties.
        /// </summary>
        public abstract Task<OCPIOperationResult>  Register(RemoteParty_Id Id);

        /// <summary>
        /// The partners whose last change the file of the partners refused
        /// and that were kept all the same - a registration they accepted: in
        /// effect, and written down with the next change the file takes, or
        /// when this EMSP stops.
        /// </summary>
        public abstract IEnumerable<RemoteParty_Id>  UnsavedRemoteParties { get; }

        #endregion

        #region Tokens

        /// <summary>
        /// The tokens this EMSP issued to its customers, as the library
        /// writes them, with "version" and "status" added.
        /// </summary>
        public abstract IEnumerable<JObject>  Tokens { get; }

        /// <summary>
        /// Whether a token of that identification exists.
        /// </summary>
        public abstract Boolean  HasToken(Token_Id Id);

        /// <summary>
        /// Issue a token. Answers with what went wrong, or null.
        /// </summary>
        public abstract Task<String?>  AddToken(TokenSpec Spec);

        /// <summary>
        /// Take a token away.
        /// </summary>
        public abstract Task<Boolean>  RemoveToken(Token_Id Id);

        /// <summary>
        /// Block a token or let it charge again: "valid" in what a partner
        /// reads of it, and its status - BLOCKED or ALLOWED - in what a
        /// partner asking about it is told. Answers with what went wrong, or
        /// null.
        /// </summary>
        public abstract Task<String?>  SetTokenValid(Token_Id Id, Boolean IsValid);

        #endregion

        #region What the partners sent

        /// <summary>The locations the partners pushed, as the library writes them.</summary>
        public abstract IEnumerable<JObject>  Locations  { get; }

        /// <summary>The tariffs the partners pushed.</summary>
        public abstract IEnumerable<JObject>  Tariffs    { get; }

        /// <summary>The charging sessions the partners pushed.</summary>
        public abstract IEnumerable<JObject>  Sessions   { get; }

        /// <summary>The charge detail records the partners pushed.</summary>
        public abstract IEnumerable<JObject>  CDRs       { get; }

        #endregion

        #region Starts and stops with a charging ticket

        /// <summary>
        /// Whether a start with a charging ticket can go out on this version:
        /// OCPI 2.2.1 and later, which carry the authorization reference the
        /// EMSP's countersignature names. 2.1.1 has none.
        /// </summary>
        public virtual Boolean  CarriesTicketStarts
            => false;

        /// <summary>
        /// The EVSE a driver's start request names, among the locations the
        /// partners pushed on this version: by its EVSE id ("DE*GEF*E2*1"),
        /// or by the uid its CPO gives it in OCPI.
        /// </summary>
        public Boolean TryFindEVSE(String                               EVSE,
                                   [NotNullWhen(true)] out EVSEPlace?   Place)
        {

            Place = null;

            String Even(String? Text)
                => (Text ?? "").Replace("*", "").Replace("-", "").Replace(" ", "").ToUpperInvariant();

            var wanted = Even(EVSE);

            if (wanted.Length == 0)
                return false;

            foreach (var location in Locations)
            {

                foreach (var evse in (location["evses"] as JArray ?? []).OfType<JObject>())
                {

                    if (Even(evse.Value<String>("evse_id")) != wanted &&
                        Even(evse.Value<String>("uid"))     != wanted)
                        continue;

                    if (!CountryCode.TryParse(location.Value<String>("country_code") ?? "", out var countryCode) ||
                        !Party_Id.   TryParse(location.Value<String>("party_id")     ?? "", out var partyId)     ||
                        location.Value<String>("id")  is not { Length: > 0 } locationId ||
                        evse.    Value<String>("uid") is not { Length: > 0 } evseUId)
                        continue;

                    Place = new EVSEPlace(
                                this,
                                RemoteParty_Id.From(countryCode, partyId, Role.CPO),
                                locationId,
                                evseUId,
                                evse.Value<String>("evse_id") ?? evseUId
                            );

                    return true;

                }

            }

            return false;

        }

        /// <summary>
        /// The charging session a partner pushed for the given authorization
        /// reference, if it did: its id, and whether it is over.
        /// </summary>
        public Boolean TryGetSessionOf(String                            AuthRef,
                                       [NotNullWhen(true)] out String?   SessionId,
                                       out Boolean                       IsOver)
        {

            SessionId  = null;
            IsOver     = false;

            var session = Sessions.LastOrDefault(session => session.Value<String>("authorization_reference") == AuthRef);

            if (session?.Value<String>("id") is not { Length: > 0 } id)
                return false;

            SessionId  = id;
            IsOver     = session.Value<String>("status") is "COMPLETED" or "INVALID";
            return true;

        }

        /// <summary>
        /// OCPI START_SESSION at the given EVSE, with the token of this start
        /// alone and its authorization reference - and, beside what OCPI
        /// knows, the charging ticket and the countersigned start request.
        /// </summary>
        public virtual Task<CommandAnswer> StartSession(EVSEPlace  Place,
                                                        Token_Id   Token,
                                                        String     AuthRef,
                                                        String     CommandId,
                                                        JObject    Beside)

            => Task.FromResult(new CommandAnswer(false, $"A start with a charging ticket needs OCPI 2.2.1 or later, and '{Place.Partner}' is a roaming partner on OCPI {Label}."));

        /// <summary>
        /// OCPI STOP_SESSION of the given session - and, beside what OCPI
        /// knows, the charging ticket and the countersigned stop request.
        /// </summary>
        public virtual Task<CommandAnswer> StopSession(RemoteParty_Id  Partner,
                                                       String          SessionId,
                                                       String          CommandId,
                                                       JObject         Beside)

            => Task.FromResult(new CommandAnswer(false, $"A stop with a charging ticket needs OCPI 2.2.1 or later, and '{Partner}' is a roaming partner on OCPI {Label}."));

        /// <summary>
        /// The CommandResult a CPO sent for a command this EMSP sent under the
        /// given id, as it sent it - where it did, and where the library still
        /// holds it: it keeps them in memory alone.
        /// </summary>
        public virtual Boolean TryGetCommandResult(String                             CommandId,
                                                   [NotNullWhen(true)] out JObject?   Result)
        {
            Result = null;
            return false;
        }

        /// <summary>
        /// The charge detail record a partner pushed for the given
        /// authorization reference, as it was last pushed, if it did.
        /// </summary>
        public JObject? CDROf(String AuthRef)

            => CDRs.LastOrDefault(cdr => cdr.Value<String>("authorization_reference") == AuthRef);

        /// <summary>
        /// The charging session a partner pushed for the given authorization
        /// reference, as it was last pushed, if it did.
        /// </summary>
        public JObject? SessionOf(String AuthRef)

            => Sessions.LastOrDefault(session => session.Value<String>("authorization_reference") == AuthRef);

        /// <summary>
        /// What goes beside a command, added to its JSON as it is written:
        /// keyed by what the command carries - the authorization reference of
        /// a start, the session id of a stop - for as long as it is sent.
        /// </summary>
        protected readonly ConcurrentDictionary<String, JObject>  besideCommands  = new ();

        /// <summary>The command's JSON with what goes beside it.</summary>
        protected JObject WithBeside(String? Key, JObject JSON)
        {

            if (Key is not null && besideCommands.TryGetValue(Key, out var beside))
                foreach (var property in beside.Properties())
                    JSON[property.Name] = property.Value.DeepClone();

            return JSON;

        }

        /// <summary>
        /// How a CPO answered a command: what it answered, and whether that is
        /// ACCEPTED - the only answer a start or a stop goes on with.
        /// </summary>
        protected static CommandAnswer Answered(String   Partner,
                                                String   What,
                                                String   StatusCode,
                                                String?  StatusMessage,
                                                String?  Result)

            => Result == "ACCEPTED"
                   ? new CommandAnswer(true,  $"'{Partner}' accepted the {What}.")
                   : Result is not null
                         ? new CommandAnswer(false, $"'{Partner}' answered the {What} with {Result}.")
                         : new CommandAnswer(false, $"'{Partner}' did not answer the {What}: {StatusCode} {StatusMessage}".TrimEnd() + ".");

        #endregion


        #region EndpointsJSON()

        /// <summary>
        /// Where this version is: its version details, its credentials
        /// endpoint and its EMSP modules, as absolute URLs a partner would be
        /// told.
        /// </summary>
        /// <remarks>
        /// Computed here rather than read back from the library, because the
        /// library builds them per request from the Host header, and a page
        /// has no request to hand it. The shape is the library's: the version
        /// details at "versions/2.2.1", the credentials at "v2.2.1/credentials",
        /// the modules at "v2.2.1/emsp/{module}".
        /// </remarks>
        public JObject EndpointsJSON()
        {

            var baseURL  = EMSP.OCPIBaseURL.ToString().TrimEnd('/');
            var prefix   = $"{baseURL}/v{Label}";

            return new JObject(
                       new JProperty("version",       Label),
                       new JProperty("details",       $"{baseURL}/versions/{Label}"),
                       new JProperty("credentials",   $"{prefix}/credentials"),
                       new JProperty("modules",       new JObject(
                           Modules.Select(module => new JProperty(module, $"{prefix}/emsp/{module}"))
                       ))
                   );

        }

        /// <summary>
        /// The EMSP modules this version offers, by the name OCPI gives them.
        /// </summary>
        protected abstract IEnumerable<String>  Modules { get; }

        #endregion

        #region (protected) Summarize(...)

        /// <summary>
        /// The parts of a remote party that every version keeps in the same
        /// place, plus the four that each version keeps somewhere else.
        /// </summary>
        protected RemotePartySummary Summarize(RemoteParty      Party,
                                               CountryCode      CountryCode,
                                               Party_Id         PartyId,
                                               Role             Role,
                                               BusinessDetails  BusinessDetails)
        {

            var local   = Party.LocalAccessInfos. FirstOrDefault();
            var remote  = Party.RemoteAccessInfos.FirstOrDefault();

            return new RemotePartySummary(
                       Label,
                       Party.Id,
                       CountryCode,
                       PartyId,
                       Role,
                       BusinessDetails.Name,
                       BusinessDetails.Website,
                       Party.Status,
                       local?. AccessToken,
                       local?. Status,
                       remote?.AccessToken,
                       remote?.VersionsURL,
                       remote?.Status,
                       remote?.SelectedVersionId,
                       Party.Created,
                       Party.LastUpdated
                   );

        }

        #endregion

        #region (protected) WithVersion(JSON, Extra...)

        /// <summary>
        /// An object as the library wrote it, with the version - and whatever
        /// else - put in front.
        /// </summary>
        protected JObject WithVersion(JObject JSON, params JProperty[] Extra)
        {

            var json = new JObject(new JProperty("version", Label));

            foreach (var property in Extra)
                json.Add(property);

            foreach (var property in JSON.Properties())
                json[property.Name] = property.Value;

            return json;

        }

        #endregion

        #region (protected) Describe(...)

        /// <summary>
        /// A registration outcome in one sentence.
        /// </summary>
        /// <param name="Id">The partner.</param>
        /// <param name="StatusCode">The OCPI status code of their answer, or of why nothing was sent.</param>
        /// <param name="StatusMessage">Its status message.</param>
        /// <param name="GotCredentials">Whether they answered with their credentials.</param>
        /// <param name="NotSaved">Whether the file of the partners refused what the registration changed.</param>
        /// <param name="Reason">Why it refused.</param>
        protected static OCPIOperationResult DescribeRegistration(RemoteParty_Id  Id,
                                                                  StatusCode?     StatusCode,
                                                                  String?         StatusMessage,
                                                                  Boolean         GotCredentials,
                                                                  Boolean         NotSaved   = false,
                                                                  String?         Reason     = null)
        {

            // Where they accepted, what they answered is kept, because they use
            // the new tokens already; where nothing was sent, nothing changed.
            if (NotSaved)
                return OCPIOperationResult.Failed(
                           GotCredentials
                               ? $"'{Id}' accepted this EMSP's credentials, and they are in effect, but could not be stored - {Reason?.TrimEnd('.') ?? "the file of the partners refused them"}. " +
                                  "Repair the file before this EMSP stops: they are written down with the next change the file takes, and when this EMSP stops at the latest."
                               : $"Nothing was sent to '{Id}', and nothing has changed: the token they would call back with could not be stored - {Reason?.TrimEnd('.') ?? "the file of the partners refused it"}.",
                           NotSaved: true
                       );

            if (GotCredentials && StatusCode == protocols.OCPI.StatusCode.Success)
                return OCPIOperationResult.Ok($"Registered with '{Id}': they accepted this EMSP's credentials and handed out theirs.");

            return OCPIOperationResult.Failed(
                       $"The registration with '{Id}' did not go through" +
                       (StatusCode.HasValue ? $" ({StatusCode}" + (StatusMessage.IsNotNullOrEmpty() ? $": {StatusMessage}" : "") + ")" : "") +
                       ". Check their versions URL and the token they handed out; every step is in the log."
                   );

        }

        #endregion

    }

}
