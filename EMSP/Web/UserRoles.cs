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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

#endregion

namespace cloud.charging.open.EMSP.Web
{

    /// <summary>
    /// What somebody signed in to this EMSP is allowed to do.
    /// </summary>
    /// <remarks>
    /// Flags rather than a list, because a permission is asked about one at a
    /// time and answered by a single test - and because the set a role grants
    /// is then a constant instead of a collection to be built and searched.
    ///
    /// Only what this EMSP actually enforces is named here. A permission with
    /// nothing behind it is a promise made to whoever reads the login file and
    /// not kept - so these arrived one at a time, as the things they guard did:
    /// which roaming partners may call this EMSP is its own permission rather
    /// than something that grew quietly inside one that already existed, and so
    /// are the tokens this EMSP hands its customers.
    /// </remarks>
    [Flags]
    public enum Permissions : UInt32
    {

        /// <summary>
        /// Nothing at all. What an unknown role would grant.
        /// </summary>
        None                    = 0,

        /// <summary>
        /// See how this EMSP is configured, and what its roaming partners have
        /// sent it: the locations, tariffs, sessions and charge detail records.
        /// </summary>
        ReadConfiguration       = 1,

        /// <summary>
        /// Change how this EMSP reaches the network: its name resolution and
        /// where it reads the time.
        /// </summary>
        /// <remarks>
        /// Reversible, and it complains: a wrong name server makes the EMSP say
        /// so, and the next change puts it right. That is what separates it
        /// from the settings describing who is around this EMSP - the roaming
        /// partners - where being wrong is quiet and somebody else notices
        /// first.
        /// </remarks>
        ChangeNetworkSettings   = 2,

        /// <summary>
        /// Make this EMSP ask a name server or a time server something, to
        /// find out whether it can.
        /// </summary>
        /// <remarks>
        /// Its own permission and not part of reading: a diagnostic sends
        /// traffic from this EMSP to a host somebody named, which is more than
        /// it sounds like to hand to everybody who may look at a page.
        /// </remarks>
        RunDiagnostics          = 4,

        /// <summary>
        /// Add, change and take away the tokens this EMSP issues to its own
        /// customers: the RFID cards and app identities that a charging station
        /// somewhere asks a roaming partner about.
        /// </summary>
        /// <remarks>
        /// Day-to-day work at an EMSP: a customer gets a card, another loses
        /// one. It is a bigger thing than changing a name server, because being
        /// wrong here is quiet - a token that was not added is a customer who
        /// cannot charge and says so to nobody but the station - so it is a
        /// permission of its own rather than part of
        /// <see cref="ChangeNetworkSettings"/>.
        /// </remarks>
        ManageTokens            = 8,

        /// <summary>
        /// Add and remove roaming partners, hand out the access token a partner
        /// signs in with, and start the OCPI peering with one.
        /// </summary>
        /// <remarks>
        /// The highest of these, and deliberately not part of running the
        /// tokens. Everything else here is about what this EMSP does; this is
        /// about whom it believes. Somebody who can add a roaming partner hands
        /// a foreign system the right to push locations, sessions and charge
        /// detail records into this EMSP and to ask it whether a customer may
        /// charge - and no other permission here reaches that far.
        /// </remarks>
        ManageRoamingPartners   = 16,

        /// <summary>
        /// Ask this EMSP for a contract certificate of one's own, and see the
        /// ones one holds.
        /// </summary>
        /// <remarks>
        /// What a driver may do, and all a driver may do. A contract
        /// certificate is made out to the account that asked for it, from a
        /// key that never left the driver's browser - so this reaches nobody
        /// else's contracts and nothing else of this EMSP.
        /// </remarks>
        IssueContracts          = 32,

        /// <summary>
        /// See every contract certificate this EMSP issued, whom it was
        /// issued to, and take one back.
        /// </summary>
        /// <remarks>
        /// The operator's side of the contracts, next to the tokens: a
        /// contract taken back is a driver who cannot charge with it, which
        /// is as quiet as a token that was not added.
        /// </remarks>
        ManageContracts         = 64

    }


    /// <summary>
    /// A role somebody signs in as: a name, and the permissions it carries.
    /// </summary>
    /// <remarks>
    /// A closed set: a role this EMSP has never heard of is a role it cannot
    /// enforce. So an unrecognised name is refused when the login file is
    /// read, rather than quietly granting nothing - or, far worse, being taken
    /// for a known one because it looks similar.
    ///
    /// The names overlap with those of the charging station, the local
    /// controller and the CSMS on purpose - all of them have "systemadmin" and
    /// "viewer" - so that one HTTPExt API handed to several of these programs
    /// makes one sign-in open all of them. What does not overlap is the
    /// operator role: a CPO's operator is not an EMSP's, and "emsp" here is
    /// what "cpo" is there.
    /// </remarks>
    /// <param name="Name">How the role is written, and the identification of the group whose members hold it.</param>
    /// <param name="Permissions">What it grants.</param>
    public sealed record UserRole(String       Name,
                                  Permissions  Permissions)
    {

        #region Properties

        /// <summary>
        /// The user group in the HTTPExt API whose members hold this role.
        /// </summary>
        public UserGroup_Id  GroupId
            => UserGroup_Id.Parse(Name);

        #endregion


        #region Data

        /// <summary>
        /// May look at this EMSP, and do nothing to it.
        /// </summary>
        public static readonly UserRole  Viewer       = new ("viewer",
                                                             Permissions.ReadConfiguration);

        /// <summary>
        /// A customer: somebody who charges with a contract this EMSP made
        /// out to them, and who signed up for it themselves.
        /// </summary>
        /// <remarks>
        /// The one role that is not about running this EMSP, and the one
        /// role an account gets without anybody handing it out: signing up
        /// puts an account here and nowhere else. It reaches the driver's own
        /// contracts and nothing beyond them - not the configuration, not the
        /// log, not the partners.
        /// </remarks>
        public static readonly UserRole  Driver       = new ("driver",
                                                             Permissions.IssueContracts);

        /// <summary>
        /// The operator of this EMSP: may point it at other name and time
        /// servers, may test them, and looks after the customers' tokens and
        /// contracts.
        /// </summary>
        /// <remarks>
        /// Day-to-day operation. An EMSP is run by whoever answers the
        /// customers long before it is run by whoever installed it, and a card
        /// that stopped working is their problem to fix.
        /// </remarks>
        public static readonly UserRole  EMSP         = new ("emsp",
                                                             Permissions.ReadConfiguration      |
                                                             Permissions.ChangeNetworkSettings  |
                                                             Permissions.RunDiagnostics         |
                                                             Permissions.ManageTokens           |
                                                             Permissions.ManageContracts);

        /// <summary>
        /// Everything this EMSP can be told, by whoever is trusted with all of
        /// it at once.
        /// </summary>
        /// <remarks>
        /// What separates it from the operator is the roaming partners: whoever
        /// runs the customers adds and removes tokens all day, and whoever
        /// decides which CPO this EMSP believes does it a few times in the
        /// life of the box.
        /// </remarks>
        public static readonly UserRole  SystemAdmin  = new ("systemadmin",
                                                             Permissions.ReadConfiguration      |
                                                             Permissions.ChangeNetworkSettings  |
                                                             Permissions.RunDiagnostics         |
                                                             Permissions.ManageTokens           |
                                                             Permissions.ManageRoamingPartners  |
                                                             Permissions.IssueContracts         |
                                                             Permissions.ManageContracts);

        /// <summary>
        /// Every role this EMSP knows.
        /// </summary>
        public static readonly IReadOnlyList<UserRole>  All = [ Viewer, Driver, EMSP, SystemAdmin ];

        #endregion


        #region (static) TryParse(Text, out Role, out Error)

        /// <summary>
        /// A role by the name the login file writes it under, in any case.
        /// </summary>
        public static Boolean TryParse(String?                           Text,
                                       [NotNullWhen(true)]  out UserRole?  Role,
                                       [NotNullWhen(false)] out String?    Error)
        {

            Role   = All.FirstOrDefault(role => String.Equals(role.Name, Text?.Trim(), StringComparison.OrdinalIgnoreCase));

            Error  = Role is null
                         ? $"\"{Text}\" is not a role this EMSP knows. Known roles: {String.Join(", ", All.Select(role => role.Name))}."
                         : null;

            return Role is not null;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()
            => Name;

        #endregion

    }


    /// <summary>
    /// What a set of roles adds up to.
    /// </summary>
    public static class UserRoleExtensions
    {

        #region PermissionsOf(this Roles)

        /// <summary>
        /// Everything the given roles grant together.
        /// </summary>
        public static Permissions PermissionsOf(this IEnumerable<UserRole> Roles)
        {

            var permissions = Permissions.None;

            foreach (var role in Roles)
                permissions |= role.Permissions;

            return permissions;

        }

        #endregion

        #region Names(this Permissions)

        /// <summary>
        /// The permissions as the web interface reads them, so that a page can
        /// grey out what this browser may not do instead of finding out by
        /// being refused.
        /// </summary>
        /// <remarks>
        /// What the browser is told is a copy of what the EMSP enforces, and
        /// not the enforcement: every request is checked again on arrival. A
        /// greyed-out button is a courtesy, not a lock.
        /// </remarks>
        public static IEnumerable<String> Names(this Permissions Permissions)

            => Enum.GetValues<Permissions>().
                    Where (permission => permission != Web.Permissions.None && Permissions.HasFlag(permission)).
                    Select(permission => Char.ToLowerInvariant(permission.ToString()[0]) + permission.ToString()[1..]);

        #endregion

    }

}
