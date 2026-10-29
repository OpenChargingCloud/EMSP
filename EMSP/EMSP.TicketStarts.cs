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
using System.Security.Cryptography;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.OCPI;

using cloud.charging.open.EMSP.OCPI;
using cloud.charging.open.EMSP.Tickets;

#endregion

namespace cloud.charging.open.EMSP
{

    #region TicketRefusal / TicketCommandResult

    /// <summary>
    /// Why a start or a stop with a charging ticket did not go on: what was
    /// asked was wrong, it does not fit what this EMSP knows, the charge
    /// point operator did not accept it, or what this EMSP keeps of it could
    /// not be written.
    /// </summary>
    public enum TicketRefusal
    {
        None,
        Request,
        Conflict,
        Partner,
        NotSaved
    }

    /// <summary>
    /// How a start or a stop with a charging ticket went.
    /// </summary>
    public sealed record TicketCommandResult(Boolean        Success,
                                             String         Message,
                                             JObject?       Data      = null,
                                             TicketRefusal  Refusal   = TicketRefusal.None)
    {

        public static TicketCommandResult Ok    (String Message, JObject Data)
            => new (true,  Message, Data);

        public static TicketCommandResult Failed(String Message, TicketRefusal Refusal)
            => new (false, Message, null, Refusal);

    }

    #endregion


    /// <summary>
    /// Starts and stops with a charging ticket: what the holder of a ticket
    /// signed, countersigned by this EMSP and sent to the charge point
    /// operator as OCPI START_SESSION and STOP_SESSION.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A start request names the ticket, the EVSE, when it was made and a
    /// nonce, and is signed by the ticket's key alone; the ticket comes with
    /// it, as this EMSP issued it - this EMSP keeps no ticket's bytes, only
    /// whose it is. This EMSP checks both, makes a token of this start alone
    /// and an authorization reference, and adds its own signature with the
    /// two and the time in its protected header. The command carries the
    /// token - APP_USER, whitelist NEVER, uid and contract id the token
    /// itself, nothing of the account - and, beside what OCPI knows, the
    /// ticket and the countersigned start request. Only where the CPO
    /// answers ACCEPTED is the countersigned request handed back.
    /// </para>
    /// <para>
    /// The token is one of this EMSP's tokens from then on, good while the
    /// ticket is and until the session is over: blocked when the holder
    /// stops it, when the CPO reports it COMPLETED, or when the ticket runs
    /// out. Bound to the EVSE of its start it is not yet: the library's real-
    /// time authorisation has no hook for that, and answers a token the same
    /// wherever it is asked about.
    /// </para>
    /// <para>
    /// A stop request names the start by the SHA-256 of its countersigned
    /// bytes, and is countersigned with the token and the authorization
    /// reference of that start.
    /// </para>
    /// </remarks>
    public partial class EMSP
    {

        #region Data

        /// <summary>How often the starts are looked at, to block the tokens of those that are over.</summary>
        public static readonly TimeSpan  TicketStartSweep  = TimeSpan.FromMinutes(1);

        private ITimer?  ticketStartSweeper;

        #endregion

        #region Properties

        /// <summary>Every start with a charging ticket this EMSP countersigned.</summary>
        public TicketStartRegistry  TicketStarts  { get; private set; } = default!;

        #endregion


        #region (private) BuildTicketStarts()

        /// <summary>
        /// The registry of the starts with charging tickets, read below
        /// "pki/tickets", and what blocks the tokens of those that are over.
        /// </summary>
        private void BuildTicketStarts()
        {

            TicketStarts        = new TicketStartRegistry(Path.Combine(PKIDirectory, TicketsDirectoryName, "starts"));

            ticketStartSweeper  = TimeProvider.CreateTimer(
                                      _ => _ = SweepTicketStartsAsync(),
                                      null,
                                      TicketStartSweep,
                                      TicketStartSweep
                                  );

        }

        #endregion


        #region StartWithTicketAsync(User, TicketId, Ticket, Request)

        /// <summary>
        /// A start request of the holder of a charging ticket: checked,
        /// countersigned with a token of this start alone, and sent to the CPO
        /// of its EVSE as OCPI START_SESSION.
        /// </summary>
        /// <param name="User">Who asks: the account the ticket was issued to.</param>
        /// <param name="TicketId">The ticket's id, as the route names it.</param>
        /// <param name="Ticket">The ticket, as this EMSP issued it.</param>
        /// <param name="Request">The start request, a COSE_Sign signed by the ticket's key.</param>
        public async Task<TicketCommandResult> StartWithTicketAsync(IUser   User,
                                                                    String  TicketId,
                                                                    Byte[]  Ticket,
                                                                    Byte[]  Request)
        {

            var now = TimeProvider.GetUtcNow();

            #region The ticket, and the request

            if (!TryTicketOf(User, TicketId, Ticket, now, out var ticket, out var error))
                return Refused(error);

            if (!TicketRequest.TryParse(Request, TicketRequest.StartType, out var start, out error))
                return Refused(error);

            if (!TryCheckRequest(start, Ticket, ticket, now, out error))
                return Refused(error);

            #endregion

            #region The EVSE, at a partner a start can go to

            var places = OCPIVersions.Reverse().
                                      Select(version => version.TryFindEVSE(start.EVSE!, out var place) ? place : null).
                                      OfType<EVSEPlace>().
                                      ToArray();

            if (places.Length == 0)
                return Refused($"This EMSP knows no EVSE '{start.EVSE}' at any of its roaming partners.", TicketRefusal.Conflict);

            var at = places.FirstOrDefault(place => place.Version.CarriesTicketStarts);

            if (at is null)
                return Refused($"A start with a charging ticket needs OCPI 2.2.1 or later, and the EVSE '{start.EVSE}' is at '{places[0].Partner}', a roaming partner on OCPI {places[0].Version.Label}.", TicketRefusal.Conflict);

            #endregion

            if (!TicketStarts.TryTakeNonce(start.NonceText, start.At, now, out error, out var notSaved))
                return notSaved
                           ? NotKept(error)
                           : Refused(error);

            #region Countersigned, with a token and a reference of this start alone

            var token          = "T-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
            var authRef        = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            var countersigned  = start.Countersign(TicketIssuer, token, authRef, now);

            var record         = new TicketStart(
                                     token,
                                     authRef,
                                     User.Id.ToString(),
                                     ticket.IdText,
                                     ticket.NotAfter,
                                     TicketRequest.Digest(countersigned),
                                     at.Version.Label,
                                     at.Partner.ToString(),
                                     at.Location,
                                     at.EVSEUId,
                                     now,
                                     "sent",
                                     null
                                 );

            if (!TicketStarts.TrySet(record, out error))
                return NotKept(error);

            var tokenId        = Token_Id.Parse(token);

            if (await at.Version.AddToken(new TokenSpec(tokenId, TokenType.APP_USER, token, BusinessDetails.Name, true, WhitelistType.NEVER, null, null)) is String notIssued)
            {
                await EndTicketStartAsync(record, "refused", now);
                return NotKept($"The token of the start could not be issued: {notIssued}");
            }

            #endregion

            #region OCPI START_SESSION

            var answer = await at.Version.StartSession(
                                   at,
                                   tokenId,
                                   authRef,
                                   new JObject(
                                       new JProperty("charging_ticket",  Convert.ToBase64String(Ticket)),
                                       new JProperty("start_request",    Convert.ToBase64String(countersigned))
                                   )
                               );

            if (!answer.Accepted)
            {
                await EndTicketStartAsync(record, "refused", now);
                Log.Warning($"A start with the charging ticket {ticket.IdText} of '{User.Id}' at '{at.EVSEId}' was not accepted: {answer.Message}", "tickets", "ocpi");
                return TicketCommandResult.Failed($"Nothing was started: {answer.Message}", TicketRefusal.Partner);
            }

            if (!TicketStarts.TrySet(record with { State = "started" }, out error))
                Log.Error($"The start {token} was accepted, and that could not be written down: {error}", "tickets", "files");

            Log.Notice($"'{at.Partner}' accepted the start with the charging ticket {ticket.IdText} of '{User.Id}' at '{at.EVSEId}', as {token}.", "tickets", "ocpi");

            #endregion

            return TicketCommandResult.Ok(
                       "Countersigned, and sent to the charge point operator, who accepted it.",
                       new JObject(
                           new JProperty("request",  Convert.ToBase64String(countersigned)),
                           new JProperty("token",    token),
                           new JProperty("authRef",  authRef)
                       )
                   );

            TicketCommandResult Refused(String Why, TicketRefusal Refusal = TicketRefusal.Request)
            {
                Log.Info($"'{User.Id}' asked to start with the charging ticket {TicketId} and was refused: {Why}", "tickets");
                return TicketCommandResult.Failed(Why, Refusal);
            }

            TicketCommandResult NotKept(String Why)
            {
                Log.Error($"'{User.Id}' asked to start with the charging ticket {TicketId}, and nothing was started: {Why}", "tickets", "files");
                return TicketCommandResult.Failed("Nothing was started: this EMSP could not write down what it needs to. Ask its operator.", TicketRefusal.NotSaved);
            }

        }

        #endregion

        #region StopWithTicketAsync(User, TicketId, Ticket, Start, Request)

        /// <summary>
        /// A stop request of the holder of a charging ticket: checked,
        /// countersigned with the token and the reference of its start, and
        /// sent to the CPO as OCPI STOP_SESSION.
        /// </summary>
        /// <param name="User">Who asks: the account the ticket was issued to.</param>
        /// <param name="TicketId">The ticket's id, as the route names it.</param>
        /// <param name="Ticket">The ticket, as this EMSP issued it.</param>
        /// <param name="Start">The start request, as this EMSP countersigned it.</param>
        /// <param name="Request">The stop request, a COSE_Sign signed by the ticket's key.</param>
        public async Task<TicketCommandResult> StopWithTicketAsync(IUser   User,
                                                                   String  TicketId,
                                                                   Byte[]  Ticket,
                                                                   Byte[]  Start,
                                                                   Byte[]  Request)
        {

            var now = TimeProvider.GetUtcNow();

            #region The ticket, the start, and the request

            if (!TryTicketOf(User, TicketId, Ticket, now, out var ticket, out var error))
                return Refused(error);

            if (!TicketStarts.TryGetByStart(TicketRequest.Digest(Start), out var record) ||
                !String.Equals(record.Owner, User.Id.ToString(), StringComparison.OrdinalIgnoreCase) ||
                record.TicketId != ticket.IdText)
                return Refused("This EMSP countersigned no such start of yours with this ticket.");

            if (record.IsOver)
                return Refused(record.State == "refused"
                                   ? $"The start {record.Token} was never accepted."
                                   : $"The session of {record.Token} is over already.",
                               TicketRefusal.Conflict);

            if (!TicketRequest.TryParse(Request, TicketRequest.StopType, out var stop, out error))
                return Refused(error);

            if (!stop.Start!.AsSpan().SequenceEqual(SHA256.HashData(Start)))
                return Refused("The stop request names another start than the one it came with.");

            if (!TryCheckRequest(stop, Ticket, ticket, now, out error))
                return Refused(error);

            #endregion

            #region The session the CPO reported for the start

            if (!TryGetOCPIVersion(record.Version, out var version))
                return Refused($"The start {record.Token} went out on OCPI {record.Version}, which this EMSP no longer offers.", TicketRefusal.Conflict);

            if (!version.TryGetSessionOf(record.AuthRef, out var sessionId, out var isOver))
                return Refused($"'{record.Partner}' has not told this EMSP of the session of {record.Token} yet.", TicketRefusal.Conflict);

            if (isOver)
            {
                await EndTicketStartAsync(record, "ended", now);
                return Refused($"The session of {record.Token} is over already.", TicketRefusal.Conflict);
            }

            #endregion

            if (!TicketStarts.TryTakeNonce(stop.NonceText, stop.At, now, out error, out var notSaved))
                return notSaved
                           ? NotKept(error)
                           : Refused(error);

            #region Countersigned, sent as OCPI STOP_SESSION

            var countersigned  = stop.Countersign(TicketIssuer, record.Token, record.AuthRef, now);

            var answer         = await version.StopSession(
                                           RemoteParty_Id.Parse(record.Partner),
                                           sessionId,
                                           new JObject(
                                               new JProperty("charging_ticket",  Convert.ToBase64String(Ticket)),
                                               new JProperty("stop_request",     Convert.ToBase64String(countersigned))
                                           )
                                       );

            if (!answer.Accepted)
            {
                Log.Warning($"A stop of {record.Token} with the charging ticket {ticket.IdText} of '{User.Id}' was not accepted: {answer.Message}", "tickets", "ocpi");
                return TicketCommandResult.Failed($"Nothing was stopped: {answer.Message}", TicketRefusal.Partner);
            }

            await EndTicketStartAsync(record, "stopped", now);

            Log.Notice($"'{record.Partner}' accepted the stop of {record.Token} with the charging ticket {ticket.IdText} of '{User.Id}'.", "tickets", "ocpi");

            #endregion

            return TicketCommandResult.Ok(
                       "Countersigned, and sent to the charge point operator, who accepted it.",
                       new JObject(
                           new JProperty("request",  Convert.ToBase64String(countersigned))
                       )
                   );

            TicketCommandResult Refused(String Why, TicketRefusal Refusal = TicketRefusal.Request)
            {
                Log.Info($"'{User.Id}' asked to stop with the charging ticket {TicketId} and was refused: {Why}", "tickets");
                return TicketCommandResult.Failed(Why, Refusal);
            }

            TicketCommandResult NotKept(String Why)
            {
                Log.Error($"'{User.Id}' asked to stop with the charging ticket {TicketId}, and nothing was stopped: {Why}", "tickets", "files");
                return TicketCommandResult.Failed("Nothing was stopped: this EMSP could not write down what it needs to. Ask its operator.", TicketRefusal.NotSaved);
            }

        }

        #endregion


        #region (private) TryTicketOf(User, TicketId, Ticket, Now, out Decoded, out Error)

        /// <summary>
        /// The ticket a start or a stop request came with: signed by this
        /// EMSP's ticket issuer, for this EMSP, the one the route names,
        /// issued to whoever asks, and good now.
        /// </summary>
        private Boolean TryTicketOf(IUser                                     User,
                                    String                                    TicketId,
                                    Byte[]                                    Ticket,
                                    DateTimeOffset                            Now,
                                    [NotNullWhen(true)]  out ChargingTicket?  Decoded,
                                    [NotNullWhen(false)] out String?          Error)
        {

            Decoded = null;

            if (!COSESign.TryParse(Ticket, out var message, out var error) || message.Payload is null)
            {
                Error = $"The ticket is no COSE_Sign with its payload{(error is not null ? $": {error}" : ".")}";
                return false;
            }

            if (!message.Signatures.Any(signature => ChargingTicket.KeyIdOf(signature)?.SequenceEqual(TicketIssuer.KeyId) == true &&
                                                     ChargingTicket.Verifies(TicketIssuer.COSEPublicKey, message, signature)))
            {
                Error = "The ticket is not signed by this EMSP's ticket issuer.";
                return false;
            }

            if (!ChargingTicket.TryDecode(message.Payload, out var ticket, out Error))
                return false;

            if (!String.Equals(ticket.IdText, TicketId, StringComparison.OrdinalIgnoreCase) ||
                ticket.EMSP != TicketParty ||
                !IssuedTickets.TryGet(ticket.IdText, out var issued) ||
                !String.Equals(issued.Owner, User.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                Error = $"This EMSP issued you no ticket '{TicketId}'.";
                return false;
            }

            if (ticket.NotAfter <= Now)
            {
                Error = $"The ticket '{TicketId}' is good no more.";
                return false;
            }

            if (ticket.NotBefore > Now + TicketRequest.ClockSkew)
            {
                Error = $"The ticket '{TicketId}' is not good yet: from {ticket.NotBefore:yyyy-MM-dd HH:mm} UTC.";
                return false;
            }

            Decoded  = ticket;
            Error    = null;
            return true;

        }

        #endregion

        #region (private static) TryCheckRequest(Request, TicketBytes, Ticket, Now, out Error)

        /// <summary>
        /// A start or a stop request that names the ticket it came with, is
        /// signed by the ticket's key alone, and was made near this EMSP's
        /// clock.
        /// </summary>
        private static Boolean TryCheckRequest(TicketRequest                     Request,
                                               Byte[]                            TicketBytes,
                                               ChargingTicket                    Ticket,
                                               DateTimeOffset                    Now,
                                               [NotNullWhen(false)] out String?  Error)
        {

            if (!Request.Ticket.AsSpan().SequenceEqual(SHA256.HashData(TicketBytes)))
            {
                Error = "The request names another ticket than the one it came with.";
                return false;
            }

            if (!Request.IsSignedBy(Ticket.Key))
            {
                Error = "The request is not signed by the ticket's key, and by nobody else.";
                return false;
            }

            if ((Request.At - Now).Duration() > TicketRequest.ClockSkew)
            {
                Error = $"The request was made {Math.Abs((Request.At - Now).TotalMinutes):0} minutes from now, and is taken within {TicketRequest.ClockSkew.TotalMinutes:0}. Is the clock of the device that made it right?";
                return false;
            }

            Error = null;
            return true;

        }

        #endregion


        #region (private) EndTicketStartAsync(Start, State, Now)

        /// <summary>
        /// A start that is over: its token blocked, and the start written down
        /// as refused, stopped, ended or expired.
        /// </summary>
        private async Task EndTicketStartAsync(TicketStart     Start,
                                               String          State,
                                               DateTimeOffset  Now)
        {

            if (TryGetOCPIVersion(Start.Version, out var version) &&
                Token_Id.TryParse(Start.Token, out var tokenId) &&
                version.HasToken(tokenId) &&
                await version.SetTokenValid(tokenId, false) is String notBlocked)
            {
                Log.Error($"The token {Start.Token} of a start that is {State} could not be blocked: {notBlocked}", "tickets", "ocpi");
                return;
            }

            if (!TicketStarts.TrySet(Start with { State = State, EndedAt = Now }, out var error))
                Log.Error($"The token {Start.Token} was blocked, and that its start is {State} could not be written down: {error}", "tickets", "files");

        }

        #endregion

        #region (private) SweepTicketStartsAsync()

        /// <summary>
        /// The tokens of the starts that are over blocked: whose ticket ran out,
        /// or whose session the CPO reported COMPLETED.
        /// </summary>
        private async Task SweepTicketStartsAsync()
        {

            try
            {

                var now = TimeProvider.GetUtcNow();

                foreach (var start in TicketStarts.All.Where(start => !start.IsOver))
                {

                    if (start.TicketNotAfter <= now)
                    {
                        await EndTicketStartAsync(start, "expired", now);
                        Log.Info($"The ticket of the start {start.Token} ran out, and its token was blocked.", "tickets");
                    }

                    else if (TryGetOCPIVersion(start.Version, out var version) &&
                             version.TryGetSessionOf(start.AuthRef, out _, out var isOver) &&
                             isOver)
                    {
                        await EndTicketStartAsync(start, "ended", now);
                        Log.Info($"'{start.Partner}' reported the session of {start.Token} over, and its token was blocked.", "tickets");
                    }

                }

            }
            catch (Exception e)
            {
                Log.Exception(e, "The starts with charging tickets could not be looked at", "tickets");
            }

        }

        #endregion

    }

}
