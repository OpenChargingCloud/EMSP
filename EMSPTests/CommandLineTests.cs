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

using org.GraphDefined.Vanaheimr.CLI;

using cloud.charging.open.protocols.WWCP.Node.CommandLine;

using cloud.charging.open.EMSP.CommandLine;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// The command line of an EMSP: the EMSP's own, EMSPCLI, whichever
    /// program runs the EMSP - over SSH as well as at the console.
    /// </summary>
    /// <remarks>
    /// There is no command yet that only an EMSP can be told; what an EMSP
    /// has is the node's. What is tested here is that the command line is the
    /// EMSP's all the same - handed out by the EMSP itself, about this EMSP,
    /// with every command of the node - and that a command of the EMSP's,
    /// once there is one, is on it: the test environment runs an EMSP without
    /// the program EMSPCLI, as every test here does.
    /// </remarks>
    public class CommandLineTests : AEMSPTests
    {

        #region OverSSHTheEMSPHandsOutItsOwnCommandLine()

        /// <summary>
        /// Whoever signs in over SSH gets the command line of the EMSP, about
        /// this EMSP, without the program that runs it having to say so.
        /// </summary>
        [Test]
        public async Task OverSSHTheEMSPHandsOutItsOwnCommandLine()
        {

            Assert.That(EMSP.CommandLines, Is.Not.Null, "a session over SSH would get the node's command line only");

            await using var terminal  = new VT100Terminal((bytes, cancellationToken) => ValueTask.CompletedTask);
            using       var cli       = EMSP.CommandLines!(terminal, CLICaller.Console);

            Assert.That(cli,                   Is.InstanceOf<EMSPCLI>());
            Assert.That(((EMSPCLI) cli).EMSP,  Is.SameAs(EMSP));

            Assert.That((await cli.Execute("who")).Single(), Is.EqualTo($"Nobody is connected to this {EMSP.Kind.Name} over SSH."));

        }

        #endregion

        #region AtTheConsoleTheEMSPsCommandLineAnswersAsOverSSH()

        /// <summary>
        /// The command line the program EMSPCLI runs at the console is the
        /// same, and answers the same.
        /// </summary>
        [Test]
        public async Task AtTheConsoleTheEMSPsCommandLineAnswersAsOverSSH()
        {

            using var cli = new EMSPCLI(EMSP);

            Assert.That(cli.EMSP,                               Is.SameAs(EMSP));
            Assert.That((await cli.Execute("who")).Single(),    Is.EqualTo($"Nobody is connected to this {EMSP.Kind.Name} over SSH."));
            Assert.That((await cli.Execute("noSuchCommand")).Single(), Does.StartWith("Unknown command"));

        }

        #endregion

        #region EveryCommandOfTheNodeAndOfTheEMSPIsOnIt()

        /// <summary>
        /// Every command of the node is on the EMSP's command line, and so is
        /// every command of the EMSP's assembly - which is found only where it
        /// is built from an EMSPCLI, or from nothing: a command built from
        /// anything else would be on no command line, and nothing else would
        /// say so.
        /// </summary>
        [Test]
        public async Task EveryCommandOfTheNodeAndOfTheEMSPIsOnIt()
        {

            await using var terminal  = new VT100Terminal((bytes, cancellationToken) => ValueTask.CompletedTask);
            using       var cli       = EMSP.CommandLines!(terminal, CLICaller.Console);

            var onIt        = cli.Commands.Select(command => command.GetType()).ToHashSet();

            var ofTheNode   = typeof(NodeCLI).Assembly.GetTypes().
                                  Where(type => typeof(ICLICommand).IsAssignableFrom(type) &&
                                               !type.IsAbstract &&
                                               !type.IsInterface &&
                                                type.GetConstructor([ typeof(NodeCLI) ]) is not null).
                                  ToArray();

            var ofTheEMSP   = typeof(EMSP).Assembly.GetTypes().
                                  Where(type => typeof(ICLICommand).IsAssignableFrom(type) &&
                                               !type.IsAbstract &&
                                               !type.IsInterface).
                                  ToArray();

            Assert.That(ofTheNode, Is.Not.Empty, "the node has commands - syncNTS among them");

            foreach (var command in ofTheNode.Concat(ofTheEMSP))
                Assert.That(onIt, Does.Contain(command), $"{command.Name} is not on the EMSP's command line");

        }

        #endregion

    }

}
