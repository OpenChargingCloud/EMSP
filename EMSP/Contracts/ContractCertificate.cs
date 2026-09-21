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

namespace cloud.charging.open.EMSP.Contracts
{

    /// <summary>
    /// One contract certificate this EMSP issued: whom to, made out to which
    /// eMAID, and for how long.
    /// </summary>
    /// <remarks>
    /// A record and not the certificate: the certificate lies beside the
    /// index as PEM, and what is kept here is what the web interface lists
    /// and what a revocation changes. Nothing here is secret - the private
    /// key never came this way.
    /// </remarks>
    /// <param name="EMAId">The e-mobility account identifier the certificate is made out to.</param>
    /// <param name="Owner">The account that asked for it.</param>
    /// <param name="SerialNumber">The serial number of the certificate, as hex.</param>
    /// <param name="Thumbprint">The SHA-256 fingerprint of the certificate.</param>
    /// <param name="NotBefore">From when the certificate is good.</param>
    /// <param name="NotAfter">Until when.</param>
    /// <param name="IssuedAt">When it was issued.</param>
    /// <param name="RevokedAt">When it was taken back, or null.</param>
    /// <param name="RevokedBy">Who took it back, or null.</param>
    public sealed record ContractCertificate(EMAId            EMAId,
                                             String           Owner,
                                             String           SerialNumber,
                                             String           Thumbprint,
                                             DateTimeOffset   NotBefore,
                                             DateTimeOffset   NotAfter,
                                             DateTimeOffset   IssuedAt,
                                             DateTimeOffset?  RevokedAt   = null,
                                             String?          RevokedBy   = null)
    {

        #region Properties

        /// <summary>
        /// The file the certificate lies in, below the registry's directory.
        /// </summary>
        public String   FileName
            => $"{EMAId.Compact}.cert.pem";

        /// <summary>
        /// Whether this contract was taken back.
        /// </summary>
        public Boolean  IsRevoked
            => RevokedAt.HasValue;

        #endregion


        #region Status(Now)

        /// <summary>
        /// One word on where this contract stands: "valid", "revoked",
        /// "expired" or "pending".
        /// </summary>
        public String Status(DateTimeOffset Now)

            => IsRevoked          ? "revoked"
             : Now >= NotAfter    ? "expired"
             : Now <  NotBefore   ? "pending"
                                  : "valid";

        #endregion

        #region ToJSON(Now)

        /// <summary>
        /// The record as the web interface reads it, and as the index writes it.
        /// </summary>
        public JObject ToJSON(DateTimeOffset Now)

            => new (
                   new JProperty("emaId",         EMAId.ToString()),
                   new JProperty("emaIdCompact",  EMAId.Compact),
                   new JProperty("owner",         Owner),
                   new JProperty("serialNumber",  SerialNumber),
                   new JProperty("thumbprint",    Thumbprint),
                   new JProperty("notBefore",     NotBefore.ToString("o")),
                   new JProperty("notAfter",      NotAfter. ToString("o")),
                   new JProperty("issuedAt",      IssuedAt. ToString("o")),
                   new JProperty("revokedAt",     RevokedAt?.ToString("o")),
                   new JProperty("revokedBy",     RevokedBy),
                   new JProperty("status",        Status(Now)),
                   new JProperty("file",          FileName)
               );

        #endregion

        #region (static) TryParse(JSON, out Contract, out Error)

        /// <summary>
        /// A record as the index wrote it.
        /// </summary>
        public static Boolean TryParse(JObject                                        JSON,
                                       [NotNullWhen(true)]  out ContractCertificate?  Contract,
                                       [NotNullWhen(false)] out String?               Error)
        {

            Contract  = null;
            Error     = null;

            if (!EMAId.TryParse(JSON.Value<String>("emaId"), out var emaId, out Error))
                return false;

            var owner         = JSON.Value<String>("owner");
            var serialNumber  = JSON.Value<String>("serialNumber");
            var thumbprint    = JSON.Value<String>("thumbprint");

            if (String.IsNullOrWhiteSpace(owner) || String.IsNullOrWhiteSpace(serialNumber) || String.IsNullOrWhiteSpace(thumbprint))
            {
                Error = $"The contract '{emaId}' has no owner, serial number or thumbprint.";
                return false;
            }

            if (!TryReadTimestamp(JSON, "notBefore", out var notBefore, out Error) ||
                !TryReadTimestamp(JSON, "notAfter",  out var notAfter,  out Error) ||
                !TryReadTimestamp(JSON, "issuedAt",  out var issuedAt,  out Error))
            {
                return false;
            }

            DateTimeOffset? revokedAt = null;

            if (JSON["revokedAt"] is JToken revoked && revoked.Type != JTokenType.Null)
            {

                if (!TryReadTimestamp(JSON, "revokedAt", out var parsedRevokedAt, out Error))
                    return false;

                revokedAt = parsedRevokedAt;

            }

            Contract = new ContractCertificate(
                           emaId,
                           owner,
                           serialNumber,
                           thumbprint,
                           notBefore,
                           notAfter,
                           issuedAt,
                           revokedAt,
                           JSON.Value<String>("revokedBy")
                       );

            return true;

        }

        #endregion


        #region (private static) TryReadTimestamp(JSON, Name, out Timestamp, out Error)

        private static Boolean TryReadTimestamp(JObject                           JSON,
                                                String                            Name,
                                                out DateTimeOffset                Timestamp,
                                                [NotNullWhen(false)] out String?  Error)
        {

            Timestamp  = default;
            Error      = null;

            var token = JSON[Name];

            if (token is null || token.Type == JTokenType.Null)
            {
                Error = $"'{Name}' of a contract is missing.";
                return false;
            }

            // Json.NET reads an ISO 8601 string as a date of its own accord
            // wherever it was not told not to, and writes such a date back in
            // whatever form the culture likes - so a date is taken as it is,
            // and only a string is parsed.
            if (token is JValue { Type: JTokenType.Date } date)
            {

                // What it parsed it into is a DateTime and not a
                // DateTimeOffset, and a DateTime without a kind is taken as
                // UTC: that is what every timestamp here is written as.
                Timestamp = date.Value switch {
                                DateTimeOffset offset                          => offset,
                                DateTime { Kind: DateTimeKind.Unspecified } dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                                DateTime dt                                    => new DateTimeOffset(dt),
                                _                                              => default
                            };

                return true;

            }

            var text = token.ToString();

            if (!DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out Timestamp))
            {
                Error = $"'{Name}' of a contract is not a timestamp: \"{text}\".";
                return false;
            }

            return true;

        }

        #endregion

    }

}
