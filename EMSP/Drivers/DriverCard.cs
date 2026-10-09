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

#endregion

namespace cloud.charging.open.EMSP.Drivers
{

    /// <summary>
    /// Where a driver's card stands: asked for, charging, blocked by its
    /// driver or the operator, or turned down.
    /// </summary>
    public enum DriverCardState
    {

        /// <summary>
        /// The driver entered it, and the operator has not decided yet. No
        /// token: no partner knows of it, and it opens nothing.
        /// </summary>
        Requested,

        /// <summary>
        /// The operator let it in: a token on every OCPI version, valid.
        /// </summary>
        Active,

        /// <summary>
        /// Let in once, and blocked since - by its driver, say, who lost it.
        /// The token is still there, invalid and BLOCKED.
        /// </summary>
        Blocked,

        /// <summary>
        /// The operator turned it down. No token; the driver sees why, and
        /// may remove it.
        /// </summary>
        Rejected

    }


    /// <summary>
    /// An RFID card a driver brought: its UID, whose it is, what the driver
    /// calls it, and where it stands.
    /// </summary>
    /// <remarks>
    /// A record beside the tokens rather than a token: a card asked for has
    /// no token yet, and a token does not say whose it is. Once the operator
    /// lets it in, the token is the card's UID on every OCPI version this
    /// EMSP speaks, made out to <see cref="ContractId"/>.
    /// </remarks>
    /// <param name="UID">The UID, as the card sends it: hex digits, upper case, nothing between them.</param>
    /// <param name="Owner">The account that brought it.</param>
    /// <param name="Label">What the driver calls it, or null.</param>
    /// <param name="State">Where it stands.</param>
    /// <param name="RequestedAt">When the driver entered it.</param>
    /// <param name="DecidedAt">When the operator let it in or turned it down, or null.</param>
    /// <param name="DecidedBy">Who did, or null.</param>
    /// <param name="Reason">Why it was turned down, or null.</param>
    /// <param name="ContractId">The contract its token is made out to, once it has one.</param>
    /// <param name="ChangedAt">When it was last blocked or let charge again, or null.</param>
    /// <param name="ChangedBy">Who did, or null.</param>
    public sealed record DriverCard(String           UID,
                                    String           Owner,
                                    String?          Label,
                                    DriverCardState  State,
                                    DateTimeOffset   RequestedAt,
                                    DateTimeOffset?  DecidedAt    = null,
                                    String?          DecidedBy    = null,
                                    String?          Reason       = null,
                                    String?          ContractId   = null,
                                    DateTimeOffset?  ChangedAt    = null,
                                    String?          ChangedBy    = null)
    {

        #region Data

        /// <summary>
        /// The longest label a driver may give a card.
        /// </summary>
        public const Int32  MaxLabelLength  = 64;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the card has a token: let in, charging or blocked.
        /// </summary>
        public Boolean  HasToken
            => State is DriverCardState.Active or DriverCardState.Blocked;

        #endregion


        #region (static) TryParseUID(Text, out UID, out Error)

        /// <summary>
        /// A card's UID as a driver types it: hex digits, in either case, with
        /// colons, dashes or spaces between the bytes - four, seven or ten of
        /// them, or anything in between that a reader prints.
        /// </summary>
        /// <param name="Text">What was typed.</param>
        /// <param name="UID">The UID, upper case and without separators.</param>
        /// <param name="Error">Why it is none.</param>
        public static Boolean TryParseUID(String?                           Text,
                                          [NotNullWhen(true)]  out String?  UID,
                                          [NotNullWhen(false)] out String?  Error)
        {

            UID   = null;
            Error = null;

            var digits = new String((Text ?? "").Where(c => c is not (':' or '-' or ' ')).ToArray()).ToUpperInvariant();

            if (digits.Length == 0)
            {
                Error = "A card's UID is required: the hex digits a reader shows, such as 04A2B3C4D5E6F7.";
                return false;
            }

            if (!digits.All(Uri.IsHexDigit))
            {
                Error = $"'{Text?.Trim()}' is not a card's UID: only the hex digits 0-9 and A-F, with colons, dashes or spaces between them.";
                return false;
            }

            if (digits.Length is < 8 or > 20 || digits.Length % 2 != 0)
            {
                Error = $"'{Text?.Trim()}' is not a card's UID: it has 4 to 10 bytes, 8 to 20 hex digits, two for each byte.";
                return false;
            }

            UID = digits;
            return true;

        }

        #endregion

        #region (static) TryParseLabel(Text, out Label, out Error)

        /// <summary>
        /// What a driver calls a card: anything up to 64 characters, or
        /// nothing.
        /// </summary>
        public static Boolean TryParseLabel(String?                           Text,
                                            out String?                       Label,
                                            [NotNullWhen(false)] out String?  Error)
        {

            Label = Text?.Trim() is { Length: > 0 } trimmed ? trimmed : null;
            Error = null;

            if (Label is not null && Label.Length > MaxLabelLength)
            {
                Error = $"A card's label has {MaxLabelLength} characters at the most.";
                Label = null;
                return false;
            }

            if (Label is not null && Label.Any(Char.IsControl))
            {
                Error = "A card's label is one line of text.";
                Label = null;
                return false;
            }

            return true;

        }

        #endregion


        #region ToJSON()

        /// <summary>
        /// The record as the web interface reads it, and as the index writes it.
        /// </summary>
        public JObject ToJSON()

            => new (
                   new JProperty("uid",          UID),
                   new JProperty("owner",        Owner),
                   new JProperty("label",        Label),
                   new JProperty("state",        StateText(State)),
                   new JProperty("requestedAt",  RequestedAt.ToString("o")),
                   new JProperty("decidedAt",    DecidedAt?.  ToString("o")),
                   new JProperty("decidedBy",    DecidedBy),
                   new JProperty("reason",       Reason),
                   new JProperty("contractId",   ContractId),
                   new JProperty("changedAt",    ChangedAt?.  ToString("o")),
                   new JProperty("changedBy",    ChangedBy)
               );

        #endregion

        #region (static) StateText(State)

        /// <summary>
        /// A state as JSON says it: "requested", "active", "blocked" or
        /// "rejected".
        /// </summary>
        public static String StateText(DriverCardState State)
            => State.ToString().ToLowerInvariant();

        #endregion

        #region (static) TryParse(JSON, out Card, out Error)

        /// <summary>
        /// A record as the index wrote it.
        /// </summary>
        public static Boolean TryParse(JObject                                JSON,
                                       [NotNullWhen(true)]  out DriverCard?   Card,
                                       [NotNullWhen(false)] out String?       Error)
        {

            Card = null;

            if (!TryParseUID(JSON.Value<String>("uid"), out var uid, out Error))
                return false;

            var owner = JSON.Value<String>("owner");

            if (String.IsNullOrWhiteSpace(owner))
            {
                Error = $"The card {uid} has no owner.";
                return false;
            }

            if (!Enum.TryParse<DriverCardState>(JSON.Value<String>("state"), true, out var state) ||
                !Enum.IsDefined(state))
            {
                Error = $"The card {uid} has no state this EMSP knows: '{JSON.Value<String>("state")}'.";
                return false;
            }

            if (!TryParseTimestamp(JSON, "requestedAt", uid, out var requestedAt, out Error) || requestedAt is null)
            {
                Error ??= $"The card {uid} says nothing of when it was asked for.";
                return false;
            }

            if (!TryParseTimestamp(JSON, "decidedAt", uid, out var decidedAt, out Error) ||
                !TryParseTimestamp(JSON, "changedAt", uid, out var changedAt, out Error))
                return false;

            Card = new DriverCard(
                       uid,
                       owner,
                       JSON.Value<String>("label"),
                       state,
                       requestedAt.Value,
                       decidedAt,
                       JSON.Value<String>("decidedBy"),
                       JSON.Value<String>("reason"),
                       JSON.Value<String>("contractId"),
                       changedAt,
                       JSON.Value<String>("changedBy")
                   );

            return true;

        }

        #endregion

        #region (private static) TryParseTimestamp(JSON, Name, UID, out Timestamp, out Error)

        private static Boolean TryParseTimestamp(JObject                           JSON,
                                                 String                            Name,
                                                 String                            UID,
                                                 out DateTimeOffset?               Timestamp,
                                                 [NotNullWhen(false)] out String?  Error)
        {

            Timestamp = null;
            Error     = null;

            var text  = JSON.Value<String>(Name);

            if (String.IsNullOrEmpty(text))
                return true;

            if (!DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            {
                Error = $"The card {UID} has a '{Name}' that is no timestamp: '{text}'.";
                return false;
            }

            Timestamp = parsed;
            return true;

        }

        #endregion

    }

}
