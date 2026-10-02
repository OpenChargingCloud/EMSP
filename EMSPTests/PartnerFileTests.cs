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
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using cloud.charging.open.EMSP.OCPI;

using cloud.charging.open.protocols.WWCP.Node.Logging;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// A roaming partner added or removed, or registered in either direction,
    /// while the file the OCPI library keeps the partners of a version in
    /// cannot be written: 500 and why, and nothing changed, neither now nor at
    /// the next start - except a registration the partner accepted, which is
    /// kept and written down later.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both were answered as done. The library wrote the line into a queue
    /// that told the debug log alone that it could not: an added partner was
    /// listed, its token opening this EMSP, and gone at the next start; a
    /// removed one was back at the next start, its token opening this EMSP
    /// again. A registration was answered as done, or as a partner that did
    /// not go along, whatever the file did.
    /// </para>
    /// <para>
    /// The file is made unwritable the way that stops root as well: a
    /// directory where it would be. All three versions an EMSP speaks are
    /// offered, because each keeps its partners in a file of its own and is
    /// bound to this EMSP by an adapter of its own.
    /// </para>
    /// </remarks>
    public class PartnerFileTests : AEMSPTests
    {

        #region Configuration - an EMSP that offers every version

        /// <summary>
        /// The default is 2.1.1 and 2.2.1.
        /// </summary>
        protected override JObject Configuration

            => new (
                   new JProperty("nts",   new JObject(
                       new JProperty("enabled",  false)
                   )),
                   new JProperty("ocpi",  new JObject(
                       new JProperty("versions",  new JArray("2.1.1", "2.2.1", "2.3.0"))
                   ))
               );

        #endregion

        #region NewEMSP() - an EMSP whose stopping can be made to fail

        /// <summary>
        /// An EMSP as any other, whose next stop can be made to fail - see
        /// EMSPWhoseStopCanFail.
        /// </summary>
        protected override EMSP NewEMSP()

            => new EMSPWhoseStopCanFail(
                   AccountsPath:  Path.Combine(Directory, "accounts"),
                   ConfigFile:    TestEMSPs.ConfigFile(Directory, Configuration),
                   Clock:         Clock
               );

        #endregion


        #region APartnerIsNotAddedWhereItsFileCannotTakeIt(Version)

        /// <summary>
        /// A partner the file of its version cannot take is not added: 500 and
        /// why, not in the list, and not there at the next start.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APartnerIsNotAddedWhereItsFileCannotTakeIt(String Version)
        {

            using var admin = await SignedIn();

            var file      = BlockPartnersFile(Version);

            var response  = await admin.PostAsync("/api/v1/ocpi/partners", Partner(Version));
            var text      = await response.Content.ReadAsStringAsync();
            var listed    = await ListedPartners(admin);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,  Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(JObject.Parse(text).Value<String>("error"),
                            Does.StartWith($"The roaming partner '{PartnerId}' was not added: '{Path.GetFileName(file)}' could not be written: "));
                Assert.That(listed,               Does.Not.Contain(PartnerId), "The partner the file refused is listed all the same.");
            });

            Assert.That(await PartnersAfterARestart(file), Does.Not.Contain(PartnerId));

        }

        #endregion

        #region APartnerIsNotRemovedWhereItsFileCannotTakeIt(Version)

        /// <summary>
        /// A partner whose removal the file of its version cannot take is not
        /// removed: 500 and why, still in the list, and still there at the next
        /// start.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APartnerIsNotRemovedWhereItsFileCannotTakeIt(String Version)
        {

            using var admin = await SignedIn();

            await AddPartner(admin, Version);

            // What the next start below reads the partner back from.
            Assert.That(await File.ReadAllTextAsync(PartnersFile(Version)), Does.Contain(protocols.OCPI.CommonHTTPAPI.addRemoteParty),
                        "The partner that was added is not in its file.");

            var file      = BlockPartnersFile(Version);

            var response  = await admin.DeleteAsync($"/api/v1/ocpi/partners/{Version}/{PartnerId}");
            var text      = await response.Content.ReadAsStringAsync();
            var listed    = await ListedPartners(admin);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,  Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(JObject.Parse(text).Value<String>("error"),
                            Does.StartWith($"The roaming partner '{PartnerId}' was not removed, and its token still opens this EMSP: " +
                                           $"'{Path.GetFileName(file)}' could not be written: "));
                Assert.That(listed,               Does.Contain(PartnerId), "The partner the file would not let go of is gone from the list.");
            });

            Assert.That(await PartnersAfterARestart(file), Does.Contain(PartnerId));

        }

        #endregion

        #region APartnerIsRemovedForGood(Version)

        /// <summary>
        /// A partner removed while its file can be written is gone: 200, not in
        /// the list, and not there at the next start.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APartnerIsRemovedForGood(String Version)
        {

            using var admin = await SignedIn();

            await AddPartner(admin, Version);

            var response  = await admin.DeleteAsync($"/api/v1/ocpi/partners/{Version}/{PartnerId}");
            var text      = await response.Content.ReadAsStringAsync();
            var listed    = await ListedPartners(admin);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,  Is.EqualTo(HttpStatusCode.OK), text);
                Assert.That(listed,               Does.Not.Contain(PartnerId), "The partner that was removed is still listed.");
            });

            Assert.That(await PartnersAfterARestart(), Does.Not.Contain(PartnerId));

        }

        #endregion


        #region ARefusalIsWhatItWasWhileThePartnersFileCannotBeWritten(Version)

        /// <summary>
        /// While the file of a version cannot be written, what is wrong with a
        /// request is said as ever: a country code of three letters is 400, and
        /// a partner that is not there is 404. Neither is the file's to answer.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ARefusalIsWhatItWasWhileThePartnersFileCannotBeWritten(String Version)
        {

            using var admin  = await SignedIn();

            BlockPartnersFile(Version);

            var wrongCountry = await admin.PostAsync("/api/v1/ocpi/partners", JSONBody(
                                   new JProperty("version",      Version),
                                   new JProperty("countryCode",  "DEU"),
                                   new JProperty("partyId",      "GEF"),
                                   new JProperty("role",         "CPO"),
                                   new JProperty("name",         "Test CPO")
                               ));

            var notThere     = await admin.DeleteAsync($"/api/v1/ocpi/partners/{Version}/{PartnerId}");

            Assert.Multiple(() => {
                Assert.That(wrongCountry.StatusCode,  Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(notThere.StatusCode,      Is.EqualTo(HttpStatusCode.NotFound));
            });

        }

        #endregion


        #region NothingIsSentWhereTheTokenToBeCalledBackWithCannotBeStored(Version)

        /// <summary>
        /// A registration whose first line the file cannot take - the token
        /// the partner would call back with - sends nothing: 500 and why, and
        /// nothing changed, neither now nor at the next start.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task NothingIsSentWhereTheTokenToBeCalledBackWithCannotBeStored(String Version)
        {

            using var admin = await SignedIn();

            await using var cpo = await StubCPO.Start(Version);

            await AddRegistrablePartner(admin, Version, cpo);

            var file      = BlockPartnersFile(Version);

            var response  = await admin.PostAsync($"/api/v1/ocpi/partners/{Version}/{PartnerId}/register", JSONBody());
            var text      = await response.Content.ReadAsStringAsync();
            var answer    = JObject.Parse(text);
            var listed    = await ListedPartner(admin, Version);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,                   Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(answer.Value<Boolean>("ok"),           Is.False,                  "The registration its file refused is said to have worked.");
                Assert.That(answer.Value<String>("message"),
                            Does.StartWith($"Nothing was sent to '{PartnerId}', and nothing has changed: the token they would call back with could not be stored - " +
                                           $"'{Path.GetFileName(file)}' could not be written: "));
                Assert.That(answer["partners"]?["partners"],       Is.InstanceOf<JArray>(),   "The answer does not bring the partners, which the page draws again.");
                Assert.That(cpo.ReceivedCredentials,               Is.Null,                   "The credentials were sent, though the token to call back with could not be stored.");
                Assert.That(listed?.Value<Boolean>("registered"),  Is.False,                  "The partner is said to be registered.");
            });

            var after = await PartnerAfterARestart(Version, file);

            Assert.Multiple(() => {
                Assert.That(after,              Is.Not.Null,  "The partner is gone at the next start.");
                Assert.That(after?.Registered,  Is.False,     "The partner is registered at the next start.");
            });

        }

        #endregion

        #region ARegistrationThePartnerAcceptedIsKeptAndWrittenDownWithTheNextChange(Version)

        /// <summary>
        /// A registration the partner accepted, whose line the file cannot
        /// take: 500 and why, and in effect all the same - the partner uses the
        /// new tokens already - and written down with the next change the file
        /// takes.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ARegistrationThePartnerAcceptedIsKeptAndWrittenDownWithTheNextChange(String Version)
        {

            using var admin = await SignedIn();

            await using var cpo = await StubCPO.Start(Version);

            await AddRegistrablePartner(admin, Version, cpo);

            cpo.WhenCredentialsArrive = () => BlockPartnersFile(Version);

            await RegisterAcceptedAndNotSaved(admin, Version, cpo);

            UnblockPartnersFile(Version);

            var other = await admin.PostAsync("/api/v1/ocpi/partners", OtherPartner(Version));

            Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.Created), $"The next change answered {(Int32) other.StatusCode}: {await other.Content.ReadAsStringAsync()}");

            var after = await PartnerAfterARestart(Version);

            Assert.Multiple(() => {
                Assert.That(after?.Registered,              Is.True,                     "The registration kept is not known at the next start, though the file took a change after it.");
                Assert.That(after?.TheirToken?.ToString(),  Is.EqualTo(StubCPO.TokenC),  "The token the partner handed out is not known at the next start.");
                Assert.That(after?.OurToken?.  ToString(),  Is.EqualTo(cpo.ReceivedCredentials?.Value<String>("token")),
                            "The token this EMSP sent the partner is not known at the next start.");
            });

        }

        #endregion

        #region ARegistrationThePartnerAcceptedIsWrittenDownWhenThisEMSPStops(Version)

        /// <summary>
        /// A registration kept is written down when this EMSP stops, where the
        /// file takes it by then.
        /// </summary>
        /// <remarks>
        /// The EMSP of this fixture is disposed here, and once more by the
        /// TearDown, which does no harm: a node that stopped stops at once,
        /// and the OCPI API has nothing left to write.
        /// </remarks>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ARegistrationThePartnerAcceptedIsWrittenDownWhenThisEMSPStops(String Version)
        {

            using var admin = await SignedIn();

            await using var cpo = await StubCPO.Start(Version);

            await AddRegistrablePartner(admin, Version, cpo);

            cpo.WhenCredentialsArrive = () => BlockPartnersFile(Version);

            await RegisterAcceptedAndNotSaved(admin, Version, cpo);

            UnblockPartnersFile(Version);

            await EMSP.DisposeAsync();

            var after = await PartnerAfterARestart(Version);

            Assert.Multiple(() => {
                Assert.That(after?.Registered,              Is.True,                     "The registration kept is not known at the next start, though the file took lines when this EMSP stopped.");
                Assert.That(after?.TheirToken?.ToString(),  Is.EqualTo(StubCPO.TokenC),  "The token the partner handed out is not known at the next start.");
                Assert.That(after?.OurToken?.  ToString(),  Is.EqualTo(cpo.ReceivedCredentials?.Value<String>("token")),
                            "The token this EMSP sent the partner is not known at the next start.");
            });

        }

        #endregion

        #region ARegistrationTheFileStillRefusesWhenThisEMSPStopsIsInTheLog(Version)

        /// <summary>
        /// A registration kept that the file still refuses when this EMSP stops
        /// is an error in the log: the next start will not know it.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ARegistrationTheFileStillRefusesWhenThisEMSPStopsIsInTheLog(String Version)
        {

            using var admin = await SignedIn();

            await using var cpo = await StubCPO.Start(Version);

            await AddRegistrablePartner(admin, Version, cpo);

            cpo.WhenCredentialsArrive = () => BlockPartnersFile(Version);

            await RegisterAcceptedAndNotSaved(admin, Version, cpo);

            await EMSP.DisposeAsync();

            var said = EMSP.Log.Recent(100, Tag: "files").
                                Where (entry => entry.Message.Contains("the next start will not know it")).
                                ToArray();

            // So that the TearDown's second disposal has nothing left to say.
            UnblockPartnersFile(Version);

            Assert.That(said, Has.Length.EqualTo(1), "The registration the next start will not know is not in the log, or more than once.");

            Assert.Multiple(() => {
                Assert.That(said[0].Level,    Is.EqualTo(LogLevel.Error));
                Assert.That(said[0].Tags,     Does.Contain("ocpi"));
                Assert.That(said[0].Message,  Does.Contain($"OCPI {Version}"),  "The log does not name the version.");
                Assert.That(said[0].Message,  Does.Contain(PartnerId),          "The log does not name the partner.");
            });

        }

        #endregion

        #region ARegistrationThePartnerAcceptedIsWrittenDownWhereThisEMSPFailsToStop(Version)

        /// <summary>
        /// A registration kept is written down when this EMSP stops, where the
        /// file takes it by then - even where stopping fails, which is said
        /// all the same.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ARegistrationThePartnerAcceptedIsWrittenDownWhereThisEMSPFailsToStop(String Version)
        {

            using var admin = await SignedIn();

            await using var cpo = await StubCPO.Start(Version);

            await AddRegistrablePartner(admin, Version, cpo);

            cpo.WhenCredentialsArrive = () => BlockPartnersFile(Version);

            await RegisterAcceptedAndNotSaved(admin, Version, cpo);

            UnblockPartnersFile(Version);

            ((EMSPWhoseStopCanFail) EMSP).NextStopFails = true;

            Assert.ThrowsAsync<InvalidOperationException>(async () => await EMSP.DisposeAsync(),
                                                          "The stop that was made to fail is not said to have failed.");

            var after = await PartnerAfterARestart(Version);

            Assert.Multiple(() => {
                Assert.That(after?.Registered,              Is.True,                     "The registration kept is not known at the next start, though the file took lines when this EMSP failed to stop.");
                Assert.That(after?.TheirToken?.ToString(),  Is.EqualTo(StubCPO.TokenC),  "The token the partner handed out is not known at the next start.");
                Assert.That(after?.OurToken?.  ToString(),  Is.EqualTo(cpo.ReceivedCredentials?.Value<String>("token")),
                            "The token this EMSP sent the partner is not known at the next start.");
            });

        }

        #endregion

        #region ARegistrationTheFileStillRefusesWhereThisEMSPFailsToStopIsInTheLog(Version)

        /// <summary>
        /// A registration kept that the file still refuses when this EMSP stops
        /// is an error in the log - even where stopping fails.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ARegistrationTheFileStillRefusesWhereThisEMSPFailsToStopIsInTheLog(String Version)
        {

            using var admin = await SignedIn();

            await using var cpo = await StubCPO.Start(Version);

            await AddRegistrablePartner(admin, Version, cpo);

            cpo.WhenCredentialsArrive = () => BlockPartnersFile(Version);

            await RegisterAcceptedAndNotSaved(admin, Version, cpo);

            ((EMSPWhoseStopCanFail) EMSP).NextStopFails = true;

            Assert.ThrowsAsync<InvalidOperationException>(async () => await EMSP.DisposeAsync(),
                                                          "The stop that was made to fail is not said to have failed.");

            var said = EMSP.Log.Recent(100, Tag: "files").
                                Where (entry => entry.Message.Contains("the next start will not know it")).
                                ToArray();

            // So that the TearDown's second disposal has nothing left to say.
            UnblockPartnersFile(Version);

            Assert.That(said, Has.Length.EqualTo(1), "The registration the next start will not know is not in the log, or more than once.");

            Assert.Multiple(() => {
                Assert.That(said[0].Level,    Is.EqualTo(LogLevel.Error));
                Assert.That(said[0].Tags,     Does.Contain("ocpi"));
                Assert.That(said[0].Message,  Does.Contain($"OCPI {Version}"),  "The log does not name the version.");
                Assert.That(said[0].Message,  Does.Contain(PartnerId),          "The log does not name the partner.");
            });

        }

        #endregion


        #region APartnerThatRegistersHereIsRegisteredForGood(Version)

        /// <summary>
        /// A partner that registers here with the token it was given is
        /// answered 1000 and a token of its own, and is registered - now and at
        /// the next start.
        /// </summary>
        /// <remarks>
        /// On OCPI 2.1.1 this was answered 500: the library took for granted a
        /// token of the partner's, which a partner added with nothing of its
        /// own does not have.
        /// </remarks>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APartnerThatRegistersHereIsRegisteredForGood(String Version)
        {

            using var admin = await SignedIn();

            await using var cpo = await StubCPO.Start(Version);

            var ourToken  = await AddPartner(admin, Version);

            using var partner = PartnerWith(ourToken, Version);

            var (details, credentials) = await Endpoints(partner, Version);

            var posted    = await partner.PostAsync(credentials, cpo.CredentialsBody());
            var text      = await posted.Content.ReadAsStringAsync();

            Assert.That(posted.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);

            var tokenNow  = JObject.Parse(text)["data"]?.Value<String>("token") ?? "";
            var listed    = await ListedPartner(admin, Version);

            using var now = PartnerWith(tokenNow, Version);

            var opens     = await now.GetAsync(details);

            Assert.Multiple(() => {
                Assert.That(StatusCodeOf(text),                    Is.EqualTo(1000),            text);
                Assert.That(listed?.Value<Boolean>("registered"),  Is.True,                     "The partner that registered is not said to be registered.");
                Assert.That(listed?.Value<String>("theirToken"),   Is.EqualTo(StubCPO.TokenC),  "The token the partner sent is not the one this EMSP calls it with.");
                Assert.That(listed?.Value<String>("ourToken"),     Is.EqualTo(tokenNow),        "The token this EMSP answered with is not the one it lists.");
                Assert.That(opens.IsSuccessStatusCode,             Is.True,                     "The token this EMSP answered with does not open it.");
            });

            var after = await PartnerAfterARestart(Version);

            Assert.Multiple(() => {
                Assert.That(after?.Registered,              Is.True,                     "The partner is not registered at the next start.");
                Assert.That(after?.OurToken?.  ToString(),  Is.EqualTo(tokenNow),        "The token this EMSP answered with is not known at the next start.");
                Assert.That(after?.TheirToken?.ToString(),  Is.EqualTo(StubCPO.TokenC),  "The token the partner sent is not known at the next start.");
            });

        }

        #endregion

        #region APartnerRegisteringHereWhileItsFileCannotTakeItKeepsItsToken(Version)

        /// <summary>
        /// A partner that registers here while the file cannot take it is
        /// answered OCPI 3000 with HTTP 500, and nothing changed: the token it
        /// came with still opens this EMSP, now and at the next start.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APartnerRegisteringHereWhileItsFileCannotTakeItKeepsItsToken(String Version)
        {

            using var admin = await SignedIn();

            await using var cpo = await StubCPO.Start(Version);

            var ourToken  = await AddPartner(admin, Version);

            using var partner = PartnerWith(ourToken, Version);

            var (details, credentials) = await Endpoints(partner, Version);

            var file      = BlockPartnersFile(Version);

            var posted    = await partner.PostAsync(credentials, cpo.CredentialsBody());
            var text      = await posted.Content.ReadAsStringAsync();
            var listed    = await ListedPartner(admin, Version);
            var stillOpen = await partner.GetAsync(details);

            Assert.Multiple(() => {
                Assert.That(posted.StatusCode,                     Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(StatusCodeOf(text),                    Is.EqualTo(3000),      text);
                Assert.That(listed?.Value<Boolean>("registered"),  Is.False,              "The partner is said to be registered, though the file refused it.");
                Assert.That(listed?.Value<String>("ourToken"),     Is.EqualTo(ourToken),  "The token the partner came with is not this EMSP's any more.");
                Assert.That(stillOpen.IsSuccessStatusCode,         Is.True,               "The token the partner came with no longer opens this EMSP.");
            });

            var after = await PartnerAfterARestart(Version, file);

            Assert.Multiple(() => {
                Assert.That(after?.Registered,            Is.False,              "The partner is registered at the next start.");
                Assert.That(after?.OurToken?.ToString(),  Is.EqualTo(ourToken),  "The token the partner came with is not known at the next start.");
            });

        }

        #endregion

        #region APartnerUnregisteringWhileItsFileCannotTakeItStaysRegistered(Version)

        /// <summary>
        /// A partner that unregisters while the file cannot take it is answered
        /// OCPI 3000 with HTTP 500, and stays registered, its token valid, now
        /// and at the next start.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task APartnerUnregisteringWhileItsFileCannotTakeItStaysRegistered(String Version)
        {

            using var admin = await SignedIn();

            await using var cpo = await StubCPO.Start(Version);

            var ourToken   = await AddPartner(admin, Version);

            using var first = PartnerWith(ourToken, Version);

            var (details, credentials) = await Endpoints(first, Version);

            var posted     = await first.PostAsync(credentials, cpo.CredentialsBody());
            var postedText = await posted.Content.ReadAsStringAsync();

            Assert.That(posted.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"The registration answered {(Int32) posted.StatusCode}: {postedText}");

            var tokenNow   = JObject.Parse(postedText)["data"]?.Value<String>("token") ?? "";

            using var partner = PartnerWith(tokenNow, Version);

            var file       = BlockPartnersFile(Version);

            var deleted    = await partner.DeleteAsync(credentials);
            var text       = await deleted.Content.ReadAsStringAsync();
            var listed     = await ListedPartner(admin, Version);
            var stillOpen  = await partner.GetAsync(details);

            Assert.Multiple(() => {
                Assert.That(deleted.StatusCode,                    Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(StatusCodeOf(text),                    Is.EqualTo(3000),  text);
                Assert.That(listed?.Value<Boolean>("registered"),  Is.True,           "The partner is not registered any more, though the file refused its unregistration.");
                Assert.That(stillOpen.IsSuccessStatusCode,         Is.True,           "The token of the partner no longer opens this EMSP.");
            });

            var after = await PartnerAfterARestart(Version, file);

            Assert.Multiple(() => {
                Assert.That(after?.Registered,            Is.True,               "The partner is not registered at the next start.");
                Assert.That(after?.OurToken?.ToString(),  Is.EqualTo(tokenNow),  "The token of the partner is not known at the next start.");
            });

        }

        #endregion


        #region ALineTheFileRefusesIsInTheLog(Version)

        /// <summary>
        /// A line the file of the partners refuses is an error in the log,
        /// naming the file and the command - and not the line, which holds
        /// tokens.
        /// </summary>
        [TestCase("2.1.1")]
        [TestCase("2.2.1")]
        [TestCase("2.3.0")]
        public async Task ALineTheFileRefusesIsInTheLog(String Version)
        {

            using var admin = await SignedIn();

            var file      = BlockPartnersFile(Version);

            var response  = await admin.PostAsync("/api/v1/ocpi/partners", Partner(Version));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError), await response.Content.ReadAsStringAsync());

            var said = EMSP.Log.Recent(100, Tag: "files").ToArray();

            Assert.That(said, Has.Length.EqualTo(1), "The line its file refused is not in the log, or more than once.");

            Assert.Multiple(() => {
                Assert.That(said[0].Level,    Is.EqualTo(LogLevel.Error));
                Assert.That(said[0].Tags,     Does.Contain("ocpi"));
                Assert.That(said[0].Message,  Does.Contain($"OCPI {Version}"),                             "The log does not name the version.");
                Assert.That(said[0].Message,  Does.Contain($"{Path.GetFileName(file)}' could not be written"), "The log does not name the file that refused.");
                Assert.That(said[0].Message,  Does.Contain(protocols.OCPI.CommonHTTPAPI.addRemoteParty),  "The log does not name the command.");
            });

        }

        #endregion


        #region (private static) PartnerId / Partner(Version) / OtherPartner(Version) / RegistrablePartner(Version, CPO)

        /// <summary>
        /// The partner every test here adds.
        /// </summary>
        private const String PartnerId = "DE-GEF_CPO";

        /// <summary>
        /// Its request body, on the given version.
        /// </summary>
        private static StringContent Partner(String Version)

            => JSONBody(
                   new JProperty("version",      Version),
                   new JProperty("countryCode",  "DE"),
                   new JProperty("partyId",      "GEF"),
                   new JProperty("role",         "CPO"),
                   new JProperty("name",         "Test CPO")
               );

        /// <summary>
        /// Another partner, for a change after the one a test is about.
        /// </summary>
        private static StringContent OtherPartner(String Version)

            => JSONBody(
                   new JProperty("version",      Version),
                   new JProperty("countryCode",  "DE"),
                   new JProperty("partyId",      "GEG"),
                   new JProperty("role",         "CPO"),
                   new JProperty("name",         "Other CPO")
               );

        /// <summary>
        /// The partner every test here adds, with the token and the versions
        /// URL it handed out - so that this EMSP can register with it.
        /// </summary>
        private static StringContent RegistrablePartner(String   Version,
                                                        StubCPO  CPO)

            => JSONBody(
                   new JProperty("version",      Version),
                   new JProperty("countryCode",  "DE"),
                   new JProperty("partyId",      "GEF"),
                   new JProperty("role",         "CPO"),
                   new JProperty("name",         "Test CPO"),
                   new JProperty("theirToken",   StubCPO.TokenA),
                   new JProperty("versionsURL",  CPO.VersionsURL)
               );

        #endregion

        #region (private static) AddPartner(Admin, Version) / AddRegistrablePartner(Admin, Version, CPO)

        /// <summary>
        /// Add the partner through the JSON API, on the given version, and hand
        /// back the token this EMSP made up for it.
        /// </summary>
        private static async Task<String> AddPartner(HttpClient  Admin,
                                                     String      Version)
        {

            var response = await Admin.PostAsync("/api/v1/ocpi/partners", Partner(Version));
            var text     = await response.Content.ReadAsStringAsync();

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                        $"Adding the partner answered {(Int32) response.StatusCode}: {text}");

            return JObject.Parse(text).Value<String>("ourToken")!;

        }

        /// <summary>
        /// Add the partner with the token and the versions URL it handed out.
        /// </summary>
        private static async Task AddRegistrablePartner(HttpClient  Admin,
                                                        String      Version,
                                                        StubCPO     CPO)
        {

            var response = await Admin.PostAsync("/api/v1/ocpi/partners", RegistrablePartner(Version, CPO));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                        $"Adding the partner answered {(Int32) response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        }

        #endregion

        #region (private) RegisterAcceptedAndNotSaved(Admin, Version, CPO)

        /// <summary>
        /// This EMSP registers with the partner, which accepts, and the file
        /// cannot take what that changed: 500 and why, and in effect all the
        /// same.
        /// </summary>
        private async Task RegisterAcceptedAndNotSaved(HttpClient  Admin,
                                                       String      Version,
                                                       StubCPO     CPO)
        {

            var response  = await Admin.PostAsync($"/api/v1/ocpi/partners/{Version}/{PartnerId}/register", JSONBody());
            var text      = await response.Content.ReadAsStringAsync();
            var answer    = JObject.Parse(text);
            var listed    = await ListedPartner(Admin, Version);

            Assert.Multiple(() => {
                Assert.That(response.StatusCode,                   Is.EqualTo(HttpStatusCode.InternalServerError), text);
                Assert.That(answer.Value<Boolean>("ok"),           Is.False,                     "The registration whose line its file refused is said to have worked.");
                Assert.That(answer.Value<String>("message"),
                            Does.StartWith($"'{PartnerId}' accepted this EMSP's credentials, and they are in effect, but could not be stored - " +
                                           $"'{Path.GetFileName(PartnersFile(Version))}' could not be written: "));
                Assert.That(answer.Value<String>("message"),
                            Does.EndWith("Repair the file before this EMSP stops: they are written down with the next change the file takes, and when this EMSP stops at the latest."));
                Assert.That(listed?.Value<Boolean>("registered"),  Is.True,                      "The registration the partner accepted is not in effect.");
                Assert.That(listed?.Value<String>("theirToken"),   Is.EqualTo(StubCPO.TokenC),   "The token the partner handed out in its answer is not used.");
                Assert.That(listed?.Value<String>("ourToken"),     Is.EqualTo(CPO.ReceivedCredentials?.Value<String>("token")),
                            "The token this EMSP sent the partner is not the one it lists.");
            });

        }

        #endregion

        #region (private) PartnerWith(Token, Version) / Endpoints(Partner, Version) / StatusCodeOf(Text)

        /// <summary>
        /// The partner calling this EMSP with the given token, encoded the way
        /// its version sends it: as it is in OCPI 2.1.1, in base64 from 2.2 on.
        /// </summary>
        private HttpClient PartnerWith(String  Token,
                                       String  Version)
        {

            var http = new HttpClient { BaseAddress = new Uri(BaseURL) };

            http.DefaultRequestHeaders.TryAddWithoutValidation(
                "Authorization",
                "Token " + (Version == "2.1.1"
                                ? Token
                                : Convert.ToBase64String(Encoding.UTF8.GetBytes(Token)))
            );

            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

            return http;

        }

        /// <summary>
        /// Where this EMSP's details of the given version are, and its
        /// credentials endpoint - as a partner finds them.
        /// </summary>
        private static async Task<(String Details, String Credentials)> Endpoints(HttpClient  Partner,
                                                                                  String      Version)
        {

            var versions   = (await GetJSON(Partner, "/ext/versions"))["data"] as JArray;
            var details    = versions!.First(version => version.Value<String>("version") == Version).Value<String>("url")!;
            var endpoints  = (await GetJSON(Partner, details))["data"]?["endpoints"] as JArray;

            return (details, endpoints!.First(endpoint => endpoint.Value<String>("identifier") == "credentials").Value<String>("url")!);

        }

        /// <summary>
        /// The OCPI status code of an answer, or null where the answer is none.
        /// </summary>
        private static Int32? StatusCodeOf(String Text)

            => Text.TrimStart().StartsWith('{')
                   ? JObject.Parse(Text).Value<Int32?>("status_code")
                   : null;

        #endregion

        #region (private static) ListedPartner(HTTP, Version) / ListedPartners(HTTP)

        /// <summary>
        /// The partner every test here adds, as the JSON API lists it on the
        /// given version, or null.
        /// </summary>
        private static async Task<JToken?> ListedPartner(HttpClient  HTTP,
                                                         String      Version)

            => ((await GetJSON(HTTP, "/api/v1/ocpi/partners"))["partners"] as JArray ?? new JArray()).
                   FirstOrDefault(partner => partner.Value<String>("id")      == PartnerId &&
                                             partner.Value<String>("version") == Version);

        /// <summary>
        /// The partners the JSON API lists, by their identification.
        /// </summary>
        private static async Task<String[]> ListedPartners(HttpClient HTTP)

            => [.. ((await GetJSON(HTTP, "/api/v1/ocpi/partners"))["partners"] as JArray ?? new JArray()).
                   Select(partner => partner.Value<String>("id") ?? "")];

        #endregion

        #region (private) PartnersFile(Version) / BlockPartnersFile(Version) / UnblockPartnersFile(Version)

        /// <summary>
        /// The file the library keeps the partners of a version in.
        /// </summary>
        private String PartnersFile(String Version)

            => Path.Combine(
                   EMSP.OCPIDirectory,
                   Version switch {
                       "2.1.1"  => protocols.OCPIv2_1_1.CommonAPI.DefaultRemotePartyDBFileName,
                       "2.2.1"  => protocols.OCPIv2_2_1.CommonAPI.DefaultRemotePartyDBFileName,
                       "2.3.0"  => protocols.OCPIv2_3_0.CommonAPI.DefaultRemotePartyDBFileName,
                       _        => throw new ArgumentException($"This EMSP offers no OCPI {Version}.", nameof(Version))
                   }
               );

        /// <summary>
        /// Make the file the library keeps the partners of a version in
        /// unwritable: a directory where it is, which stops root as well. What
        /// it held is put aside, for the next start to find again.
        /// </summary>
        private String BlockPartnersFile(String Version)
        {

            var file = PartnersFile(Version);

            if (File.Exists(file))
                File.Move(file, file + ".aside");

            System.IO.Directory.CreateDirectory(file);

            return file;

        }

        /// <summary>
        /// Give the file the library keeps the partners of a version in back
        /// what it held, while this EMSP runs.
        /// </summary>
        private void UnblockPartnersFile(String Version)
            => Unblock(PartnersFile(Version));

        /// <summary>
        /// Give a partners' file that was blocked back what it held.
        /// </summary>
        private static void Unblock(String BlockedPartnersFile)
        {

            System.IO.Directory.Delete(BlockedPartnersFile);

            if (File.Exists(BlockedPartnersFile + ".aside"))
                File.Move(BlockedPartnersFile + ".aside", BlockedPartnersFile);

        }

        #endregion

        #region (private) PartnerAfterARestart(Version, BlockedPartnersFile = null)

        /// <summary>
        /// The partner every test here adds, as the next start in the same
        /// directory knows it on the given version, or null - see
        /// PartnersAfterARestart.
        /// </summary>
        private async Task<RemotePartySummary?> PartnerAfterARestart(String   Version,
                                                                     String?  BlockedPartnersFile   = null)

            => (await PartnersAfterARestart(emsp => emsp.OCPIVersions.
                                                         Where     (version => version.Label == Version).
                                                         SelectMany(version => version.RemoteParties).
                                                         ToArray(),
                                            BlockedPartnersFile)).
                   FirstOrDefault(partner => partner.Id.ToString() == PartnerId);

        #endregion

        #region (private) PartnersAfterARestart(BlockedPartnersFile = null)

        /// <summary>
        /// Stop this EMSP, give a partners' file that was blocked back what it
        /// held, and ask the next start in the same directory which partners it
        /// knows.
        /// </summary>
        private Task<String[]> PartnersAfterARestart(String? BlockedPartnersFile = null)

            => PartnersAfterARestart(emsp => emsp.OCPIVersions.
                                                  SelectMany(version => version.RemoteParties).
                                                  Select    (partner => partner.Id.ToString()).
                                                  ToArray(),
                                     BlockedPartnersFile);

        /// <summary>
        /// Stop this EMSP, give a partners' file that was blocked back what it
        /// held, and ask the next start in the same directory.
        /// </summary>
        private async Task<T> PartnersAfterARestart<T>(Func<EMSP, T>  Ask,
                                                       String?        BlockedPartnersFile   = null)
        {

            await EMSP.Stop();

            if (BlockedPartnersFile is not null)
                Unblock(BlockedPartnersFile);

            var again = await TestPorts.StartedOnFreshPorts(() => TestEMSPs.New(Directory, Configuration, Clock));

            try
            {
                return Ask(again);
            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

    }

}
