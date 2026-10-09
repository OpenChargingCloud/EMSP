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

namespace cloud.charging.open.EMSP.Drivers
{

    /// <summary>
    /// Every RFID card the drivers brought, between starts.
    /// </summary>
    /// <remarks>
    /// One index, rewritten whole and atomically at every change - as the
    /// registry of contracts is - so that it is either the old file or the
    /// new one and never half of each. A change that cannot be written is
    /// no change: the card stays as it was, in memory as on disk.
    /// </remarks>
    public sealed class DriverCardRegistry
    {

        #region Data

        /// <summary>
        /// The directory beside the configuration file that holds the index.
        /// </summary>
        public const String  DirectoryName  = "cards";

        /// <summary>
        /// The index file.
        /// </summary>
        public const String  IndexFileName  = "index.json";

        private readonly Dictionary<String, DriverCard>  cards         = [];
        private readonly Lock                            registryLock  = new ();

        #endregion

        #region Properties

        /// <summary>
        /// Where the index lives.
        /// </summary>
        public String  Directory    { get; }

        /// <summary>
        /// The index.
        /// </summary>
        public String  IndexPath
            => Path.Combine(Directory, IndexFileName);

        /// <summary>
        /// Every card, the newest first.
        /// </summary>
        public IReadOnlyList<DriverCard>  All
        {
            get
            {
                lock (registryLock)
                    return [.. cards.Values.OrderByDescending(card => card.RequestedAt)];
            }
        }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// The registry in the given directory, read.
        /// </summary>
        /// <param name="Directory">Where the index lives; created when it does not exist.</param>
        /// <exception cref="InvalidOperationException">When the index is there but cannot be read.</exception>
        public DriverCardRegistry(String Directory)
        {

            this.Directory = Directory;

            System.IO.Directory.CreateDirectory(Directory);

            if (!File.Exists(IndexPath))
                return;

            JObject index;

            try
            {

                // The timestamps are strings until DriverCard reads them, rather
                // than whatever Json.NET would make of them on its own.
                using var reader = new JsonTextReader(new StringReader(File.ReadAllText(IndexPath))) {
                                       DateParseHandling = DateParseHandling.None
                                   };

                index = JObject.Load(reader);

            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"The index of the drivers' cards '{IndexPath}' could not be read: {e.Message} Repair or remove it and start again.", e);
            }

            if (index["cards"] is not JArray records)
                throw new InvalidOperationException($"The index of the drivers' cards '{IndexPath}' has no 'cards' array. Repair or remove it and start again.");

            foreach (var record in records.OfType<JObject>())
            {

                if (!DriverCard.TryParse(record, out var card, out var error))
                    throw new InvalidOperationException($"The index of the drivers' cards '{IndexPath}' could not be read: {error} Repair or remove it and start again.");

                cards[card.UID] = card;

            }

        }

        #endregion


        #region OfOwner(Owner)

        /// <summary>
        /// The cards of one account, the newest first.
        /// </summary>
        public IReadOnlyList<DriverCard> OfOwner(String Owner)
        {
            lock (registryLock)
                return [.. cards.Values.
                               Where(card => String.Equals(card.Owner, Owner, StringComparison.OrdinalIgnoreCase)).
                               OrderByDescending(card => card.RequestedAt)];
        }

        #endregion

        #region TryGet(UID, out Card)

        /// <summary>
        /// The card of the given UID, when there is one.
        /// </summary>
        public Boolean TryGet(String                                UID,
                              [NotNullWhen(true)] out DriverCard?   Card)
        {
            lock (registryLock)
                return cards.TryGetValue(UID, out Card);
        }

        #endregion

        #region TryAdd(Card, out Error, out NotSaved)

        /// <summary>
        /// A card a driver entered.
        /// </summary>
        /// <param name="Card">The card.</param>
        /// <param name="Error">Why it did not go in.</param>
        /// <param name="NotSaved">True where the index could not be written.</param>
        public Boolean TryAdd(DriverCard                        Card,
                              [NotNullWhen(false)] out String?  Error,
                              out Boolean                       NotSaved)
        {

            NotSaved = false;

            lock (registryLock)
            {

                if (cards.TryGetValue(Card.UID, out var existing))
                {
                    Error = String.Equals(existing.Owner, Card.Owner, StringComparison.OrdinalIgnoreCase)
                                ? $"You entered the card {Card.UID} already."
                                : $"The card {Card.UID} is somebody else's.";
                    return false;
                }

                cards[Card.UID] = Card;

                if (!TryWriteIndex(out Error))
                {
                    cards.Remove(Card.UID);
                    NotSaved = true;
                    return false;
                }

                return true;

            }

        }

        #endregion

        #region TryReplace(Existing, Changed, out Error, out NotSaved)

        /// <summary>
        /// A card as it is now, in place of the one it was - where nothing
        /// else changed it in between.
        /// </summary>
        /// <param name="Existing">The card as it was read.</param>
        /// <param name="Changed">The card as it is to be.</param>
        /// <param name="Error">Why it did not change.</param>
        /// <param name="NotSaved">True where the index could not be written: the card is as it was.</param>
        public Boolean TryReplace(DriverCard                        Existing,
                                  DriverCard                        Changed,
                                  [NotNullWhen(false)] out String?  Error,
                                  out Boolean                       NotSaved)
        {

            NotSaved = false;

            lock (registryLock)
            {

                if (!cards.TryGetValue(Existing.UID, out var current) || current != Existing)
                {
                    Error = $"The card {Existing.UID} was changed or removed meanwhile. Reload and try again.";
                    return false;
                }

                cards[Existing.UID] = Changed;

                if (!TryWriteIndex(out Error))
                {
                    cards[Existing.UID] = Existing;
                    NotSaved = true;
                    return false;
                }

                return true;

            }

        }

        #endregion

        #region TryRemove(Existing, out Error, out NotSaved)

        /// <summary>
        /// A card taken out of the registry - where nothing changed it in
        /// between.
        /// </summary>
        public Boolean TryRemove(DriverCard                        Existing,
                                 [NotNullWhen(false)] out String?  Error,
                                 out Boolean                       NotSaved)
        {

            NotSaved = false;

            lock (registryLock)
            {

                if (!cards.TryGetValue(Existing.UID, out var current) || current != Existing)
                {
                    Error = $"The card {Existing.UID} was changed or removed meanwhile. Reload and try again.";
                    return false;
                }

                cards.Remove(Existing.UID);

                if (!TryWriteIndex(out Error))
                {
                    cards[Existing.UID] = Existing;
                    NotSaved = true;
                    return false;
                }

                return true;

            }

        }

        #endregion


        #region (private) TryWriteIndex(out Error)

        /// <summary>
        /// The whole index, written beside itself and moved into place - or
        /// why it could not be, the index as it was.
        /// </summary>
        private Boolean TryWriteIndex([NotNullWhen(false)] out String? Error)
        {

            var json = new JObject(
                           new JProperty("cards", new JArray(
                               cards.Values.
                                     OrderBy(card => card.RequestedAt).
                                     Select (card => card.ToJSON())
                           ))
                       );

            var temporary = IndexPath + ".new";

            try
            {
                File.WriteAllText(temporary, json.ToString());
                File.Move        (temporary, IndexPath, overwrite: true);
            }
            catch (Exception e)
            {
                Error = $"The index of the drivers' cards '{IndexPath}' could not be written: {e.Message}";
                return false;
            }

            Error = null;
            return true;

        }

        #endregion

    }

}
