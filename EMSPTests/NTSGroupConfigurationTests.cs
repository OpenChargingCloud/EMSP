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

using org.GraphDefined.Vanaheimr.Hermod.DNS;

using cloud.charging.open.protocols.WWCP.Node.Configuration;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// What the "nts" section may say about a group of time servers.
    /// </summary>
    /// <remarks>
    /// What it may say on every node is tested in WWCP_Node_Tests, under the
    /// same names this suite had for its copies of it; what is left here is
    /// the group an EMSP starts with.
    /// </remarks>
    [TestFixture]
    public class NTSGroupConfigurationTests
    {

        #region Data

        private String directory = default!;

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void MakeADirectory()
        {
            directory = TestEMSPs.TemporaryDirectory("nts-group");
        }

        [TearDown]
        public void RemoveTheDirectory()
        {
            TestEMSPs.Remove(directory);
        }

        #endregion


        #region TheDefaultFourAreWhatAnEMSPStartsWith()

        /// <summary>
        /// An EMSP nobody has configured asks the PTB's four.
        /// </summary>
        /// <remarks>
        /// This is the case every existing installation is in - built the way
        /// it has always been built, with no "nts" section at all - and it
        /// used to be a group of one. Four is the better default for a clock
        /// that contracts and roaming records are dated by: one host being
        /// rebooted no longer leaves the EMSP without a time, and two that
        /// agree catch what one cannot, a server that is wrong rather than
        /// absent.
        ///
        /// The first of the four is still what the single-server client points
        /// at, so the group and the client cannot name different hosts - which
        /// is what the third assertion is for, and why it reads the same as it
        /// did when there was only one.
        /// </remarks>
        [Test]
        public async Task TheDefaultFourAreWhatAnEMSPStartsWith()
        {

            await using var EMSP = TestEMSPs.New(directory);

            Assert.Multiple(() => {
                Assert.That(EMSP.TimeSources.Bands(),                 Has.Count.EqualTo(1),  "peers, asked together");
                Assert.That(EMSP.TimeSources.Bands()[0],              Has.Count.EqualTo(4));
                Assert.That(EMSP.TimeSources.Bands()[0][0].Hostname,  Is.EqualTo(EMSP.NTSClient.Hostname));
                Assert.That(EMSP.TimeSources.MinServers,              Is.EqualTo(2));
            });

        }

        #endregion

    }

}
