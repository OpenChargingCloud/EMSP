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
    /// Who an EMSP is in OCPI at its start: what it was built with, and what
    /// its configuration file says instead.
    /// </summary>
    /// <remarks>
    /// The configuration is read in the constructor, so these tests only build
    /// an EMSP. What every node does at its start - the account it makes up
    /// and keeps only the hash of, the accounts it finds at the next, a file
    /// it cannot read, the clock set before anything asks it, a time client
    /// switched off - is tested in WWCP_Node_Tests and asked of this EMSP by
    /// the node's conformance suite.
    /// </remarks>
    public class StartupTests
    {

        #region Data

        private String directory = default!;

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void MakeADirectory()
        {
            directory = TestEMSPs.TemporaryDirectory("startup");
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void RemoveTheDirectory()
            => TestEMSPs.Remove(directory);

        #endregion


        #region AEMSPWithNoFilesRunsOnItsDefaults()

        /// <summary>
        /// Who an EMSP nobody has configured is in OCPI: the party and the name
        /// it was built with. What a node nobody has configured runs on is
        /// asked of it by the node's conformance suite.
        /// </summary>
        [Test]
        public async Task AEMSPWithNoFilesRunsOnItsDefaults()
        {

            await using var EMSP = TestEMSPs.New(directory);

            Assert.Multiple(() => {
                Assert.That(EMSP.PartyId.CountryCode.ToString(), Is.EqualTo(OCPIConfiguration.DefaultCountryCode));
                Assert.That(EMSP.PartyId.PartyId.ToString(),     Is.EqualTo(OCPIConfiguration.DefaultPartyId));
                Assert.That(EMSP.BusinessDetails.Name,           Is.EqualTo(OCPIConfiguration.DefaultName));
                Assert.That(EMSP.OCPIVersions.Select(version => version.Label), Is.EqualTo(OCPIConfiguration.DefaultVersions));
            });

        }

        #endregion

        #region TheFileDecidesWhoThisEMSPIsInOCPI()

        /// <summary>
        /// Read once, at the start. What the file says beats what the
        /// constructor was handed, and what it does not mention is left alone.
        /// </summary>
        [Test]
        public async Task TheFileDecidesWhoThisEMSPIsInOCPI()
        {

            var configuration = new JObject(
                                    new JProperty("nts",  new JObject(new JProperty("enabled", false))),
                                    new JProperty("ocpi", new JObject(
                                        new JProperty("countryCode",  "NL"),
                                        new JProperty("partyId",      "ABC"),
                                        new JProperty("name",         "Somebody Else")
                                    ))
                                );

            await using var EMSP = TestEMSPs.New(directory, configuration);

            Assert.Multiple(() => {
                Assert.That(EMSP.PartyIdText,               Is.EqualTo("NL-ABC"));
                Assert.That(EMSP.BusinessDetails.Name,      Is.EqualTo("Somebody Else"));
                // Not mentioned, so the default stands: the two classic versions.
                Assert.That(EMSP.OCPIVersions.Select(version => version.Label), Is.EqualTo(OCPIConfiguration.DefaultVersions));
            });

        }

        #endregion

    }

}
