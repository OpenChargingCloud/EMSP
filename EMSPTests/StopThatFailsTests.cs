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

using System.Net;
using System.Net.Sockets;

using NUnit.Framework;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// An EMSP whose stopping fails: said to whoever lets go of it, and let go
    /// of all the same.
    /// </summary>
    /// <remarks>
    /// Its DisposeAsync writes out what the OCPI library holds whatever Stop()
    /// did - see PartnerFileTests - and lets go of the node below in a finally.
    /// That node began its stop again there, and failed again, where every
    /// stop failed: its port stayed open, and nothing below was let go of.
    /// </remarks>
    public class StopThatFailsTests : AEMSPTests
    {

        #region NewEMSP() - an EMSP whose stopping can be made to fail

        /// <summary>
        /// An EMSP as any other, whose stopping can be made to fail - see
        /// EMSPWhoseStopCanFail.
        /// </summary>
        protected override EMSP NewEMSP()

            => new EMSPWhoseStopCanFail(
                   AccountsPath:  Path.Combine(Directory, "accounts"),
                   ConfigFile:    TestEMSPs.ConfigFile(Directory, Configuration),
                   Clock:         Clock
               );

        #endregion


        #region AnEMSPWhoseEveryStopFailsStillClosesItsPort()

        /// <summary>
        /// Letting go of an EMSP whose every stop fails says so, and closes its
        /// port all the same.
        /// </summary>
        [Test]
        public async Task AnEMSPWhoseEveryStopFailsStillClosesItsPort()
        {

            var port = new Uri(BaseURL).Port;
            var emsp = (EMSPWhoseStopCanFail) EMSP;

            emsp.EveryStopFails = true;

            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await EMSP.DisposeAsync(),
                                                                    "The stop that was made to fail is not said to have failed.");
            }
            finally
            {
                // So that the TearDown's second disposal can stop where this
                // one could not.
                emsp.EveryStopFails = false;
            }

            using var client = new TcpClient();

            var refused = await Assert.CatchAsync<SocketException>(async () => await client.ConnectAsync(IPAddress.Loopback, port),
                                                                   "The port of an EMSP whose stop failed is still open.");

            Assert.That(refused?.SocketErrorCode, Is.EqualTo(SocketError.ConnectionRefused));

        }

        #endregion

    }

}
