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

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// What an EMSP does with the configuration file it is handed
    /// and the accounts it finds, and what it refuses to do.
    /// </summary>
    /// <remarks>
    /// The configuration is read in the constructor, so those tests only build
    /// an EMSP. The accounts are made by <c>Start()</c>, because creating
    /// one is asynchronous - so the tests about them start the EMSP, and
    /// pay for a socket to do it.
    /// </remarks>
    public class StartupTests
    {

        #region Data

        private String directory = default!;

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void MakeADirectory()
        {
            directory = TestEMSPs.TemporaryDirectory("startup");
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void RemoveTheDirectory()
            => TestEMSPs.Remove(directory);

        #endregion


        #region AFirstStartMakesUpAnAccountAndKeepsOnlyItsHash()

        /// <summary>
        /// Nobody can sign in to a web interface with no accounts in it, and
        /// an unauthenticated setup page would be a door of its own. So the
        /// password is made up, handed back once, and kept only as a hash.
        /// </summary>
        [Test]
        public async Task AFirstStartMakesUpAnAccountAndKeepsOnlyItsHash()
        {

            await using var EMSP = TestEMSPs.New(directory, TestEMSPs.Offline);

            await EMSP.Start();

            Assert.Multiple(() => {

                Assert.That(EMSP.GeneratedPassword,      Is.Not.Null.And.Not.Empty);
                Assert.That(EMSP.ExtAPI.Users.Count(),   Is.EqualTo(1));
                Assert.That(EMSP.ExtAPI.Users.First().Id.ToString(),
                                                               Is.EqualTo(EMSP.DefaultAdminUser));

                // The password is nowhere below the accounts directory, in any
                // of the files the HTTPExt API writes - only the hash of it.
                var written = String.Join(
                                  "\n",
                                  Directory.GetFiles(EMSP.AccountsPath, "*", SearchOption.AllDirectories).
                                            Select(File.ReadAllText)
                              );

                Assert.That(written, Does.Not.Contain(EMSP.GeneratedPassword!),
                            "The password this EMSP made up was written to disk in the clear.");
                Assert.That(written, Does.Contain("$pbkdf2"));

            });

        }

        #endregion

        #region ASecondStartUsesTheAccountsItFindsAndMakesUpNothing()

        [Test]
        public async Task ASecondStartUsesTheAccountsItFindsAndMakesUpNothing()
        {

            String firstPassword;

            await using (var first = TestEMSPs.New(directory, TestEMSPs.Offline))
            {
                await first.Start();
                firstPassword = first.GeneratedPassword!;
            }

            await using var second = TestEMSPs.New(directory, TestEMSPs.Offline);

            await second.Start();

            Assert.Multiple(() => {
                Assert.That(second.GeneratedPassword,    Is.Null,
                            "An EMSP that found accounts made up another password anyway.");
                Assert.That(second.ExtAPI.Users.Count(), Is.EqualTo(1),
                            "A second account was made beside the one the first start wrote.");
            });

            // That the first password still opens it is checked over the wire
            // in AuthenticationTests.TheAccountSurvivesARestart; here what is
            // asked is only that nothing was made up a second time.
            Assert.That(firstPassword, Is.Not.Null.And.Not.Empty);

        }

        #endregion

        #region AnUnreadableConfigurationStopsTheEMSP()

        /// <summary>
        /// Somebody wrote down what their EMSP is and got it wrong.
        /// Quietly running as something else would be worse than stopping.
        /// </summary>
        [Test]
        public void AnUnreadableConfigurationStopsTheEMSP()
        {

            File.WriteAllText(Path.Combine(directory, "configuration.json"), "{ dns: [ unquoted");

            // Configuration: null, so that the broken file written above is
            // left exactly as it is.
            var problem = Assert.Throws<InvalidOperationException>(
                              () => TestEMSPs.New(directory)
                          );

            Assert.That(problem!.Message, Does.Contain("configuration.json"));

        }

        #endregion

        #region AEMSPWithNoFilesRunsOnItsDefaults()

        [Test]
        public async Task AEMSPWithNoFilesRunsOnItsDefaults()
        {

            await using var EMSP = TestEMSPs.New(directory);

            Assert.Multiple(() => {
                Assert.That(EMSP.DNSEnabled,           Is.True);
                Assert.That(EMSP.NTSEnabled,           Is.True);
                Assert.That(EMSP.PartyId.CountryCode.ToString(), Is.EqualTo(OCPIConfiguration.DefaultCountryCode));
                Assert.That(EMSP.PartyId.PartyId.ToString(),     Is.EqualTo(OCPIConfiguration.DefaultPartyId));
                Assert.That(EMSP.BusinessDetails.Name,           Is.EqualTo(OCPIConfiguration.DefaultName));
                Assert.That(EMSP.Version,              Is.Not.Empty);
                Assert.That(EMSP.CreatedAt,            Is.Not.EqualTo(default(DateTimeOffset)));
            });

        }

        #endregion

        #region TheFileDecidesWhoThisEMSPIsInOCPI()

        /// <summary>
        /// Read once, at the start. What the file says beats what the
        /// constructor was handed, and what it does not mention is left alone.
        /// </summary>
        [Test]
        public async Task TheFileDecidesWhoThisEMSPIsInOCPI()
        {

            var configuration = new JObject(
                                    new JProperty("nts",  new JObject(new JProperty("enabled", false))),
                                    new JProperty("ocpi", new JObject(
                                        new JProperty("countryCode",  "NL"),
                                        new JProperty("partyId",      "ABC"),
                                        new JProperty("name",         "Somebody Else")
                                    ))
                                );

            await using var EMSP = TestEMSPs.New(directory, configuration);

            Assert.Multiple(() => {
                Assert.That(EMSP.PartyIdText,               Is.EqualTo("NL-ABC"));
                Assert.That(EMSP.BusinessDetails.Name,      Is.EqualTo("Somebody Else"));
                // Not mentioned, so the default stands: the two classic versions.
                Assert.That(EMSP.OCPIVersions.Select(version => version.Label), Is.EqualTo(OCPIConfiguration.DefaultVersions));
            });

        }

        #endregion

        #region TheClockIsSetBeforeAnythingAsksTheTime()

        /// <summary>
        /// The event log stamps its entries with the EMSP's clock, and it
        /// is built inside the constructor - so an EMSP handed a clock has
        /// to be using it from its very first line, or the log reads the system
        /// one and cannot be held against anything.
        /// </summary>
        [Test]
        public async Task TheClockIsSetBeforeAnythingAsksTheTime()
        {

            var clock = TestClock.At(2000, 1, 1);

            await using var EMSP = TestEMSPs.New(directory, TestEMSPs.Offline, clock);

            Assert.Multiple(() => {

                Assert.That(EMSP.CreatedAt,   Is.EqualTo(clock.Now));
                Assert.That(EMSP.TimeProvider, Is.SameAs(clock));

                // Everything the EMSP said while it was being built.
                Assert.That(EMSP.Log.Count, Is.GreaterThan(0),
                            "An EMSP that said nothing while starting up cannot show this.");

                Assert.That(EMSP.Log.Recent(100).Select(entry => entry.Timestamp),
                            Is.All.EqualTo(clock.Now),
                            "Something was logged against a clock other than the EMSP's own.");

            });

        }

        #endregion

        #region ASwitchedOffTimeClientScheduleNothing()

        /// <summary>
        /// The whole reason the fixtures write that section: switched off, no
        /// timer is put on the network at all.
        /// </summary>
        [Test]
        public async Task ASwitchedOffTimeClientSchedulesNothing()
        {

            await using var EMSP = TestEMSPs.New(directory, TestEMSPs.Offline);

            await EMSP.Start();

            Assert.Multiple(() => {

                Assert.That(EMSP.NTSEnabled, Is.False);

                Assert.That(EMSP.Log.Recent(200).Any(entry => entry.Message.Contains("not being checked")),
                            Is.True,
                            "An EMSP with its time client switched off did not say that it is not checking its clock.");

                Assert.That(EMSP.Log.Recent(200).Any(entry => entry.Message.Contains("will be checked against")),
                            Is.False,
                            "An EMSP with its time client switched off scheduled a check anyway.");

            });

            await EMSP.Stop();

        }

        #endregion

    }

}
