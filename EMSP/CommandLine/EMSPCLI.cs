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

using System.Reflection;

using org.GraphDefined.Vanaheimr.CLI;

using cloud.charging.open.protocols.WWCP.Node.CommandLine;

// By its full name, because inside cloud.charging.open.EMSP "EMSP" is the
// namespace rather than the class.
using Provider = cloud.charging.open.EMSP.EMSP;

#endregion

namespace cloud.charging.open.EMSP.CommandLine
{

    /// <summary>
    /// The command line of a running EMSP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node's command line, with the commands every node has - syncNTS
    /// among them - and the console until 'quit', Ctrl+C or SIGTERM. What only
    /// an EMSP can be told is a command built from an EMSPCLI in this
    /// assembly, found as the node's are: a new command is a new file and
    /// nothing else. Everything it needs is reachable from here - the EMSP
    /// itself, and through it its configuration, its log and everything the
    /// JSON API can do - for a command is a second way of asking for the same
    /// thing as the web interface, never an implementation of its own.
    /// </para>
    /// <para>
    /// Here, beside the EMSP, and not in the program that runs it: what an
    /// EMSP can be told is the EMSP's, and is told over SSH as well as at the
    /// console. The EMSP hands it to whoever signs in over SSH by itself (see
    /// EMSP.cs), so that an EMSP run by another program - the test
    /// environment - has the same commands as one run by the program
    /// EMSPCLI. A program with commands of its own derives from this one and
    /// sets WWCPNode.CommandLines anew.
    /// </para>
    /// </remarks>
    public class EMSPCLI : NodeCLI
    {

        #region Data

        /// <summary>
        /// What stands in front of the command being typed.
        /// </summary>
        public const String Prompt = "EMSP> ";

        #endregion

        #region Properties

        /// <summary>
        /// The EMSP these commands are about.
        /// </summary>
        public Provider  EMSP    { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create the command line of the given EMSP.
        /// </summary>
        /// <param name="EMSP">The running EMSP.</param>
        /// <param name="AssembliesWithCLICommands">Further assemblies to search for commands. This one is searched either way.</param>
        public EMSPCLI(Provider           EMSP,
                       params Assembly[]  AssembliesWithCLICommands)

            : base(EMSP, AssembliesWithCLICommands)

        {

            this.EMSP = EMSP;

            RegisterCLIType(typeof(EMSPCLI));

        }

        /// <summary>
        /// Create the command line of the given EMSP on the given terminal,
        /// for the given caller - a session over SSH.
        /// </summary>
        /// <param name="EMSP">The running EMSP.</param>
        /// <param name="Terminal">What the command line is typed at and written on.</param>
        /// <param name="Caller">Who is typing at it.</param>
        /// <param name="AssembliesWithCLICommands">Further assemblies to search for commands. This one and the node's are searched either way.</param>
        public EMSPCLI(Provider           EMSP,
                       ICLITerminal       Terminal,
                       CLICaller          Caller,
                       params Assembly[]  AssembliesWithCLICommands)

            : base(EMSP, Terminal, Caller, AssembliesWithCLICommands)

        {

            this.EMSP = EMSP;

            RegisterCLIType(typeof(EMSPCLI));

        }

        #endregion


        #region (protected override) GetPrompt()

        /// <summary>
        /// What this program is, because an EMSP usually runs on a bench next to
        /// a CSMS, a charging station and a vehicle, and their consoles should
        /// not have to be told apart by what scrolls past on them.
        /// </summary>
        /// <remarks>
        /// Not its OCPI party: that is written in the banner and in the log, and
        /// in front of every command it would say less than this does - there is
        /// one EMSP on a bench, and it is the one with this prompt.
        /// </remarks>
        protected override String GetPrompt()

            => Prompt;

        #endregion

    }

}
