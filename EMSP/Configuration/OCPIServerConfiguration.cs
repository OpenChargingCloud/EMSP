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

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.Logins;

#endregion

namespace cloud.charging.open.EMSP.Configuration
{

    /// <summary>
    /// The "webSocketServer" section of the configuration file of an EMSP: the
    /// WebSocket server a CPO - a CSMS of a roaming agreement - connects to, to
    /// speak OCPI over it rather than over HTTP, and whether it may connect over
    /// the SSH subsystem of OCPI as well.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The section of the same name of a node of OCPP, as far as it is no
    /// matter of OCPP: whether the server listens, where, and how a client may
    /// say who it is - by HTTP authentication without TLS or over it, or by its
    /// TLS client certificate. Who it is, is its WebSocket login. The
    /// subprotocols are OCPI's, of the versions this EMSP speaks, and not
    /// configured here.
    /// </para>
    /// <para>
    /// <b>Switched off unless somebody says otherwise</b>, as the server of the
    /// charging stations of a node of OCPP: it opens a port to a network rather
    /// than reaching out to one. OCPI over HTTP, at the web interface's port,
    /// does not need it.
    /// </para>
    /// <para>
    /// As in every section, null means "the file does not say", and the
    /// defaults here stand.
    /// </para>
    /// </remarks>
    /// <param name="Enabled">Whether this EMSP listens for WebSockets of OCPI at all.</param>
    /// <param name="Address">Which interface it listens on; every one unless said otherwise.</param>
    /// <param name="TCPPort">Which TCP port it listens on.</param>
    /// <param name="ClientAuthentication">How a client may authenticate: by HTTP authentication without TLS or over it, or by its TLS client certificate.</param>
    /// <param name="CheckCertificateRevocation">Whether a client certificate is checked against its issuer's revocation list.</param>
    /// <param name="SSH">Whether a CPO may connect over the SSH subsystem of OCPI of this EMSP's SSH server as well.</param>
    public sealed record OCPIServerConfiguration(Boolean?                                       Enabled                      = null,
                                                 IPAddress?                                     Address                      = null,
                                                 IPPort?                                        TCPPort                      = null,
                                                 IReadOnlyList<WebSocketClientAuthentication>?  ClientAuthentication         = null,
                                                 Boolean?                                       CheckCertificateRevocation   = null,
                                                 Boolean?                                       SSH                          = null)
    {

        #region Data

        /// <summary>
        /// The name of this section in the configuration file.
        /// </summary>
        public const String  SectionName  = "webSocketServer";

        /// <summary>
        /// The name of the switch of OCPI over SSH within it: "ssh": { "enabled": true }.
        /// </summary>
        public const String  SSHFieldName = "ssh";

        /// <summary>
        /// The TCP port a CPO connects to, unless another is given.
        /// </summary>
        /// <remarks>
        /// Beside the web interface of this EMSP (2355) and not on it, as a
        /// node of OCPP keeps the port of its stations beside its own - and
        /// beyond the ports the other OpenChargingCloud boxes use: a CSMS 2351
        /// and 2352, a gateway 2353 and 2354, a roaming hub 2356, a grid
        /// operator 2358, a PKI 2360, which are tried out on the same bench.
        /// </remarks>
        public static readonly IPPort                           DefaultTCPPort               = IPPort.Parse(2359);

        /// <summary>
        /// How a client may authenticate where nothing says otherwise: every way.
        /// </summary>
        public static readonly WebSocketClientAuthentication[]  DefaultClientAuthentication  = WebSocketClientAuthentications.All;

        #endregion

        #region Properties

        /// <summary>
        /// Whether this EMSP listens for WebSockets of OCPI.
        /// </summary>
        public Boolean IsEnabled
            => Enabled == true;

        /// <summary>
        /// Whether a CPO may connect over SSH as well.
        /// </summary>
        public Boolean TakesSSH
            => SSH == true;

        /// <summary>
        /// Whether this section says anything at all.
        /// </summary>
        public Boolean IsEmpty

            => Enabled                    is null &&
               Address                    is null &&
               TCPPort                    is null &&
               ClientAuthentication       is null &&
               CheckCertificateRevocation is null &&
               SSH                        is null;

        #endregion


        #region (static) TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// The section, or the one sentence that says what is wrong with it.
        /// </summary>
        public static Boolean TryParse(JObject                                           JSON,
                                       [NotNullWhen(true)]  out OCPIServerConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?                   Error)
        {

            Configuration = null;

            if (!ConfigurationReader.TryReadBoolean(JSON, "enabled",                     SectionName, out var enabled,      out Error) ||
                !ConfigurationReader.TryReadPort   (JSON, "port",                        SectionName, out var port,         out Error) ||
                !ConfigurationReader.TryReadBoolean(JSON, "checkCertificateRevocation",  SectionName, out var checkCRL,     out Error) ||
                !ConfigurationReader.TryReadString (JSON, "address",                     SectionName, 45, out var addressText, out Error))
            {
                return false;
            }

            #region The interface to listen on

            IPAddress? address = null;

            if (addressText is not null && (address = IPAddress.TryParse(addressText)) is null)
            {
                Error = $"'{SectionName}.address' must be an IP address, e.g. \"0.0.0.0\" for every interface.";
                return false;
            }

            #endregion

            #region How a client may authenticate

            IReadOnlyList<WebSocketClientAuthentication>? clientAuthentication = null;

            if (JSON["clientAuthentication"] is JToken authenticationToken && authenticationToken.Type != JTokenType.Null)
            {

                var ways = $"{String.Join(", ", WebSocketClientAuthentications.Names.Select(name => $"\"{name}\""))}";

                if (authenticationToken is not JArray authenticationArray)
                {
                    Error = $"'{SectionName}.clientAuthentication' must be an array of the ways a client may authenticate: {ways}.";
                    return false;
                }

                var authentications = new List<WebSocketClientAuthentication>();

                foreach (var token in authenticationArray)
                {

                    if (token.Type != JTokenType.String ||
                        !WebSocketClientAuthentications.TryParse(token.Value<String>(), out var authentication))
                    {
                        Error = $"'{SectionName}.clientAuthentication': {token.ToString(Newtonsoft.Json.Formatting.None)} is no way a client may authenticate; they are {ways}.";
                        return false;
                    }

                    if (!authentications.Contains(authentication))
                        authentications.Add(authentication);

                }

                if (authentications.Count == 0)
                {
                    Error = $"'{SectionName}.clientAuthentication' must allow at least one way, or no client can connect at all. Switch the server off instead.";
                    return false;
                }

                clientAuthentication = [.. authentications.Order()];

            }

            #endregion

            #region OCPI over SSH

            Boolean? ssh = null;

            if (JSON[SSHFieldName] is JToken sshToken && sshToken.Type != JTokenType.Null)
            {

                if (sshToken is not JObject sshJSON)
                {
                    Error = $"'{SectionName}.{SSHFieldName}' must be a JSON object, e.g. {{ \"enabled\": true }}.";
                    return false;
                }

                if (!ConfigurationReader.TryReadBoolean(sshJSON, "enabled", $"{SectionName}.{SSHFieldName}", out ssh, out Error))
                    return false;

            }

            #endregion

            Configuration = new OCPIServerConfiguration(
                                enabled,
                                address,
                                port,
                                clientAuthentication,
                                checkCRL,
                                ssh
                            );

            Error = null;
            return true;

        }

        #endregion

        #region Effective()

        /// <summary>
        /// This section with every field filled in.
        /// </summary>
        public OCPIServerConfiguration Effective()

            => new (
                   Enabled                    ?? false,
                   Address                    ?? IPv4Address.Any,
                   TCPPort                    ?? DefaultTCPPort,
                   ClientAuthentication       ?? DefaultClientAuthentication,
                   CheckCertificateRevocation ?? false,
                   SSH                        ?? false
               );

        #endregion

        #region ToJSON()

        /// <summary>
        /// The section as it is written to the file: what it says, and nothing
        /// it was not told.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject();

            if (Enabled.HasValue)                     json.Add("enabled",                     Enabled.Value);
            if (Address is not null)                  json.Add("address",                     Address.ToString());
            if (TCPPort.HasValue)                     json.Add("port",                        TCPPort.Value.ToUInt16());
            if (ClientAuthentication is not null)     json.Add("clientAuthentication",        new JArray(ClientAuthentication.Select(authentication => authentication.ToText())));
            if (CheckCertificateRevocation.HasValue)  json.Add("checkCertificateRevocation",  CheckCertificateRevocation.Value);
            if (SSH.HasValue)                         json.Add(SSHFieldName,                  new JObject(new JProperty("enabled", SSH.Value)));

            return json;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()

            => !IsEnabled
                   ? "WebSocket server off"
                   : $"WebSocket server on {Address?.ToString() ?? "every interface"}" +
                     (TCPPort.HasValue ? $":{TCPPort}"           : "") +
                     (TakesSSH         ? ", over SSH as well"    : "");

        #endregion

    }

}
