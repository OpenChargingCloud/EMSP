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
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.PKI;

using cloud.charging.open.EMSP.Configuration;
using cloud.charging.open.EMSP.Contracts;
using cloud.charging.open.EMSP.Web;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// The mobility operator's side of Plug &amp; Charge: the MO root this
    /// EMSP holds, the contract certificates it signs for its drivers, and
    /// the sign-up that makes somebody a driver in the first place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A contract certificate is what a vehicle presents to a charging
    /// station instead of a card: an eMAID, a public key, and the signature
    /// of a mobility operator the charge point operator trusts. The private
    /// key is made in the driver's browser and never leaves it - the browser
    /// sends a certificate signing request, this EMSP checks that the request
    /// is signed with the key it carries, makes up an eMAID, signs a
    /// certificate to it below its MO Sub-CA 2, and hands the certificate and
    /// the sub-CAs back. The driver bundles those with their key into a
    /// PKCS#12 and loads it into the vehicle, and the MO root into the
    /// vehicle's and, by whatever road, the CPO's trust store.
    /// </para>
    /// <para>
    /// Every contract is also a token: the same eMAID goes into the OCPI
    /// tokens of every version this EMSP speaks, so that a roaming partner
    /// that asks about it gets the answer the certificate already gave.
    /// Taking a contract back takes the token with it.
    /// </para>
    /// <para>
    /// Signing up is Hermod's own opt-in: POST auth/signup below the HTTPExt
    /// API makes an account and signs it in. What this EMSP adds is where
    /// that account lands - in its organization, so that the sign-in door
    /// opens for it, and in the driver group, so that it may ask for
    /// contracts and nothing else. See <see cref="EnrolDriver"/>.
    /// </para>
    /// </remarks>
    public partial class EMSP
    {

        #region Data

        /// <summary>
        /// The directory beside the configuration file that holds the
        /// certificate authority and the registry of contracts.
        /// </summary>
        public const String  PKIDirectoryName  = "pki";

        /// <summary>
        /// The OCPI token type a contract is filed under: OCPI has no type for
        /// a certificate, and "OTHER" is what it says for that.
        /// </summary>
        public const String  ContractTokenType = "OTHER";

        private SelfSignUpAPI?          signUpAPI;
        private ContractsConfiguration  contractSettings  = new ();

        #endregion

        #region Properties

        /// <summary>
        /// The MO root, the two sub-CAs and the signing of contracts.
        /// </summary>
        public ContractCertificateAuthority  ContractCA            { get; private set; } = default!;

        /// <summary>
        /// Every contract certificate this EMSP issued.
        /// </summary>
        public ContractRegistry              Contracts             { get; private set; } = default!;

        /// <summary>
        /// Where the authority and the registry live.
        /// </summary>
        public String                        PKIDirectory          { get; private set; } = default!;

        /// <summary>
        /// Whether anybody may sign up for an account, and so become a driver.
        /// </summary>
        public Boolean                       SelfSignUpEnabled
            => signUpAPI is not null;

        /// <summary>
        /// How long a contract certificate is good for.
        /// </summary>
        public TimeSpan                      ContractValidity
            => TimeSpan.FromDays(contractSettings.ValidityDays ?? ContractsConfiguration.DefaultValidityDays);

        /// <summary>
        /// Where a driver signs up: the page of the web interface.
        /// </summary>
        public URL                           SignUpURL
            => URL.Parse($"{WebInterfaceURL}signup");

        /// <summary>
        /// How many contracts this EMSP issued, over their whole lives.
        /// </summary>
        public Int32                         ContractCount
            => Contracts.Count;

        #endregion


        #region (private) BuildContracts(Configuration)

        /// <summary>
        /// The certificate authority and the registry, read or made, and the
        /// sign-up attached to the HTTPExt API where the configuration allows
        /// it.
        /// </summary>
        /// <remarks>
        /// After the OCPI side, because the authority's subjects carry who
        /// this EMSP is in OCPI - the country, the party, the name - and
        /// after the HTTPExt API, because the sign-up hangs off it.
        /// </remarks>
        private void BuildContracts(ContractsConfiguration? Configuration)
        {

            contractSettings  = Configuration ?? new ContractsConfiguration();

            PKIDirectory      = Path.Combine(Path.GetDirectoryName(ConfigFile.Path) ?? ".", PKIDirectoryName);

            ContractCA        = ContractCertificateAuthority.OpenOrCreate(
                                    Path.Combine(PKIDirectory, ContractCertificateAuthority.DirectoryName),
                                    PartyId.CountryCode.ToString(),
                                    PartyId.PartyId.   ToString(),
                                    BusinessDetails.Name
                                );

            Contracts         = new ContractRegistry(
                                    Path.Combine(PKIDirectory, ContractRegistry.DirectoryName)
                                );

            Log.Log(
                ContractCA.WasCreated ? Logging.LogLevel.Notice : Logging.LogLevel.Info,
                ContractCA.WasCreated
                    ? $"A mobility operator root was made for {PartyIdText}: '{ContractCA.RootSubject}', fingerprint {ContractCA.RootFingerprint}, in '{ContractCA.Directory}'. Hand '{ContractCA.RootTrustPath}' to every CPO and vehicle that should believe the contracts."
                    : $"The mobility operator root '{ContractCA.RootSubject}' (fingerprint {ContractCA.RootFingerprint}) was read from '{ContractCA.Directory}'; {Contracts.Count} contract(s) on record.",
                "contracts", "pki"
            );

            if (contractSettings.SelfSignUp ?? ContractsConfiguration.DefaultSelfSignUp)
            {

                signUpAPI = new SelfSignUpAPI(ExtAPI, OnSignedUp: EnrolDriver);

                Log.Info($"Drivers may sign up at {SignUpURL}; a new account lands in the {UserRole.Driver.Name} group.", "web", "auth", "contracts");

            }

        }

        #endregion

        #region (private) EnrolDriver(User, Request)

        /// <summary>
        /// What happens to an account the moment it signed up: it lands in
        /// this EMSP's organization, so that the sign-in door opens for it
        /// tomorrow, and in the driver group, so that it may ask for
        /// contracts and nothing else.
        /// </summary>
        /// <remarks>
        /// Called by the sign-up after the account was made and before it is
        /// signed in. Hermod's sign-up makes an account in no organization,
        /// and the sign-in door of the web interface refuses those - so
        /// without this a driver could sign up and never come back.
        /// </remarks>
        /// <returns>Null when the account is set up, otherwise why it is not.</returns>
        private async Task<String?> EnrolDriver(IUser        User,
                                                HTTPRequest  Request)
        {

            #region Into the organization, so that the sign-in door opens

            if (!ExtAPI.TryGetOrganization(Organization_Id.Parse(DefaultOrganization), out var organization))
            {
                Log.Error($"'{User.Id}' signed up, but the organization '{DefaultOrganization}' of this EMSP does not exist, so the account cannot sign in.", "web", "auth", "contracts");
                return "The account was made, but this EMSP has no organization to put it in. Ask its operator.";
            }

            var joined = await ExtAPI.AddUserToOrganization(User, User2OrganizationEdgeLabel.IsMember, organization);

            if (!joined.IsSuccess)
            {
                Log.Error($"'{User.Id}' signed up, but could not be put into the organization '{DefaultOrganization}': {joined.ErrorDescription?.FirstText()} The account cannot sign in.", "web", "auth", "contracts");
                return "The account was made, but could not be put into the organization of this EMSP. Ask its operator.";
            }

            #endregion

            #region Into the driver group, so that it may ask for contracts

            if (!ExtAPI.TryGetUser     (User.Id,                 out var stored)  || stored is not User      user  ||
                !ExtAPI.TryGetUserGroup(UserRole.Driver.GroupId, out var group)   || group  is not UserGroup driverGroup)
            {
                Log.Error($"'{User.Id}' signed up, but the {UserRole.Driver.Name} group could not be found, so the account may do nothing.", "web", "auth", "contracts");
                return $"The account was made, but this EMSP has no {UserRole.Driver.Name} group to put it in. Ask its operator.";
            }

            var enrolled = await ExtAPI.AddUserToUserGroup(user, User2UserGroupEdgeLabel.IsMember, driverGroup);

            if (!enrolled.IsSuccess)
            {
                Log.Error($"'{User.Id}' signed up, but could not be put into the {UserRole.Driver.Name} group: {enrolled.ErrorDescription?.FirstText()} The account may do nothing.", "web", "auth", "contracts");
                return $"The account was made, but could not be put into the {UserRole.Driver.Name} group. Ask the operator of this EMSP.";
            }

            #endregion

            Log.Notice($"'{User.Id}' signed up from {Request.RemoteSocket} and is a {UserRole.Driver.Name} now.", "web", "auth", "contracts");

            return null;

        }

        #endregion


        #region IssueContractAsync(User, CSR)

        /// <summary>
        /// A contract certificate for the given account, from its certificate
        /// signing request - and the token that goes with it.
        /// </summary>
        /// <param name="User">Whose contract it is.</param>
        /// <param name="CSR">The PKCS#10 request, as PEM, signed with the key it carries.</param>
        public async Task<ContractOperationResult> IssueContractAsync(IUser    User,
                                                                      String?  CSR)
        {

            var now = TimeProvider.GetUtcNow();

            #region An eMAID nobody has

            EMAId emaId;

            do
            {
                emaId = EMAId.Random(PartyId.CountryCode.ToString(), PartyId.PartyId.ToString());
            }
            while (Contracts.Contains(emaId));

            #endregion

            #region The certificate

            if (!ContractCA.TryIssue(CSR, emaId, ContractValidity, now, out var certificate, out var error))
            {
                Log.Warning($"'{User.Id}' asked for a contract certificate and was refused: {error}", "contracts", "pki");
                return ContractOperationResult.Failed(error);
            }

            var contract  = new ContractCertificate(
                                emaId,
                                User.Id.ToString(),
                                certificate.SerialNumber.ToString(16).ToUpperInvariant(),
                                ContractCertificateAuthority.FingerprintOf(certificate),
                                new DateTimeOffset(certificate.NotBefore, TimeSpan.Zero),
                                new DateTimeOffset(certificate.NotAfter,  TimeSpan.Zero),
                                now
                            );

            var pem       = certificate.ToPEM();

            Contracts.Add(contract, pem);

            Log.Notice($"The contract certificate {emaId} was issued to '{User.Id}', good until {contract.NotAfter:yyyy-MM-dd}.", "contracts", "pki");

            #endregion

            #region The token, on every OCPI version

            foreach (var version in OCPIVersions)
            {

                var token = await AddTokenAsync(
                                      new JObject(
                                          new JProperty("version",       version.Label),
                                          new JProperty("uid",           emaId.Compact),
                                          new JProperty("type",          ContractTokenType),
                                          new JProperty("contractId",    emaId.ToString()),
                                          new JProperty("visualNumber",  emaId.ToString()),
                                          new JProperty("whitelist",     "ALLOWED"),
                                          new JProperty("valid",         true)
                                      )
                                  );

                if (!token.Success)
                    Log.Warning($"The token for the contract {emaId} could not be issued on OCPI {version.Label}: {token.Message}", "contracts", "ocpi", "tokens");

            }

            #endregion

            var json = contract.ToJSON(now);

            json["certificate"] = pem;

            return ContractOperationResult.Ok(
                       $"The contract certificate {emaId} was issued, good until {contract.NotAfter:yyyy-MM-dd}.",
                       new JObject(
                           new JProperty("contract",     json),
                           new JProperty("certificate",  pem),
                           new JProperty("chain",        ContractCA.ChainPEM),
                           new JProperty("moRoot",       ContractCA.RootPEM)
                       )
                   );

        }

        #endregion

        #region RevokeContractAsync(EMAId, By)

        /// <summary>
        /// Take a contract back: the registry says it is over, and the token
        /// goes with it. The certificate stays where it is, as a record.
        /// </summary>
        public async Task<ContractOperationResult> RevokeContractAsync(EMAId  EMAId,
                                                                       IUser  By)
        {

            var now = TimeProvider.GetUtcNow();

            if (!Contracts.TryRevoke(EMAId, By.Id.ToString(), now, out var contract))
                return ContractOperationResult.Failed($"There is no contract {EMAId} to take back, or it was taken back already.");

            foreach (var version in OCPIVersions)
                await RemoveTokenAsync(version.Label, EMAId.Compact);

            Log.Notice($"The contract certificate {EMAId} of '{contract.Owner}' was taken back by '{By.Id}'.", "contracts", "pki");

            return ContractOperationResult.Ok(
                       $"The contract {EMAId} was taken back.",
                       new JObject(new JProperty("contract", contract.ToJSON(now)))
                   );

        }

        #endregion

        #region ContractsJSON(Owner, Everyone)

        /// <summary>
        /// The contracts as the web interface reads them: one account's, or
        /// everybody's for whoever may manage them - with the MO root and the
        /// sub-CAs, which a driver needs to bundle a certificate.
        /// </summary>
        /// <param name="Owner">Whose contracts, when not everybody's.</param>
        /// <param name="Everyone">Whether to list every contract this EMSP issued.</param>
        public JObject ContractsJSON(String?  Owner,
                                     Boolean  Everyone)
        {

            var now        = TimeProvider.GetUtcNow();

            var contracts  = Everyone
                                 ? Contracts.All
                                 : Contracts.OfOwner(Owner ?? "");

            return new JObject(

                       new JProperty("party",         PartyIdText),
                       new JProperty("issuer",        BusinessDetails.Name),
                       new JProperty("validityDays",  (UInt32) ContractValidity.TotalDays),
                       new JProperty("signUp",        SelfSignUpEnabled),
                       new JProperty("everyone",      Everyone),

                       new JProperty("moRoot",        new JObject(
                           new JProperty("subject",       ContractCA.RootSubject),
                           new JProperty("fingerprint",   ContractCA.RootFingerprint),
                           new JProperty("notAfter",      ContractCA.RootNotAfter.ToString("o")),
                           new JProperty("pem",           ContractCA.RootPEM),
                           new JProperty("file",          ContractCA.RootTrustPath)
                       )),

                       new JProperty("chain",         ContractCA.ChainPEM),
                       new JProperty("directory",     PKIDirectory),

                       new JProperty("contracts",     new JArray(
                           contracts.Select(contract => {

                               var json = contract.ToJSON(now);

                               if (Contracts.TryReadPEM(contract, out var pem))
                                   json["certificate"] = pem;

                               return json;

                           })
                       ))

                   );

        }

        #endregion

    }

}
