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

using cloud.charging.open.protocols.WWCP.Node.Configuration;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// The file an EMSP writes itself down in.
    /// </summary>
    /// <remarks>
    /// What every node's file does - read, merged, written, and refused where
    /// it cannot be read - is tested in WWCP_Node_Tests; what is left here is
    /// the EMSP's own section beside the node's.
    /// </remarks>
    public class ConfigurationFileTests
    {

        #region Data

        private String                directory   = default!;
        private WWCPConfigFile  file        = default!;

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void MakeAFile()
        {
            directory = TestEMSPs.TemporaryDirectory("config");
            Directory.CreateDirectory(directory);
            file      = new WWCPConfigFile(Path.Combine(directory, "configuration.json"));
        }

        [TearDown]
        public void RemoveTheDirectory()
            => TestEMSPs.Remove(directory);

        #endregion


        #region TheEMSPsSectionComesBackBesideTheNodes()

        /// <summary>
        /// The EMSP's own section and the node's, written into one file, each
        /// come back as they were written.
        /// </summary>
        /// <remarks>
        /// One file with two readers: the sections every node has, which the
        /// node below reads, and the EMSP's own, read from the same document.
        /// Neither is lost to the other.
        /// </remarks>
        [Test]
        public void TheEMSPsSectionComesBackBesideTheNodes()
        {

            var written = new WWCPConfiguration(
                              DNS:   new DNSConfiguration(Enabled: false),
                              NTS:   new NTSConfiguration(Enabled: true)
                          ).ToJSON();

            written.Merge(new EMSPConfiguration(
                              OCPI:  new OCPIConfiguration(CountryCode: "NL", PartyId: "ABC")
                          ).ToJSON());

            file.TryWrite(written, out _);

            Assert.That(file.TryLoad        (out var node,     out var error),     Is.True, error);
            Assert.That(file.TryLoadDocument(out var document, out var readError), Is.True, readError);
            Assert.That(EMSPConfiguration.TryParse(document!, out var emsp, out var parseError), Is.True, parseError);

            Assert.Multiple(() => {
                Assert.That(node!.DNS?.Enabled,        Is.False);
                Assert.That(node.NTS?.Enabled,         Is.True);
                Assert.That(emsp!.OCPI?.CountryCode,   Is.EqualTo("NL"));
                Assert.That(emsp.OCPI?.PartyId,        Is.EqualTo("ABC"));
                Assert.That(node.IsEmpty,              Is.False);
                Assert.That(emsp.IsEmpty,              Is.False);
            });

        }

        #endregion

    }

}
