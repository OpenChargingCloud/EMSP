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
using System.Net.Http.Headers;
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.Mail;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Web;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// Who may do what on an EMSP: its four resources beside the node's, its
    /// driver and its operator beside the node's viewer and administrators -
    /// and a role from the configuration file, heard by the API like every
    /// other.
    /// </summary>
    /// <remarks>
    /// The vehicle's tests of the same (EV c1d5fcf), with the EMSP's roles in
    /// them: each allowed exactly what it was allowed before roles were data.
    /// </remarks>
    public class EMSPAccessTests
    {

        #region Data

        private String?  directory;
        private EMSP?    emsp;
        private Uri?     address;

        #endregion

        #region Setup / TearDown

        [SetUp]
        public void Setup()
        {
            directory = TestEMSPs.TemporaryDirectory("access");
        }

        [TearDown]
        public async Task TearDown()
        {

            if (emsp is not null)
                await emsp.DisposeAsync();

            TestEMSPs.Remove(directory);

        }

        #endregion


        #region (helper) NewEMSP(Configuration = null)

        /// <summary>
        /// An EMSP with the given configuration file, on a free port of the
        /// loopback - made, and not yet started. Its time servers are never
        /// asked.
        /// </summary>
        private EMSP NewEMSP(JObject? Configuration = null)
        {

            var configuration = TestEMSPs.Offline;

            if (Configuration is not null)
                configuration.Merge(Configuration);

            emsp     = TestEMSPs.New(directory!, configuration);
            address  = new Uri(emsp.WebInterfaceURL.ToString());

            return emsp;

        }

        #endregion

        #region (helper) SignedInAs(Name, Role)

        /// <summary>
        /// A client signed in with a password as an account of the given name,
        /// made for the purpose and put in the group of the given role - made
        /// the way the EMSP makes its first one, in its one organization, so
        /// that it may sign in.
        /// </summary>
        private async Task<HttpClient> SignedInAs(String  Name,
                                                  String  Role)
        {

            var password = "correct-horse-battery-" + Guid.NewGuid().ToString("N")[..8];

            Assert.That(emsp!.ExtAPI.TryGetOrganization(Organization_Id.Parse(EMSP.DefaultOrganization), out var organization) &&
                        organization is Organization, Is.True, "the EMSP's organization is not there");

            var account = await emsp.ExtAPI.CreateUser(
                                    User_Id.Parse(Name),
                                    I18NString.Create(Languages.en, Name),
                                    SimpleEMailAddress.Parse($"{Name}@localhost"),
                                    User2OrganizationEdgeLabel.IsMember,
                                    (Organization) organization!,
                                    Password:                  password,
                                    SkipDefaultNotifications:  true,
                                    SkipNewUserEMail:          true,
                                    SkipNewUserNotifications:  true,
                                    AcceptedEULA:              DateTimeOffset.UtcNow.AddSeconds(-1),
                                    IsAuthenticated:           true
                                );

            Assert.That(account,                                                               Is.Not.Null, $"the account '{Name}' was not made");
            Assert.That(emsp.ExtAPI.TryGetUser(User_Id.Parse(Name), out var stored),           Is.True);
            Assert.That(emsp.ExtAPI.TryGetUserGroup(UserGroup_Id.Parse(Role), out var group),  Is.True, $"the EMSP has no group '{Role}'");

            var joined = await emsp.ExtAPI.AddUserToUserGroup((User) stored!, User2UserGroupEdgeLabel.IsMember, (UserGroup) group!);

            Assert.That(joined.IsSuccess, Is.True, $"'{Name}' could not be put in '{Role}'");

            var client = new HttpClient {
                             BaseAddress  = address,
                             Timeout      = TimeSpan.FromSeconds(30)
                         };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                             "Basic",
                                                             Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Name}:{password}"))
                                                         );

            return client;

        }

        #endregion

        #region (helper) Post(Client, Path, JSON)

        private static Task<HttpResponseMessage> Post(HttpClient  Client,
                                                      String      Path,
                                                      String      JSON)

            => Client.PostAsync(Path, new StringContent(JSON, Encoding.UTF8, "application/json"));

        #endregion

        #region (helper) EMSPAccessControl()

        /// <summary>
        /// The EMSP's resources and roles, as a node told nothing else puts
        /// them together.
        /// </summary>
        private static AccessControl EMSPAccessControl()
        {

            Assert.That(AccessControl.TryCombine(EMSPAccess.Resources, EMSPAccess.Roles, null, null,
                                                 "EMSP", out var access, out _, out var error),
                        Is.True, error);

            return access!;

        }

        #endregion


        #region AnEMSPKnowsItsResourcesAndItsFourRoles()

        /// <summary>
        /// The node brings the viewer and the administrators, and the EMSP its
        /// driver and its operator - by the names they always had, so that the
        /// groups an EMSP made before are the ones it finds.
        /// </summary>
        [Test]
        public void AnEMSPKnowsItsResourcesAndItsFourRoles()
        {

            var node = NewEMSP();

            Assert.Multiple(() => {
                Assert.That(node.Roles,             Is.EqualTo(new[] { "viewer", "driver", "emsp", WWCPNode.AdminRole }));
                Assert.That(node.Access.Resources,  Is.EqualTo(new[] { "configuration", "dns", "nts", "certificates", "ssh", "ocpi", "partners", "tokens", "contracts" }));
            });

        }

        #endregion

        #region EachRoleMayDoWhatItAlwaysMayDo(Role, Permission, Allowed)

        /// <summary>
        /// What each role could do before roles were data, permission by
        /// permission: the viewer looks, the driver asks for contracts of its
        /// own and sees nothing else, the operator runs the name and time
        /// servers, the tokens and everybody's contracts, and only the
        /// administrators touch the partners, the certificates and the SSH
        /// server.
        /// </summary>
        [TestCase("viewer",       "configuration:read",  true)]
        [TestCase("viewer",       "dns:read",            true)]
        [TestCase("viewer",       "ocpi:read",           true)]
        [TestCase("viewer",       "partners:read",       true)]
        [TestCase("viewer",       "tokens:read",         true)]
        [TestCase("viewer",       "certificates:read",   true)]
        [TestCase("viewer",       "ssh:read",            true)]
        [TestCase("viewer",       "dns:edit",            false)]
        [TestCase("viewer",       "dns:run",             false)]
        [TestCase("viewer",       "tokens:edit",         false)]
        [TestCase("viewer",       "contracts:run",       false)]
        [TestCase("viewer",       "contracts:edit",      false)]
        [TestCase("viewer",       "ssh:edit",            false)]

        [TestCase("driver",       "contracts:run",       true)]
        [TestCase("driver",       "configuration:read",  false)]
        [TestCase("driver",       "dns:read",            false)]
        [TestCase("driver",       "ocpi:read",           false)]
        [TestCase("driver",       "tokens:read",         false)]
        [TestCase("driver",       "contracts:edit",      false)]
        [TestCase("driver",       "certificates:read",   false)]
        [TestCase("driver",       "ssh:read",            false)]

        [TestCase("emsp",         "configuration:read",  true)]
        [TestCase("emsp",         "dns:edit",            true)]
        [TestCase("emsp",         "dns:run",             true)]
        [TestCase("emsp",         "nts:edit",            true)]
        [TestCase("emsp",         "nts:run",             true)]
        [TestCase("emsp",         "tokens:edit",         true)]
        [TestCase("emsp",         "contracts:edit",      true)]
        [TestCase("emsp",         "certificates:read",   true)]
        [TestCase("emsp",         "ssh:read",            true)]
        [TestCase("emsp",         "partners:edit",       false)]
        [TestCase("emsp",         "partners:run",        false)]
        [TestCase("emsp",         "contracts:run",       false)]
        [TestCase("emsp",         "certificates:edit",   false)]
        [TestCase("emsp",         "ssh:edit",            false)]

        [TestCase("systemadmin",  "partners:edit",       true)]
        [TestCase("systemadmin",  "partners:run",        true)]
        [TestCase("systemadmin",  "contracts:run",       true)]
        [TestCase("systemadmin",  "certificates:edit",   true)]
        [TestCase("systemadmin",  "ssh:edit",            true)]
        public void EachRoleMayDoWhatItAlwaysMayDo(String Role, String Permission, Boolean Allowed)
        {

            Assert.That(protocols.WWCP.Node.Web.Permission.TryParse(Permission, out var permission, out var error), Is.True, error);

            Assert.That(EMSPAccessControl().RoleNamed(Role)!.Allows(permission.Resource, permission.Operation), Is.EqualTo(Allowed));

        }

        #endregion


        #region AnOperatorLooksAfterTheTokensAndIsToldWhoMayAddAPartner()

        /// <summary>
        /// Over the wire, as a browser signed in as the operator sees it: the
        /// partners are listed, adding one is refused with the role to ask
        /// for, and what the browser is told it may do says the same
        /// beforehand.
        /// </summary>
        [Test]
        public async Task AnOperatorLooksAfterTheTokensAndIsToldWhoMayAddAPartner()
        {

            await TestPorts.StartedOnFreshPorts(() => NewEMSP());

            using var operatorOf  = await SignedInAs("operator1", "emsp");

            var looked            = await operatorOf.GetAsync("api/v1/ocpi/partners");
            var added             = await Post(operatorOf, "api/v1/ocpi/partners", "{}");
            var refusal           = await added.Content.ReadAsStringAsync();
            var me                = JObject.Parse(await (await operatorOf.GetAsync("api/v1/auth/me")).Content.ReadAsStringAsync());
            var permissions       = me["permissions"]!.Values<String>().OfType<String>().ToArray();

            Assert.Multiple(() => {
                Assert.That(looked.StatusCode,               Is.EqualTo(HttpStatusCode.OK));
                Assert.That(added.StatusCode,                Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(refusal,                         Does.Contain("This needs the systemadmin role."));
                Assert.That(me["roles"]!.Values<String>(),   Is.EqualTo(new[] { "emsp" }));
                Assert.That(permissions,                     Does.Contain("tokens:edit").And.Contain("contracts:edit").And.Contain("partners:read"));
                Assert.That(permissions,                     Does.Not.Contain("partners:edit").And.Not.Contain("certificates:edit").And.Not.Contain("ssh:edit"));
                Assert.That(permissions.Any(permission => permission.StartsWith('*')),
                            Is.False,
                            "spelt out resource by resource, so that a page asking \"partners:read\" need not know what \"*\" is");
            });

        }

        #endregion

        #region ARoleFromTheConfigurationFileIsHeardByTheAPI()

        /// <summary>
        /// A role nobody compiled in: the file names it, the start makes its
        /// group, and a route asking for a permission lets it in or not by what
        /// the file says it carries.
        /// </summary>
        /// <remarks>
        /// Before roles were data, a "roles" section stopped this EMSP's start:
        /// it named its roles and enforced them itself, and so could not hear
        /// one it had never been told about.
        /// </remarks>
        [Test]
        public async Task ARoleFromTheConfigurationFileIsHeardByTheAPI()
        {

            var configuration = new JObject(
                                    new JProperty("roles", new JObject(
                                        new JProperty("support", new JArray("dns:read"))
                                    ))
                                );

            await TestPorts.StartedOnFreshPorts(() => NewEMSP(configuration));

            using var support  = await SignedInAs("supporter", "support");

            var dns            = await support.GetAsync("api/v1/configuration/dns");
            var nts            = await support.GetAsync("api/v1/configuration/nts");
            var refusal        = await nts.Content.ReadAsStringAsync();

            Assert.Multiple(() => {
                Assert.That(emsp!.Roles,       Is.EqualTo(new[] { "viewer", "driver", "emsp", "support", WWCPNode.AdminRole }));
                Assert.That(dns.StatusCode,    Is.EqualTo(HttpStatusCode.OK));
                Assert.That(nts.StatusCode,    Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(refusal,           Does.Contain("This needs the viewer or emsp or systemadmin role."),
                            "the file's role carries dns:read and nothing else, so it is not among the ones to ask for");
            });

        }

        #endregion

        #region TheLogAndTheClockAreTheOperatorsAndNotTheDrivers()

        /// <summary>
        /// The node's JSON API asks what an EMSP asks of its log, its event
        /// stream and its clock: an operator reads them, a driver is refused -
        /// and the clock is at /api/v1/clock, where every node has it, with its
        /// old path a JSON 404.
        /// </summary>
        /// <remarks>
        /// A node asks nothing beyond a sign-in for these. An EMSP's accounts
        /// include its customers, and the log and the clock are the operator's,
        /// which the EMSP says through ToReadTheLog and ToReadTheClock since
        /// its API is the node's.
        /// </remarks>
        [Test]
        public async Task TheLogAndTheClockAreTheOperatorsAndNotTheDrivers()
        {

            await TestPorts.StartedOnFreshPorts(() => NewEMSP());

            using var operatorOf  = await SignedInAs("operator1", "emsp");
            using var driver      = await SignedInAs("driver1",   "driver");

            var clock             = await operatorOf.GetAsync("api/v1/clock");
            var logs              = await operatorOf.GetAsync("api/v1/logs");
            var oldClock          = await operatorOf.GetAsync("api/v1/configuration/time");
            var oldSaid           = await oldClock.Content.ReadAsStringAsync();

            var driversClock      = await driver.GetAsync("api/v1/clock");
            var driversLogs       = await driver.GetAsync("api/v1/logs");
            var driversEvents     = await driver.GetAsync("api/v1/events", HttpCompletionOption.ResponseHeadersRead);

            Assert.Multiple(() => {
                Assert.That(clock.StatusCode,          Is.EqualTo(HttpStatusCode.OK));
                Assert.That(logs.StatusCode,           Is.EqualTo(HttpStatusCode.OK));
                Assert.That(oldClock.StatusCode,       Is.EqualTo(HttpStatusCode.NotFound),   "the clock's old path");
                Assert.That(oldSaid,                   Does.Contain("Unknown API path"));
                Assert.That(driversClock.StatusCode,   Is.EqualTo(HttpStatusCode.Forbidden),  "a driver is refused the clock");
                Assert.That(driversLogs.StatusCode,    Is.EqualTo(HttpStatusCode.Forbidden),  "and the log");
                Assert.That(driversEvents.StatusCode,  Is.EqualTo(HttpStatusCode.Forbidden),  "and its event stream");
            });

        }

        #endregion

    }

}
