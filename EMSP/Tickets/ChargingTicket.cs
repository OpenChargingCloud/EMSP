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
using System.Formats.Cbor;
using System.Security.Cryptography;

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
    /// </remarks>
    /// <param name="Id">Sixteen random bytes, chosen by the driver's browser, unique at this EMSP.</param>
    /// <param name="EMSP">The EMSP it is good with, as its party: "DE*GDF".</param>
    /// <param name="Key">The ticket's public key, on P-256.</param>
    /// <param name="NotBefore">From when.</param>
    /// <param name="NotAfter">Until when.</param>
    /// <param name="MaxKW">The most power a charge with it may draw, or null.</param>
    /// <param name="MaxMinutes">The longest a charge with it may take, or null.</param>
    /// <param name="MaxKWh">The most energy a charge with it may take, or null.</param>
    public sealed record ChargingTicket(Byte[]          Id,
                                        String          EMSP,
                                        ECParameters    Key,
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

        #endregion

        #region Properties

        /// <summary>The identifier as hex, upper case.</summary>
        public String  IdText
            => Convert.ToHexString(Id);

        #endregion


        #region Encode()

        /// <summary>
        /// The payload as canonical CBOR.
        /// </summary>
        public Byte[] Encode()
        {

            var limits  = new List<(String, Action<CborWriter>)>();

            if (MaxKW      is Decimal kW)       limits.Add(("kW",      w => WriteNumber(w, kW)));
            if (MaxMinutes is UInt32  minutes)  limits.Add(("minutes", w => w.WriteUInt32(minutes)));
            if (MaxKWh     is Decimal kWh)      limits.Add(("kWh",     w => WriteNumber(w, kWh)));

            var writer  = new CborWriter(CborConformanceMode.Canonical);

            writer.WriteStartMap(limits.Count > 0 ? 8 : 7);

            writer.WriteTextString("typ");   writer.WriteTextString(Type);
            writer.WriteTextString("v");     writer.WriteInt32(Version);
            writer.WriteTextString("id");    writer.WriteByteString(Id);
            writer.WriteTextString("emsp");  writer.WriteTextString(EMSP);
            writer.WriteTextString("key");   COSE.WriteKey(writer, Key);
            writer.WriteTextString("nbf");   writer.WriteInt64(NotBefore.ToUnixTimeSeconds());
            writer.WriteTextString("exp");   writer.WriteInt64(NotAfter. ToUnixTimeSeconds());

            if (limits.Count > 0)
            {
                writer.WriteTextString("limits");
                writer.WriteStartMap(limits.Count);
                foreach (var (name, write) in limits)
                {
                    writer.WriteTextString(name);
                    write(writer);
                }
                writer.WriteEndMap();
            }

            writer.WriteEndMap();

            return writer.Encode();

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

                var reader   = new CborReader(Payload, CborConformanceMode.Lax);

                String?        type       = null;
                Int64?         version    = null;
                Byte[]?        id         = null;
                String?        emsp       = null;
                ECParameters?  key        = null;
                Int64?         notBefore  = null;
                Int64?         notAfter   = null;
                Decimal?       maxKW      = null;
                UInt32?        maxMinutes = null;
                Decimal?       maxKWh     = null;

                var count    = reader.ReadStartMap();

                for (var i = 0; count is null ? reader.PeekState() != CborReaderState.EndMap : i < count; i++)
                {

                    if (reader.PeekState() != CborReaderState.TextString)
                    {
                        reader.SkipValue();
                        reader.SkipValue();
                        continue;
                    }

                    switch (reader.ReadTextString())
                    {

                        case "typ":   type       = reader.ReadTextString();  break;
                        case "v":     version    = reader.ReadInt64();       break;
                        case "id":    id         = reader.ReadByteString();  break;
                        case "emsp":  emsp       = reader.ReadTextString();  break;
                        case "nbf":   notBefore  = reader.ReadInt64();       break;
                        case "exp":   notAfter   = reader.ReadInt64();       break;

                        case "key":
                            if (!COSE.TryReadKey(reader, out var parsed, out Error))
                                return false;
                            key = parsed;
                            break;

                        case "limits":
                            if (!TryReadLimits(reader, out maxKW, out maxMinutes, out maxKWh, out Error))
                                return false;
                            break;

                        default:
                            reader.SkipValue();
                            break;

                    }

                }

                reader.ReadEndMap();

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
                             key.Value,
                             DateTimeOffset.FromUnixTimeSeconds(notBefore.Value),
                             DateTimeOffset.FromUnixTimeSeconds(notAfter. Value),
                             maxKW,
                             maxMinutes,
                             maxKWh
                         );

                Error = null;
                return true;

            }
            catch (Exception e) when (e is CborContentException or InvalidOperationException or FormatException or OverflowException or ArgumentOutOfRangeException)
            {
                Error = $"It is no charging ticket: {e.Message}";
                return false;
            }

        }

        #endregion


        #region (private static) TryReadLimits(...)

        private static Boolean TryReadLimits(CborReader                         Reader,
                                             out Decimal?                       MaxKW,
                                             out UInt32?                        MaxMinutes,
                                             out Decimal?                       MaxKWh,
                                             [NotNullWhen(false)] out String?   Error)
        {

            MaxKW       = null;
            MaxMinutes  = null;
            MaxKWh      = null;

            var count   = Reader.ReadStartMap();

            for (var i = 0; count is null ? Reader.PeekState() != CborReaderState.EndMap : i < count; i++)
            {

                var name = Reader.PeekState() == CborReaderState.TextString ? Reader.ReadTextString() : null;

                switch (name)
                {
                    case "kW":       MaxKW       = ReadNumber(Reader);    break;
                    case "minutes":  MaxMinutes  = Reader.ReadUInt32();   break;
                    case "kWh":      MaxKWh      = ReadNumber(Reader);    break;
                    default:
                        if (name is null) Reader.SkipValue();
                        Reader.SkipValue();
                        break;
                }

            }

            Reader.ReadEndMap();

            if (MaxKW is <= 0 || MaxKWh is <= 0 || MaxMinutes is 0)
            {
                Error = "A charging ticket's limits are more than nothing: kW, minutes and kWh above 0.";
                return false;
            }

            Error = null;
            return true;

        }

        #endregion

        #region (private static) ReadNumber(Reader) / WriteNumber(Writer, Value)

        /// <summary>A number, as an integer or a float - JavaScript writes 22 as one and 22.5 as the other.</summary>
        private static Decimal ReadNumber(CborReader Reader)

            => Reader.PeekState() switch {
                   CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger  => Reader.ReadInt64(),
                   CborReaderState.HalfPrecisionFloat                                   => (Decimal) (Double) Reader.ReadHalf(),
                   CborReaderState.SinglePrecisionFloat                                 => (Decimal) Reader.ReadSingle(),
                   CborReaderState.DoublePrecisionFloat                                 => (Decimal) Reader.ReadDouble(),
                   _                                                                    => throw new FormatException("A limit is a number.")
               };

        private static void WriteNumber(CborWriter Writer, Decimal Value)
        {
            if (Value == Decimal.Truncate(Value))
                Writer.WriteInt64((Int64) Value);
            else
                Writer.WriteDouble((Double) Value);
        }

        #endregion

    }

}
