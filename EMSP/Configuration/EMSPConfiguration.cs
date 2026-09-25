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

using cloud.charging.open.protocols.WWCP.Node.Configuration;

#endregion

namespace cloud.charging.open.EMSP.Configuration
{

    /// <summary>
    /// Everything this EMSP can be told in writing beyond what every node can:
    /// one document with one section per thing that can be configured.
    /// </summary>
    /// <remarks>
    /// One file rather than one per subject, because these settings are read
    /// together, changed together and backed up together - and because the
    /// question "what is this EMSP configured as" should have one answer that
    /// fits on a screen instead of a directory to go through. The sections
    /// every node has - "dns", "nts" and "certificates" - are in the same file
    /// and are the node's to read; this passes them over, as the node passes
    /// over these.
    ///
    /// Every section is optional and so is every field inside it. A section
    /// that is absent is not a section set to nothing: it means the file has no
    /// opinion, and whatever the EMSP was handed at construction stands. An
    /// EMSP handed nothing either falls back to the system default. So the
    /// order is: system default, then what the constructor was given, then what
    /// this file says - each one only where it actually speaks.
    /// </remarks>
    /// <param name="OCPI">Who this EMSP is when it speaks OCPI, and which versions of it it speaks.</param>
    /// <param name="Contracts">Whether drivers may sign up, and how long their contract certificates are good for.</param>
    public sealed record EMSPConfiguration(OCPIConfiguration?       OCPI        = null,
                                           ContractsConfiguration?  Contracts   = null)
    {

        #region Properties

        /// <summary>
        /// Whether this document says anything at all.
        /// </summary>
        public Boolean IsEmpty
            => OCPI is null && Contracts is null;

        #endregion


        #region (static) TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// The EMSP's sections of the document, or the one sentence that says
        /// what is wrong with them.
        /// </summary>
        /// <remarks>
        /// A section of the wrong kind is an error rather than a section
        /// skipped: <c>"ocpi": null</c> is a file that has nothing to say about
        /// OCPI, but <c>"ocpi": "DE*GDF"</c> is a file whose author believed
        /// they had configured something.
        ///
        /// Sections this EMSP does not know are passed over without a word -
        /// the node's among them. A file written by a newer EMSP should still
        /// start an older one, and the file keeps them - see
        /// <see cref="WWCPConfigFile.TryReplaceSection"/>.
        /// </remarks>
        public static Boolean TryParse(JObject                                     JSON,
                                       [NotNullWhen(true)]  out EMSPConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?             Error)
        {

            Configuration  = null;
            Error          = null;

            #region OCPI

            OCPIConfiguration? ocpi = null;

            if (JSON[OCPIConfiguration.SectionName] is JToken ocpiToken && ocpiToken.Type != JTokenType.Null)
            {

                if (ocpiToken is not JObject ocpiJSON)
                {
                    Error = $"'{OCPIConfiguration.SectionName}' must be a JSON object.";
                    return false;
                }

                if (!OCPIConfiguration.TryParse(ocpiJSON, out ocpi, out Error))
                    return false;

            }

            #endregion

            #region Contracts

            ContractsConfiguration? contracts = null;

            if (JSON[ContractsConfiguration.SectionName] is JToken contractsToken && contractsToken.Type != JTokenType.Null)
            {

                if (contractsToken is not JObject contractsJSON)
                {
                    Error = $"'{ContractsConfiguration.SectionName}' must be a JSON object.";
                    return false;
                }

                if (!ContractsConfiguration.TryParse(contractsJSON, out contracts, out Error))
                    return false;

            }

            #endregion

            Configuration = new EMSPConfiguration(ocpi, contracts);
            return true;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The EMSP's sections as they are written to the file.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject();

            if (OCPI is not null)
                json.Add(OCPIConfiguration.SectionName,  OCPI.ToJSON());

            if (Contracts is not null)
                json.Add(ContractsConfiguration.SectionName, Contracts.ToJSON());

            return json;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()

            => IsEmpty
                   ? "nothing configured"
                   : String.Join(", ",
                         new[] {
                             OCPI      is not null ? OCPI.     ToString() : null,
                             Contracts is not null ? Contracts.ToString() : null
                         }.Where(section => section is not null));

        #endregion

    }

}
