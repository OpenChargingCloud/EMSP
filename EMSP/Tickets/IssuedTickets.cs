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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#endregion

namespace cloud.charging.open.EMSP.Tickets
{

    /// <summary>
    /// A charging ticket this EMSP signed, as it remembers it: which ticket,
    /// for how long and within which limits - and, for this EMSP alone,
    /// whose it is and which of the driver's keys asked for it.
    /// </summary>
    /// <remarks>
    /// The ticket a charge point operator is shown says nothing of the
    /// driver. What links the two is here, for billing a charge and stopping
    /// a ticket that is misused, and goes nowhere else.
    /// </remarks>
    public sealed record IssuedTicket(String           Id,
                                      String           Owner,
                                      String           AccountCertificate,
                                      DateTimeOffset   NotBefore,
                                      DateTimeOffset   NotAfter,
                                      Decimal?         MaxKW,
                                      UInt32?          MaxMinutes,
                                      Decimal?         MaxKWh,
                                      DateTimeOffset   IssuedAt)
    {

        /// <summary>"valid", "expired" or "pending".</summary>
        public String Status(DateTimeOffset Now)

            => Now >= NotAfter   ? "expired"
             : Now <  NotBefore  ? "pending"
                                 : "valid";

        /// <summary>The record as the web interface reads it, and as the index writes it.</summary>
        public JObject ToJSON(DateTimeOffset Now)

            => new (
                   new JProperty("id",                  Id),
                   new JProperty("owner",               Owner),
                   new JProperty("accountCertificate",  AccountCertificate),
                   new JProperty("notBefore",           NotBefore.ToString("o")),
                   new JProperty("notAfter",            NotAfter. ToString("o")),
                   new JProperty("maxKW",               MaxKW),
                   new JProperty("maxMinutes",          MaxMinutes),
                   new JProperty("maxKWh",              MaxKWh),
                   new JProperty("issuedAt",            IssuedAt. ToString("o")),
                   new JProperty("status",              Status(Now))
               );

        /// <summary>A record as the index wrote it.</summary>
        public static Boolean TryParse(JObject                                  JSON,
                                       [NotNullWhen(true)]  out IssuedTicket?   Ticket,
                                       [NotNullWhen(false)] out String?         Error)
        {

            Ticket = null;
            Error  = null;

            var id       = JSON.Value<String>("id");
            var owner    = JSON.Value<String>("owner");
            var account  = JSON.Value<String>("accountCertificate");

            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(owner) || String.IsNullOrEmpty(account) ||
                !Timestamp(JSON, "notBefore", out var notBefore) ||
                !Timestamp(JSON, "notAfter",  out var notAfter)  ||
                !Timestamp(JSON, "issuedAt",  out var issuedAt))
            {
                Error = $"The ticket '{id}' lacks its id, owner, account certificate or one of its dates.";
                return false;
            }

            Ticket = new IssuedTicket(id, owner, account, notBefore, notAfter,
                                      JSON.Value<Decimal?>("maxKW"), JSON.Value<UInt32?>("maxMinutes"), JSON.Value<Decimal?>("maxKWh"),
                                      issuedAt);
            return true;

        }

        private static Boolean Timestamp(JObject JSON, String Name, out DateTimeOffset Value)
        {
            Value = default;
            return JSON.Value<String>(Name) is { Length: > 0 } text &&
                   DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out Value);
        }

    }


    /// <summary>
    /// Every charging ticket this EMSP signed, between starts: one index,
    /// written whole and atomically at every change.
    /// </summary>
    public sealed class IssuedTicketRegistry
    {

        public const String  IndexFileName  = "index.json";

        private readonly Dictionary<String, IssuedTicket>  tickets       = [];
        private readonly Lock                              registryLock  = new ();

        /// <summary>Where the index lies.</summary>
        public String  Directory  { get; }

        public String  IndexPath
            => Path.Combine(Directory, IndexFileName);

        /// <summary>Every ticket, the newest first.</summary>
        public IReadOnlyList<IssuedTicket>  All
        {
            get
            {
                lock (registryLock)
                    return [.. tickets.Values.OrderByDescending(ticket => ticket.IssuedAt)];
            }
        }

        /// <summary>
        /// The registry in the given directory, read.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the index is there but cannot be read.</exception>
        public IssuedTicketRegistry(String Directory)
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
                throw new InvalidOperationException($"The index of the charging tickets '{IndexPath}' could not be read: {e.Message} Repair or remove it and start again.", e);
            }

            foreach (var record in (index["tickets"] as JArray ?? []).OfType<JObject>())
            {

                if (!IssuedTicket.TryParse(record, out var ticket, out var error))
                    throw new InvalidOperationException($"The index of the charging tickets '{IndexPath}' could not be read: {error} Repair or remove it and start again.");

                tickets[ticket.Id] = ticket;

            }

        }

        /// <summary>The tickets of one account, the newest first.</summary>
        public IReadOnlyList<IssuedTicket> OfOwner(String Owner)
        {
            lock (registryLock)
                return [.. tickets.Values.
                               Where(ticket => String.Equals(ticket.Owner, Owner, StringComparison.OrdinalIgnoreCase)).
                               OrderByDescending(ticket => ticket.IssuedAt)];
        }

        /// <summary>Whether a ticket of that id was ever signed.</summary>
        public Boolean Contains(String Id)
        {
            lock (registryLock)
                return tickets.ContainsKey(Id);
        }

        /// <summary>
        /// A ticket just signed - refused where one of its id was signed before,
        /// or where the index cannot be written.
        /// </summary>
        public Boolean TryAdd(IssuedTicket                      Ticket,
                              [NotNullWhen(false)] out String?  Error,
                              out Boolean                       NotSaved)
        {

            NotSaved = false;

            lock (registryLock)
            {

                if (tickets.ContainsKey(Ticket.Id))
                {
                    Error = $"A ticket {Ticket.Id} was signed before: every ticket has an id of its own.";
                    return false;
                }

                tickets[Ticket.Id] = Ticket;

                var now        = DateTimeOffset.UtcNow;
                var json       = new JObject(new JProperty("tickets", new JArray(tickets.Values.OrderBy(ticket => ticket.IssuedAt).Select(ticket => ticket.ToJSON(now)))));
                var temporary  = IndexPath + ".new";

                try
                {
                    File.WriteAllText(temporary, json.ToString());
                    File.Move        (temporary, IndexPath, overwrite: true);
                }
                catch (Exception e)
                {
                    tickets.Remove(Ticket.Id);
                    Error    = $"The index of the charging tickets '{IndexPath}' could not be written: {e.Message}";
                    NotSaved = true;
                    return false;
                }

                Error = null;
                return true;

            }

        }

    }

}
