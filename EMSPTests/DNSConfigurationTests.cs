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
    /// What the "dns" section may say about the name servers - and what it may
    /// not, said as a sentence about the file rather than as an exception.
    /// </summary>
    /// <remarks>
    /// The log and the banner name a name server as "udp://10.0.0.1:53", which
    /// is not a form this section has ever taken - here, or in the CSMS, the
    /// charging station and the vehicle whose sections it shares. An EMSP
    /// given it stopped at its start with an ArgumentException out of
    /// IPAddress.TryParse, whose message named neither the file nor the key,
    /// and the DNS page got an internal server error for it: the parser found
    /// an address somewhere in the text and handed all of the text to one that
    /// threw on the rest.
    ///
    /// What the section may say on every node is tested in WWCP_Node_Tests,
    /// under the same names this suite had for its copies of it; what is left
    /// here is an EMSP stopping at its start with the sentence.
    /// </remarks>
    [TestFixture]
    public class DNSConfigurationTests
    {

        #region Data

        private String directory = default!;

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void MakeADirectory()
        {
            directory = TestEMSPs.TemporaryDirectory("dns");
        }

        [TearDown]
        public void RemoveTheDirectory()
        {
            TestEMSPs.Remove(directory);
        }

        #endregion


        #region AnEMSPWhoseFileSaysSoStopsWithASentence()

        /// <summary>
        /// And at a start: the EMSP stops over the file the way it stops over
        /// any file it cannot read, saying what is wrong and where - rather
        /// than with an ArgumentException naming neither, which is how it
        /// stopped before.
        /// </summary>
        /// <remarks>
        /// Built and never started: the constructor is what reads the file.
        /// </remarks>
        [Test]
        public void AnEMSPWhoseFileSaysSoStopsWithASentence()
        {

            var file     = Path.Combine(directory, "configuration.json");

            var problem  = Assert.Throws<InvalidOperationException>(() => TestEMSPs.New(
                                                                              directory,
                                                                              JObject.Parse("""{ "dns": { "servers": [ "udp://213.133.98.98:53" ] } }""")
                                                                          ));

            Assert.That(problem?.Message,  Does.Contain("'dns.servers'").And.Contain("udp://213.133.98.98:53").And.Contain(file));

        }

        #endregion

    }

}
