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
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.OCPI;

using cloud.charging.open.EMSP.Drivers;
using cloud.charging.open.EMSP.OCPI;

using cloud.charging.open.protocols.WWCP.Node.Logging;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// The drivers' side of this EMSP: the organization they are in, the RFID
    /// cards they bring, what they charged, and leaving.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A driver brings a card by its UID; it opens nothing until the
    /// operator lets it in, and then it is a token on every OCPI version this
    /// EMSP speaks - as a contract is. Its driver may block it and let it
    /// charge again, and take it away; the operator may do all of that too.
    /// </para>
    /// <para>
    /// What a driver charged is what the partners pushed - sessions and
    /// charge detail records - with one of the driver's cards or contracts
    /// in it.
    /// </para>
    /// </remarks>
    public partial class EMSP
    {

        #region Data

        /// <summary>
        /// The organization every driver signs up into: apart from the one
        /// whoever runs this EMSP is in.
        /// </summary>
        public const String  DriverOrganization  = "EVDrivers";

        /// <summary>
        /// The OCPI token type a driver's card is filed under.
        /// </summary>
        public const String  CardTokenType       = "RFID";

        #endregion

        #region Properties

        /// <summary>
        /// Every RFID card the drivers brought.
        /// </summary>
        public DriverCardRegistry  Cards  { get; private set; } = default!;

        #endregion


        #region (private) BuildDrivers()

        /// <summary>
        /// The registry of the drivers' cards, read or made, beside the
        /// configuration file.
        /// </summary>
        private void BuildDrivers()
        {

            Cards = new DriverCardRegistry(
                        Path.Combine(Path.GetDirectoryName(ConfigFile.Path) ?? ".", DriverCardRegistry.DirectoryName)
                    );

            var waiting = Cards.All.Count(card => card.State == DriverCardState.Requested);

            Log.Info(
                $"{Cards.All.Count} driver card(s) on record in '{Cards.Directory}'" +
                (waiting > 0 ? $", {waiting} of them waiting to be let in." : "."),
                "cards", "tokens"
            );

        }

        #endregion


        #region CardsJSON(Owner, Everyone)

        /// <summary>
        /// The cards as the web interface reads them: one account's, or every
        /// driver's for whoever may manage the tokens.
        /// </summary>
        public JObject CardsJSON(String?  Owner,
                                 Boolean  Everyone)
        {

            var cards = Everyone
                            ? Cards.All
                            : Cards.OfOwner(Owner ?? "");

            return new JObject(
                       new JProperty("everyone",  Everyone),
                       new JProperty("issuer",    BusinessDetails.Name),
                       new JProperty("waiting",   cards.Count(card => card.State == DriverCardState.Requested)),
                       new JProperty("cards",     new JArray(cards.Select(card => card.ToJSON())))
                   );

        }

        #endregion

        #region RequestCardAsync(User, UID, Label)

        /// <summary>
        /// A driver bringing a card: entered, and waiting for the operator.
        /// </summary>
        public Task<OCPIOperationResult> RequestCardAsync(IUser    User,
                                                          String?  UID,
                                                          String?  Label)
        {

            if (!DriverCard.TryParseUID(UID, out var uid, out var error))
                return Task.FromResult(OCPIOperationResult.Failed(error));

            if (!DriverCard.TryParseLabel(Label, out var label, out error))
                return Task.FromResult(OCPIOperationResult.Failed(error));

            // A token of that UID that no driver brought: a card the operator
            // issued, or somebody's contract. Either way not this driver's.
            if (!Cards.TryGet(uid, out _) &&
                OCPIVersions.Any(version => Token_IdOf(uid) is { } tokenId && version.HasToken(tokenId)))
                return Task.FromResult(OCPIOperationResult.Failed($"The card {uid} is somebody else's."));

            var card = new DriverCard(
                           uid,
                           User.Id.ToString(),
                           label,
                           DriverCardState.Requested,
                           TimeProvider.GetUtcNow()
                       );

            if (!Cards.TryAdd(card, out error, out var notSaved))
            {

                if (notSaved)
                    Log.Error($"'{User.Id}' entered the card {uid}, and it could not be kept: {error}", "cards", "tokens");

                return Task.FromResult(
                           notSaved
                               ? OCPIOperationResult.Failed("The card was not entered: this EMSP could not write to its registry of cards. Ask its operator.", NotSaved: true)
                               : OCPIOperationResult.Failed(error)
                       );

            }

            Log.Notice($"'{User.Id}' entered the card {uid}{(label is not null ? $" ('{label}')" : "")}; it waits to be let in.", "cards", "tokens");

            return Task.FromResult(
                       OCPIOperationResult.Ok(
                           $"The card {uid} was entered. It charges once this EMSP's operator lets it in.",
                           new JObject(new JProperty("card", card.ToJSON()))
                       )
                   );

        }

        #endregion

        #region ApproveCardAsync(UID, By, Whitelist)

        /// <summary>
        /// The operator letting a card in: a token on every OCPI version, and
        /// the card active. Where one version declines the token, none keeps
        /// it and the card still waits.
        /// </summary>
        public async Task<OCPIOperationResult> ApproveCardAsync(String   UID,
                                                                IUser    By,
                                                                String?  Whitelist = null)
        {

            if (!Cards.TryGet(UID, out var card))
                return OCPIOperationResult.Failed($"There is no card {UID}.");

            if (card.State != DriverCardState.Requested)
                return OCPIOperationResult.Failed($"The card {UID} does not wait to be let in: it is {DriverCard.StateText(card.State)}.");

            var contractId  = $"{PartyId.CountryCode}-{PartyId.PartyId}-C{card.UID}";
            var issued      = new List<OCPIVersion>();

            foreach (var version in OCPIVersions)
            {

                var token = await AddTokenAsync(
                                      new JObject(
                                          new JProperty("version",     version.Label),
                                          new JProperty("uid",         card.UID),
                                          new JProperty("type",        CardTokenType),
                                          new JProperty("contractId",  contractId),
                                          new JProperty("whitelist",   String.IsNullOrWhiteSpace(Whitelist) ? "ALLOWED" : Whitelist),
                                          new JProperty("valid",       true)
                                      )
                                  );

                if (!token.Success)
                {

                    foreach (var done in issued)
                        await RemoveTokenAsync(done.Label, card.UID);

                    return OCPIOperationResult.Failed($"The card {UID} was not let in: {token.Message}");

                }

                issued.Add(version);

            }

            var now     = TimeProvider.GetUtcNow();
            var active  = card with {
                              State       = DriverCardState.Active,
                              DecidedAt   = now,
                              DecidedBy   = By.Id.ToString(),
                              Reason      = null,
                              ContractId  = contractId
                          };

            if (!Cards.TryReplace(card, active, out var error, out var notSaved))
            {

                foreach (var done in issued)
                    await RemoveTokenAsync(done.Label, card.UID);

                if (notSaved)
                    Log.Error($"'{By.Id}' let the card {UID} in, and the registry could not write that down, so it still waits: {error}", "cards", "tokens");

                return notSaved
                           ? OCPIOperationResult.Failed($"The card {UID} was not let in: this EMSP could not write to its registry of cards.", NotSaved: true)
                           : OCPIOperationResult.Failed(error);

            }

            Log.Notice($"'{By.Id}' let the card {UID} of '{card.Owner}' in, as a token made out to {contractId}.", "cards", "tokens");

            return OCPIOperationResult.Ok(
                       $"The card {UID} of '{card.Owner}' was let in and charges from now on.",
                       new JObject(new JProperty("card", active.ToJSON()))
                   );

        }

        #endregion

        #region RejectCardAsync(UID, By, Reason)

        /// <summary>
        /// The operator turning a card down: no token, and the driver told why.
        /// </summary>
        public Task<OCPIOperationResult> RejectCardAsync(String   UID,
                                                         IUser    By,
                                                         String?  Reason)
        {

            if (!Cards.TryGet(UID, out var card))
                return Task.FromResult(OCPIOperationResult.Failed($"There is no card {UID}."));

            if (card.State != DriverCardState.Requested)
                return Task.FromResult(OCPIOperationResult.Failed($"The card {UID} does not wait to be let in: it is {DriverCard.StateText(card.State)}."));

            var reason    = Reason?.Trim() is { Length: > 0 } trimmed
                                ? trimmed.Length > 200 ? trimmed[..200] : trimmed
                                : null;

            var rejected  = card with {
                                State      = DriverCardState.Rejected,
                                DecidedAt  = TimeProvider.GetUtcNow(),
                                DecidedBy  = By.Id.ToString(),
                                Reason     = reason
                            };

            if (!Cards.TryReplace(card, rejected, out var error, out var notSaved))
                return Task.FromResult(
                           notSaved
                               ? OCPIOperationResult.Failed($"The card {UID} was not turned down: this EMSP could not write to its registry of cards.", NotSaved: true)
                               : OCPIOperationResult.Failed(error)
                       );

            Log.Notice($"'{By.Id}' turned the card {UID} of '{card.Owner}' down{(reason is not null ? $": {reason}" : ".")}", "cards", "tokens");

            return Task.FromResult(
                       OCPIOperationResult.Ok(
                           $"The card {UID} of '{card.Owner}' was turned down.",
                           new JObject(new JProperty("card", rejected.ToJSON()))
                       )
                   );

        }

        #endregion

        #region SetCardBlockedAsync(Card, Blocked, By)

        /// <summary>
        /// Block a card that charges, or let a blocked one charge again: its
        /// token on every OCPI version, then the card. Where one version
        /// declines, the others are put back.
        /// </summary>
        public async Task<OCPIOperationResult> SetCardBlockedAsync(DriverCard  Card,
                                                                   Boolean     Blocked,
                                                                   IUser       By)
        {

            var from  = Blocked ? DriverCardState.Active  : DriverCardState.Blocked;
            var to    = Blocked ? DriverCardState.Blocked : DriverCardState.Active;

            if (Card.State == to)
                return OCPIOperationResult.Failed($"The card {Card.UID} is {DriverCard.StateText(to)} already.");

            if (Card.State != from)
                return OCPIOperationResult.Failed($"The card {Card.UID} is {DriverCard.StateText(Card.State)}: only a card that was let in can be blocked or let charge again.");

            if (Token_IdOf(Card.UID) is not { } tokenId)
                return OCPIOperationResult.Failed($"'{Card.UID}' is no token identification.");

            var changed = new List<OCPIVersion>();

            foreach (var version in OCPIVersions)
            {

                // A token the operator took away by hand stays away.
                if (!version.HasToken(tokenId))
                    continue;

                if (await version.SetTokenValid(tokenId, !Blocked) is String declined)
                {

                    foreach (var done in changed)
                        await done.SetTokenValid(tokenId, Blocked);

                    return OCPIOperationResult.Failed($"The card {Card.UID} was not {(Blocked ? "blocked" : "let charge again")}: {declined}");

                }

                changed.Add(version);

            }

            var updated = Card with {
                              State      = to,
                              ChangedAt  = TimeProvider.GetUtcNow(),
                              ChangedBy  = By.Id.ToString()
                          };

            if (!Cards.TryReplace(Card, updated, out var error, out var notSaved))
            {

                foreach (var done in changed)
                    await done.SetTokenValid(tokenId, Blocked);

                if (notSaved)
                    Log.Error($"'{By.Id}' {(Blocked ? "blocked" : "unblocked")} the card {Card.UID}, and the registry could not write that down, so it is as it was: {error}", "cards", "tokens");

                return notSaved
                           ? OCPIOperationResult.Failed($"The card {Card.UID} is as it was: this EMSP could not write to its registry of cards.", NotSaved: true)
                           : OCPIOperationResult.Failed(error);

            }

            Log.Notice($"'{By.Id}' {(Blocked ? "blocked the card" : "let the card")} {Card.UID} of '{Card.Owner}'{(Blocked ? "." : " charge again.")}", "cards", "tokens");

            return OCPIOperationResult.Ok(
                       Blocked
                           ? $"The card {Card.UID} is blocked: it charges nowhere until it is let charge again."
                           : $"The card {Card.UID} charges again.",
                       new JObject(new JProperty("card", updated.ToJSON()))
                   );

        }

        #endregion

        #region RemoveCardAsync(Card, By)

        /// <summary>
        /// Take a card away: its token first, on every OCPI version, and then
        /// the card - so that a card that could not be struck off at least
        /// charges nowhere.
        /// </summary>
        public async Task<OCPIOperationResult> RemoveCardAsync(DriverCard  Card,
                                                               IUser       By)
        {

            if (Card.HasToken && Token_IdOf(Card.UID) is { } tokenId)
                foreach (var version in OCPIVersions)
                    if (version.HasToken(tokenId))
                        await RemoveTokenAsync(version.Label, Card.UID);

            if (!Cards.TryRemove(Card, out var error, out var notSaved))
            {

                if (notSaved)
                    Log.Error($"'{By.Id}' took the card {Card.UID} away; its token is gone, and the registry could not write that down: {error}", "cards", "tokens");

                return notSaved
                           ? OCPIOperationResult.Failed($"The card {Card.UID} charges nowhere any more, and is still listed: this EMSP could not write to its registry of cards.", NotSaved: true)
                           : OCPIOperationResult.Failed(error);

            }

            Log.Notice($"'{By.Id}' took the card {Card.UID} of '{Card.Owner}' away.", "cards", "tokens");

            return OCPIOperationResult.Ok($"The card {Card.UID} was removed.");

        }

        #endregion


        #region ChargingJSON(User)

        /// <summary>
        /// What a driver charged: the sessions and the charge detail records
        /// the partners pushed with one of the driver's cards or contracts in
        /// them, the newest first - each in the same few words whatever its
        /// OCPI version.
        /// </summary>
        public JObject ChargingJSON(IUser User)
        {

            var owner        = User.Id.ToString();
            var identifiers  = new HashSet<String>(StringComparer.Ordinal);

            foreach (var card in Cards.OfOwner(owner).Where(card => card.HasToken))
            {
                identifiers.Add(Normalized(card.UID));
                if (card.ContractId is not null)
                    identifiers.Add(Normalized(card.ContractId));
            }

            foreach (var contract in Contracts.OfOwner(owner))
            {
                identifiers.Add(Normalized(contract.EMAId.Compact));
                identifiers.Add(Normalized(contract.EMAId.ToString()));
            }

            JArray Mine(IEnumerable<JObject> Items, String Kind)

                => new (Items.Where      (item => identifiers.Count > 0 && Identifiers(item).Any(identifiers.Contains)).
                              Select     (item => Summary(item, Kind)).
                              OrderByDescending(summary => summary.Value<String>("start") ?? ""));

            return new JObject(
                       new JProperty("sessions",  Mine(OCPIVersions.SelectMany(version => version.Sessions), "session")),
                       new JProperty("cdrs",      Mine(OCPIVersions.SelectMany(version => version.CDRs),     "cdr")),
                       new JProperty("cards",     Cards.OfOwner(owner).Count(card => card.HasToken)),
                       new JProperty("contracts", Contracts.OfOwner(owner).Count(contract => !contract.IsRevoked))
                   );

        }

        #endregion

        #region (private static) Identifiers(Item)

        /// <summary>
        /// Whom a session or a charge detail record says it charged for: the
        /// token's UID and contract from OCPI 2.2 on, the authorization
        /// identification in 2.1.1.
        /// </summary>
        private static IEnumerable<String> Identifiers(JObject Item)
        {

            if (Item["cdr_token"] is JObject token)
            {

                if (token.Value<String>("uid")         is { Length: > 0 } uid)
                    yield return Normalized(uid);

                if (token.Value<String>("contract_id") is { Length: > 0 } contractId)
                    yield return Normalized(contractId);

            }

            if (Item.Value<String>("auth_id") is { Length: > 0 } authId)
                yield return Normalized(authId);

        }

        #endregion

        #region (private static) Summary(Item, Kind)

        /// <summary>
        /// A session or a charge detail record in the few words a driver's
        /// page shows, the same whatever the OCPI version.
        /// </summary>
        private static JObject Summary(JObject Item, String Kind)
        {

            var location  = Item["cdr_location"] as JObject ?? Item["location"] as JObject;
            var cost      = Item["total_cost"] switch {
                                JObject price  => price.Value<Decimal?>("incl_vat") ?? price.Value<Decimal?>("excl_vat"),
                                JValue  value  => value.Value<Decimal?>(),
                                _              => null
                            };

            return new JObject(
                       new JProperty("kind",      Kind),
                       new JProperty("version",   Item.Value<String>("version")),
                       new JProperty("id",        Item.Value<String>("id")),
                       new JProperty("party",     $"{Item.Value<String>("country_code")}*{Item.Value<String>("party_id")}"),
                       new JProperty("status",    Item.Value<String>("status")),
                       new JProperty("start",     Text(Item, "start_date_time") ?? Text(Item, "start_datetime")),
                       new JProperty("end",       Text(Item, "end_date_time")   ?? Text(Item, "end_datetime") ?? Text(Item, "stop_date_time")),
                       new JProperty("kWh",       Item.Value<Decimal?>("kwh")   ?? Item.Value<Decimal?>("total_energy")),
                       new JProperty("cost",      cost),
                       new JProperty("currency",  Item.Value<String>("currency")),
                       new JProperty("location",  location?.Value<String>("name") ?? Item.Value<String>("location_id") ?? location?.Value<String>("id")),
                       new JProperty("address",   location is null
                                                      ? null
                                                      : String.Join(", ", new[] { location.Value<String>("address"), location.Value<String>("city") }.Where(part => !String.IsNullOrWhiteSpace(part)))),
                       new JProperty("evse",      Item.Value<String>("evse_uid") ?? location?.Value<String>("evse_uid") ?? (location?["evses"] as JArray)?.FirstOrDefault()?.Value<String>("uid")),
                       new JProperty("token",     Item["cdr_token"]?.Value<String>("uid") ?? Item.Value<String>("auth_id"))
                   );

        }

        #endregion

        #region (private static) Text(Item, Name)

        /// <summary>
        /// A timestamp as JSON wrote it - a string, or a date Json.NET made of
        /// one - in the round-trip form.
        /// </summary>
        private static String? Text(JObject Item, String Name)

            => Item[Name] switch {
                   JValue { Type: JTokenType.Date } date  => date.Value<DateTimeOffset>().ToString("o"),
                   JValue { Type: JTokenType.String } s   => s.Value<String>(),
                   _                                      => null
               };

        #endregion

        #region (private static) Normalized(Identifier)

        /// <summary>
        /// A UID, a contract or an eMAID as it is compared: upper case,
        /// without the dashes and spaces some write and others do not.
        /// </summary>
        private static String Normalized(String Identifier)
            => new String(Identifier.Where(c => c is not ('-' or ' ' or ':' or '*')).ToArray()).ToUpperInvariant();

        #endregion


        #region DeleteDriverAsync(User)

        /// <summary>
        /// A driver leaving: every contract taken back, every card taken away,
        /// out of the organizations - which Hermod asks of an account it is
        /// to delete - and the account deleted.
        /// </summary>
        /// <remarks>
        /// For a driver and nobody else: an account with another role is
        /// whoever looks after this EMSP, and leaves by an administrator's
        /// hand. Where taking a contract back or a card away fails, the
        /// account stays, so that nothing is left behind that charges for
        /// nobody.
        /// </remarks>
        public async Task<OCPIOperationResult> DeleteDriverAsync(IUser User)
        {

            var roles = RolesOf(User);

            if (roles.Count == 0 || roles.Any(role => role.Name != EMSPAccess.Driver.Name))
                return OCPIOperationResult.Failed("Only a driver deletes their own account here; an account that looks after this EMSP is deleted by an administrator.");

            foreach (var contract in Contracts.OfOwner(User.Id.ToString()).Where(contract => !contract.IsRevoked))
            {
                var revoked = await RevokeContractAsync(contract.EMAId, User);
                if (!revoked.Success)
                    return OCPIOperationResult.Failed($"The account was not deleted: {revoked.Message}", revoked.NotSaved);
            }

            foreach (var card in Cards.OfOwner(User.Id.ToString()))
            {
                var removed = await RemoveCardAsync(card, User);
                if (!removed.Success)
                    return OCPIOperationResult.Failed($"The account was not deleted: {removed.Message}", removed.NotSaved);
            }

            foreach (var edge in User.User2Organization_OutEdges.ToArray())
            {

                var left = await ExtAPI.RemoveUserFromOrganization(User, edge.EdgeLabel, edge.Target);

                if (!left.IsSuccess)
                {
                    Log.Error($"'{User.Id}' asked for their account to be deleted, and it could not be taken out of the organization '{edge.Target.Id}': {left.ErrorDescription?.FirstText()}", "web", "auth");
                    return OCPIOperationResult.Failed("The account was not deleted: it could not be taken out of its organization. Ask the operator of this EMSP.");
                }

            }

            var deleted = await ExtAPI.DeleteUser(User, SkipUserDeletedNotifications: true, CurrentUserId: User.Id);

            if (deleted.Result != CommandResult.Success)
            {
                Log.Error($"'{User.Id}' asked for their account to be deleted, and Hermod declined: {deleted.Description?.FirstText()}", "web", "auth");
                return OCPIOperationResult.Failed("The account was not deleted. Ask the operator of this EMSP.");
            }

            Log.Notice($"'{User.Id}' deleted their account; their contracts were taken back and their cards taken away.", "web", "auth", "contracts", "cards");

            return OCPIOperationResult.Ok("Your account was deleted.");

        }

        #endregion


        #region (private static) Token_IdOf(UID)

        private static Token_Id? Token_IdOf(String UID)
            => Token_Id.TryParse(UID, out var tokenId) ? tokenId : null;

        #endregion

    }

}
