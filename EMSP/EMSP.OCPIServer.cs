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

using System.Net.Sockets;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.SSH;
using org.GraphDefined.Vanaheimr.Hermod.WebSocket;

using cloud.charging.open.EMSP.Configuration;

using cloud.charging.open.protocols.WWCP.SSH;
using cloud.charging.open.protocols.WWCP.WebSockets;
using cloud.charging.open.protocols.WWCP.NetworkingNode;
using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Certificates;
using cloud.charging.open.protocols.WWCP.Node.Connections;
using cloud.charging.open.protocols.WWCP.Node.Logins;
using cloud.charging.open.protocols.WWCP.Node.SecureShell;

using cloud.charging.open.protocols.OCPI.Transport;

using ConnectionType = cloud.charging.open.protocols.WWCP.Node.Connections.ConnectionType;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// The CPOs that connect to this EMSP to speak OCPI over a WebSocket or over
    /// SSH, rather than over HTTP: a WebSocket server of a port of its own, and
    /// the SSH subsystem "ocpi@open.charging.cloud" of the node's SSH server -
    /// and the CPOs this EMSP connects to the same ways.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A CPO that connects is written down as a connection</b> - of the type
    /// "CPO", one the other end makes, of the version of OCPI it speaks - in
    /// the store of connections beside the configuration file, as a node of
    /// OCPP writes down the CSMS that connects to it. Nothing that is not
    /// written down gets in.
    /// </para>
    /// <para>
    /// <b>A CPO this EMSP connects to is a connection of the same type</b>, made
    /// by this EMSP: by its networking node of OCPI, over a WebSocket of the
    /// subprotocols of its version or over the subsystem of OCPI - to the
    /// station port of a CSMS, say, which hands it to its servers of OCPI.
    /// Connected once this EMSP is up, and connected again as every node
    /// connects again.
    /// </para>
    /// <para>
    /// <b>Who signs in is decided as at the station port of a node of OCPP</b>,
    /// by the very same checks (<see cref="WebSocketSignIns"/>) and the same
    /// kind of logins (<see cref="WebSocketLogins"/>): a password, a one-time
    /// password, a TLS client certificate of a chain this EMSP accepts or the
    /// key pin of one - and over SSH a password, a one-time password bound to
    /// the session or an SSH key (<see cref="LoginSSHSignIns"/>).
    /// </para>
    /// <para>
    /// <b>One server per version of OCPI</b>, as a node of OCPP has one per
    /// version: the first listens and hands every WebSocket to the one of the
    /// subprotocol it asks for, and the subsystem hands a HELLO to the one of
    /// its subprotocol. A connection is let in at the server of its own version,
    /// and at no other. For now the line is held, and nothing goes over it:
    /// what OCPI says over it is what comes next.
    /// </para>
    /// </remarks>
    public partial class EMSP
    {

        #region Data

        /// <summary>
        /// The listener of the node's certificate store the WebSocket port of
        /// OCPI is: what it presents is a TLS server identity told "ocpi".
        /// </summary>
        public const String WebSocketListener = "ocpi";

        /// <summary>
        /// What OCPI over SSH is called in what the SSH server says.
        /// </summary>
        public const String OCPIOverSSHLabel  = "OCPI";

        /// <summary>
        /// What the port is to the log, where it can not be had.
        /// </summary>
        public static readonly NodePort  OCPIServerPort  = new ("The WebSocket server of OCPI");

        /// <summary>
        /// What the door of the WebSocket port calls the port and who comes to it.
        /// </summary>
        public static readonly WebSocketSignInWords CPOWords = new () {
                                                                   Port            = "the WebSocket port of OCPI",
                                                                   Who             = "A CPO",
                                                                   PinnedLogin     = "the CPO",
                                                                   Realm           = "OCPI",
                                                                   Tags            = [ "ocpi", "cpo" ],
                                                                   InboundTags     = [ "ocpi", "cpo" ]
                                                               };

        private OCPIServerConfiguration                        ocpiServerSettings  = new OCPIServerConfiguration().Effective();

        private OCPINetworkingNode?                            ocpiNetworkingNode;

        /// <summary>
        /// The WebSocket servers of OCPI, by the version they speak: the first
        /// of them listens.
        /// </summary>
        private readonly List<(String Version, WWCPWebSocketServer Server)>  ocpiServers     = [];

        /// <summary>
        /// The servers of OCPI over SSH, by the version they speak.
        /// </summary>
        private readonly List<(String Version, WWCPSshServer Server)>        ocpiSshServers  = [];

        private LoginSSHSignIns?                               cpoSSHSignIns;

        private volatile Boolean                               ocpiServerStarted;
        private volatile Boolean                               ocpiServerTLS;
        private volatile Boolean                               ocpiOverSSHServed;

        /// <summary>
        /// Whether this EMSP connects to its CPOs: from when it started until it stops.
        /// </summary>
        private volatile Boolean                               connectsToCPOs;

        #endregion

        #region Properties

        /// <summary>
        /// Which CPOs may sign in, and with what.
        /// </summary>
        public WebSocketLogins          WebSocketLogins         { get; private set; } = default!;

        /// <summary>
        /// Which chains the certificate of a CPO may lead to.
        /// </summary>
        public ClientTrustStore         ClientTrust             { get; private set; } = default!;

        /// <summary>
        /// Who comes in at the WebSocket port of OCPI: the sign-in checks of its door.
        /// </summary>
        public WebSocketSignIns         OCPISignIns             { get; private set; } = default!;

        /// <summary>
        /// What the WebSocket port of OCPI presents, out of the node's store:
        /// the listener "ocpi" - see <see cref="WebSocketListener"/>.
        /// </summary>
        public ListenerCertificate      OCPIServerCertificate   { get; private set; } = default!;

        /// <summary>
        /// The networking node of OCPI of this EMSP: what a CPO connects to over
        /// a WebSocket or over SSH.
        /// </summary>
        public OCPINetworkingNode       OCPINetworkingNode
            => ocpiNetworkingNode ?? throw new InvalidOperationException("The networking node of OCPI of this EMSP is not built yet.");

        /// <summary>
        /// What the WebSocket server of OCPI is configured as, with every field
        /// filled in.
        /// </summary>
        public OCPIServerConfiguration  OCPIServerSettings
            => ocpiServerSettings;

        /// <summary>
        /// Whether the WebSocket server of OCPI is listening right now.
        /// </summary>
        public Boolean                  OCPIServerRunning
            => ocpiServerStarted;

        /// <summary>
        /// Whether the WebSocket port of OCPI speaks TLS, as decided when it started.
        /// </summary>
        public Boolean                  OCPIServerTLS
            => ocpiServerTLS;

        /// <summary>
        /// Whether a CPO may connect over the SSH subsystem of OCPI right now.
        /// </summary>
        public Boolean                  OCPIOverSSHServed
            => ocpiOverSSHServed;

        /// <summary>
        /// The names of the CPOs connected to this EMSP right now, over a
        /// WebSocket or over SSH, in order.
        /// </summary>
        public IReadOnlyList<String>    CPOsIn
        {
            get
            {

                var states = ConnectionStates;

                return [.. Connections.Connections.
                               Where  (entry => entry.Inbound &&
                                                entry.ConnectionType == ConnectionType.CPO &&
                                                entry.InboundName is not null &&
                                                states.TryGetValue(entry.Id, out var state) &&
                                                state.Status == ConnectionStatus.Connected).
                               Select (entry => entry.InboundName!).
                               Distinct(StringComparer.Ordinal).
                               Order  (StringComparer.Ordinal)];

            }
        }

        /// <summary>
        /// The WebSocket servers of OCPI, one per version: the first listens.
        /// </summary>
        public IEnumerable<WWCPWebSocketServer>  OCPIServers
        {
            get
            {
                lock (ocpiServers)
                    return [.. ocpiServers.Select(served => served.Server)];
            }
        }

        /// <summary>
        /// The servers of OCPI over SSH, one per version.
        /// </summary>
        public IEnumerable<WWCPSshServer>        OCPISshServers
        {
            get
            {
                lock (ocpiSshServers)
                    return [.. ocpiSshServers.Select(served => served.Server)];
            }
        }

        #endregion


        #region (private) BuildOCPIServer(Configuration)

        /// <summary>
        /// The stores of who connects to this EMSP, the networking node of OCPI,
        /// and its servers: over a WebSocket and over SSH. Nothing listens yet.
        /// </summary>
        /// <remarks>
        /// Everything lives beside the configuration file, as the stores of a
        /// node of OCPP do.
        /// </remarks>
        /// <param name="Configuration">The "webSocketServer" section of the file, where it says anything.</param>
        private void BuildOCPIServer(OCPIServerConfiguration? Configuration)
        {

            var directory = Path.GetDirectoryName(Path.GetFullPath(ConfigFile.Path)) ?? ".";

            ocpiServerSettings = (Configuration ?? new OCPIServerConfiguration()).Effective();

            #region The connections: CPOs that connect, over OCPI only

            Connections = new ConnectionStore(
                              Path.Combine(directory, ConnectionStore.DefaultDirectoryName),
                              TimeProvider,
                              NodeNoun
                          ) {

                              // A CPO, and nothing else: over OCPI, made by the
                              // other end or by this EMSP.
                              Rules = new ConnectionRules {
                                          Types                  = [ ConnectionType.CPO ],
                                          OCPP1_6                = false,
                                          NamesTheNodeItLeadsTo  = false
                                      }

                          };

            // A connection changed while this EMSP connects: what is up is let go
            // of and connected again as it is written now.
            Connections.OnConnectionsChanged += () => {
                                                    if (connectsToCPOs)
                                                        _ = ApplyConnectionChanges();
                                                };

            Connections.KnownCertificateIds  = () => Certificates.ByKind(CertificateKind.TLSIdentity).Select(entry => entry.Id);
            Connections.OnNotice            += (level, message) => Log.Log(level, message, "ocpi", "connections");

            // How the servers of the CPOs this EMSP connects to over wss:// are
            // believed by their key pins, where they are.
            keyPinning = NewKeyPinning();

            // What every node that connects has, for what it shares with them.
            SshKey = new SshClientKey(
                         Path.Combine(Connections.Path, SshClientKey.DirectoryName),
                         $"emsp@{Environment.MachineName}",
                         NodeNoun:  NodeNoun
                     );

            SshKey.OnNotice += (level, message) => Log.Log(level, message, "ocpi", "connections", "ssh", "security");

            #endregion

            #region What it presents, out of the node's store

            ListenerCertificates.KeepServingLast(WebSocketListener);

            OCPIServerCertificate = ListenerCertificates.For(WebSocketListener, "the WebSocket port of OCPI");

            #endregion

            #region The two stores of who signs in

            ClientTrust      = new ClientTrustStore(
                                   Path.Combine(directory, ClientTrustStore.DefaultDirectoryName),
                                   TimeProvider,
                                   NodeNoun,
                                   "CPO"
                               );

            ClientTrust.OnNotice += (level, message) => Log.Log(level, message, "ocpi", "tls", "trust");

            ClientTrust.KeepToItsOwner();

            WebSocketLogins  = new WebSocketLogins(
                                   Path.Combine(directory, WebSocketLogins.DefaultFileName),
                                   TimeProvider,
                                   NodeNoun
                               );

            WebSocketLogins.OnNotice += (level, message) => Log.Log(level, message, "ocpi", "cpo", "auth");

            if (!WebSocketLogins.TryLoad(out var loginsError))
                throw new InvalidOperationException($"{loginsError} Repair or remove it and start again.");

            WebSocketLogins.OnChanged += () => {

                foreach (var server in OCPIServers)
                    OCPISignIns.ApplyLogins(server);

                RecheckSSHSessions();

                FollowInboundConnections();

            };

            #endregion

            #region The networking node of OCPI

            ocpiNetworkingNode = new OCPINetworkingNode(
                                     NetworkingNode_Id.Parse($"{PartyId.CountryCode}-{PartyId.PartyId}"),
                                     I18NString.Create($"The networking node of OCPI of the EMSP {PartyIdText}"),
                                     DNSClient,
                                     TimeProvider
                                 );

            // What lets a CPO in is the networking node of OCPI: asked of a
            // connection of OCPI, it is that node.
            SetConnectingOf(ConnectionUse.OCPI,
                            new MessageConnecting(
                                this,
                                () => OCPINetworkingNode,
                                connection => OCPIWireNames.Offer(connection.OCPIVersion ?? "", connection.Encoding),
                                OCPIWireNames.SshSubsystem
                            ));

            #endregion

            #region The door of the WebSocket port

            OCPISignIns      = new WebSocketSignIns(
                                   Logins:               WebSocketLogins,
                                   ClientTrust:          ClientTrust,
                                   SignInBrake:          SignInBrake,
                                   Log:                  Log,
                                   Authentications:      () => ocpiServerSettings.ClientAuthentication ?? OCPIServerConfiguration.DefaultClientAuthentication,
                                   OverTLS:              () => ocpiServerTLS,
                                   ChecksRevocation:     () => ocpiServerSettings.CheckCertificateRevocation == true,
                                   SaysSignIns:          () => true,
                                   InboundConnectionOf:  (server, name) => VersionOf(server) is String version
                                                                               ? InboundConnectionOf(ProtocolVersion.OCPI(version), name)
                                                                               : null,
                                   LetInAsInbound:       LetInAsInbound,
                                   LetInOthers:          (connection, login, how, detail, byCertificate) =>
                                                             $"'{login}' is no CPO that connects to this EMSP: no connection is written down that comes under that name",
                                   Words:                CPOWords
                               );

            #endregion

            #region The servers, one per version of OCPI this EMSP speaks

            var versions = OCPIVersions.Select (version => version.Label).
                                        Where  (OCPIWireNames.Versions.Contains).
                                        ToArray();

            foreach (var version in versions)
            {

                var listens = ocpiServers.Count == 0;

                var server  = OCPINetworkingNode.AttachWebSocketServer(
                                  HTTPServiceName:             $"OpenChargingCloud {Kind.Product} v{Version}",
                                  IPAddress:                   ocpiServerSettings.Address,
                                  TCPPort:                     ocpiServerSettings.TCPPort,
                                  Description:                 I18NString.Create($"The CPOs of OCPI {version} of this {NodeNoun}"),

                                  // The door decides, see OCPISignIns: a CPO with a
                                  // certificate has no password to send.
                                  RequireAuthentication:       false,
                                  SecWebSocketProtocols:       OCPIWireNames.SubprotocolsOf(version),

                                  // The TLS of the port is the listening server's.
                                  ClientCertificateValidator:  listens ? OCPISignIns.ValidateCertificate : null,
                                  ClientCertificateRequired:   listens ? true                            : null,
                                  CheckCertificateRevocation:  ocpiServerSettings.CheckCertificateRevocation
                              );

                // A UDP port is opened by nobody who did not ask for it.
                server.AcceptHTTP3 = false;

                OCPISignIns.Guard(server);

                lock (ocpiServers)
                {

                    if (!listens)
                        ocpiServers[0].Server.RouteBySubprotocol(server);

                    ocpiServers.Add((version, server));

                }

                lock (ocpiSshServers)
                    ocpiSshServers.Add((version, OCPINetworkingNode.AttachSshServer(version)));

            }

            #endregion

            #region OCPI over SSH, on the node's SSH server

            cpoSSHSignIns = new LoginSSHSignIns(
                                WebSocketLogins,
                                OCPIWireNames.SshSubsystem,
                                () => ocpiOverSSHServed,
                                TimeProvider,
                                () => SSHSubsystems.Select(subsystem => subsystem.Name)
                            );

            cpoSSHSignIns.OnNotice += (level, message) => Log.Log(level, message, "ocpi", "cpo", "auth", "ssh");

            AddSSHSignIns(cpoSSHSignIns);

            #endregion

            Log.Info(
                ocpiServerSettings.IsEnabled
                    ? $"The WebSocket server of OCPI will listen on {ocpiServerSettings.Address}:{ocpiServerSettings.TCPPort}, " +
                      $"speaking {String.Join(", ", versions.SelectMany(OCPIWireNames.SubprotocolsOf))}" +
                      (ocpiServerSettings.TakesSSH ? $", and over the SSH subsystem {OCPIWireNames.SshSubsystem} as well." : ".")
                    : "The WebSocket server of OCPI is switched off; no CPO can connect over a WebSocket or over SSH.",
                "ocpi", "cpo"
            );

        }

        #endregion

        #region (private) StartOCPIServer() / StopOCPIServer()

        /// <summary>
        /// Start listening for CPOs, where this EMSP is meant to - and serve
        /// OCPI over SSH, where the section says so.
        /// </summary>
        private async Task StartOCPIServer()
        {

            if (ocpiServerStarted || !ocpiServerSettings.IsEnabled)
                return;

            WWCPWebSocketServer listening;

            lock (ocpiServers)
            {

                if (ocpiServers.Count == 0)
                {
                    Log.Warning("The WebSocket server of OCPI is switched on, and this EMSP speaks no version of OCPI that goes over it.", "ocpi", "cpo");
                    return;
                }

                listening = ocpiServers[0].Server;

            }

            var wantsTLS = (ocpiServerSettings.ClientAuthentication ?? OCPIServerConfiguration.DefaultClientAuthentication).Any(authentication => authentication.IsOverTLS());

            ocpiServerTLS = wantsTLS && OCPIServerCertificate.Choose() is not null;

            listening.ServerCertificateChainSelector = ocpiServerTLS
                                                           ? (server, client) => OCPIServerCertificate.Current
                                                           : null;

            if (wantsTLS && !ocpiServerTLS)
                Log.Warning(
                    "HTTP authentication over TLS or a TLS client certificate is configured, but there is no server certificate - so the " +
                    "WebSocket port of OCPI is unencrypted and only HTTP authentication without TLS can be used on it. Make a TLS server " +
                    $"identity for the listener '{WebSocketListener}'; the port encrypts from the next start.",
                    "ocpi", "cpo", "tls"
                );

            try
            {
                await listening.Start();
            }
            catch (SocketException problem)
            {
                throw new PortUnavailableException(
                          ocpiServerSettings.TCPPort ?? OCPIServerConfiguration.DefaultTCPPort,
                          problem,
                          OCPIServerPort
                      );
            }

            ocpiServerStarted = true;

            foreach (var (version, server) in ocpiServers.ToArray())
                ServeInbound(ProtocolVersion.OCPI(version), server);

            Log.Notice(
                $"The CPOs connect to {(ocpiServerTLS ? "wss" : "ws")}://{ocpiServerSettings.Address}:{ocpiServerSettings.TCPPort}/<their name> " +
                $"({(ocpiServerTLS ? "TLS" : "unencrypted")}, {WebSocketLogins.EnabledCount} login(s), {ClientTrust.EnabledCount} accepted chain(s)).",
                "ocpi", "cpo"
            );

            if (ocpiServerSettings.TakesSSH)
                ServeOCPIOverSSH();

        }

        /// <summary>
        /// Stop listening for CPOs: whoever is in is let go of.
        /// </summary>
        private async Task StopOCPIServer()
        {

            if (!ocpiServerStarted)
                return;

            ocpiServerStarted = false;

            StopServingOCPIOverSSH($"The WebSocket server of OCPI of this {NodeNoun} stopped.");

            var servers = ocpiServers.ToArray();

            foreach (var (version, _) in servers)
                StopServingInbound(ProtocolVersion.OCPI(version), $"The WebSocket server of OCPI of this {NodeNoun} stopped.");

            foreach (var (_, server) in servers)
                await server.Shutdown($"The {NodeNoun} is shutting down.");

            Log.Info("The WebSocket server of OCPI has stopped listening.", "ocpi", "cpo");

        }

        #endregion

        #region (private) ServeOCPIOverSSH() / StopServingOCPIOverSSH(Why)

        /// <summary>
        /// Serve OCPI over SSH from now on: the subsystem on the node's SSH
        /// server, each HELLO handed to the server of the version it asks for.
        /// </summary>
        private void ServeOCPIOverSSH()
        {

            if (ocpiOverSSHServed)
                return;

            var servers = ocpiSshServers.Select(served => served.Server).ToArray();

            if (servers.Length == 0)
                return;

            // Before the subsystem is there, so that a CPO asking for it the
            // moment it is finds itself let in.
            ocpiOverSSHServed = true;

            try
            {
                AddSSHSubsystem(new SSHSubsystem(
                                    OCPIWireNames.SshSubsystem,
                                    OCPIOverSSHLabel,
                                    servers.Length == 1
                                        ? servers[0].Handler
                                        : SshMessageServer.RoutedBySubprotocol([.. servers.Select(server => server.MessageServer)])
                                ));
            }
            catch (Exception e)
            {
                ocpiOverSSHServed = false;
                Log.Critical($"OCPI over SSH could not be served: {e.Message}", "ocpi", "cpo", "ssh");
                return;
            }

            foreach (var (version, server) in ocpiSshServers.ToArray())
                ServeInbound(ProtocolVersion.OCPI(version), server);

            if (!SSHEnabled)
                Log.Warning($"OCPI over SSH is switched on, but the SSH server of this {NodeNoun} is off - so no CPO can reach it over SSH.",
                            "ocpi", "cpo", "ssh");

            else
                Log.Notice($"The CPOs may connect over SSH as well: {SSHURL}, subsystem {OCPIWireNames.SshSubsystem}, host key {SSHHostKey ?? "not known yet"}.",
                           "ocpi", "cpo", "ssh");

        }

        /// <summary>
        /// Serve OCPI over SSH no longer: every CPO connected over it ends.
        /// </summary>
        /// <param name="Why">Why, as the other end is told.</param>
        private void StopServingOCPIOverSSH(String Why)
        {

            if (!ocpiOverSSHServed)
                return;

            ocpiOverSSHServed = false;

            foreach (var (version, _) in ocpiSshServers.ToArray())
                StopServingInboundOverSSH(ProtocolVersion.OCPI(version), Why);

            RemoveSSHSubsystem(OCPIWireNames.SshSubsystem);

        }

        #endregion

        #region (private) ConnectToTheCPOs() / HangUpOnTheCPOs()

        /// <summary>
        /// Make every connection of the store that connects by itself: the CPOs
        /// this EMSP connects to.
        /// </summary>
        /// <remarks>
        /// Nothing here throws: a connection that cannot be made is a line in
        /// the log and a state the page shows.
        /// </remarks>
        private async Task ConnectToTheCPOs()
        {

            connectsToCPOs = true;

            var ownConnections = Connections.Connections.Count(connection => connection.AutoConnect && !connection.Inbound);

            if (ownConnections > 0)
                Log.Notice($"This EMSP makes {ownConnections} connection{(ownConnections == 1 ? "" : "s")} to CPOs by itself, over OCPI.",
                           "ocpi", "connections");

            try
            {
                await ConnectConfiguredConnections();
            }
            catch (Exception e)
            {
                Log.Warning($"The connections of this EMSP could not be made: {e.Message}", "ocpi", "connections");
            }

        }

        /// <summary>
        /// Hang up on every CPO this EMSP connected to.
        /// </summary>
        private async Task HangUpOnTheCPOs()
        {

            connectsToCPOs = false;

            try
            {
                await HangUp();
            }
            catch (Exception e)
            {
                Log.Warning($"Hanging up on what this EMSP connected to failed: {e.Message}", "ocpi", "connections");
            }

        }

        #endregion

        #region TryAddCPOSSHKey(Login, Line, Label, GroupId, Description, CreatedBy, out Key, out Error, out NotSaved)

        /// <summary>
        /// Give a CPO an SSH key for OCPI over SSH - adding its login where it is
        /// not listed yet.
        /// </summary>
        /// <remarks>
        /// Refused where an account of this EMSP has the name: both would sign
        /// in to the same SSH server under it, and a name that is both is let in
        /// as neither.
        /// </remarks>
        /// <param name="Login">The login the CPO signs in to SSH as.</param>
        /// <param name="Line">Its public key, as one line of an authorized_keys file.</param>
        /// <param name="Label">What the key is called, if anything.</param>
        /// <param name="GroupId">The group it should belong to, or null for the one it is in already.</param>
        /// <param name="Description">What this CPO is - in as many languages as it is said in.</param>
        /// <param name="CreatedBy">Who added the key, if anybody said.</param>
        /// <param name="Key">The key, as it was added.</param>
        /// <param name="Error">Why it was refused.</param>
        /// <param name="NotSaved">True where the file could not be written.</param>
        public Boolean TryAddCPOSSHKey(User_Id                               Login,
                                       String?                               Line,
                                       String?                               Label,
                                       String?                               GroupId,
                                       I18NString?                           Description,
                                       String?                               CreatedBy,
                                       [NotNullWhen(true)]  out UserSSHKey?  Key,
                                       [NotNullWhen(false)] out String?      Error,
                                       out Boolean                           NotSaved)
        {

            Key       = null;
            NotSaved  = false;

            if (ExtAPI.TryGetUser(Login, out _))
            {
                Error = $"'{Login}' is an account of this {NodeNoun}. A CPO of the same name could not sign in over SSH: " +
                         "both would sign in under one name, and a name that is both is let in as neither.";
                return false;
            }

            return WebSocketLogins.TryAddSSHKey(
                       Login,
                       Line,
                       Label,
                       GroupId,
                       Description,
                       CreatedBy,
                       out Key,
                       out Error,
                       out NotSaved
                   );

        }

        #endregion

        #region (protected override) InboundLoginLetsIn(Connection) / WhyInboundCanNotSignIn(Connection, Server)

        /// <summary>
        /// Whether the WebSocket login of the name a CPO comes under is switched
        /// on - or there is none: then a certificate issued to that name, of a
        /// chain this EMSP accepts, is what lets it in.
        /// </summary>
        protected override Boolean InboundLoginLetsIn(ConnectionEntry Connection)

            => Connection.InboundName is String name &&
               OCPISignIns.LoginLetsIn(name);

        /// <summary>
        /// Why a CPO can not sign in, as its WebSocket login and the server are
        /// now - or null.
        /// </summary>
        protected override String? WhyInboundCanNotSignIn(ConnectionEntry  Connection,
                                                          IWWCPServer      Server)
        {

            var name = Connection.InboundName ?? "";

            if (Connection.IsSSH)
            {

                if (!SSHEnabled)
                    return $"the SSH server of this {NodeNoun} is off.";

                if (!WebSocketLogins.TryGet(WebSocketLogins.LoginOf(name), out var sshLogin))
                    return $"there is no WebSocket login '{name}' it could sign in to the SSH server with: add one, with its password, " +
                            "its one-time password or its SSH key.";

                if (!sshLogin.Enabled)
                    return $"its WebSocket login '{name}' is switched off.";

                return null;

            }

            return OCPISignIns.WhyCanNotSignIn(name, ocpiServerStarted, Server);

        }

        #endregion

        #region (private) VersionOf(Server)

        /// <summary>
        /// The version of OCPI the given WebSocket server speaks, or null where
        /// it is none of this EMSP's.
        /// </summary>
        private String? VersionOf(AWebSocketEndpoint Server)
        {
            lock (ocpiServers)
                return ocpiServers.FirstOrDefault(served => ReferenceEquals(served.Server, Server)).Version;
        }

        #endregion

    }

}
