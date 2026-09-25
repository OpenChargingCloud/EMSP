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

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Norn.NTS;

using cloud.charging.open.EMSP.Configuration;
using cloud.charging.open.EMSP.Web;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.Logging;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// One e-mobility service provider: the OCPI endpoints its roaming partners
    /// call, the HTTP server in front of them, the JSON API at "/api" and the
    /// web interface at "/".
    /// </summary>
    /// <remarks>
    /// The web interface is a bundle of HTML, CSS and JavaScript built by
    /// webpack from Frontend/ and embedded into this assembly, so that the
    /// EMSP is one file to deploy and needs nothing installed beside it. The
    /// browser and the EMSP talk over the JSON API and one Server-Sent Events
    /// stream; nothing is rendered on the server.
    ///
    /// An EMSP is the other end of every roaming agreement: the charge point
    /// operators it is peered with push their locations, tariffs, sessions and
    /// charge detail records into it, ask it whether a customer's token may
    /// charge, and take commands from it. That is what this web interface is
    /// for: it is the one place where somebody can see which partners are
    /// registered, what they sent, and which of the customers' tokens are out
    /// there - without reading a log file over somebody else's shoulder.
    ///
    /// What an EMSP has in common with a vehicle and a charging station - the
    /// log, the configuration file, the name and time servers and the clock's
    /// check, the diagnostics, the certificate store, the accounts and the
    /// HTTP server with the web interface in front - is the WWCP node's below
    /// it. What is the EMSP's own is OCPI, the contracts, its sections of the
    /// one configuration file, its roles and its JSON API.
    /// </remarks>
    public partial class EMSP : WWCPNode
    {

        #region Data

        /// <summary>
        /// The manifest resource prefix of the embedded frontend bundle
        /// (see the EmbedFrontend target of EMSP.csproj).
        /// </summary>
        public const String  HTTPRoot            = "cloud.charging.open.EMSP.HTTPRoot.";

        /// <summary>
        /// The TCP port the web interface listens on, unless another is given.
        /// </summary>
        /// <remarks>
        /// Beyond the ports the other OpenChargingCloud boxes use - a vehicle
        /// 2347, a charging station 2348 and 2349, a local controller 2350, a
        /// CSMS 2351 and its OCPP ports up to 2354 - and not one of them: all
        /// of these are routinely tried out on the same bench, and two web
        /// interfaces fighting over one socket is a confusing way to find that
        /// out.
        /// </remarks>
        public static new readonly IPPort DefaultHTTPPort = IPPort.Parse(2355);

        /// <summary>
        /// The organization the accounts belong to.
        /// </summary>
        /// <remarks>
        /// An EMSP on a bench has no organizations to speak of, and this one
        /// exists because the HTTPExt API's sign-in refuses an account that is
        /// in none - "You do not have access to any organization!" - however
        /// right its password is. So there is exactly one, named after the
        /// thing it stands for. It is written into the accounts at the first
        /// start and read back at every start after it, and must never change.
        /// </remarks>
        public const String  DefaultOrganization = "EMSP";

        /// <summary>
        /// What a line the libraries below write has to contain to be tagged,
        /// and with what: the table the debug bridge of an EMSP reads by.
        /// </summary>
        /// <remarks>
        /// What an EMSP overhears is mostly OCPI: what the roaming partners
        /// push and fetch, the peering with them, and the endpoints it serves
        /// them - so the table names the modules of OCPI, as the Logs page
        /// filters by them, and the two sides of it. The table the EMSP's own
        /// bridge had, word for word; none of the vehicle's ISO 15118 and SLAC,
        /// which an EMSP never hears.
        /// </remarks>
        public static readonly IReadOnlyList<(String Needle, String Tag)> TraceTags = [
            ("ocpi",           "ocpi"),
            ("credentials",    "credentials"),
            ("versions",       "versions"),
            ("location",       "locations"),
            ("evse",           "locations"),
            ("tariff",         "tariffs"),
            ("session",        "sessions"),
            ("cdr",            "cdrs"),
            ("charge detail",  "cdrs"),
            ("token",          "tokens"),
            ("command",        "commands"),
            ("remote party",   "partner"),
            ("remoteparty",    "partner"),
            ("websocket",      "websocket"),
            ("http",           "http"),
            ("tls",            "tls"),
            ("certificate",    "tls"),
            ("dns",            "dns"),
            ("nts",            "nts"),
            ("ntp",            "nts"),
            ("emsp",           "emsp"),
            ("cpo",            "cpo")
        ];

        #endregion

        #region Properties

        /// <summary>
        /// The JSON API at "/api/".
        /// </summary>
        public EMSPHTTPAPI  API    { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create an EMSP with a web interface in front of it.
        /// Nothing listens yet: <see cref="WWCPNode.Start"/> does.
        /// </summary>
        /// <param name="DNSClient">The DNS client used by everything below.</param>
        /// <param name="NTSClient">The time client.</param>
        /// <param name="HTTPServer">An HTTP server to register within, or null to make one.</param>
        /// <param name="BasePath">What everything of this EMSP sits below; the root by default. Something else only where several of these programs share one HTTP server.</param>
        /// <param name="HTTPRootPath">The root path of the JSON API, "/api" below <paramref name="BasePath"/> by default.</param>
        /// <param name="ExtAPI">An HTTPExt API to sign in against, or null for one of this EMSP's own. Handing one in is what makes one sign-in open several of these programs at once.</param>
        /// <param name="AccountsPath">The directory the accounts live in between starts.</param>
        /// <param name="HTTPHostname">The address to listen on; the loopback address by default.</param>
        /// <param name="HTTPPort">The TCP port to listen on; <see cref="DefaultHTTPPort"/> by default.</param>
        /// <param name="ConfigFile">Where everything this EMSP can be told in writing lives: one file, whose sections the node below and the EMSP each read for themselves; "configuration.json" beside the process by default.</param>
        /// <param name="OCPI">Who this EMSP is in OCPI, unless the configuration file says otherwise.</param>
        /// <param name="Frontend">Where the web interface comes from; the bundle embedded in this assembly by default.</param>
        /// <param name="CertificatesPath">The directory the certificate store of the node below lives in between starts; what the file says, or "certificates" beside it, by default.</param>
        /// <param name="Log">The event log; a new one by default.</param>
        /// <param name="LogToConsole">Whether the event log is also written to the console.</param>
        /// <param name="ConsoleLogLevel">What the console shows of it.</param>
        /// <param name="LogPath">The directory the log files are written to, or null to write none.</param>
        /// <param name="BridgeDebugLog">Whether what the libraries below write with DebugX ends up in the log.</param>
        /// <param name="TimeProvider">Where this EMSP reads the time; the system clock by default.</param>
        public EMSP(DNSClient?             DNSClient          = null,
                    NTSClient?             NTSClient          = null,
                    HTTPServer?            HTTPServer         = null,
                    HTTPPath?              BasePath           = null,
                    HTTPPath?              HTTPRootPath       = null,
                    HTTPExtAPI?            ExtAPI             = null,
                    String?                AccountsPath       = null,
                    IIPAddress?            HTTPHostname       = null,
                    IPPort?                HTTPPort           = null,
                    WWCPConfigFile?        ConfigFile         = null,
                    OCPIConfiguration?     OCPI               = null,
                    IStaticContentSource?  Frontend           = null,
                    String?                CertificatesPath   = null,
                    EventLog?              Log                = null,
                    Boolean                LogToConsole       = true,
                    LogLevel               ConsoleLogLevel    = LogLevel.Info,
                    String?                LogPath            = null,
                    Boolean                BridgeDebugLog     = true,
                    TimeProvider?          TimeProvider       = null)

            // Every name as it was before there was a node below: the entries
            // about the EMSP itself are tagged "emsp", its first line is "EMSP
            // v... starting up" and its last "The EMSP is shutting down.", the
            // Server header says "OpenChargingCloud EMSP", and a day's log file
            // is "emsp-2026-09-25.log". The organization is written into the
            // accounts at the first start and read back at every start after
            // it, and must never change at all.
            : base(Kind:              new NodeKind(
                                          Name:           "EMSP",
                                          Tag:            "emsp",
                                          Product:        "EMSP",
                                          Organization:   DefaultOrganization,
                                          LogFilePrefix:  "emsp"
                                      ),
                   Version:           typeof(EMSP).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                   HTTPPort:          HTTPPort ?? DefaultHTTPPort,
                   HTTPHostname:      HTTPHostname,
                   HTTPServer:        HTTPServer,
                   BasePath:          BasePath,
                   HTTPRootPath:      HTTPRootPath,
                   ExtAPI:            ExtAPI,
                   AccountsPath:      AccountsPath,
                   Roles:             UserRole.All.Select(role => role.Name),
                   ConfigFile:        ConfigFile,
                   DNSClient:         DNSClient,
                   NTSClient:         NTSClient,
                   Frontend:          Frontend ?? new EmbeddedContentSource(HTTPRoot, typeof(EMSP).Assembly),
                   CertificatesPath:  CertificatesPath,
                   Log:               Log,
                   LogToConsole:      LogToConsole,
                   ConsoleLogLevel:   ConsoleLogLevel,
                   LogPath:           LogPath,
                   BridgeDebugLog:    BridgeDebugLog,
                   TraceTags:         TraceTags,
                   TimeProvider:      TimeProvider)

        {

            // "this." throughout, and not for tidiness: the parameters of this
            // constructor shadow the properties of the same name, and a
            // parameter such as "Log" or "ConfigFile" is null whenever the
            // caller did not bring one of its own.

            #region What the configuration file says about an EMSP

            // Its own sections of the document the node below has already
            // read: the ones that reading passed over are the ones this is
            // for. A file that is there but cannot be read has stopped the
            // node before this line; a section of it that is wrong stops the
            // EMSP here, for the same reason - somebody wrote down what their
            // EMSP is and got it wrong, and quietly running as something else
            // instead would be worse than stopping.
            if (!EMSPConfiguration.TryParse(ConfigurationDocument, out var configuration, out var problem))
                throw new InvalidOperationException($"'{this.ConfigFile.Path}': {problem} Repair or remove '{this.ConfigFile.Path}' and start again.");

            if (!configuration.IsEmpty)
                this.Log.Info($"EMSP configuration from '{this.ConfigFile.Path}': {configuration}.", "config");

            #endregion

            #region The accounts, and the JSON API

            this.Log.Info(
                OwnsExtAPI
                    ? $"The HTTPExt API is at '{this.ExtAPI.RootPath}', its accounts in '{this.ExtAPI.DatabaseFileName}'."
                    : $"This EMSP signs in against accounts it shares, at '{this.ExtAPI.RootPath}'.",
                "web", "http"
            );

            // The JSON API at "/api", within the server the node made or was
            // handed: below the node's HTTPRootPath, and so the more specific
            // of it and the web interface, which is the catch-all.
            this.API           = new EMSPHTTPAPI(
                                     HTTPServer:  this.HTTPServer,
                                     EMSP:        this,
                                     ExtAPI:      this.ExtAPI,
                                     Log:         this.Log,
                                     APIPath:     this.HTTPRootPath,
                                     Version:     this.Version
                                 );

            #endregion

            #region The OCPI endpoints the roaming partners call

            // After the HTTPExt API, because they hang off it, and after the
            // configuration file, because who this EMSP is in OCPI is written
            // there. They share the HTTP server above rather than opening a
            // port of their own: OCPI is plain HTTP, and one EMSP is one
            // address to point a partner at.
            BuildOCPI(configuration.OCPI ?? OCPI);

            #endregion

            #region The contracts: the MO root, the registry and the sign-up

            // After OCPI, because the authority's subjects carry who this EMSP
            // is in OCPI; and after the HTTPExt API, because the sign-up hangs
            // off it. See EMSP.Contracts.cs.
            BuildContracts(configuration.Contracts);

            #endregion

        }

        #endregion


        #region (protected override) OnStarted()

        /// <summary>
        /// Where the JSON API is, and where the roaming partners find this EMSP,
        /// once the web interface is listening.
        /// </summary>
        protected override Task OnStarted()
        {

            Log.Info   ($"The JSON API is at {APIURL}v1/status", "web", "http");
            Log.Notice ($"Roaming partners find this EMSP at {OCPIVersionsURL} " +
                        $"(OCPI {String.Join(", ", OCPIVersions.Select(version => version.Label))}, " +
                        $"{RemotePartyCount} partner(s), {TokenCount} token(s)).",
                        "ocpi");

            return Task.CompletedTask;

        }

        #endregion

        #region (protected override) OnStopping()

        /// <summary>
        /// End the event streams before the server stops.
        /// </summary>
        /// <remarks>
        /// Before the server, and that order is the whole point: every browser
        /// with the Logs page open holds a request that is waiting for the next
        /// log entry rather than for its socket, and the HTTP server waits for
        /// every request it started. Closing the sockets does not wake those,
        /// so they are ended here first - whoever owns the server, because the
        /// streams are this EMSP's.
        /// </remarks>
        protected override Task OnStopping()
        {

            API.CloseEventStreams();

            return Task.CompletedTask;

        }

        #endregion


        #region ConfigurationJSON()

        /// <summary>
        /// What this EMSP is made of, as the Configuration page of the web
        /// interface reads it: the node's cards, and the EMSP's on top.
        /// </summary>
        /// <remarks>
        /// Read-only: it answers "what am I running", not "change it". Nothing
        /// here is a secret - the accounts appear as the path they live at and
        /// the route to sign in, and never as anything about a password; the
        /// roaming partners appear as a count, and never as their tokens.
        /// </remarks>
        public override JObject ConfigurationJSON()
        {

            var json = base.ConfigurationJSON();

            // First, because it is the card the page leads with.
            json.AddFirst(new JProperty("EMSP",       new JObject(
                              new JProperty("version",        Version),
                              new JProperty("createdAt",      CreatedAt.ToString("o")),
                              new JProperty("machine",        Environment.MachineName),
                              new JProperty("runtime",        Environment.Version.ToString()),
                              new JProperty("os",             Environment.OSVersion.ToString())
                          )));

            json.Add(new JProperty("ocpi",       new JObject(
                         new JProperty("role",             "EMSP"),
                         new JProperty("partyId",          PartyIdText),
                         new JProperty("countryCode",      PartyId.CountryCode.ToString()),
                         new JProperty("party",            PartyId.PartyId.ToString()),
                         new JProperty("name",             BusinessDetails.Name),
                         new JProperty("website",          BusinessDetails.Website?.ToString()),
                         new JProperty("versions",         new JArray(OCPIVersions.Select(version => version.Label))),
                         new JProperty("versionsURL",      OCPIVersionsURL.ToString()),
                         new JProperty("partners",         RemotePartyCount),
                         new JProperty("tokens",           TokenCount),
                         new JProperty("file",             ConfigFile.Path)
                     )));

            json.Add(new JProperty("contracts",  new JObject(
                         new JProperty("moRoot",           ContractCA.RootSubject),
                         new JProperty("moRootFingerprint",ContractCA.RootFingerprint),
                         new JProperty("moRootNotAfter",   ContractCA.RootNotAfter.ToString("o")),
                         new JProperty("moRootFile",       ContractCA.RootTrustPath),
                         new JProperty("contracts",        Contracts.Count),
                         new JProperty("validityDays",     (UInt32) ContractValidity.TotalDays),
                         new JProperty("selfSignUp",       SelfSignUpEnabled),
                         new JProperty("signUpURL",        SignUpURL.ToString()),
                         new JProperty("directory",        PKIDirectory)
                     )));

            json.Add(new JProperty("assemblies", new JArray(
                         AssemblyJSON<HTTPServer>                                  ("Hermod"),
                         AssemblyJSON<NTSClient>                                   ("Norn"),
                         AssemblyJSON<WWCPNode>                                    ("WWCP Node"),
                         AssemblyJSON<protocols.OCPI.CommonHTTPAPI>                ("OCPI"),
                         AssemblyJSON<protocols.OCPIv2_1_1.CommonAPI>              ("OCPI 2.1.1"),
                         AssemblyJSON<protocols.OCPIv2_2_1.CommonAPI>              ("OCPI 2.2.1"),
                         AssemblyJSON<protocols.OCPIv2_3_0.CommonAPI>              ("OCPI 2.3.0")
                     )));

            return json;

        }

        #endregion


        #region (private static) AssemblyJSON<T>(Name)

        private static JObject AssemblyJSON<T>(String Name)
        {

            var assembly = typeof(T).Assembly.GetName();

            return new JObject(
                       new JProperty("name",      Name),
                       new JProperty("assembly",  assembly.Name),
                       new JProperty("version",   assembly.Version?.ToString(3))
                   );

        }

        #endregion

    }

}
