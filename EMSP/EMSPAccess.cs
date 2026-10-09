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

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// What an EMSP adds to the resources every node has, and the roles of the
    /// people around it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node brings the viewer, who may look at everything, and the
    /// administrators, who may do everything - the roaming partners and the
    /// certificates included, which are the two resources nobody else may
    /// edit: somebody who can add a roaming partner hands a foreign system the
    /// right to push locations, sessions and charge detail records into this
    /// EMSP and to ask it whether a customer may charge, and somebody who can
    /// add a root can make this EMSP believe a server nobody else would.
    /// </para>
    /// <para>
    /// The configuration file may add roles to these and say differently what
    /// one of them may do - see the node's "roles" section. What is written
    /// here is what an EMSP is when its file says nothing, and it is what the
    /// EMSP's own roles allowed before they were data.
    /// </para>
    /// </remarks>
    public static class EMSPAccess
    {

        #region Resources

        /// <summary>
        /// Who this EMSP is in OCPI, and what its roaming partners pushed into
        /// it: the locations, tariffs, sessions and charge detail records.
        /// </summary>
        /// <remarks>
        /// Only read: what a partner pushed is the partner's to change, and who
        /// this EMSP is in OCPI is written in the configuration file.
        /// </remarks>
        public const String  OCPI       = "ocpi";

        /// <summary>
        /// The roaming partners: which charge point operators may call this
        /// EMSP, and - edited - the access token each of them signs in with,
        /// added and taken away; run, the OCPI peering with one.
        /// </summary>
        /// <remarks>
        /// The highest of these. Everything else here is about what this EMSP
        /// does; this is about whom it believes.
        /// </remarks>
        public const String  Partners   = "partners";

        /// <summary>
        /// The tokens this EMSP issues to its own customers: the RFID cards and
        /// app identities that a charging station somewhere asks a roaming
        /// partner about - edited, added and taken away, and a driver's card
        /// let in or turned down. Run: bring a card of one's own, block it and
        /// take it away, and see what one charged with it.
        /// </summary>
        /// <remarks>
        /// Day-to-day work at an EMSP: a customer gets a card, another loses
        /// one. Being wrong here is quiet - a token that was not added is a
        /// customer who cannot charge and says so to nobody but the station.
        /// </remarks>
        public const String  Tokens     = "tokens";

        /// <summary>
        /// The contract certificates this EMSP signs below its MO root. Run:
        /// ask for one of one's own, and see and take back the ones one holds.
        /// Edited: see every contract, whom it was issued to, and take one back.
        /// </summary>
        /// <remarks>
        /// Two operations and no third: nothing here asks for reading the
        /// contracts, because which contracts exist says who this EMSP's
        /// customers are, and the viewer, who may read everything, should not
        /// learn that from a resource called "contracts". Seeing everybody's
        /// is editing them.
        /// </remarks>
        public const String  Contracts  = "contracts";

        /// <summary>
        /// All four.
        /// </summary>
        public static readonly IReadOnlyList<String>  Resources = [ OCPI, Partners, Tokens, Contracts ];

        #endregion

        #region Roles

        /// <summary>
        /// A customer: somebody who charges with a contract this EMSP made out
        /// to them, and who signed up for it themselves.
        /// </summary>
        /// <remarks>
        /// The one role that is not about running this EMSP, and the one role
        /// an account gets without anybody handing it out: signing up puts an
        /// account here and nowhere else. It reaches the driver's own contracts
        /// and cards, and what they charged with them, and nothing beyond -
        /// not the configuration, not the log, not the partners.
        /// </remarks>
        public static readonly Role  Driver    = new ("driver",
                                                      [ Permission.Run(Contracts),
                                                        Permission.Run(Tokens) ],
                                                      "a customer: asks for contract certificates and brings RFID cards of their own, blocks and takes back what they hold, and sees what they charged");

        /// <summary>
        /// The operator of this EMSP: may point it at other name and time
        /// servers and test them, and looks after the customers' tokens and
        /// contracts.
        /// </summary>
        /// <remarks>
        /// Day-to-day operation. An EMSP is run by whoever answers the
        /// customers long before it is run by whoever installed it, and a card
        /// that stopped working is their problem to fix. What separates it
        /// from the administrators is the roaming partners, the certificates
        /// and the SSH server: whoever runs the customers adds and removes
        /// tokens all day, and whoever decides which CPO and which server this
        /// EMSP believes, and who may sign in over SSH, does it a few times in
        /// the life of the box.
        /// </remarks>
        public static readonly Role  Operator  = new ("emsp",
                                                      [ Permission.Read(Permission.AnyResource),
                                                        Permission.Edit(NodeResources.DNS),
                                                        Permission.Run (NodeResources.DNS),
                                                        Permission.Edit(NodeResources.NTS),
                                                        Permission.Run (NodeResources.NTS),
                                                        Permission.Edit(Tokens),
                                                        Permission.Edit(Contracts) ],
                                                      "runs the EMSP: its name and time servers, and the customers' tokens and contracts");

        /// <summary>
        /// Both, in the order a sentence naming them reads best.
        /// </summary>
        public static readonly IReadOnlyList<Role>  Roles = [ Driver, Operator ];

        #endregion

    }

}
