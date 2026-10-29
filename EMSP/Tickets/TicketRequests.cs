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
using System.Security.Cryptography;

using org.GraphDefined.Vanaheimr.Illias;

#endregion

namespace cloud.charging.open.EMSP.Tickets
{

    /// <summary>
    /// A start or a stop request the holder of a charging ticket made: the
    /// payload of a COSE_Sign signed by the ticket's key alone, which this
    /// EMSP countersigns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A CBOR map with text keys, the body's protected header naming which of
    /// the two it is:
    /// </para>
    /// <code>
    /// {3: "application/start-request+cbor"}
    /// {
    ///   "typ":    "StartRequest",
    ///   "v":      1,
    ///   "ticket": h'SHA-256 of the ticket, as this EMSP issued it',
    ///   "evse":   "DE*GEF*E2*1",
    ///   "at":     1793200000,                ; seconds since 1970
    ///   "nonce":  h'16 random bytes',
    ///   "proof":  h'...',                    ; optional
    ///   "tariff": "DE*GEF*T_DC"              ; optional
    /// }
    /// </code>
    /// <para>
    /// A stop request is a "StopRequest" of "application/stop-request+cbor",
    /// naming the start it ends by the SHA-256 of the start as this EMSP
    /// countersigned it ("start") rather than an EVSE.
    /// </para>
    /// <para>
    /// What this EMSP adds goes into the protected header of its own
    /// signature, where it is covered by that signature alone and the
    /// ticket key's stays good: the token of this start, the authorization
    /// reference, and when it signed. Nothing of what came is written anew -
    /// the body's protected header, the payload and the ticket key's signature
    /// are the bytes as they came.
    /// </para>
    /// </remarks>
    public sealed class TicketRequest
    {

        #region Data

        /// <summary>What "typ" says of a start request, and of a stop request.</summary>
        public const String  StartType          = "StartRequest";
        public const String  StopType           = "StopRequest";

        /// <summary>The version of the payload.</summary>
        public const Int32   Version            = 1;

        /// <summary>The content types the body's protected header names.</summary>
        public const String  StartContentType   = "application/start-request+cbor";
        public const String  StopContentType    = "application/stop-request+cbor";

        /// <summary>The labels of what this EMSP adds to the protected header of its signature.</summary>
        public const String  TokenLabel         = "token";
        public const String  AuthRefLabel       = "authRef";
        public const String  SignedAtLabel      = "iat";

        /// <summary>How far a request's "at" may be from this EMSP's clock, either way.</summary>
        public static readonly TimeSpan  ClockSkew  = TimeSpan.FromMinutes(5);

        #endregion

        #region Properties

        /// <summary>The COSE_Sign, as it was read.</summary>
        public COSESign        Message   { get; }

        /// <summary>"StartRequest" or "StopRequest".</summary>
        public String          Type      { get; }

        /// <summary>The SHA-256 of the ticket it names.</summary>
        public Byte[]          Ticket    { get; }

        /// <summary>When its holder asked.</summary>
        public DateTimeOffset  At        { get; }

        /// <summary>Sixteen random bytes, used once.</summary>
        public Byte[]          Nonce     { get; }

        /// <summary>The EVSE a start request names, as the roaming network names it; null in a stop request.</summary>
        public String?         EVSE      { get; }

        /// <summary>The SHA-256 of the countersigned start a stop request ends; null in a start request.</summary>
        public Byte[]?         Start     { get; }

        /// <summary>The nonce as hex, upper case.</summary>
        public String          NonceText
            => Convert.ToHexString(Nonce);

        #endregion

        #region Constructor(s)

        private TicketRequest(COSESign        Message,
                              String          Type,
                              Byte[]          Ticket,
                              DateTimeOffset  At,
                              Byte[]          Nonce,
                              String?         EVSE,
                              Byte[]?         Start)
        {
            this.Message  = Message;
            this.Type     = Type;
            this.Ticket   = Ticket;
            this.At       = At;
            this.Nonce    = Nonce;
            this.EVSE     = EVSE;
            this.Start    = Start;
        }

        #endregion


        #region (static) TryParse(Bytes, Type, out Request, out Error)

        /// <summary>
        /// A start request ("StartRequest") or a stop request ("StopRequest"):
        /// a COSE_Sign of its content type, its payload of its type and
        /// version, with what that type has to name.
        /// </summary>
        public static Boolean TryParse(Byte[]                                   Bytes,
                                       String                                   Type,
                                       [NotNullWhen(true)]  out TicketRequest?  Request,
                                       [NotNullWhen(false)] out String?         Error)
        {

            Request = null;

            var what         = Type == StartType ? "start request" : "stop request";
            var contentType  = Type == StartType ? StartContentType : StopContentType;

            if (!COSESign.TryParse(Bytes, out var message, out var error))
            {
                Error = $"The {what} is no COSE_Sign: {error}";
                return false;
            }

            if (message.Payload is null)
            {
                Error = $"A {what} carries its payload: it is not detached.";
                return false;
            }

            if (message.ProtectedHeader.ContentType?.TryGetText(out var named) != true || named != contentType)
            {
                Error = $"The body's protected header of a {what} says it is \"{contentType}\".";
                return false;
            }

            try
            {

                String?  type    = null;
                Int64?   version = null;
                Byte[]?  ticket  = null;
                Int64?   at      = null;
                Byte[]?  nonce   = null;
                String?  evse    = null;
                Byte[]?  start   = null;

                foreach (var (name, value) in CBORValue.Parse(message.Payload).AsMap())
                {

                    if (!name.TryGetText(out var text))
                        continue;

                    switch (text)
                    {
                        case "typ":     type     = value.AsText();   break;
                        case "v":       version  = value.AsInt64();  break;
                        case "ticket":  ticket   = value.AsBytes();  break;
                        case "at":      at       = value.AsInt64();  break;
                        case "nonce":   nonce    = value.AsBytes();  break;
                        case "evse":    evse     = value.AsText();   break;
                        case "start":   start    = value.AsBytes();  break;
                    }

                }

                if (type != Type || version != Version)
                {
                    Error = $"It is no {Type} of version {Version}: \"typ\" has to say \"{Type}\" and \"v\" {Version}.";
                    return false;
                }

                if (ticket is not { Length: 32 })
                {
                    Error = $"A {what} names its ticket in \"ticket\": the SHA-256 of the ticket.";
                    return false;
                }

                if (at is null)
                {
                    Error = $"A {what} says when it was made, in \"at\".";
                    return false;
                }

                if (nonce is not { Length: 16 })
                {
                    Error = $"A {what}'s \"nonce\" is 16 random bytes.";
                    return false;
                }

                if (Type == StartType && String.IsNullOrWhiteSpace(evse))
                {
                    Error = "A start request names its EVSE in \"evse\".";
                    return false;
                }

                if (Type == StopType && start is not { Length: 32 })
                {
                    Error = "A stop request names the start it ends in \"start\": the SHA-256 of the start as this EMSP countersigned it.";
                    return false;
                }

                Request = new TicketRequest(
                              message,
                              Type,
                              ticket,
                              DateTimeOffset.FromUnixTimeSeconds(at.Value),
                              nonce,
                              Type == StartType ? evse!.Trim() : null,
                              Type == StopType  ? start        : null
                          );

                Error = null;
                return true;

            }
            catch (Exception e) when (e is CBORException or InvalidOperationException or FormatException or OverflowException or ArgumentOutOfRangeException)
            {
                Error = $"It is no {what}: {e.Message}";
                return false;
            }

        }

        #endregion

        #region IsSignedBy(TicketKey)

        /// <summary>
        /// Whether it is signed by the ticket's key, and by nobody else: one
        /// signature, kid "ticket" in its protected header, ES256.
        /// </summary>
        public Boolean IsSignedBy(COSEKey TicketKey)

            => Message.Signatures.Count == 1 &&
               ChargingTicket.KeyIdOf(Message.Signatures[0])?.SequenceEqual(ChargingTicket.TicketKeyId) == true &&
               ChargingTicket.Verifies(TicketKey.ToPublicKey(), Message, Message.Signatures[0]);

        #endregion

        #region Countersign(Issuer, Token, AuthRef, SignedAt)

        /// <summary>
        /// The request with this EMSP's signature added: ES256, kid the
        /// SHA-256 of the ticket issuer's certificate, the token, the
        /// authorization reference and when it signed in its protected header.
        /// </summary>
        public Byte[] Countersign(SigningKey      Issuer,
                                  String          Token,
                                  String          AuthRef,
                                  DateTimeOffset  SignedAt)

            => Message.AddSignature(
                   Issuer.COSEKey,
                   ChargingTicket.SignatureHeader(Issuer.KeyId).
                       Set(CBORValue.FromText(TokenLabel),     CBORValue.FromText(Token)).
                       Set(CBORValue.FromText(AuthRefLabel),   CBORValue.FromText(AuthRef)).
                       Set(CBORValue.FromText(SignedAtLabel),  CBORValue.FromInt64(SignedAt.ToUnixTimeSeconds()))
               ).ToByteArray();

        #endregion

        #region (static) Digest(Bytes)

        /// <summary>How a ticket or a start is named: the SHA-256 of its bytes, as hex, upper case.</summary>
        public static String Digest(Byte[] Bytes)
            => Convert.ToHexString(SHA256.HashData(Bytes));

        #endregion

    }

}
