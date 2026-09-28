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

using cloud.charging.open.protocols.WWCP.Node.Certificates;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// The certificate store of this EMSP's node: what it believes, what it
    /// presents, and what it recognises a server by.
    /// </summary>
    /// <remarks>
    /// The store is the node's - the same WWCP_Node CertificateStore a vehicle
    /// and a charging station keep, a directory of files with an index beside
    /// them, "certificates" beside the configuration file unless the file says
    /// otherwise. What an EMSP keeps in it is decided here, in
    /// <see cref="StoredCertificateKinds"/>; what is done with each kind is the
    /// node's.
    ///
    /// The contract PKI is not in it and does not go into it: the MO root this
    /// EMSP signs its contracts below and the two sub-CAs under it carry their
    /// private keys, which a store of roots to believe must never hold - they
    /// stay below "pki" beside the configuration file (see EMSP.Contracts.cs).
    /// </remarks>
    public partial class EMSP
    {

        #region StoredCertificateKinds

        /// <summary>
        /// The kinds of certificate this EMSP's store keeps: TLS's four, and the
        /// three roots of Plug &amp; Charge.
        /// </summary>
        /// <remarks>
        /// <para>
        /// TLS's four because any node connects to servers and is connected to:
        /// the roots a time server or a name server may be vouched for by, the
        /// roots a client connecting here has to chain to, the certificates a
        /// server may be held to by its fingerprint, and what this EMSP
        /// presents itself. An EMSP's roaming partners are exactly such clients
        /// and servers.
        /// </para>
        /// <para>
        /// The V2G, Mobility Operator and OEM roots because they are what Plug
        /// &amp; Charge checks a station's chain, a contract and an OEM
        /// provisioning certificate against - an EMSP issues contracts into that
        /// world, and the Mobility Operator roots of other providers and of the
        /// PKIs are the ones a contract may chain to besides its own. The three
        /// are kept apart for the reason the node gives. What only a vehicle
        /// holds - its own certificate, its contracts, its provisioning
        /// certificate, the key it checks a tariff with - is not offered: a
        /// page offering a kind that means nothing here would be offering a
        /// mistake.
        /// </para>
        /// </remarks>
        public static readonly IReadOnlyList<CertificateKind> StoredCertificateKinds = [
            CertificateKind.V2GRoot,
            CertificateKind.MORoot,
            CertificateKind.OEMRoot,
            CertificateKind.TLSRoot,
            CertificateKind.ClientRoot,
            CertificateKind.TLSServer,
            CertificateKind.TLSIdentity
        ];

        #endregion

    }

}
