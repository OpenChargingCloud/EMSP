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
using System.Text.RegularExpressions;

#endregion

namespace cloud.charging.open.EMSP.Contracts
{

    /// <summary>
    /// An e-mobility account identifier: what a contract certificate is made
    /// out to, what a vehicle presents at a charging station, and what a
    /// charging station asks a roaming partner about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fifteen characters, as ISO 15118-1 Annex H writes them: a country code,
    /// the three characters of the provider, nine characters that name the
    /// contract, and a check digit over the fourteen in front of it. People
    /// read it with hyphens - "DE-GDF-C12345678-X" - and a certificate
    /// carries it without them.
    /// </para>
    /// <para>
    /// The instance this EMSP hands out starts with a "C", which is the EMI3
    /// convention every roaming platform reads as "a contract" - and which
    /// is what tells an identifier in this form apart from the older, shorter
    /// DIN SPEC 91286 one with a check digit of its own.
    /// </para>
    /// </remarks>
    public sealed record EMAId
    {

        #region Data

        /// <summary>
        /// The two letters of the country code, the three characters of the
        /// provider, the nine of the instance and the check digit - with or
        /// without a hyphen between them, and with or without the check digit.
        /// </summary>
        private static readonly Regex pattern = new (@"^([A-Z]{2})-?([A-Z0-9]{3})-?([A-Z0-9]{9})(?:-?([A-Z0-9]))?$",
                                                     RegexOptions.Compiled);

        /// <summary>
        /// What a made-up instance is drawn from.
        /// </summary>
        private const String alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        #endregion

        #region Properties

        /// <summary>
        /// The country, e.g. "DE".
        /// </summary>
        public String  CountryCode    { get; }

        /// <summary>
        /// The provider, e.g. "GDF".
        /// </summary>
        public String  ProviderId     { get; }

        /// <summary>
        /// The nine characters that name the contract, e.g. "C12345678".
        /// </summary>
        public String  Instance       { get; }

        /// <summary>
        /// The check digit over the fourteen characters in front of it.
        /// </summary>
        public Char    CheckDigit     { get; }

        /// <summary>
        /// All fifteen characters without separators: the form a certificate
        /// carries in its common name.
        /// </summary>
        public String  Compact
            => $"{CountryCode}{ProviderId}{Instance}{CheckDigit}";

        /// <summary>
        /// The country code and the provider together, as OCPI writes a
        /// party: "DE-GDF".
        /// </summary>
        public String  PartyId
            => $"{CountryCode}-{ProviderId}";

        #endregion

        #region Constructor(s)

        private EMAId(String  CountryCode,
                      String  ProviderId,
                      String  Instance,
                      Char    CheckDigit)
        {

            this.CountryCode  = CountryCode;
            this.ProviderId   = ProviderId;
            this.Instance     = Instance;
            this.CheckDigit   = CheckDigit;

        }

        #endregion


        #region (static) Create(CountryCode, ProviderId, Instance)

        /// <summary>
        /// An identifier out of its parts, with the check digit calculated.
        /// </summary>
        /// <param name="CountryCode">Two letters.</param>
        /// <param name="ProviderId">Three letters or digits.</param>
        /// <param name="Instance">Nine letters or digits.</param>
        public static EMAId Create(String  CountryCode,
                                   String  ProviderId,
                                   String  Instance)
        {

            if (TryParse($"{CountryCode}-{ProviderId}-{Instance}", out var emaId, out var error))
                return emaId;

            throw new ArgumentException(error);

        }

        #endregion

        #region (static) Random(CountryCode, ProviderId)

        /// <summary>
        /// A fresh identifier for the given provider: "C" and eight characters
        /// nobody chose.
        /// </summary>
        /// <remarks>
        /// Random rather than counted up, so that the identifiers this EMSP
        /// hands out say nothing about how many it has handed out - and so
        /// that two EMSPs of the same provider on two benches do not hand out
        /// the same one. Thirty-six to the eighth is enough for that.
        /// </remarks>
        public static EMAId Random(String  CountryCode,
                                   String  ProviderId)
        {

            var instance = new Char[9];

            instance[0] = 'C';

            for (var i = 1; i < instance.Length; i++)
                instance[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

            return Create(CountryCode, ProviderId, new String(instance));

        }

        #endregion

        #region (static) TryParse(Text, out EMAId, out Error)

        /// <summary>
        /// An identifier as somebody wrote it: with or without hyphens, in any
        /// case, and with or without its check digit - which, when it is
        /// there, has to be the right one.
        /// </summary>
        public static Boolean TryParse(String?                            Text,
                                       [NotNullWhen(true)]  out EMAId?    EMAId,
                                       [NotNullWhen(false)] out String?   Error)
        {

            EMAId  = null;
            Error  = null;

            var text   = Text?.Trim().ToUpperInvariant() ?? "";
            var match  = pattern.Match(text);

            if (!match.Success)
            {
                Error = $"\"{Text}\" is not an eMAID: two letters, three characters, nine characters and a check digit, e.g. DE-GDF-C12345678-X.";
                return false;
            }

            var countryCode  = match.Groups[1].Value;
            var providerId   = match.Groups[2].Value;
            var instance     = match.Groups[3].Value;

            if (!EMAIdCheckDigit.TryCompute(countryCode + providerId + instance, out var checkDigit, out Error))
                return false;

            if (match.Groups[4].Success && match.Groups[4].Value[0] != checkDigit)
            {
                Error = $"\"{Text}\" is not an eMAID: its check digit should be \"{checkDigit}\".";
                return false;
            }

            EMAId = new EMAId(countryCode, providerId, instance, checkDigit);
            return true;

        }

        #endregion


        #region (override) ToString()

        /// <summary>
        /// The identifier as people read it: "DE-GDF-C12345678-X".
        /// </summary>
        public override String ToString()
            => $"{CountryCode}-{ProviderId}-{Instance}-{CheckDigit}";

        #endregion

    }

}
