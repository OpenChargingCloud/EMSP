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

namespace cloud.charging.open.EMSP.Configuration
{

    /// <summary>
    /// The "contracts" section of the configuration file: whether anybody may
    /// sign up for an account, and how long a contract certificate is good
    /// for.
    /// </summary>
    /// <remarks>
    /// Read once, at the start, like the OCPI identity: the sign-up is a
    /// route that is either registered or not, and the validity is written
    /// into certificates that outlive any change to it.
    ///
    /// Who this EMSP is in its certificates - the country, the provider, the
    /// name in every subject - is not here: it is the OCPI identity, because
    /// an eMAID starts with the same country code and provider identification
    /// a roaming partner knows this EMSP by, and one EMSP has one of each.
    /// </remarks>
    /// <param name="SelfSignUp">Whether anybody may sign up for an account and become a driver.</param>
    /// <param name="ValidityDays">How many days a contract certificate is good for.</param>
    public sealed record ContractsConfiguration(Boolean?  SelfSignUp     = null,
                                                UInt32?   ValidityDays   = null)
    {

        #region Data

        /// <summary>
        /// The name of the section in the configuration file.
        /// </summary>
        public const String   SectionName           = "contracts";

        /// <summary>
        /// Anybody may sign up, unless the file says otherwise: an EMSP with
        /// no drivers has nothing to do.
        /// </summary>
        public const Boolean  DefaultSelfSignUp     = true;

        /// <summary>
        /// Two years: what the ISO 15118-2 profile gives a contract
        /// certificate.
        /// </summary>
        public const UInt32   DefaultValidityDays   = 730;

        /// <summary>
        /// One day is the least a certificate is worth issuing for.
        /// </summary>
        public const UInt32   MinValidityDays       = 1;

        /// <summary>
        /// Ten years is more than any contract runs, and more than a vehicle
        /// should carry the same key.
        /// </summary>
        public const UInt32   MaxValidityDays       = 3650;

        #endregion


        #region (static) TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// The section as the file wrote it, or the sentence that says what
        /// is wrong with it.
        /// </summary>
        public static Boolean TryParse(JObject                                          JSON,
                                       [NotNullWhen(true)]  out ContractsConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?                  Error)
        {

            Configuration  = null;
            Error          = null;

            if (!ConfigurationReader.TryReadBoolean(JSON, "selfSignUp",   SectionName,                                   out var selfSignUp,   out Error) ||
                !ConfigurationReader.TryReadUInt32 (JSON, "validityDays", SectionName, MinValidityDays, MaxValidityDays, out var validityDays, out Error))
            {
                return false;
            }

            Configuration = new ContractsConfiguration(selfSignUp, validityDays);
            return true;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The section as it is written to the file: only what was said.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject();

            if (SelfSignUp.  HasValue)  json.Add("selfSignUp",   SelfSignUp.  Value);
            if (ValidityDays.HasValue)  json.Add("validityDays", ValidityDays.Value);

            return json;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()

            => $"contracts ({(SelfSignUp ?? DefaultSelfSignUp ? "sign-up open" : "sign-up closed")}, {ValidityDays ?? DefaultValidityDays} days)";

        #endregion

    }

}
