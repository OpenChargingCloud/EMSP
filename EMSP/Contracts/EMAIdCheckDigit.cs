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

#endregion

namespace cloud.charging.open.EMSP.Contracts
{

    /// <summary>
    /// The check digit of an e-mobility account identifier, as ISO 15118-1
    /// Annex H specifies it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fourteen characters go in - the country code, the provider
    /// identification and the nine characters of the instance, without
    /// separators - and one character comes out. It catches every single
    /// wrong character and every swap of two neighbours, which is what a
    /// check digit is for: an eMAID is read off a screen and typed into a
    /// form by people, and it is what a charging station asks a roaming
    /// partner about.
    /// </para>
    /// <para>
    /// The arithmetic is the standard's and not obvious: every character is
    /// one of thirty-six points, and a point is a pair of two-by-two
    /// matrices - one over the integers modulo two, one modulo three. Each
    /// position of the identifier is weighted with a power of a fixed
    /// matrix, the weighted points are added up, and the sum is decoded back
    /// into a character. The tables and matrices below are the ones the
    /// standard prints; the OCHP clearing house publishes the same algorithm
    /// with a worked example, and the test vectors come from there.
    /// </para>
    /// </remarks>
    public static class EMAIdCheckDigit
    {

        #region Data

        /// <summary>
        /// How many characters go into the calculation.
        /// </summary>
        public const Int32  Length  = 14;

        /// <summary>
        /// Every character as the standard encodes it: bit 0 and bit 1 are
        /// the first matrix (modulo two), bits 2 and 3 and bits 4 and 5 are
        /// the second (modulo three).
        /// </summary>
        private static readonly IReadOnlyDictionary<Char, Int32> encoding = new Dictionary<Char, Int32> {
            { '0',  0 }, { '1', 16 }, { '2', 32 },
            { '3',  4 }, { '4', 20 }, { '5', 36 },
            { '6',  8 }, { '7', 24 }, { '8', 40 },
            { '9',  2 }, { 'A', 18 }, { 'B', 34 },
            { 'C',  6 }, { 'D', 22 }, { 'E', 38 },
            { 'F', 10 }, { 'G', 26 }, { 'H', 42 },
            { 'I',  1 }, { 'J', 17 }, { 'K', 33 },
            { 'L',  5 }, { 'M', 21 }, { 'N', 37 },
            { 'O',  9 }, { 'P', 25 }, { 'Q', 41 },
            { 'R',  3 }, { 'S', 19 }, { 'T', 35 },
            { 'U',  7 }, { 'V', 23 }, { 'W', 39 },
            { 'X', 11 }, { 'Y', 27 }, { 'Z', 43 }
        };

        /// <summary>
        /// The same table the other way round: from the encoded sum back to
        /// the character.
        /// </summary>
        private static readonly IReadOnlyDictionary<Int32, Char> decoding
            = encoding.ToDictionary(pair => pair.Value, pair => pair.Key);

        /// <summary>
        /// The weights of the fourteen positions: the powers one to fourteen
        /// of the two generator matrices the standard names.
        /// </summary>
        private static readonly Matrix[]  weights1  = Powers(new Matrix(0, 1, 1, 1));
        private static readonly Matrix[]  weights2  = Powers(new Matrix(0, 1, 1, 2));

        /// <summary>
        /// The inverse of the fifteenth power of the second generator,
        /// negated, modulo three: what turns the second sum into the second
        /// half of the check character.
        /// </summary>
        private static readonly Matrix    finalWeight2  = new (0, 2, 2, 1);

        #endregion


        #region Of(Text)

        /// <summary>
        /// The check digit of the given fourteen characters.
        /// </summary>
        /// <param name="Text">The country code, the provider identification and the instance, without separators, in any case.</param>
        /// <exception cref="ArgumentException">When the text is not fourteen letters and digits.</exception>
        public static Char Of(String Text)
        {

            if (TryCompute(Text, out var checkDigit, out var error))
                return checkDigit;

            throw new ArgumentException(error, nameof(Text));

        }

        #endregion

        #region TryCompute(Text, out CheckDigit, out Error)

        /// <summary>
        /// The check digit of the given fourteen characters, or the sentence
        /// that says why there is none.
        /// </summary>
        /// <param name="Text">The country code, the provider identification and the instance, without separators, in any case.</param>
        /// <param name="CheckDigit">The check digit.</param>
        /// <param name="Error">What is wrong with the text.</param>
        public static Boolean TryCompute(String?                          Text,
                                         out Char                         CheckDigit,
                                         [NotNullWhen(false)] out String?  Error)
        {

            CheckDigit  = default;
            Error       = null;

            var text = Text?.Trim().ToUpperInvariant() ?? "";

            if (text.Length != Length)
            {
                Error = $"A check digit is calculated over {Length} characters, and \"{text}\" has {text.Length}.";
                return false;
            }

            var sum1 = new Vector(0, 0);
            var sum2 = new Vector(0, 0);

            for (var i = 0; i < Length; i++)
            {

                if (!encoding.TryGetValue(text[i], out var encoded))
                {
                    Error = $"\"{text[i]}\" is not a character an eMAID may contain; only letters and digits are.";
                    return false;
                }

                var point = Decode(encoded);

                sum1 += new Vector(point.M11, point.M12) * weights1[i];
                sum2 += new Vector(point.M21, point.M22) * weights2[i];

            }

            sum2 *= finalWeight2;

            var result = new Matrix(sum1.V1 & 1,
                                    sum1.V2 & 1,
                                    sum2.V1 % 3,
                                    sum2.V2 % 3);

            CheckDigit = decoding[Encode(result)];
            return true;

        }

        #endregion


        #region (private) Decode(Value) / Encode(Matrix)

        private static Matrix Decode(Int32 Value)
            => new (Value & 1, (Value >> 1) & 1, (Value >> 2) & 3, Value >> 4);

        private static Int32 Encode(Matrix Matrix)
            => Matrix.M11 + (Matrix.M12 << 1) + (Matrix.M21 << 2) + (Matrix.M22 << 4);

        #endregion

        #region (private) Powers(Generator)

        /// <summary>
        /// The powers one to fourteen of a matrix.
        /// </summary>
        private static Matrix[] Powers(Matrix Generator)
        {

            var powers = new Matrix[Length];
            var power  = Generator;

            for (var i = 0; i < Length; i++)
            {
                powers[i]  = power;
                power     *= Generator;
            }

            return powers;

        }

        #endregion


        #region (private) Matrix / Vector

        /// <summary>
        /// A two-by-two matrix of small integers.
        /// </summary>
        private readonly record struct Matrix(Int32 M11, Int32 M12, Int32 M21, Int32 M22)
        {

            public static Matrix operator * (Matrix A, Matrix B)

                => new (A.M11 * B.M11 + A.M12 * B.M21,
                        A.M11 * B.M12 + A.M12 * B.M22,
                        A.M21 * B.M11 + A.M22 * B.M21,
                        A.M21 * B.M12 + A.M22 * B.M22);

        }

        /// <summary>
        /// A row vector of two small integers.
        /// </summary>
        private readonly record struct Vector(Int32 V1, Int32 V2)
        {

            public static Vector operator + (Vector A, Vector B)
                => new (A.V1 + B.V1, A.V2 + B.V2);

            public static Vector operator * (Vector V, Matrix M)
                => new (V.V1 * M.M11 + V.V2 * M.M21,
                        V.V1 * M.M12 + V.V2 * M.M22);

        }

        #endregion

    }

}
