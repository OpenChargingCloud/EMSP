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
    /// A certificate of a driver's own long-term key: what says that a
    /// signature by that key is the account's. For nothing but that - it opens
    /// no charging station; a charging ticket signed with it does.
    /// </summary>
    /// <param name="Id">The SHA-256 of the certificate, hex: how a signature names it.</param>
    /// <param name="Owner">The account it is made out to.</param>
    /// <param name="Label">What the driver calls the key, or null.</param>
    /// <param name="SerialNumber">The serial number, hex.</param>
    /// <param name="NotBefore">From when it is good: a minute before it was made.</param>
    /// <param name="NotAfter">Until when.</param>
    /// <param name="IssuedAt">When it was made.</param>
    /// <param name="RevokedAt">When it was taken back, or null.</param>
    /// <param name="RevokedBy">Who took it back, or null.</param>
    public sealed record AccountCertificate(String           Id,
                                            String           Owner,
                                            String?          Label,
                                            String           SerialNumber,
                                            DateTimeOffset   NotBefore,
                                            DateTimeOffset   NotAfter,
                                            DateTimeOffset   IssuedAt,
                                            DateTimeOffset?  RevokedAt  = null,
                                            String?          RevokedBy  = null)
    {

        /// <summary>The file the certificate lies in.</summary>
        public String   FileName
            => $"{Id}.cert.pem";

        /// <summary>Whether it was taken back.</summary>
        public Boolean  IsRevoked
            => RevokedAt.HasValue;

        /// <summary>"valid", "revoked", "expired" or "pending".</summary>
        public String Status(DateTimeOffset Now)

            => IsRevoked          ? "revoked"
             : Now >= NotAfter    ? "expired"
             : Now <  NotBefore   ? "pending"
                                  : "valid";

        /// <summary>Whether a signature by its key counts now.</summary>
        public Boolean IsValid(DateTimeOffset Now)
            => Status(Now) == "valid";

        /// <summary>The record as the web interface reads it, and as the index writes it.</summary>
        public JObject ToJSON(DateTimeOffset Now)

            => new (
                   new JProperty("id",            Id),
                   new JProperty("owner",         Owner),
                   new JProperty("label",         Label),
                   new JProperty("serialNumber",  SerialNumber),
                   new JProperty("notBefore",     NotBefore.ToString("o")),
                   new JProperty("notAfter",      NotAfter. ToString("o")),
                   new JProperty("issuedAt",      IssuedAt. ToString("o")),
                   new JProperty("revokedAt",     RevokedAt?.ToString("o")),
                   new JProperty("revokedBy",     RevokedBy),
                   new JProperty("status",        Status(Now))
               );

        /// <summary>A record as the index wrote it.</summary>
        public static Boolean TryParse(JObject                                       JSON,
                                       [NotNullWhen(true)]  out AccountCertificate?  Certificate,
                                       [NotNullWhen(false)] out String?              Error)
        {

            Certificate = null;
            Error       = null;

            var id      = JSON.Value<String>("id");
            var owner   = JSON.Value<String>("owner");
            var serial  = JSON.Value<String>("serialNumber");

            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(owner) || String.IsNullOrEmpty(serial) ||
                !Timestamp(JSON, "notBefore", out var notBefore) ||
                !Timestamp(JSON, "notAfter",  out var notAfter)  ||
                !Timestamp(JSON, "issuedAt",  out var issuedAt))
            {
                Error = $"The account certificate '{id}' lacks its id, owner, serial number or one of its dates.";
                return false;
            }

            DateTimeOffset? revokedAt = Timestamp(JSON, "revokedAt", out var revoked) ? revoked : null;

            Certificate = new AccountCertificate(id, owner, JSON.Value<String>("label"), serial,
                                                 notBefore, notAfter, issuedAt, revokedAt, JSON.Value<String>("revokedBy"));
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
    /// Every account certificate this EMSP issued, between starts: one PEM
    /// per certificate and an index of whose each is, written whole and
    /// atomically at every change - as the registry of contracts is.
    /// </summary>
    public sealed class AccountCertificateRegistry
    {

        #region Data

        /// <summary>The index file.</summary>
        public const String  IndexFileName  = "index.json";

        private readonly Dictionary<String, AccountCertificate>  certificates  = [];
        private readonly Lock                                    registryLock  = new ();

        #endregion

        #region Properties

        /// <summary>Where the certificates and the index lie.</summary>
        public String  Directory  { get; }

        /// <summary>The index.</summary>
        public String  IndexPath
            => Path.Combine(Directory, IndexFileName);

        /// <summary>Every certificate, the newest first.</summary>
        public IReadOnlyList<AccountCertificate>  All
        {
            get
            {
                lock (registryLock)
                    return [.. certificates.Values.OrderByDescending(certificate => certificate.IssuedAt)];
            }
        }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// The registry in the given directory, read.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the index is there but cannot be read.</exception>
        public AccountCertificateRegistry(String Directory)
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
                throw new InvalidOperationException($"The index of the account certificates '{IndexPath}' could not be read: {e.Message} Repair or remove it and start again.", e);
            }

            foreach (var record in (index["certificates"] as JArray ?? []).OfType<JObject>())
            {

                if (!AccountCertificate.TryParse(record, out var certificate, out var error))
                    throw new InvalidOperationException($"The index of the account certificates '{IndexPath}' could not be read: {error} Repair or remove it and start again.");

                certificates[certificate.Id] = certificate;

            }

        }

        #endregion


        /// <summary>The certificates of one account, the newest first.</summary>
        public IReadOnlyList<AccountCertificate> OfOwner(String Owner)
        {
            lock (registryLock)
                return [.. certificates.Values.
                               Where(certificate => String.Equals(certificate.Owner, Owner, StringComparison.OrdinalIgnoreCase)).
                               OrderByDescending(certificate => certificate.IssuedAt)];
        }

        /// <summary>The certificate of the given id, when there is one.</summary>
        public Boolean TryGet(String                                        Id,
                              [NotNullWhen(true)] out AccountCertificate?   Certificate)
        {
            lock (registryLock)
                return certificates.TryGetValue(Id.ToUpperInvariant(), out Certificate);
        }

        /// <summary>The certificate as PEM, as it was written.</summary>
        public Boolean TryReadPEM(AccountCertificate                Certificate,
                                  [NotNullWhen(true)] out String?   PEM)
        {
            var path = Path.Combine(Directory, Certificate.FileName);
            PEM = File.Exists(path) ? File.ReadAllText(path) : null;
            return PEM is not null;
        }

        /// <summary>
        /// A freshly issued certificate: its PEM and its record. Where either
        /// cannot be written, nothing goes in.
        /// </summary>
        public Boolean TryAdd(AccountCertificate                Certificate,
                              String                            PEM,
                              [NotNullWhen(false)] out String?  Error)
        {

            lock (registryLock)
            {

                var file = Path.Combine(Directory, Certificate.FileName);

                try
                {
                    File.WriteAllText(file, PEM);
                }
                catch (Exception e)
                {
                    Error = $"The account certificate {Certificate.Id} could not be written to '{file}': {e.Message}";
                    return false;
                }

                certificates[Certificate.Id] = Certificate;

                if (!TryWriteIndex(out Error))
                {
                    certificates.Remove(Certificate.Id);
                    try { File.Delete(file); } catch { }
                    return false;
                }

                return true;

            }

        }

        /// <summary>
        /// A certificate taken back. Where the index cannot be written, it
        /// stays as it was - good.
        /// </summary>
        public Boolean TryRevoke(String                                         Id,
                                 String                                         By,
                                 DateTimeOffset                                 Now,
                                 [NotNullWhen(true)]  out AccountCertificate?   Certificate,
                                 [NotNullWhen(false)] out String?               Error,
                                 out Boolean                                    NotSaved)
        {

            Certificate  = null;
            NotSaved     = false;

            lock (registryLock)
            {

                if (!certificates.TryGetValue(Id.ToUpperInvariant(), out var existing) || existing.IsRevoked)
                {
                    Error = $"There is no account certificate {Id} to take back, or it was taken back already.";
                    return false;
                }

                var revoked = existing with { RevokedAt = Now, RevokedBy = By };

                certificates[existing.Id] = revoked;

                if (!TryWriteIndex(out Error))
                {
                    certificates[existing.Id] = existing;
                    NotSaved = true;
                    return false;
                }

                Certificate = revoked;
                return true;

            }

        }

        private Boolean TryWriteIndex([NotNullWhen(false)] out String? Error)
        {

            var now        = DateTimeOffset.UtcNow;
            var json       = new JObject(new JProperty("certificates", new JArray(certificates.Values.OrderBy(certificate => certificate.IssuedAt).Select(certificate => certificate.ToJSON(now)))));
            var temporary  = IndexPath + ".new";

            try
            {
                File.WriteAllText(temporary, json.ToString());
                File.Move        (temporary, IndexPath, overwrite: true);
            }
            catch (Exception e)
            {
                Error = $"The index of the account certificates '{IndexPath}' could not be written: {e.Message}";
                return false;
            }

            Error = null;
            return true;

        }

    }

}
