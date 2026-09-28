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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.Mail;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// The event stream as only an EMSP has it: ended for an account that may
    /// no longer read the log, although it is still signed in.
    /// </summary>
    /// <remarks>
    /// What every node's stream does - its header, its heartbeat, and ending
    /// with the session, the API key or the password that opened it - is
    /// asked of this EMSP by the node's conformance suite; see
    /// EMSPConformance. The suite asks as an account that may do everything,
    /// and an EMSP's log is the operator's and not its drivers': what the
    /// stream does for an account taken out of the operator's group is said
    /// here.
    /// </remarks>
    public class EventStreamTests : AEMSPTests
    {

        #region (private static) ReadUntil(Reader, Wanted, Within)

        /// <summary>
        /// Read the stream line by line until a line satisfies the condition,
        /// and say whether one did in time.
        /// </summary>
        private static async Task<Boolean> ReadUntil(StreamReader           Reader,
                                                     Func<String, Boolean>  Wanted,
                                                     TimeSpan               Within)
        {

            using var timeout = new CancellationTokenSource(Within);

            try
            {
                while (await Reader.ReadLineAsync(timeout.Token) is String line)
                {
                    if (Wanted(line))
                        return true;
                }
            }
            catch (OperationCanceledException)
            { }

            return false;

        }

        #endregion

        #region (private) OpenStream(Client, Lines)

        /// <summary>
        /// Open the event stream and wait until an entry logged after it was
        /// opened has come down it, keeping every line that came.
        /// </summary>
        private async Task<StreamReader> OpenStream(HttpClient Client, List<String> Lines)
        {

            var response = await Client.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead);

            Assert.That(response.IsSuccessStatusCode, Is.True, "the event stream opened");

            var reader   = new StreamReader(await response.Content.ReadAsStreamAsync());
            var marker   = "The stream is live " + Guid.NewGuid().ToString("N")[..8];

            EMSP.Log.Info(marker, "test");

            Assert.That(await ReadUntil(reader, line => { Lines.Add(line); return line.Contains(marker); }, TimeSpan.FromSeconds(10)),
                        Is.True, "an entry logged after the stream opened came down it");

            return reader;

        }

        #endregion

        #region (private static) EndsWithin(Reader, Lines, Within)

        /// <summary>
        /// Whether the EMSP ends the stream within the given time, keeping
        /// every line that came before it did.
        /// </summary>
        /// <remarks>
        /// ReadUntil() cannot tell the two apart: it answers false both for a
        /// stream that ended and for one that merely went on without the line,
        /// and a stream that goes on is exactly what a test of a stream that
        /// should have ended is looking for.
        /// </remarks>
        private static async Task<Boolean> EndsWithin(StreamReader  Reader,
                                                      List<String>  Lines,
                                                      TimeSpan      Within)
        {

            using var timeout = new CancellationTokenSource(Within);

            try
            {
                while (await Reader.ReadLineAsync(timeout.Token) is String line)
                    Lines.Add(line);

                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (IOException)
            {
                // Cut rather than closed is ended, too.
                return true;
            }

        }

        #endregion

        #region (private) AnOperatorSignedIn()

        /// <summary>
        /// A browser signed in, with a session, as an account made for the
        /// purpose and put in the operator's group - and that group, for taking
        /// the account out of it again.
        /// </summary>
        private async Task<(HttpClient HTTP, User Operator, UserGroup Group)> AnOperatorSignedIn()
        {

            var name      = "operator-" + Guid.NewGuid().ToString("N")[..8];
            var password  = "correct-horse-battery-" + Guid.NewGuid().ToString("N")[..8];

            Assert.That(EMSP.ExtAPI.TryGetOrganization(Organization_Id.Parse(EMSP.DefaultOrganization), out var organization) &&
                        organization is Organization, Is.True, "the EMSP's organization is not there");

            await EMSP.ExtAPI.CreateUser(User_Id.Parse(name),
                                         I18NString.Create(Languages.en, name),
                                         SimpleEMailAddress.Parse($"{name}@localhost"),
                                         User2OrganizationEdgeLabel.IsMember,
                                         (Organization) organization!,
                                         Password:                  password,
                                         SkipDefaultNotifications:  true,
                                         SkipNewUserEMail:          true,
                                         SkipNewUserNotifications:  true,
                                         AcceptedEULA:              DateTimeOffset.UtcNow.AddSeconds(-1),
                                         IsAuthenticated:           true);

            Assert.That(EMSP.ExtAPI.TryGetUser(User_Id.Parse(name), out var stored),              Is.True, $"the account '{name}' was not made");
            Assert.That(EMSP.ExtAPI.TryGetUserGroup(UserGroup_Id.Parse("emsp"), out var group),   Is.True, "the EMSP has no group 'emsp'");
            Assert.That((await EMSP.ExtAPI.AddUserToUserGroup((User) stored!, User2UserGroupEdgeLabel.IsMember, (UserGroup) group!)).IsSuccess,
                        Is.True, $"'{name}' could not be put in 'emsp'");

            return (await SignedInAs(name, password), (User) stored!, (UserGroup) group!);

        }

        #endregion


        #region AStreamEndsWhenItsAccountMayNoLongerReadTheLog()

        /// <summary>
        /// An operator taken out of the operator's group is sent no further
        /// line of the log, although still signed in - and a new stream is
        /// refused with a 403, which the Logs page takes as final.
        /// </summary>
        /// <remarks>
        /// The EMSP's own case: every role of a vehicle may read its log, and
        /// here the log is the operator's and not the driver's. Every other
        /// request of such an account is refused from the moment it is out of
        /// the group; a stream that went on would be the one that was not.
        /// </remarks>
        [Test]
        public async Task AStreamEndsWhenItsAccountMayNoLongerReadTheLog()
        {

            EMSP.API.EventStreamHeartbeat = TimeSpan.FromMilliseconds(300);

            var (http, account, group)  = await AnOperatorSignedIn();

            using var client            = http;

            var lines                   = new List<String>();
            using var reader            = await OpenStream(client, lines);

            Assert.That((await EMSP.ExtAPI.RemoveUserFromUserGroup(account, group)).IsSuccess, Is.True,
                        "the operator could not be taken out of the group");

            var afterwards              = "Logged after the operator was taken out " + Guid.NewGuid().ToString("N")[..8];
            EMSP.Log.Info(afterwards, "test");

            var ended                   = await EndsWithin(reader, lines, TimeSpan.FromSeconds(5));

            using var again             = await client.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead);

            Assert.Multiple(() => {
                Assert.That(ended,                                          Is.True,   "the stream went on after its account had lost the log");
                Assert.That(lines.Any(line => line.Contains(afterwards)),   Is.False,  "a line logged after the account had lost the log was sent to it");
                Assert.That(again.StatusCode,                               Is.EqualTo(HttpStatusCode.Forbidden),
                            "a new stream is refused to an account that is still signed in");
            });

        }

        #endregion

    }

}
