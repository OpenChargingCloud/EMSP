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

using System.Diagnostics.CodeAnalysis;

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.Node.Web;
using cloud.charging.open.protocols.WWCP.Node.Logins;
using cloud.charging.open.protocols.WWCP.Node.CommandLine;

using Provider        = cloud.charging.open.EMSP.EMSP;
using ConnectionType  = cloud.charging.open.protocols.WWCP.Node.Connections.ConnectionType;

#endregion

namespace cloud.charging.open.EMSP.CommandLine
{

    #region CPOsCommand

    /// <summary>
    /// 'cpos': where the WebSocket server of OCPI is, and every CPO that
    /// connects to it - its connection, what it signs in with, and whether it
    /// is connected.
    /// </summary>
    public class CPOsCommand(EMSPCLI CLI) : ACLICommand<EMSPCLI>(CLI),
                                            ICLICommand
    {

        #region Data

        public const String CommandName = "cpos";

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)

            => Arguments.Length == 1 && CommandName.StartsWith(Arguments[0], StringComparison.OrdinalIgnoreCase)
                   ? [ SuggestionResponse.CommandCompleted(CommandName) ]
                   : [];

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override Task<String[]> Execute(String[]           Arguments,
                                               CancellationToken  CancellationToken)
        {

            if (!cli.MayDo(Permission.Read(EMSPAccess.Partners), CommandName, out var refused))
                return Task.FromResult<String[]>([ refused ]);

            var emsp       = cli.EMSP;
            var settings   = emsp.OCPIServerSettings;
            var connected  = emsp.CPOsIn.ToHashSet(StringComparer.Ordinal);
            var lines      = new List<String>();

            #region The server

            lines.Add(settings.IsEnabled
                          ? emsp.OCPIServerRunning
                                ? $"The WebSocket server of OCPI listens at port {settings.TCPPort?.ToUInt16()}{(emsp.OCPIServerTLS ? ", with TLS" : ", without TLS")}."
                                : $"The WebSocket server of OCPI is switched on at port {settings.TCPPort?.ToUInt16()}, and does not listen."
                          : "The WebSocket server of OCPI is switched off.");

            lines.Add(emsp.OCPIOverSSHServed
                          ? $"OCPI is served over SSH, at port {emsp.SSHPort?.ToUInt16()}."
                          : "OCPI is not served over SSH.");

            #endregion

            #region Every CPO that connects

            var entries = emsp.Connections.Connections.
                              Where  (entry => entry.Inbound && entry.ConnectionType == ConnectionType.CPO).
                              OrderBy(entry => entry.InboundName, StringComparer.Ordinal).
                              ToArray();

            var named   = entries.Select(entry => entry.InboundName).OfType<String>().ToHashSet(StringComparer.Ordinal);

            if (entries.Length == 0)
                lines.Add("No CPO that connects is written down: none may connect.");

            foreach (var entry in entries)
            {

                var name = entry.InboundName ?? "?";

                lines.Add($"{name}  {(entry.AutoConnect ? "on" : "off")}  {(connected.Contains(name) ? "connected" : "not connected")}  " +
                          $"{entry.URL}  {entry.Description.FirstText()}");

                if (User_Id.TryParse(name) is User_Id login && emsp.WebSocketLogins.TryGet(login, out var signsIn))
                    lines.Add($"  signs in with {String.Join(", ", WaysIn(signsIn))}, in the group '{signsIn.GroupId}'{(signsIn.Enabled ? "" : ", switched off")}");
                else
                    lines.Add("  has no login: it cannot sign in.");

            }

            // A login without a connection signs in and is let go of.
            foreach (var login in emsp.WebSocketLogins.Logins.Where(login => !named.Contains(login.Login.ToString())).OrderBy(login => login.Login))
                lines.Add($"{login.Login}  has a login and no connection: it is let go of when it connects.");

            #endregion

            #region The CPOs this EMSP connects to

            foreach (var entry in emsp.Connections.Connections.
                                       Where  (entry => !entry.Inbound && entry.ConnectionType == ConnectionType.CPO).
                                       OrderBy(entry => entry.URL.ToString(), StringComparer.Ordinal))
                lines.Add($"this EMSP connects to {entry.URL}  {(entry.AutoConnect ? "on" : "off")}  {entry.Description.FirstText()}");

            #endregion

            return Task.FromResult<String[]>([ .. lines ]);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} - the CPOs that connect to this EMSP over a WebSocket or SSH, what they sign in with, and whether they are connected";

        #endregion


        #region (private static) WaysIn(Login)

        private static IEnumerable<String> WaysIn(WebSocketLogin Login)
        {

            var ways = new List<String>();

            if (Login.HasPassword)
                ways.Add("a password");

            if (Login.HasTOTP)
                ways.Add("TOTP");

            if (Login.HasSSHKey)
                ways.Add($"{Login.Keys.Count} SSH key(s)");

            if (Login.HasKeyPin)
                ways.Add($"{Login.Pins.Count} key pin(s)");

            return ways.Count > 0 ? ways : [ "nothing yet" ];

        }

        #endregion

    }

    #endregion

    #region CPOKeysCommand

    /// <summary>
    /// 'cpoKeys': the SSH keys the CPOs sign in to OCPI over SSH with.
    /// </summary>
    public class CPOKeysCommand(EMSPCLI CLI) : LoginKeysCommand(CLI, CommandName, EMSPAccess.Partners)
    {

        public const String CommandName = "cpoKeys";

        private Provider EMSPNode
            => ((EMSPCLI) cli).EMSP;

        protected override WebSocketLogins        Logins      => EMSPNode.WebSocketLogins;
        protected override String                 LoginNoun   => "CPO";
        protected override String                 LoginWord   => "login";
        protected override String                 KeysAreFor  => "for OCPI over SSH";
        protected override IReadOnlyList<String>  LogTags     => [ "ocpi", "cpo" ];

        /// <summary>
        /// As the EMSP gives a CPO a key: refused for the name of an account.
        /// </summary>
        protected override Boolean TryAddKey(User_Id                               Login,
                                             String                                Line,
                                             String                                CreatedBy,
                                             [NotNullWhen(true)]  out UserSSHKey?  Key,
                                             [NotNullWhen(false)] out String?      Error)

            => EMSPNode.TryAddCPOSSHKey(Login, Line, null, null, null, CreatedBy, out Key, out Error, out _);

        protected override IEnumerable<String> WhyNoLoginSignsIn()
        {

            if (!EMSPNode.OCPIOverSSHServed)
                yield return "OCPI is not served over SSH: no CPO signs in over SSH.";

            else if (!EMSPNode.SSHEnabled)
                yield return "The SSH server of this EMSP is off: no CPO signs in over SSH.";

        }

    }

    #endregion

    #region CPOPinsCommand

    /// <summary>
    /// 'cpoPins': the key pins the CPOs sign in to the WebSocket port of OCPI with.
    /// </summary>
    public class CPOPinsCommand(EMSPCLI CLI) : LoginPinsCommand(CLI, CommandName, EMSPAccess.Partners)
    {

        public const String CommandName = "cpoPins";

        private Provider EMSPNode
            => ((EMSPCLI) cli).EMSP;

        protected override WebSocketLogins        Logins      => EMSPNode.WebSocketLogins;
        protected override String                 LoginNoun   => "CPO";
        protected override String                 LoginWord   => "login";
        protected override String                 PinsAreFor  => "on the WebSocket port of OCPI";
        protected override IReadOnlyList<String>  LogTags     => [ "ocpi", "cpo" ];

        protected override IEnumerable<String> WhyNoLoginSignsIn()
        {

            if (!EMSPNode.OCPIServerSettings.IsEnabled)
                yield return "The WebSocket server of OCPI is switched off: no CPO signs in.";

            else if (EMSPNode.OCPIServerRunning && !EMSPNode.OCPIServerTLS)
                yield return "The WebSocket port of OCPI is not encrypted: no key pin lets a CPO in until it is.";

        }

    }

    #endregion

}
