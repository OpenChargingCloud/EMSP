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
    /// The event stream every browser hangs on, as a proxy in front of the
    /// EMSP sees it.
    /// </summary>
    /// <remarks>
    /// Against an EMSP that is started, because what is tested is what goes
    /// over the wire: a header, what the stream says while nothing happens, and
    /// that it ends when whoever opened it would no longer be let in. The
    /// vehicle's tests (EV 1d46e21, b648caf and 4b86e52), signed in the way this
    /// EMSP is - and one of the EMSP's own, an account that may no longer read
    /// the log.
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

        #region (private) WithAPIKey(NotAfter = null)

        /// <summary>
        /// A client that opens the EMSP with an API key of the account its first
        /// start made up, and nothing else: no session, no password.
        /// </summary>
        private async Task<(HttpClient HTTP, APIKey Key)> WithAPIKey(DateTimeOffset? NotAfter = null)
        {

            var key   = new APIKey(APIKey_Id.Parse("event-stream-" + Guid.NewGuid().ToString("N")),
                                   User_Id.Parse(EMSP.DefaultAdminUser),
                                   NotAfter: NotAfter);

            await EMSP.ExtAPI.AddAPIKey(key);

            Assert.That(EMSP.ExtAPI.TryGetAPIKey(key.Id, out _), Is.True,
                        "The API key was not added, so a test of taking it back would pass for the wrong reason.");

            var http  = new HttpClient {
                            BaseAddress  = new Uri(BaseURL),
                            Timeout      = TimeSpan.FromSeconds(30)
                        };

            http.DefaultRequestHeaders.Add("API-Key", key.Id.ToString());

            return (http, key);

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


        #region TheStreamAsksAProxyNotToBufferIt()

        /// <summary>
        /// "X-Accel-Buffering: no" on the event stream.
        /// </summary>
        /// <remarks>
        /// nginx buffers what it passes on unless it is told otherwise, and a
        /// buffered event stream reached the vehicle's browser as nothing at all -
        /// not even its header - until nginx gave up on it after 60 silent
        /// seconds. The Logs page said "reconnecting ..." all the while, and
        /// never asked for its snapshot, which it does when the stream opens.
        /// </remarks>
        [Test]
        public async Task TheStreamAsksAProxyNotToBufferIt()
        {

            using var http      = await SignedIn();
            using var response  = await http.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,                                                 Is.EqualTo(HttpStatusCode.OK));
                Assert.That(response.Content.Headers.ContentType?.MediaType,                     Is.EqualTo("text/event-stream"));
                Assert.That(response.Headers.TryGetValues("X-Accel-Buffering", out var values),  Is.True, "the header is there");
                Assert.That(values,                                                              Is.EqualTo(new[] { "no" }));
            });

        }

        #endregion

        #region ASilentStreamSaysSoAndThenCarriesOn()

        /// <summary>
        /// A comment whenever the stream has been silent for the heartbeat, and
        /// the next entry after it as if nothing had happened.
        /// </summary>
        /// <remarks>
        /// nginx gives up on an upstream that has sent nothing for 60 seconds, and
        /// an EMSP whose partners have nothing to say says nothing for longer
        /// than that. The second half is the one that could go wrong: the stream
        /// waits for the next entry across the heartbeat instead of asking for it
        /// again, and an entry that arrived during one must neither be lost nor
        /// come twice.
        /// </remarks>
        [Test]
        public async Task ASilentStreamSaysSoAndThenCarriesOn()
        {

            using var http      = await SignedIn();

            EMSP.API.EventStreamHeartbeat = TimeSpan.FromMilliseconds(300);

            using var response  = await http.GetAsync("/api/v1/events", HttpCompletionOption.ResponseHeadersRead);
            using var reader    = new StreamReader(await response.Content.ReadAsStreamAsync());

            var heartbeat       = await ReadUntil(reader, line => line == ": keep-alive", TimeSpan.FromSeconds(10));

            // Each step is judged as soon as it is taken. A read that timed out
            // has closed the connection under the reader, and the next read
            // would fail with an ObjectDisposedException that says nothing about
            // why - which is how a stream without a heartbeat failed the vehicle's
            // copy of this test (EV ea59d6a).
            Assert.That(heartbeat,    Is.True,  "a comment came while nothing was logged");

            var marker          = "A line for the event stream " + Guid.NewGuid().ToString("N")[..8];
            EMSP.Log.Info(marker, "test");

            var lines           = new List<String>();
            var entry           = await ReadUntil(reader, line => { lines.Add(line); return line.Contains(marker); }, TimeSpan.FromSeconds(10));

            Assert.That(entry,        Is.True,  "the entry logged after the heartbeat arrived");

            // And the one after it, to be sure the stream is still waiting for
            // entries and not only for the heartbeat.
            var second          = marker + " (second)";
            EMSP.Log.Info(second, "test");

            var secondEntry     = await ReadUntil(reader, line => { lines.Add(line); return line.Contains(second); }, TimeSpan.FromSeconds(10));

            Assert.Multiple(() => {
                Assert.That(secondEntry,                                          Is.True,        "and so did the one after it");
                Assert.That(lines.Count(line => line.Contains($"\"{marker}\"")),  Is.EqualTo(1),  "once");
            });

        }

        #endregion

        #region AStreamEndsWithTheSessionThatOpenedIt()

        /// <summary>
        /// Signed out, a stream opened with that session ends - and a line
        /// logged after the sign-out does not come down it first.
        /// </summary>
        /// <remarks>
        /// Measured on a local controller before this was so: signed out, the
        /// Logs page went on saying "live" and showing every line it wrote for
        /// as long as it was watched. A stream is a request that is answered
        /// for hours, and it was asked about its session once, when it opened.
        /// </remarks>
        [Test]
        public async Task AStreamEndsWithTheSessionThatOpenedIt()
        {

            EMSP.API.EventStreamHeartbeat = TimeSpan.FromMilliseconds(300);

            using var http      = await SignedIn();

            var lines           = new List<String>();
            using var reader    = await OpenStream(http, lines);

            Assert.That((await http.PostAsync("/api/v1/auth/logout", null)).StatusCode,
                        Is.EqualTo(HttpStatusCode.NoContent));

            var afterwards      = "Logged after the sign-out " + Guid.NewGuid().ToString("N")[..8];
            EMSP.Log.Info(afterwards, "test");

            var ended           = await EndsWithin(reader, lines, TimeSpan.FromSeconds(5));

            Assert.Multiple(() => {
                Assert.That(ended,                                          Is.True,   "the stream went on after its session had ended");
                Assert.That(lines.Any(line => line.Contains(afterwards)),   Is.False,  "a line logged after the sign-out was sent to the session that had signed out");
            });

        }

        #endregion

        #region AQuietStreamEndsWithItsSessionToo()

        /// <summary>
        /// And a stream nothing is logged into ends at its next heartbeat, not
        /// whenever the next line happens to be written - however the session
        /// ended. Here all of an account's sessions are taken back at once,
        /// the way a new password takes them, which logs nothing at all.
        /// </summary>
        [Test]
        public async Task AQuietStreamEndsWithItsSessionToo()
        {

            EMSP.API.EventStreamHeartbeat = TimeSpan.FromMilliseconds(300);

            using var http      = await SignedIn();

            var lines           = new List<String>();
            using var reader    = await OpenStream(http, lines);

            var session         = EMSP.ExtAPI.Sessions.Single();

            Assert.That(EMSP.ExtAPI.Sessions.RemoveAllForUser(session.UserId), Is.EqualTo(1));

            Assert.That(await EndsWithin(reader, lines, TimeSpan.FromSeconds(3)), Is.True,
                        "a stream nothing was logged into went on after its session had ended");

        }

        #endregion

        #region AStreamOfAnotherSessionGoesOn()

        /// <summary>
        /// Only the stream of the session that ended ends: a second browser,
        /// signed in on its own, goes on being sent the log.
        /// </summary>
        [Test]
        public async Task AStreamOfAnotherSessionGoesOn()
        {

            using var mine      = await SignedIn();
            using var theirs    = await SignedIn();

            var endingLines     = new List<String>();
            var goingLines      = new List<String>();
            using var ending    = await OpenStream(mine,   endingLines);
            using var going     = await OpenStream(theirs, goingLines);

            Assert.That((await mine.PostAsync("/api/v1/auth/logout", null)).StatusCode,
                        Is.EqualTo(HttpStatusCode.NoContent));

            var afterwards      = "Logged after one of two signed out " + Guid.NewGuid().ToString("N")[..8];
            EMSP.Log.Info(afterwards, "test");

            var arrived         = await ReadUntil (going,  line => { goingLines.Add(line); return line.Contains(afterwards); }, TimeSpan.FromSeconds(10));
            var ended           = await EndsWithin(ending, endingLines, TimeSpan.FromSeconds(5));

            Assert.Multiple(() => {
                Assert.That(arrived,                                              Is.True,   "the stream of the session still signed in stopped too");
                Assert.That(ended,                                                Is.True,   "the stream of the session that signed out went on");
                Assert.That(endingLines.Any(line => line.Contains(afterwards)),   Is.False,  "a line logged after the sign-out was sent to the session that had signed out");
            });

        }

        #endregion

        #region AStreamOpenedWithAnAPIKeyEndsWithTheKey()

        /// <summary>
        /// A stream opened with an API key ends when the key is taken back -
        /// and a line logged afterwards does not come down it first.
        /// </summary>
        /// <remarks>
        /// Such a stream has no session that could end, and was held to its
        /// account alone on the vehicle: a key that was revoked went on being
        /// sent the log for as long as the account it belonged to was there.
        /// </remarks>
        [Test]
        public async Task AStreamOpenedWithAnAPIKeyEndsWithTheKey()
        {

            EMSP.API.EventStreamHeartbeat = TimeSpan.FromMilliseconds(300);

            var (http, key)     = await WithAPIKey();

            using var client    = http;

            var lines           = new List<String>();
            using var reader    = await OpenStream(client, lines);

            await EMSP.ExtAPI.RemoveAPIKey(key);

            Assert.That(EMSP.ExtAPI.TryGetAPIKey(key.Id, out _), Is.False, "the API key is gone");

            var afterwards      = "Logged after the key was taken back " + Guid.NewGuid().ToString("N")[..8];
            EMSP.Log.Info(afterwards, "test");

            var ended           = await EndsWithin(reader, lines, TimeSpan.FromSeconds(5));

            Assert.Multiple(() => {
                Assert.That(ended,                                          Is.True,   "the stream went on after its API key had been taken back");
                Assert.That(lines.Any(line => line.Contains(afterwards)),   Is.False,  "a line logged after the key was taken back was sent over it");
            });

        }

        #endregion

        #region AStreamEndsWhenItsAPIKeyRunsOut()

        /// <summary>
        /// And one whose key runs out ends at the next heartbeat after, with
        /// nothing logged and nobody taking anything back.
        /// </summary>
        [Test]
        public async Task AStreamEndsWhenItsAPIKeyRunsOut()
        {

            EMSP.API.EventStreamHeartbeat = TimeSpan.FromMilliseconds(300);

            // Long enough for the stream to open before the key runs out.
            var runsOut         = DateTimeOffset.UtcNow.AddSeconds(5);
            var (http, _)       = await WithAPIKey(runsOut);

            using var client    = http;

            var lines           = new List<String>();
            using var reader    = await OpenStream(client, lines);

            var ended           = await EndsWithin(reader, lines, runsOut - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3));
            var endedAt         = DateTimeOffset.UtcNow;

            Assert.Multiple(() => {
                Assert.That(ended,    Is.True,                                                "the stream went on after its API key had run out");
                Assert.That(endedAt,  Is.GreaterThanOrEqualTo(runsOut.AddMilliseconds(-100)),  "the stream ended before its API key ran out, so something else ended it");
            });

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
