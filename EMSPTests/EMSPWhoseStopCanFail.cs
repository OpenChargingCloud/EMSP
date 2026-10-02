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

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// An EMSP as TestEMSPs builds any other, whose stopping can be made to
    /// fail - the way stopping a server that had not begun to listen yet once
    /// failed.
    /// </summary>
    /// <remarks>
    /// It fails once this EMSP has ended what it ends before its server stops,
    /// as a kind of node fails whose OnStopping throws. A stop that fails has
    /// stopped all the same, its port closed, and is not begun again: the node
    /// below stops at once where it is asked to stop a second time.
    /// </remarks>
    internal sealed class EMSPWhoseStopCanFail(String          AccountsPath,
                                               WWCPConfigFile  ConfigFile,
                                               TimeProvider?   Clock)

        : EMSP(HTTPPort:        IPPort.Parse(TestPorts.Free()),
               AccountsPath:    AccountsPath,
               ConfigFile:      ConfigFile,
               LogToConsole:    false,
               BridgeDebugLog:  false,
               TimeProvider:    Clock)

    {

        /// <summary>
        /// Whether the next stop fails.
        /// </summary>
        public Boolean NextStopFails  { get; set; }

        /// <summary>
        /// Whether every stop fails, from now on - the next one, and any that
        /// begins again.
        /// </summary>
        public Boolean EveryStopFails { get; set; }

        protected override async Task OnStopping()
        {

            await base.OnStopping();

            if (NextStopFails || EveryStopFails)
            {
                NextStopFails = false;
                throw new InvalidOperationException("This EMSP was made to fail to stop.");
            }

        }

    }

}
