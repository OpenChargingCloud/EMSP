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

using NUnit.Framework;

using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// What the EMSP's own code says, held to the kit's SourceRules, as its
    /// pages are held to the node's page rules.
    /// </summary>
    public class SourceTests
    {

        #region NoNameIsGivenAnArticleOfItsOwn()

        /// <summary>
        /// Nothing the EMSP writes puts "a" or "an" in front of a name it fills
        /// in: which of the two a name takes goes by how the name is said, and
        /// a role or a kind of certificate named anew would be said with the
        /// wrong one. "is a {EMSPAccess.Driver.Name} now" read right only as
        /// long as the role was called "driver".
        /// </summary>
        [Test]
        public void NoNameIsGivenAnArticleOfItsOwn()
        {

            // By a file of each project, not by their directories: built with
            // --artifacts-path, artifacts/bin holds a directory named after
            // every project and was taken for the repository, where the rule
            // found no source to read.
            var repository = SourceRules.RepositoryAbove(AppContext.BaseDirectory, "EMSP/EMSP.csproj",
                                                                                   "EMSPTests/EMSPTests.csproj");

            Assert.That(SourceRules.ArticlesBeforeANameIn(Path.Combine(repository, "EMSP")),
                        Is.Empty);

        }

        #endregion

    }

}
