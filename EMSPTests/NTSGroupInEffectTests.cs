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

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// What an EMSP makes of an "nts" section: the section put into effect
    /// on top of the group of time servers it already has.
    /// </summary>
    /// <remarks>
    /// Beside the tests of the section on its own, because the rule that
    /// matters here - what a section does not mention is left as it is - can
    /// only be seen against something that is already there.
    ///
    /// The EMSPs are built and never started. The constructor is what
    /// applies the file, and it is Start() that would put a timer on the
    /// network to ask the servers.
    ///
    /// What every node does with the section is tested in WWCP_Node_Tests,
    /// under the same names this suite had for its copies of it; what is
    /// left here is a running EMSP's clock check taking a new interval.
    /// </remarks>
    [TestFixture]
    public class NTSGroupInEffectTests
    {

        #region Data

        private String directory = default!;

        private String ConfigurationPath
            => Path.Combine(directory, "configuration.json");

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void MakeADirectory()
        {
            directory = TestEMSPs.TemporaryDirectory("nts-in-effect");
        }

        [TearDown]
        public void RemoveTheDirectory()
        {
            TestEMSPs.Remove(directory);
        }

        #endregion


        #region (helper) NewEMSP(Configuration = null)

        /// <summary>
        /// An EMSP as it stands after reading this configuration file, or
        /// after reading none.
        /// </summary>
        private EMSP NewEMSP(String? Configuration = null)

            => TestEMSPs.New(directory,
                             Configuration is not null
                                 ? JObject.Parse(Configuration)
                                 : null);

        #endregion


        #region ARunningEMSPPutsANewIntervalIntoItsClockCheckAtOnce()

        /// <summary>
        /// How often the clock is checked, and whether it is, are put into the
        /// check of a running EMSP at once - not at its next start.
        /// </summary>
        /// <remarks>
        /// The check runs on a timer set at the start, and a save used to change
        /// only the setting: the page said "in effect" about an interval the
        /// timer did not have until the next start. Seen here in the line the
        /// check writes whenever it is set. Started, and the first check is a
        /// minute in, so a test that is over long before that asks no time
        /// server anything.
        /// </remarks>
        [Test]
        public async Task ARunningEMSPPutsANewIntervalIntoItsClockCheckAtOnce()
        {

            await using var EMSP = NewEMSP();

            await EMSP.Start();

            var before = EMSP.Log.LastId;

            Assert.That(EMSP.TryUpdateNTSConfiguration(JObject.Parse("""{ "checkEverySeconds": 600 }"""), out var error),  Is.True,  error);
            Assert.That(EMSP.TryUpdateNTSConfiguration(JObject.Parse("""{ "enabled": false }"""),          out error),      Is.True,  error);

            var said = EMSP.Log.Recent(50, before, "clock").Select(entry => entry.Message).ToArray();

            Assert.Multiple(() => {
                Assert.That(said,  Has.Some.Contains("will be checked against").And.Contains("every 10 minute(s)"),  String.Join(" | ", said));
                Assert.That(said,  Has.Some.Contains("is not being checked: NTS is switched off"),                   String.Join(" | ", said));
            });

        }

        #endregion

    }

}
