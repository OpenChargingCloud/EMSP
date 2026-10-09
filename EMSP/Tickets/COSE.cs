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
    /// One signature of a COSE_Sign: its protected header as it was encoded,
    /// what that header says - the algorithm and whose key - and the signature.
    /// </summary>
    /// <param name="Protected">The protected header, a CBOR map, as the bytes the signature covers.</param>
    /// <param name="Algorithm">The algorithm the protected header names: -7 for ES256.</param>
    /// <param name="KeyId">Whose key, as the protected header names it.</param>
    /// <param name="Value">The signature: for ES256, r and s of 32 bytes each.</param>
    public sealed record COSESignature(Byte[]   Protected,
                                       Int64?   Algorithm,
                                       Byte[]?  KeyId,
                                       Byte[]   Value);


    /// <summary>
    /// A COSE_Sign (RFC 9052, CBOR tag 98): a payload with any number of
    /// signatures, each by a key of its own.
    /// </summary>
    /// <param name="Protected">The body's protected header, as the bytes every signature covers.</param>
    /// <param name="Payload">The payload, as the bytes every signature covers.</param>
    /// <param name="Signatures">The signatures, in the order they came.</param>
    public sealed record COSESign(Byte[]                        Protected,
                                  Byte[]                        Payload,
                                  IReadOnlyList<COSESignature>  Signatures);


    /// <summary>
    /// The little of COSE (RFC 9052, 9053) a charging ticket needs: COSE_Sign
    /// with ES256 signatures - ECDSA on P-256 with SHA-256 - and a public key
    /// on P-256 written as a COSE_Key.
    /// </summary>
    /// <remarks>
    /// Read in CBOR's lax mode: what is signed are the bytes as they came, so
    /// how a browser chose to encode a map does not change what is checked.
    /// Written canonically.
    /// </remarks>
    public static class COSE
    {

        #region Data

        /// <summary>The CBOR tag of a COSE_Sign.</summary>
        public const UInt64  SignTag             = 98;

        /// <summary>Header labels: the algorithm, the content type, whose key.</summary>
        public const Int32   HeaderAlgorithm     = 1;
        public const Int32   HeaderContentType   = 3;
        public const Int32   HeaderKeyId         = 4;

        /// <summary>ECDSA on P-256 with SHA-256.</summary>
        public const Int64   ES256               = -7;

        /// <summary>COSE_Key labels and values for an EC2 key on P-256.</summary>
        public const Int32   KeyType             =  1;
        public const Int32   KeyTypeEC2          =  2;
        public const Int32   KeyCurve            = -1;
        public const Int32   CurveP256           =  1;
        public const Int32   KeyX                = -2;
        public const Int32   KeyY                = -3;

        #endregion


        #region TryDecodeSign(Bytes, out Message, out Error)

        /// <summary>
        /// A COSE_Sign, tagged 98 or not.
        /// </summary>
        public static Boolean TryDecodeSign(Byte[]                              Bytes,
                                            [NotNullWhen(true)]  out COSESign?  Message,
                                            [NotNullWhen(false)] out String?    Error)
        {

            Message = null;

            try
            {

                var reader = new CborReader(Bytes, CborConformanceMode.Lax);

                if (reader.PeekState() == CborReaderState.Tag && reader.ReadTag() != (CborTag) SignTag)
                {
                    Error = "It is tagged, and not as a COSE_Sign (98).";
                    return false;
                }

                if (reader.ReadStartArray() != 4)
                {
                    Error = "A COSE_Sign is an array of four: the protected header, the unprotected one, the payload and the signatures.";
                    return false;
                }

                var bodyProtected = reader.ReadByteString();
                reader.SkipValue();                               // the unprotected header
                var payload       = reader.ReadByteString();

                var count         = reader.ReadStartArray();
                var signatures    = new List<COSESignature>();

                for (var i = 0; count is null ? reader.PeekState() != CborReaderState.EndArray : i < count; i++)
                {

                    if (reader.ReadStartArray() != 3)
                    {
                        Error = "A COSE_Signature is an array of three: its protected header, its unprotected one and the signature.";
                        return false;
                    }

                    var signProtected  = reader.ReadByteString();
                    reader.SkipValue();                           // its unprotected header
                    var value          = reader.ReadByteString();

                    reader.ReadEndArray();

                    ReadHeader(signProtected, out var algorithm, out var keyId);

                    signatures.Add(new COSESignature(signProtected, algorithm, keyId, value));

                }

                reader.ReadEndArray();
                reader.ReadEndArray();

                if (reader.BytesRemaining != 0)
                {
                    Error = "There is more after the COSE_Sign.";
                    return false;
                }

                Message = new COSESign(bodyProtected, payload, signatures);
                Error   = null;
                return true;

            }
            catch (Exception e) when (e is CborContentException or InvalidOperationException or FormatException)
            {
                Error = $"It is no COSE_Sign: {e.Message}";
                return false;
            }

        }

        #endregion

        #region EncodeSign(Message)

        /// <summary>
        /// A COSE_Sign as CBOR, tagged 98, every unprotected header empty.
        /// </summary>
        public static Byte[] EncodeSign(COSESign Message)
        {

            var writer = new CborWriter(CborConformanceMode.Canonical);

            writer.WriteTag((CborTag) SignTag);
            writer.WriteStartArray(4);
            writer.WriteByteString(Message.Protected);
            writer.WriteStartMap(0);
            writer.WriteEndMap();
            writer.WriteByteString(Message.Payload);

            writer.WriteStartArray(Message.Signatures.Count);

            foreach (var signature in Message.Signatures)
            {
                writer.WriteStartArray(3);
                writer.WriteByteString(signature.Protected);
                writer.WriteStartMap(0);
                writer.WriteEndMap();
                writer.WriteByteString(signature.Value);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndArray();

            return writer.Encode();

        }

        #endregion

        #region SignatureHeader(KeyId) / BodyHeader(ContentType)

        /// <summary>
        /// The protected header of an ES256 signature by the given key:
        /// { 1: -7, 4: kid }.
        /// </summary>
        public static Byte[] SignatureHeader(Byte[] KeyId)
        {

            var writer = new CborWriter(CborConformanceMode.Canonical);

            writer.WriteStartMap(2);
            writer.WriteInt32(HeaderAlgorithm);
            writer.WriteInt64(ES256);
            writer.WriteInt32(HeaderKeyId);
            writer.WriteByteString(KeyId);
            writer.WriteEndMap();

            return writer.Encode();

        }

        /// <summary>
        /// The protected header of a body: { 3: content type }.
        /// </summary>
        public static Byte[] BodyHeader(String ContentType)
        {

            var writer = new CborWriter(CborConformanceMode.Canonical);

            writer.WriteStartMap(1);
            writer.WriteInt32(HeaderContentType);
            writer.WriteTextString(ContentType);
            writer.WriteEndMap();

            return writer.Encode();

        }

        #endregion

        #region ToBeSigned(BodyProtected, SignProtected, Payload)

        /// <summary>
        /// What a signature of a COSE_Sign is made over: the Sig_structure
        /// ["Signature", body_protected, sign_protected, external_aad, payload],
        /// the external data empty.
        /// </summary>
        public static Byte[] ToBeSigned(Byte[] BodyProtected,
                                        Byte[] SignProtected,
                                        Byte[] Payload)
        {

            var writer = new CborWriter(CborConformanceMode.Canonical);

            writer.WriteStartArray(5);
            writer.WriteTextString("Signature");
            writer.WriteByteString(BodyProtected);
            writer.WriteByteString(SignProtected);
            writer.WriteByteString([]);
            writer.WriteByteString(Payload);
            writer.WriteEndArray();

            return writer.Encode();

        }

        #endregion

        #region Sign(Key, KeyId, BodyProtected, Payload)

        /// <summary>
        /// An ES256 signature of a COSE_Sign by the given key.
        /// </summary>
        public static COSESignature Sign(ECDsa   Key,
                                         Byte[]  KeyId,
                                         Byte[]  BodyProtected,
                                         Byte[]  Payload)
        {

            var header = SignatureHeader(KeyId);

            return new COSESignature(
                       header,
                       ES256,
                       KeyId,
                       Key.SignData(ToBeSigned(BodyProtected, header, Payload), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                   );

        }

        #endregion

        #region Verifies(Key, Message, Signature)

        /// <summary>
        /// Whether a signature of a COSE_Sign is an ES256 signature by the
        /// given key over its body and payload.
        /// </summary>
        public static Boolean Verifies(ECDsa          Key,
                                       COSESign       Message,
                                       COSESignature  Signature)
        {

            if (Signature.Algorithm != ES256 || Signature.Value.Length != 64)
                return false;

            try
            {
                return Key.VerifyData(ToBeSigned(Message.Protected, Signature.Protected, Message.Payload),
                                      Signature.Value,
                                      HashAlgorithmName.SHA256,
                                      DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            catch (CryptographicException)
            {
                return false;
            }

        }

        #endregion


        #region WriteKey(Writer, Key) / TryReadKey(Reader, out Key, out Error)

        /// <summary>
        /// A public key on P-256 as a COSE_Key: { 1: 2, -1: 1, -2: x, -3: y }.
        /// </summary>
        public static void WriteKey(CborWriter    Writer,
                                    ECParameters  Key)
        {
            Writer.WriteStartMap(4);
            Writer.WriteInt32(KeyType);   Writer.WriteInt32(KeyTypeEC2);
            Writer.WriteInt32(KeyCurve);  Writer.WriteInt32(CurveP256);
            Writer.WriteInt32(KeyX);      Writer.WriteByteString(Key.Q.X!);
            Writer.WriteInt32(KeyY);      Writer.WriteByteString(Key.Q.Y!);
            Writer.WriteEndMap();
        }

        /// <summary>
        /// A public key on P-256 from a COSE_Key, checked to be a point on the
        /// curve.
        /// </summary>
        public static Boolean TryReadKey(CborReader                          Reader,
                                         out ECParameters                    Key,
                                         [NotNullWhen(false)] out String?    Error)
        {

            Key = default;

            Int64?  type   = null;
            Int64?  curve  = null;
            Byte[]? x      = null;
            Byte[]? y      = null;

            var count = Reader.ReadStartMap();

            for (var i = 0; count is null ? Reader.PeekState() != CborReaderState.EndMap : i < count; i++)
            {

                if (Reader.PeekState() is not (CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger))
                {
                    Reader.SkipValue();
                    Reader.SkipValue();
                    continue;
                }

                switch (Reader.ReadInt64())
                {
                    case KeyType:   type   = Reader.ReadInt64();      break;
                    case KeyCurve:  curve  = Reader.ReadInt64();      break;
                    case KeyX:      x      = Reader.ReadByteString(); break;
                    case KeyY:      y      = Reader.ReadByteString(); break;
                    default:        Reader.SkipValue();               break;
                }

            }

            Reader.ReadEndMap();

            if (type != KeyTypeEC2 || curve != CurveP256 || x?.Length != 32 || y?.Length != 32)
            {
                Error = "The key is no COSE_Key of an elliptic curve key on P-256 (kty 2, crv 1, x and y of 32 bytes).";
                return false;
            }

            Key = new ECParameters {
                      Curve  = ECCurve.NamedCurves.nistP256,
                      Q      = new ECPoint { X = x, Y = y }
                  };

            try
            {
                using var check = ECDsa.Create(Key);
            }
            catch (CryptographicException)
            {
                Error = "The key is no point on P-256.";
                return false;
            }

            Error = null;
            return true;

        }

        #endregion


        #region (private static) ReadHeader(Protected, out Algorithm, out KeyId)

        /// <summary>
        /// The algorithm and the key a protected header names, where it does.
        /// </summary>
        private static void ReadHeader(Byte[]       Protected,
                                       out Int64?   Algorithm,
                                       out Byte[]?  KeyId)
        {

            Algorithm  = null;
            KeyId      = null;

            if (Protected.Length == 0)
                return;

            var reader = new CborReader(Protected, CborConformanceMode.Lax);
            var count  = reader.ReadStartMap();

            for (var i = 0; count is null ? reader.PeekState() != CborReaderState.EndMap : i < count; i++)
            {

                if (reader.PeekState() is not (CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger))
                {
                    reader.SkipValue();
                    reader.SkipValue();
                    continue;
                }

                switch (reader.ReadInt64())
                {

                    case HeaderAlgorithm:
                        Algorithm = reader.PeekState() is CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger
                                        ? reader.ReadInt64()
                                        : SkipAndNull(reader);
                        break;

                    case HeaderKeyId:
                        KeyId = reader.PeekState() == CborReaderState.ByteString ? reader.ReadByteString() : SkipAndNullBytes(reader);
                        break;

                    default:
                        reader.SkipValue();
                        break;

                }

            }

        }

        private static Int64? SkipAndNull(CborReader Reader)
        {
            Reader.SkipValue();
            return null;
        }

        private static Byte[]? SkipAndNullBytes(CborReader Reader)
        {
            Reader.SkipValue();
            return null;
        }

        #endregion

    }

}
