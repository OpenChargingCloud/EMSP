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

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using cloud.charging.open.EMSP.Configuration;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// What an EMSP says it is beyond what every node says: who it is in OCPI,
    /// and its own sections of the configuration.
    /// </summary>
    /// <remarks>
    /// What every node answers - its status, its sections, name resolution
    /// and the time servers, read and changed - is asked of this EMSP by the
    /// node's conformance suite; see EMSPConformance.
    /// </remarks>
    public class ConfigurationAPITests : AEMSPTests
    {

        #region TheStatusSaysWhoThisEMSPIsInOCPI()

        /// <summary>
        /// What an EMSP's status says beyond every node's: who it is in OCPI,
        /// right after the version. What every node's says is asked by the
        /// conformance suite.
        /// </summary>
        [Test]
        public async Task TheStatusSaysWhoThisEMSPIsInOCPI()
        {

            using var http = await SignedIn();

            var status = await GetJSON(http, "/api/v1/status");

            Assert.Multiple(() => {
                Assert.That(status.Value<String>("service"),  Is.EqualTo("EMSP"));
                Assert.That(status.Value<String>("partyId"),  Is.EqualTo(EMSP.PartyIdText));
                Assert.That(status.Properties().Select(property => property.Name).Take(3),
                            Is.EqualTo(new[] { "service", "version", "partyId" }),
                            "what an EMSP adds comes right after the version");
            });

        }

        #endregion

        #region TheConfigurationNamesTheEMSPsOwnSections()

        /// <summary>
        /// The Configuration page renders whatever the EMSP sends rather
        /// than a list of its own, so a section going missing is not a broken
        /// page - it is a page that quietly stops mentioning something. These
        /// are the EMSP's own; the sections every node has are asked by the
        /// conformance suite.
        /// </summary>
        [Test]
        public async Task TheConfigurationNamesTheEMSPsOwnSections()
        {

            using var http = await SignedIn();

            var configuration = await GetJSON(http, "/api/v1/configuration");

            Assert.Multiple(() => {
                Assert.That(configuration.Properties().First().Name,  Is.EqualTo("EMSP"), "the card the page leads with");
                Assert.That(configuration["ocpi"],                    Is.TypeOf<JObject>());
                Assert.That(configuration["contracts"],               Is.TypeOf<JObject>());
                Assert.That(configuration["assemblies"],              Is.TypeOf<JArray>());
            });

        }

        #endregion

        #region TheOCPISectionDescribesTheParty()

        [Test]
        public async Task TheOCPISectionDescribesTheParty()
        {

            using var http = await SignedIn();

            var ocpi = (await GetJSON(http, "/api/v1/configuration"))["ocpi"];

            Assert.Multiple(() => {
                Assert.That(ocpi?.Value<String>("role"),         Is.EqualTo("EMSP"));
                Assert.That(ocpi?.Value<String>("partyId"),      Is.EqualTo(EMSP.PartyIdText));
                Assert.That(ocpi?.Value<String>("name"),         Is.EqualTo(EMSP.BusinessDetails.Name));
                Assert.That(ocpi?.Value<String>("versionsURL"),  Is.EqualTo(EMSP.OCPIVersionsURL.ToString()));
                Assert.That(ocpi?["versions"]?.Values<String>(), Is.EquivalentTo(OCPIConfiguration.DefaultVersions));
            });

        }

        #endregion

    }

}
