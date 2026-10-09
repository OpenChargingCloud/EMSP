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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.EMSP.OCPI;
using cloud.charging.open.EMSP.Tickets;

using cloud.charging.open.protocols.WWCP.Node.Logging;

#endregion

namespace cloud.charging.open.EMSP
{

    /// <summary>
    /// A driver's long-term keys, and the charging tickets signed with them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An account certificate says that a key is a driver's: the driver's
    /// browser makes the key and a signing request, this EMSP signs a
    /// certificate to the account below its account CA, good for two years
    /// from a minute before it was made, and keeps it with the account. It
    /// opens nothing on its own - it is how a driver says "this is me".
    /// </para>
    /// <para>
    /// A charging ticket says that whoever holds a key may charge: the
    /// driver's browser makes a key pair for the ticket alone, writes the
    /// ticket - its id, this EMSP, the key, from when until when, its limits -
    /// and signs it twice, with the ticket key, to show it holds it, and with
    /// one of its account keys, to show whose it is. This EMSP checks both,
    /// takes the account's signature off, signs the ticket with its ticket
    /// issuer's key, and hands back the ticket signed by the ticket key and by
    /// itself. A charge point operator who believes the ticket issuer's
    /// certificate lets the holder of the ticket key charge, and learns
    /// nothing of who that is. This EMSP keeps whose it is, and no one else.
    /// </para>
    /// </remarks>
    public partial class EMSP
    {

        #region Data

        /// <summary>The directories below "pki" that hold the account CA and the ticket issuer.</summary>
        public const String  AccountsDirectoryName  = "accounts";
        public const String  TicketsDirectoryName   = "tickets";

        /// <summary>How long an account certificate is good for: two years.</summary>
        public static readonly TimeSpan  AccountCertificateValidity  = TimeSpan.FromDays(730);

        /// <summary>The longest a charging ticket may be good for.</summary>
        public static readonly TimeSpan  MaxTicketValidity           = TimeSpan.FromDays(30);

        /// <summary>How far a driver's clock may be behind this EMSP's when a ticket begins.</summary>
        public static readonly TimeSpan  TicketClockSkew             = TimeSpan.FromMinutes(5);

        /// <summary>How long the two keys' own certificates are good for.</summary>
        private static readonly TimeSpan  OwnKeyValidity             = TimeSpan.FromDays(3653);

        #endregion

        #region Properties

        /// <summary>What the drivers' account certificates are signed by.</summary>
        public SigningKey                  AccountCA            { get; private set; } = default!;

        /// <summary>Every account certificate issued.</summary>
        public AccountCertificateRegistry  AccountCertificates  { get; private set; } = default!;

        /// <summary>What the charging tickets are signed by.</summary>
        public SigningKey                  TicketIssuer         { get; private set; } = default!;

        /// <summary>Every charging ticket signed.</summary>
        public IssuedTicketRegistry        IssuedTickets        { get; private set; } = default!;

        /// <summary>Who this EMSP is in a charging ticket: "DE*GDF".</summary>
        public String                      TicketParty
            => $"{PartyId.CountryCode}*{PartyId.PartyId}";

        #endregion


        #region (private) BuildTickets()

        /// <summary>
        /// The account CA and the ticket issuer, read or made below "pki", and
        /// the registries of what they signed.
        /// </summary>
        private void BuildTickets()
        {

            var now          = TimeProvider.GetUtcNow();

            X500DistinguishedName Subject(String CommonName)
            {
                var builder = new X500DistinguishedNameBuilder();
                builder.AddCountryOrRegion(PartyId.CountryCode.ToString());
                builder.AddOrganizationName(BusinessDetails.Name);
                builder.AddCommonName(CommonName);
                return builder.Build();
            }

            AccountCA            = SigningKey.OpenOrCreate(
                                       Path.Combine(PKIDirectory, AccountsDirectoryName),
                                       "account_ca",
                                       Subject($"{BusinessDetails.Name} Account CA"),
                                       IsAuthority:  true,
                                       OwnKeyValidity,
                                       now
                                   );

            AccountCertificates  = new AccountCertificateRegistry(Path.Combine(PKIDirectory, AccountsDirectoryName, "certificates"));

            TicketIssuer         = SigningKey.OpenOrCreate(
                                       Path.Combine(PKIDirectory, TicketsDirectoryName),
                                       "ticket_issuer",
                                       Subject($"{BusinessDetails.Name} Charging Ticket Issuer"),
                                       IsAuthority:  false,
                                       OwnKeyValidity,
                                       now
                                   );

            IssuedTickets        = new IssuedTicketRegistry(Path.Combine(PKIDirectory, TicketsDirectoryName, "issued"));

            Log.Log(
                TicketIssuer.WasCreated ? LogLevel.Notice : LogLevel.Info,
                TicketIssuer.WasCreated
                    ? $"A charging ticket issuer was made for {TicketParty}, fingerprint {TicketIssuer.Fingerprint}. Hand '{TicketIssuer.CertificatePath}' to every CPO that should believe this EMSP's charging tickets."
                    : $"The charging ticket issuer (fingerprint {TicketIssuer.Fingerprint}) and the account CA were read; {AccountCertificates.All.Count} account certificate(s) and {IssuedTickets.All.Count} ticket(s) on record.",
                "tickets", "pki"
            );

        }

        #endregion


        #region IssueAccountCertificateAsync(User, CSR, Label)

        /// <summary>
        /// An account certificate for the key in a driver's signing request:
        /// made out to the account, good for two years from a minute ago.
        /// </summary>
        public Task<OCPIOperationResult> IssueAccountCertificateAsync(IUser    User,
                                                                      String?  CSR,
                                                                      String?  Label)
        {

            if (String.IsNullOrWhiteSpace(CSR))
                return Task.FromResult(OCPIOperationResult.Failed("A 'csr' is required: a PKCS#10 certificate signing request in PEM, signed with the key it carries."));

            var label = Label?.Trim() is { Length: > 0 } trimmed ? trimmed : null;

            if (label is not null && (label.Length > 64 || label.Any(Char.IsControl)))
                return Task.FromResult(OCPIOperationResult.Failed("A key's label is one line of 64 characters at the most."));

            CertificateRequest request;

            try
            {
                // Its signature checked: the proof that whoever sent it holds the key.
                request = CertificateRequest.LoadSigningRequestPem(CSR, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.Default);
            }
            catch (Exception e) when (e is CryptographicException or ArgumentException)
            {
                return Task.FromResult(OCPIOperationResult.Failed($"The signing request could not be read, or its signature is not by the key it carries: {e.Message}"));
            }

            using (var key = request.PublicKey.GetECDsaPublicKey())
            {
                if (key is null || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                    return Task.FromResult(OCPIOperationResult.Failed("An account key is an elliptic curve key on P-256."));
            }

            var now        = TimeProvider.GetUtcNow();

            var subject    = new X500DistinguishedNameBuilder();
            subject.AddOrganizationName(BusinessDetails.Name);
            subject.AddOrganizationalUnitName("EV drivers");
            subject.AddCommonName(User.Id.ToString());

            var toSign     = new CertificateRequest(subject.Build(), request.PublicKey, HashAlgorithmName.SHA256);

            toSign.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            toSign.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            toSign.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            toSign.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(AccountCA.Certificate, true, false));

            var serial     = RandomNumberGenerator.GetBytes(16);
            serial[0]     &= 0x7F;

            using var certificate = toSign.Create(
                                        AccountCA.Certificate.SubjectName,
                                        X509SignatureGenerator.CreateForECDsa(AccountCA.Key),
                                        now.AddMinutes(-1),
                                        now.Add(AccountCertificateValidity),
                                        serial
                                    );

            var pem        = certificate.ExportCertificatePem() + "\n";

            var record     = new AccountCertificate(
                                 Convert.ToHexString(SHA256.HashData(certificate.RawData)),
                                 User.Id.ToString(),
                                 label,
                                 Convert.ToHexString(serial),
                                 now.AddMinutes(-1),
                                 now.Add(AccountCertificateValidity),
                                 now
                             );

            if (!AccountCertificates.TryAdd(record, pem, out var notKept))
            {
                Log.Error($"The account certificate for '{User.Id}' could not be kept, so it was not handed out: {notKept}", "tickets", "pki");
                return Task.FromResult(OCPIOperationResult.Failed("No account certificate was issued: this EMSP could not write it down. Ask its operator.", NotSaved: true));
            }

            Log.Notice($"An account certificate was issued to '{User.Id}'{(label is not null ? $" for the key '{label}'" : "")}, good until {record.NotAfter:yyyy-MM-dd}, fingerprint {record.Id}.", "tickets", "pki");

            var json = record.ToJSON(now);
            json["certificate"] = pem;

            return Task.FromResult(
                       OCPIOperationResult.Ok(
                           $"An account certificate was issued, good until {record.NotAfter:yyyy-MM-dd}.",
                           new JObject(
                               new JProperty("accountCertificate",  json),
                               new JProperty("certificate",         pem),
                               new JProperty("ca",                  AccountCA.CertificatePEM)
                           )
                       )
                   );

        }

        #endregion

        #region RevokeAccountCertificateAsync(Id, By)

        /// <summary>
        /// An account certificate taken back: no ticket is signed on its word
        /// from now on. The tickets signed before stay what they are.
        /// </summary>
        public Task<OCPIOperationResult> RevokeAccountCertificateAsync(String  Id,
                                                                       IUser   By)
        {

            if (!AccountCertificates.TryRevoke(Id, By.Id.ToString(), TimeProvider.GetUtcNow(), out var revoked, out var error, out var notSaved))
            {

                if (notSaved)
                    Log.Error($"'{By.Id}' took the account certificate {Id} back, and that could not be written down, so it is still good: {error}", "tickets", "pki");

                return Task.FromResult(
                           notSaved
                               ? OCPIOperationResult.Failed($"The account certificate {Id} was not taken back and is still good: this EMSP could not write that down.", NotSaved: true)
                               : OCPIOperationResult.Failed(error)
                       );

            }

            Log.Notice($"'{By.Id}' took the account certificate {revoked.Id} of '{revoked.Owner}' back.", "tickets", "pki");

            return Task.FromResult(OCPIOperationResult.Ok($"The account certificate was taken back; no ticket is signed on its word any more."));

        }

        #endregion

        #region AccountCertificatesJSON(Owner, Everyone)

        /// <summary>
        /// The account certificates as the web interface reads them - one
        /// account's, or everybody's - with the account CA.
        /// </summary>
        public JObject AccountCertificatesJSON(String?  Owner,
                                               Boolean  Everyone)
        {

            var now           = TimeProvider.GetUtcNow();
            var certificates  = Everyone ? AccountCertificates.All : AccountCertificates.OfOwner(Owner ?? "");

            return new JObject(
                       new JProperty("everyone",      Everyone),
                       new JProperty("validityDays",  (Int32) AccountCertificateValidity.TotalDays),
                       new JProperty("ca",            OwnKeyJSON(AccountCA)),
                       new JProperty("certificates",  new JArray(certificates.Select(certificate => {
                           var json = certificate.ToJSON(now);
                           if (AccountCertificates.TryReadPEM(certificate, out var pem))
                               json["certificate"] = pem;
                           return json;
                       })))
                   );

        }

        #endregion


        #region IssueTicketAsync(User, Request)

        /// <summary>
        /// A charging ticket a driver's browser wrote and signed twice, checked
        /// and signed by this EMSP - the account's signature taken off.
        /// </summary>
        /// <param name="User">Who asks: the account whose key signed it.</param>
        /// <param name="Request">The COSE_Sign, as CBOR.</param>
        public Task<OCPIOperationResult> IssueTicketAsync(IUser   User,
                                                          Byte[]  Request)
        {

            var now = TimeProvider.GetUtcNow();

            #region What it is

            if (!COSE.TryDecodeSign(Request, out var message, out var error))
                return Refused(error);

            if (!ChargingTicket.TryDecode(message.Payload, out var ticket, out error))
                return Refused(error);

            if (ticket.EMSP != TicketParty)
                return Refused($"The ticket is for '{ticket.EMSP}', and this EMSP is {TicketParty}.");

            if (ticket.NotAfter <= ticket.NotBefore)
                return Refused("The ticket ends before it begins.");

            if (ticket.NotAfter <= now)
                return Refused("The ticket is over already.");

            if (ticket.NotBefore < now - TicketClockSkew)
                return Refused($"The ticket begins in the past: from now on, or later. Is the clock of the device that made it {(now - ticket.NotBefore).TotalMinutes:0} minutes behind?");

            if (ticket.NotAfter - ticket.NotBefore > MaxTicketValidity)
                return Refused($"A ticket is good for {MaxTicketValidity.TotalDays:0} days at the most.");

            if (ticket.NotBefore > now + MaxTicketValidity)
                return Refused($"A ticket begins within {MaxTicketValidity.TotalDays:0} days.");

            if (IssuedTickets.Contains(ticket.IdText))
                return Refused($"A ticket {ticket.IdText} was signed before: every ticket has an id of its own.");

            #endregion

            #region Who signed it

            if (message.Signatures.Count != 2)
                return Refused("A ticket comes signed twice: with its own key, and with one of the account's keys.");

            var own      = message.Signatures.FirstOrDefault(signature => signature.KeyId is not null && signature.KeyId.SequenceEqual(ChargingTicket.TicketKeyId));
            var account  = message.Signatures.FirstOrDefault(signature => signature != own);

            if (own is null || account is null)
                return Refused("A ticket comes signed twice: with its own key (kid \"ticket\"), and with one of the account's keys (kid: the SHA-256 of its certificate).");

            using (var ticketKey = ECDsa.Create(ticket.Key))
            {
                if (!COSE.Verifies(ticketKey, message, own))
                    return Refused("The ticket's own signature is not by its key.");
            }

            if (account.KeyId is not { Length: 32 } || !AccountCertificates.TryGet(Convert.ToHexString(account.KeyId), out var certificate) ||
                !String.Equals(certificate.Owner, User.Id.ToString(), StringComparison.OrdinalIgnoreCase))
                return Refused("The ticket is not signed by a key of yours: the second signature names no account certificate of this account.");

            if (!certificate.IsValid(now))
                return Refused($"The account certificate it is signed with is {certificate.Status(now)}.");

            if (!AccountCertificates.TryReadPEM(certificate, out var pem))
                return Refused("The account certificate it is signed with could not be read. Ask the operator of this EMSP.");

            using (var accountCertificate = X509Certificate2.CreateFromPem(pem))
            using (var accountKey = accountCertificate.GetECDsaPublicKey())
            {
                if (accountKey is null || !COSE.Verifies(accountKey, message, account))
                    return Refused("The account's signature is not by the key of the account certificate it names.");
            }

            #endregion

            #region Signed, the account's signature taken off

            var issued   = new COSESign(
                               message.Protected,
                               message.Payload,
                               [ own, COSE.Sign(TicketIssuer.Key, TicketIssuer.KeyId, message.Protected, message.Payload) ]
                           );

            var record   = new IssuedTicket(
                               ticket.IdText,
                               User.Id.ToString(),
                               certificate.Id,
                               ticket.NotBefore,
                               ticket.NotAfter,
                               ticket.MaxKW,
                               ticket.MaxMinutes,
                               ticket.MaxKWh,
                               now
                           );

            if (!IssuedTickets.TryAdd(record, out error, out var notSaved))
            {
                if (notSaved)
                    Log.Error($"A charging ticket for '{User.Id}' could not be written down, so it was not signed: {error}", "tickets");
                return Task.FromResult(OCPIOperationResult.Failed(notSaved ? "No ticket was signed: this EMSP could not write it down. Ask its operator." : error, notSaved));
            }

            Log.Notice($"A charging ticket was signed for '{User.Id}', good from {ticket.NotBefore:yyyy-MM-dd HH:mm} until {ticket.NotAfter:yyyy-MM-dd HH:mm} UTC.", "tickets");

            var bytes    = COSE.EncodeSign(issued);

            return Task.FromResult(
                       OCPIOperationResult.Ok(
                           "The ticket was signed.",
                           new JObject(
                               new JProperty("ticket",   Convert.ToBase64String(bytes)),
                               new JProperty("record",   record.ToJSON(now)),
                               new JProperty("issuer",   TicketIssuer.Fingerprint)
                           )
                       )
                   );

            #endregion

            Task<OCPIOperationResult> Refused(String Why)
            {
                Log.Info($"'{User.Id}' asked for a charging ticket and was refused: {Why}", "tickets");
                return Task.FromResult(OCPIOperationResult.Failed(Why));
            }

        }

        #endregion

        #region TicketsJSON(Owner, Everyone)

        /// <summary>
        /// The charging tickets as the web interface reads them - one
        /// account's, or everybody's - with the ticket issuer a CPO believes
        /// them by.
        /// </summary>
        public JObject TicketsJSON(String?  Owner,
                                   Boolean  Everyone)
        {

            var now      = TimeProvider.GetUtcNow();
            var tickets  = Everyone ? IssuedTickets.All : IssuedTickets.OfOwner(Owner ?? "");

            return new JObject(
                       new JProperty("everyone",         Everyone),
                       new JProperty("party",            TicketParty),
                       new JProperty("maxValidityDays",  (Int32) MaxTicketValidity.TotalDays),
                       new JProperty("issuer",           OwnKeyJSON(TicketIssuer)),
                       new JProperty("tickets",          new JArray(tickets.Select(ticket => ticket.ToJSON(now))))
                   );

        }

        #endregion


        #region (private static) OwnKeyJSON(Key)

        private static JObject OwnKeyJSON(SigningKey Key)

            => new (
                   new JProperty("subject",      Key.Certificate.Subject),
                   new JProperty("fingerprint",  Key.Fingerprint),
                   new JProperty("notAfter",     new DateTimeOffset(Key.Certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero).ToString("o")),
                   new JProperty("pem",          Key.CertificatePEM),
                   new JProperty("file",         Key.CertificatePath)
               );

        #endregion

    }

}
