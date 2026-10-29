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
using System.Globalization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#endregion

namespace cloud.charging.open.EMSP.Tickets
{

    /// <summary>
    /// A start with a charging ticket this EMSP countersigned, as it remembers
    /// it: the token of this start alone and the authorization reference it
    /// handed the charge point operator, and - for this EMSP alone - whose
    /// start it was.
    /// </summary>
    /// <param name="Token">The OCPI token of this start alone: what the CPO asks about and bills to.</param>
    /// <param name="AuthRef">The OCPI authorization reference of this start.</param>
    /// <param name="Owner">Whose start it was: the account that asked.</param>
    /// <param name="TicketId">The ticket it was made with.</param>
    /// <param name="TicketNotAfter">Until when that ticket is good: the token is good no longer.</param>
    /// <param name="Start">The SHA-256 of the start request as this EMSP countersigned it, as hex: how a stop request names it.</param>
    /// <param name="Version">The OCPI version the command went out on.</param>
    /// <param name="Partner">The roaming partner it went to.</param>
    /// <param name="Location">The location, as the CPO names it.</param>
    /// <param name="EVSE">The EVSE, as the CPO names it in OCPI (its uid).</param>
    /// <param name="StartedAt">When it was countersigned.</param>
    /// <param name="State">"sent", "started", "refused", "failed", "stopped", "ended" or "expired".</param>
    /// <param name="EndedAt">When its token was blocked, or null while it is good.</param>
    /// <param name="EVSEId">The EVSE as the start request names it ("DE*GEF*E1*1").</param>
    /// <param name="StartCommand">The command id of its START_SESSION, under which the CPO's CommandResult comes.</param>
    /// <param name="StopCommand">The command id of its STOP_SESSION, once one went out.</param>
    /// <param name="Result">The CPO's CommandResult of the START_SESSION, as it sent it, with when this EMSP took it.</param>
    /// <param name="StopResult">The same of the STOP_SESSION.</param>
    public sealed record TicketStart(String           Token,
                                     String           AuthRef,
                                     String           Owner,
                                     String           TicketId,
                                     DateTimeOffset   TicketNotAfter,
                                     String           Start,
                                     String           Version,
                                     String           Partner,
                                     String           Location,
                                     String           EVSE,
                                     DateTimeOffset   StartedAt,
                                     String           State,
                                     DateTimeOffset?  EndedAt,
                                     String?          EVSEId        = null,
                                     String?          StartCommand  = null,
                                     String?          StopCommand   = null,
                                     JObject?         Result        = null,
                                     JObject?         StopResult    = null)
    {

        /// <summary>Whether its token is blocked: refused, failed, stopped, ended or expired.</summary>
        public Boolean  IsOver
            => EndedAt.HasValue;

        /// <summary>The record as the index writes it.</summary>
        public JObject ToJSON()

            => new (
                   new JProperty("token",           Token),
                   new JProperty("authRef",         AuthRef),
                   new JProperty("owner",           Owner),
                   new JProperty("ticket",          TicketId),
                   new JProperty("ticketNotAfter",  TicketNotAfter.ToString("o")),
                   new JProperty("start",           Start),
                   new JProperty("version",         Version),
                   new JProperty("partner",         Partner),
                   new JProperty("location",        Location),
                   new JProperty("evse",            EVSE),
                   new JProperty("startedAt",       StartedAt.ToString("o")),
                   new JProperty("state",           State),
                   new JProperty("endedAt",         EndedAt?.ToString("o")),
                   new JProperty("evseId",          EVSEId),
                   new JProperty("startCommand",    StartCommand),
                   new JProperty("stopCommand",     StopCommand),
                   new JProperty("result",          Result),
                   new JProperty("stopResult",      StopResult)
               );

        /// <summary>A record as the index wrote it.</summary>
        public static Boolean TryParse(JObject                                JSON,
                                       [NotNullWhen(true)]  out TicketStart?  Start,
                                       [NotNullWhen(false)] out String?       Error)
        {

            Start = null;

            String? Text(String Name)
                => JSON.Value<String>(Name) is { Length: > 0 } text ? text : null;

            if (Text("token") is not String token || Text("authRef") is not String authRef || Text("owner") is not String owner ||
                Text("ticket") is not String ticket || Text("start") is not String start || Text("version") is not String version ||
                Text("partner") is not String partner || Text("location") is not String location || Text("evse") is not String evse ||
                Text("state") is not String state ||
                !Timestamp(JSON, "ticketNotAfter", out var ticketNotAfter) ||
                !Timestamp(JSON, "startedAt",      out var startedAt))
            {
                Error = $"The start '{JSON.Value<String>("token")}' lacks one of its fields.";
                return false;
            }

            DateTimeOffset? endedAt = Timestamp(JSON, "endedAt", out var ended) ? ended : null;

            Start = new TicketStart(token, authRef, owner, ticket, ticketNotAfter, start, version, partner, location, evse, startedAt, state, endedAt,
                                    Text("evseId"), Text("startCommand"), Text("stopCommand"), JSON["result"] as JObject, JSON["stopResult"] as JObject);
            Error = null;
            return true;

        }

        internal static Boolean Timestamp(JObject JSON, String Name, out DateTimeOffset Value)
        {
            Value = default;
            return JSON.Value<String>(Name) is { Length: > 0 } text &&
                   DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out Value);
        }

    }


    /// <summary>
    /// Every start with a charging ticket this EMSP countersigned, and the
    /// nonces of the start and stop requests it took, between starts of its
    /// own: one index, written whole and atomically at every change.
    /// </summary>
    /// <remarks>
    /// A nonce is kept for as long as a request with it would still be
    /// near enough to this EMSP's clock to be taken: twice the clock skew
    /// after the moment the request says it was made.
    /// </remarks>
    public sealed class TicketStartRegistry
    {

        public const String  IndexFileName  = "index.json";

        private readonly Dictionary<String, TicketStart>     starts        = [];
        private readonly Dictionary<String, DateTimeOffset>  nonces        = [];
        private readonly Lock                                registryLock  = new ();

        /// <summary>Where the index lies.</summary>
        public String  Directory  { get; }

        public String  IndexPath
            => Path.Combine(Directory, IndexFileName);

        /// <summary>Every start, the newest first.</summary>
        public IReadOnlyList<TicketStart>  All
        {
            get
            {
                lock (registryLock)
                    return [.. starts.Values.OrderByDescending(start => start.StartedAt)];
            }
        }

        /// <summary>
        /// The registry in the given directory, read.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the index is there but cannot be read.</exception>
        public TicketStartRegistry(String Directory)
        {

            this.Directory = Directory;

            System.IO.Directory.CreateDirectory(Directory);

            if (!File.Exists(IndexPath))
                return;

            JObject index;

            try
            {
                using var reader = new JsonTextReader(new StringReader(File.ReadAllText(IndexPath))) { DateParseHandling = DateParseHandling.None };
                index = JObject.Load(reader);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"The index of the starts with charging tickets '{IndexPath}' could not be read: {e.Message} Repair or remove it and start again.", e);
            }

            foreach (var record in (index["starts"] as JArray ?? []).OfType<JObject>())
            {

                if (!TicketStart.TryParse(record, out var start, out var error))
                    throw new InvalidOperationException($"The index of the starts with charging tickets '{IndexPath}' could not be read: {error} Repair or remove it and start again.");

                starts[start.Token] = start;

            }

            foreach (var nonce in (index["nonces"] as JArray ?? []).OfType<JObject>())
                if (nonce.Value<String>("nonce") is { Length: > 0 } text && TicketStart.Timestamp(nonce, "at", out var at))
                    nonces[text] = at;

        }

        /// <summary>The start a token is of, if any.</summary>
        public Boolean TryGet(String                                Token,
                              [NotNullWhen(true)] out TicketStart?  Start)
        {
            lock (registryLock)
                return starts.TryGetValue(Token, out Start);
        }

        /// <summary>The start a stop request names by the SHA-256 of its countersigned bytes, if any.</summary>
        public Boolean TryGetByStart(String                                Digest,
                                     [NotNullWhen(true)] out TicketStart?  Start)
        {
            lock (registryLock)
                Start = starts.Values.FirstOrDefault(start => String.Equals(start.Start, Digest, StringComparison.OrdinalIgnoreCase));
            return Start is not null;
        }

        /// <summary>
        /// A nonce taken: refused where it was seen before, or where the index
        /// cannot be written - the nonce then is not taken.
        /// </summary>
        public Boolean TryTakeNonce(String                            Nonce,
                                    DateTimeOffset                    At,
                                    DateTimeOffset                    Now,
                                    [NotNullWhen(false)] out String?  Error,
                                    out Boolean                       NotSaved)
        {

            lock (registryLock)
            {

                foreach (var old in nonces.Where(nonce => nonce.Value < Now - 2 * TicketRequest.ClockSkew).Select(nonce => nonce.Key).ToArray())
                    nonces.Remove(old);

                if (nonces.ContainsKey(Nonce))
                {
                    Error    = "The request's nonce was seen before: a request is taken once.";
                    NotSaved = false;
                    return false;
                }

                nonces[Nonce] = At;

                if (!TryWrite(out Error))
                {
                    nonces.Remove(Nonce);
                    NotSaved = true;
                    return false;
                }

                NotSaved = false;
                return true;

            }

        }

        /// <summary>
        /// A start just countersigned, or a start changed - refused where the
        /// index cannot be written, the start then as it was.
        /// </summary>
        public Boolean TrySet(TicketStart                       Start,
                              [NotNullWhen(false)] out String?  Error)
        {

            lock (registryLock)
            {

                starts.TryGetValue(Start.Token, out var before);

                starts[Start.Token] = Start;

                if (!TryWrite(out Error))
                {
                    if (before is null)
                        starts.Remove(Start.Token);
                    else
                        starts[Start.Token] = before;
                    return false;
                }

                return true;

            }

        }

        private Boolean TryWrite([NotNullWhen(false)] out String? Error)
        {

            var json       = new JObject(
                                 new JProperty("starts",  new JArray(starts.Values.OrderBy(start => start.StartedAt).Select(start => start.ToJSON()))),
                                 new JProperty("nonces",  new JArray(nonces.Select(nonce => new JObject(
                                                                                                new JProperty("nonce",  nonce.Key),
                                                                                                new JProperty("at",     nonce.Value.ToString("o"))
                                                                                            ))))
                             );

            var temporary  = IndexPath + ".new";

            try
            {
                File.WriteAllText(temporary, json.ToString());
                File.Move        (temporary, IndexPath, overwrite: true);
            }
            catch (Exception e)
            {
                Error = $"The index of the starts with charging tickets '{IndexPath}' could not be written: {e.Message}";
                return false;
            }

            Error = null;
            return true;

        }

    }

}
