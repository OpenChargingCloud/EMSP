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

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// A roaming partner added or removed while the file the OCPI library
    /// keeps the partners of a version in cannot be written: 500 and why, and
    /// nothing changed, neither now nor at the next start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both were answered as done. The library wrote the line into a queue
    /// that told the debug log alone that it could not: an added partner was
    /// listed, its token opening this EMSP, and gone at the next start; a
    /// removed one was back at the next start, its token opening this EMSP
    /// again.
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


        #region (private static) PartnerId / Partner(Version)

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

        #endregion

        #region (private static) AddPartner(Admin, Version)

        /// <summary>
        /// Add the partner through the JSON API, on the given version.
        /// </summary>
        private static async Task AddPartner(HttpClient  Admin,
                                             String      Version)
        {

            var response = await Admin.PostAsync("/api/v1/ocpi/partners", Partner(Version));

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created),
                        $"Adding the partner answered {(Int32) response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        }

        #endregion

        #region (private static) ListedPartners(HTTP)

        /// <summary>
        /// The partners the JSON API lists, by their identification.
        /// </summary>
        private static async Task<String[]> ListedPartners(HttpClient HTTP)

            => [.. ((await GetJSON(HTTP, "/api/v1/ocpi/partners"))["partners"] as JArray ?? new JArray()).
                   Select(partner => partner.Value<String>("id") ?? "")];

        #endregion

        #region (private) PartnersFile(Version) / BlockPartnersFile(Version)

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

        #endregion

        #region (private) PartnersAfterARestart(BlockedPartnersFile = null)

        /// <summary>
        /// Stop this EMSP, give a partners' file that was blocked back what it
        /// held, and ask the next start in the same directory which partners it
        /// knows.
        /// </summary>
        private async Task<String[]> PartnersAfterARestart(String? BlockedPartnersFile = null)
        {

            await EMSP.Stop();

            if (BlockedPartnersFile is not null)
            {

                System.IO.Directory.Delete(BlockedPartnersFile);

                if (File.Exists(BlockedPartnersFile + ".aside"))
                    File.Move(BlockedPartnersFile + ".aside", BlockedPartnersFile);

            }

            var again = await TestPorts.StartedOnFreshPorts(() => TestEMSPs.New(Directory, Configuration, Clock));

            try
            {
                return [.. again.OCPIVersions.SelectMany(version => version.RemoteParties).Select(partner => partner.Id.ToString())];
            }
            finally
            {
                await again.DisposeAsync();
            }

        }

        #endregion

    }

}
