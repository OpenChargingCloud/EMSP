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

using Org.BouncyCastle.Crypto;

using org.GraphDefined.Vanaheimr.Illias;

#endregion

namespace cloud.charging.open.EMSP.Tickets
{

    /// <summary>
    /// What a charging ticket says - the payload of its COSE_Sign: that
    /// whoever holds the private half of <see cref="Key"/> may charge, between
    /// two moments, within limits, on the word of the EMSP that signed it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A CBOR map with text keys - a certificate of its own kind, in CBOR
    /// rather than ASN.1:
    /// </para>
    /// <code>
    /// {
    ///   "typ":    "ChargingTicket",
    ///   "v":      1,
    ///   "id":     h'16 random bytes',
    ///   "emsp":   "DE*GDF",
    ///   "key":    { 1: 2, -1: 1, -2: h'x', -3: h'y' },     ; COSE_Key, P-256
    ///   "nbf":    1791560000,                              ; seconds since 1970
    ///   "exp":    1791646400,
    ///   "limits": { "kW": 22, "minutes": 120, "kWh": 40 }  ; each optional
    /// }
    /// </code>
    /// <para>
    /// Nothing in it says who the driver is: the ticket key is made for this
    /// ticket alone, and the driver's own signature is taken off before the
    /// EMSP signs it.
    /// </para>
    /// <para>
    /// CBOR and COSE (RFC 9052, 9053) are Styx's: what is signed are the
    /// bytes as they came, so how a browser chose to encode a map does not
    /// change what is checked. Written deterministically.
    /// </para>
    /// </remarks>
    /// <param name="Id">Sixteen random bytes, chosen by the driver's browser, unique at this EMSP.</param>
    /// <param name="EMSP">The EMSP it is good with, as its party: "DE*GDF".</param>
    /// <param name="Key">The ticket's public key, on P-256, as a COSE_Key.</param>
    /// <param name="NotBefore">From when.</param>
    /// <param name="NotAfter">Until when.</param>
    /// <param name="MaxKW">The most power a charge with it may draw, or null.</param>
    /// <param name="MaxMinutes">The longest a charge with it may take, or null.</param>
    /// <param name="MaxKWh">The most energy a charge with it may take, or null.</param>
    public sealed record ChargingTicket(Byte[]          Id,
                                        String          EMSP,
                                        COSEKey         Key,
                                        DateTimeOffset  NotBefore,
                                        DateTimeOffset  NotAfter,
                                        Decimal?        MaxKW       = null,
                                        UInt32?         MaxMinutes  = null,
                                        Decimal?        MaxKWh      = null)
    {

        #region Data

        /// <summary>What "typ" says.</summary>
        public const String  Type          = "ChargingTicket";

        /// <summary>The version of the payload.</summary>
        public const Int32   Version       = 1;

        /// <summary>The content type the body's protected header names.</summary>
        public const String  ContentType   = "application/charging-ticket+cbor";

        /// <summary>
        /// The key identifier a ticket's own signature carries: the key in the
        /// payload, which needs no other name.
        /// </summary>
        public static readonly Byte[]  TicketKeyId  = "ticket"u8.ToArray();

        /// <summary>
        /// The body's protected header of a ticket, and of a request for one:
        /// { 3: "application/charging-ticket+cbor" }.
        /// </summary>
        public static COSEHeaders  BodyHeader  { get; } = new ((COSEHeaderLabel.ContentType, CBORValue.FromText(ContentType)));

        /// <summary>How a payload is written: deterministically, RFC 8949, Section 4.2.1.</summary>
        private static readonly CBORWriterOptions  Deterministic  = new () { Deterministic = true };

        #endregion

        #region Properties

        /// <summary>The identifier as hex, upper case.</summary>
        public String  IdText
            => Convert.ToHexString(Id);

        #endregion


        #region Encode()

        /// <summary>
        /// The payload as deterministic CBOR.
        /// </summary>
        public Byte[] Encode()
        {

            var limits   = new List<KeyValuePair<CBORValue, CBORValue>>();

            if (MaxKW      is Decimal kW)       limits.Add(new ("kW",      Number(kW)));
            if (MaxMinutes is UInt32  minutes)  limits.Add(new ("minutes", CBORValue.FromUInt64(minutes)));
            if (MaxKWh     is Decimal kWh)      limits.Add(new ("kWh",     Number(kWh)));

            var payload  = new List<KeyValuePair<CBORValue, CBORValue>> {
                               new ("typ",   Type),
                               new ("v",     Version),
                               new ("id",    Id),
                               new ("emsp",  EMSP),
                               new ("key",   Key.ToCBOR()),
                               new ("nbf",   NotBefore.ToUnixTimeSeconds()),
                               new ("exp",   NotAfter. ToUnixTimeSeconds())
                           };

            if (limits.Count > 0)
                payload.Add(new ("limits", CBORValue.FromMap(limits)));

            return CBORValue.FromMap(payload).ToByteArray(Deterministic);

        }

        #endregion

        #region (static) SignatureHeader(KeyId) / KeyIdOf(Signature)

        /// <summary>
        /// The protected header of an ES256 signature by the given key:
        /// { 1: -7, 4: kid }.
        /// </summary>
        public static COSEHeaders SignatureHeader(Byte[] KeyId)

            => COSEHeaders.Create(COSEAlgorithm.ES256, KeyId);

        /// <summary>
        /// Whose key a signature names in its protected header - a kid in its
        /// unprotected one is nothing it signed.
        /// </summary>
        public static Byte[]? KeyIdOf(COSESignature Signature)

            => Signature.ProtectedHeader.KeyIdentifier;

        #endregion

        #region (static) Verifies(Key, Message, Signature)

        /// <summary>
        /// Whether a signature of a COSE_Sign is an ES256 signature by the
        /// given key over its body and payload.
        /// </summary>
        public static Boolean Verifies(AsymmetricKeyParameter  Key,
                                       COSESign                Message,
                                       COSESignature           Signature)
        {
            try
            {
                return Message.Verify(Signature, Key, out _, ExpectedAlgorithm: COSEAlgorithm.ES256);
            }
            catch (Exception e) when (e is COSEException or CryptoException or ArgumentException or InvalidCastException)
            {
                return false;
            }
        }

        #endregion

        #region (static) TryDecode(Payload, out Ticket, out Error)

        /// <summary>
        /// A payload as a driver's browser wrote it.
        /// </summary>
        public static Boolean TryDecode(Byte[]                                   Payload,
                                        [NotNullWhen(true)]  out ChargingTicket? Ticket,
                                        [NotNullWhen(false)] out String?         Error)
        {

            Ticket = null;

            try
            {

                String?   type       = null;
                Int64?    version    = null;
                Byte[]?   id         = null;
                String?   emsp       = null;
                COSEKey?  key        = null;
                Int64?    notBefore  = null;
                Int64?    notAfter   = null;
                Decimal?  maxKW      = null;
                UInt32?   maxMinutes = null;
                Decimal?  maxKWh     = null;

                foreach (var (name, value) in CBORValue.Parse(Payload).AsMap())
                {

                    if (!name.TryGetText(out var text))
                        continue;

                    switch (text)
                    {

                        case "typ":   type       = value.AsText();   break;
                        case "v":     version    = value.AsInt64();  break;
                        case "id":    id         = value.AsBytes();  break;
                        case "emsp":  emsp       = value.AsText();   break;
                        case "nbf":   notBefore  = value.AsInt64();  break;
                        case "exp":   notAfter   = value.AsInt64();  break;

                        case "key":
                            if (!TryReadKey(value, out key, out Error))
                                return false;
                            break;

                        case "limits":
                            if (!TryReadLimits(value, out maxKW, out maxMinutes, out maxKWh, out Error))
                                return false;
                            break;

                    }

                }

                if (type != Type || version != Version)
                {
                    Error = $"It is no charging ticket of version {Version}: \"typ\" has to say \"{Type}\" and \"v\" {Version}.";
                    return false;
                }

                if (id is null || id.Length != 16)
                {
                    Error = "A charging ticket's \"id\" is 16 random bytes.";
                    return false;
                }

                if (String.IsNullOrWhiteSpace(emsp))
                {
                    Error = "A charging ticket names the EMSP it is good with in \"emsp\".";
                    return false;
                }

                if (key is null)
                {
                    Error = "A charging ticket carries its public key in \"key\".";
                    return false;
                }

                if (notBefore is null || notAfter is null)
                {
                    Error = "A charging ticket says from when (\"nbf\") and until when (\"exp\") it is good.";
                    return false;
                }

                Ticket = new ChargingTicket(
                             id,
                             emsp,
                             key,
                             DateTimeOffset.FromUnixTimeSeconds(notBefore.Value),
                             DateTimeOffset.FromUnixTimeSeconds(notAfter. Value),
                             maxKW,
                             maxMinutes,
                             maxKWh
                         );

                Error = null;
                return true;

            }
            catch (Exception e) when (e is CBORException or COSEException or InvalidOperationException or FormatException or OverflowException or ArgumentOutOfRangeException)
            {
                Error = $"It is no charging ticket: {e.Message}";
                return false;
            }

        }

        #endregion


        #region (private static) TryReadKey(Value, out Key, out Error)

        /// <summary>
        /// A public key on P-256 from a COSE_Key, checked to be a point on the
        /// curve.
        /// </summary>
        private static Boolean TryReadKey(CBORValue                          Value,
                                          [NotNullWhen(true)]  out COSEKey?  Key,
                                          [NotNullWhen(false)] out String?   Error)
        {

            if (!COSEKey.TryParse(Value, out Key, out _) ||
                Key.KeyType != COSEKeyType.EC2 || Key.Curve != COSECurve.P256 || Key.X?.Length != 32 || Key.Y?.Length != 32)
            {
                Key    = null;
                Error  = "The key is no COSE_Key of an elliptic curve key on P-256 (kty 2, crv 1, x and y of 32 bytes).";
                return false;
            }

            if (!Key.TryToPublicKey(out _, out _))
            {
                Key    = null;
                Error  = "The key is no point on P-256.";
                return false;
            }

            Error = null;
            return true;

        }

        #endregion

        #region (private static) TryReadLimits(...)

        private static Boolean TryReadLimits(CBORValue                          Value,
                                             out Decimal?                       MaxKW,
                                             out UInt32?                        MaxMinutes,
                                             out Decimal?                       MaxKWh,
                                             [NotNullWhen(false)] out String?   Error)
        {

            MaxKW       = null;
            MaxMinutes  = null;
            MaxKWh      = null;

            foreach (var (name, value) in Value.AsMap())
            {
                switch (name.TryGetText(out var text) ? text : null)
                {
                    case "kW":       MaxKW       = ReadNumber(value);                   break;
                    case "minutes":  MaxMinutes  = checked((UInt32) value.AsUInt64());  break;
                    case "kWh":      MaxKWh      = ReadNumber(value);                   break;
                }
            }

            if (MaxKW is <= 0 || MaxKWh is <= 0 || MaxMinutes is 0)
            {
                Error = "A charging ticket's limits are more than nothing: kW, minutes and kWh above 0.";
                return false;
            }

            Error = null;
            return true;

        }

        #endregion

        #region (private static) ReadNumber(Value) / Number(Value)

        /// <summary>A number, as an integer or a float - JavaScript writes 22 as one and 22.5 as the other.</summary>
        private static Decimal ReadNumber(CBORValue Value)

            => Value.Kind switch {
                   CBORValueKind.UnsignedInteger or CBORValueKind.NegativeInteger                     => Value.AsInt64(),
                   CBORValueKind.HalfFloat or CBORValueKind.SingleFloat or CBORValueKind.DoubleFloat  => (Decimal) Value.AsDouble(),
                   _                                                                                  => throw new FormatException("A limit is a number.")
               };

        /// <summary>A number as JavaScript writes it: whole as an integer, else as the shortest float that keeps it.</summary>
        private static CBORValue Number(Decimal Value)

            => Value == Decimal.Truncate(Value)
                   ? CBORValue.FromInt64((Int64) Value)
                   : CBORValue.FromDouble((Double) Value);

        #endregion

    }

}
