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

namespace cloud.charging.open.EMSP.Contracts
{

    /// <summary>
    /// Every contract certificate this EMSP issued, between starts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A directory: one PEM per certificate, named after the eMAID, and an
    /// index that says whom each one belongs to and whether it was taken
    /// back. The index is rewritten whole and atomically at every change - a
    /// few hundred records at most, and a file that is either the old one
    /// or the new one and never half of each.
    /// </para>
    /// <para>
    /// The certificates themselves are public: anybody who charged with one
    /// has shown it to a station. What the index adds is the owner, which
    /// is why the registry lies beside the accounts and not in the web root.
    /// </para>
    /// </remarks>
    public sealed class ContractRegistry
    {

        #region Data

        /// <summary>
        /// The directory below the PKI directory that holds the registry.
        /// </summary>
        public const String  DirectoryName  = "contracts";

        /// <summary>
        /// The index file.
        /// </summary>
        public const String  IndexFileName  = "index.json";

        private readonly Dictionary<String, ContractCertificate>  contracts  = [];
        private readonly Lock                                     registryLock = new ();

        #endregion

        #region Properties

        /// <summary>
        /// Where the certificates and the index live.
        /// </summary>
        public String  Directory    { get; }

        /// <summary>
        /// Where the index lies.
        /// </summary>
        public String  IndexPath
            => Path.Combine(Directory, IndexFileName);

        /// <summary>
        /// How many contracts there are, revoked and expired ones included.
        /// </summary>
        public Int32   Count
        {
            get
            {
                lock (registryLock)
                    return contracts.Count;
            }
        }

        /// <summary>
        /// Every contract, newest first.
        /// </summary>
        public IReadOnlyList<ContractCertificate>  All
        {
            get
            {
                lock (registryLock)
                    return [.. contracts.Values.OrderByDescending(contract => contract.IssuedAt)];
            }
        }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// The registry in the given directory, read.
        /// </summary>
        /// <param name="Directory">Where the certificates and the index live; created when it does not exist.</param>
        /// <exception cref="InvalidOperationException">When the index is there but cannot be read.</exception>
        public ContractRegistry(String Directory)
        {

            this.Directory = Directory;

            System.IO.Directory.CreateDirectory(Directory);

            if (!File.Exists(IndexPath))
                return;

            JObject index;

            try
            {

                // Json.NET would otherwise read every ISO 8601 string as a
                // date of its own accord and hand it back in whatever form the
                // culture likes; the timestamps here are strings until
                // ContractCertificate reads them.
                using var reader = new JsonTextReader(new StringReader(File.ReadAllText(IndexPath))) {
                                       DateParseHandling = DateParseHandling.None
                                   };

                index = JObject.Load(reader);

            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"The contract index '{IndexPath}' could not be read: {e.Message} Repair or remove it and start again.", e);
            }

            if (index["contracts"] is not JArray records)
                throw new InvalidOperationException($"The contract index '{IndexPath}' has no 'contracts' array. Repair or remove it and start again.");

            foreach (var record in records.OfType<JObject>())
            {

                if (!ContractCertificate.TryParse(record, out var contract, out var error))
                    throw new InvalidOperationException($"The contract index '{IndexPath}' could not be read: {error} Repair or remove it and start again.");

                contracts[contract.EMAId.Compact] = contract;

            }

        }

        #endregion


        #region OfOwner(Owner)

        /// <summary>
        /// The contracts of one account, newest first.
        /// </summary>
        public IReadOnlyList<ContractCertificate> OfOwner(String Owner)
        {
            lock (registryLock)
                return [.. contracts.Values.
                               Where(contract => String.Equals(contract.Owner, Owner, StringComparison.OrdinalIgnoreCase)).
                               OrderByDescending(contract => contract.IssuedAt)];
        }

        #endregion

        #region TryGet(EMAId, out Contract)

        /// <summary>
        /// The contract made out to the given eMAID, when there is one.
        /// </summary>
        public Boolean TryGet(EMAId                                          EMAId,
                              [NotNullWhen(true)] out ContractCertificate?  Contract)
        {
            lock (registryLock)
                return contracts.TryGetValue(EMAId.Compact, out Contract);
        }

        #endregion

        #region Contains(EMAId)

        /// <summary>
        /// Whether a contract was ever made out to the given eMAID.
        /// </summary>
        public Boolean Contains(EMAId EMAId)
        {
            lock (registryLock)
                return contracts.ContainsKey(EMAId.Compact);
        }

        #endregion

        #region Add(Contract, PEM)

        /// <summary>
        /// Put a freshly issued contract into the registry: its certificate
        /// as a file of its own, and its record into the index.
        /// </summary>
        public void Add(ContractCertificate  Contract,
                        String               PEM)
        {

            lock (registryLock)
            {

                File.WriteAllText(Path.Combine(Directory, Contract.FileName), PEM);

                contracts[Contract.EMAId.Compact] = Contract;

                WriteIndex();

            }

        }

        #endregion

        #region TryRevoke(EMAId, By, Now, out Contract)

        /// <summary>
        /// Take a contract back. The certificate stays where it is - a
        /// record of what was issued - and the index says it is over.
        /// </summary>
        /// <returns>False when there is no such contract, or it was already taken back.</returns>
        public Boolean TryRevoke(EMAId                                          EMAId,
                                 String                                         By,
                                 DateTimeOffset                                 Now,
                                 [NotNullWhen(true)] out ContractCertificate?  Contract)
        {

            lock (registryLock)
            {

                if (!contracts.TryGetValue(EMAId.Compact, out var existing) || existing.IsRevoked)
                {
                    Contract = null;
                    return false;
                }

                Contract = existing with {
                               RevokedAt  = Now,
                               RevokedBy  = By
                           };

                contracts[EMAId.Compact] = Contract;

                WriteIndex();

                return true;

            }

        }

        #endregion

        #region TryReadPEM(Contract, out PEM)

        /// <summary>
        /// The certificate of a contract, as it was written.
        /// </summary>
        public Boolean TryReadPEM(ContractCertificate                 Contract,
                                  [NotNullWhen(true)] out String?     PEM)
        {

            var path = Path.Combine(Directory, Contract.FileName);

            if (!File.Exists(path))
            {
                PEM = null;
                return false;
            }

            PEM = File.ReadAllText(path);
            return true;

        }

        #endregion


        #region (private) WriteIndex()

        /// <summary>
        /// The whole index, written beside itself and moved into place.
        /// </summary>
        private void WriteIndex()
        {

            var now   = DateTimeOffset.UtcNow;

            var json  = new JObject(
                            new JProperty("contracts", new JArray(
                                contracts.Values.
                                          OrderBy(contract => contract.IssuedAt).
                                          Select (contract => contract.ToJSON(now))
                            ))
                        );

            var temporary = IndexPath + ".new";

            File.WriteAllText(temporary, json.ToString());
            File.Move        (temporary, IndexPath, overwrite: true);

        }

        #endregion

    }

}
