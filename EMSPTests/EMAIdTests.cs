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

using NUnit.Framework;

using cloud.charging.open.EMSP.Contracts;

#endregion

namespace cloud.charging.open.EMSP.Tests
{

    /// <summary>
    /// The e-mobility account identifiers this EMSP makes out contracts to,
    /// and the check digit at the end of them.
    /// </summary>
    public class EMAIdTests
    {

        #region TheCheckDigitMatchesThePublishedVectors()

        /// <summary>
        /// The vectors are the ones the OCHP clearing house and the New
        /// Motion's mobilityid library publish for ISO 15118-1 Annex H, so a
        /// check digit this EMSP calculates is one every roaming platform
        /// agrees with.
        /// </summary>
        [TestCase("NN123ABCDEFGHI", 'T')]
        [TestCase("FRXYZ123456789", '2')]
        [TestCase("ITA1B2C3E4F5G6", '4')]
        [TestCase("ESZU8WOX834H1D", 'R')]
        [TestCase("PT73902837ABCZ", 'Z')]
        [TestCase("DE83DUIEN83QGZ", 'D')]
        [TestCase("DE83DUIEN83ZGQ", 'M')]
        [TestCase("DE8AA001234567", '0')]
        [TestCase("NLTNM000122045", 'U')]
        [TestCase("NLTNM000722345", 'X')]
        [TestCase("NLTNMC00122045", 'K')]
        [TestCase("nltnmc00122045", 'K')]
        public void TheCheckDigitMatchesThePublishedVectors(String Text, Char Expected)
        {
            Assert.That(EMAIdCheckDigit.Of(Text), Is.EqualTo(Expected));
        }

        #endregion

        #region TheCheckDigitRefusesWhatIsNotFourteenLettersAndDigits()

        [TestCase("")]
        [TestCase("DE8AA0012345678")]
        [TestCase("DE8AA00123456")]
        [TestCase("DE-8AA-00123456")]
        [TestCase("DE٨٣DUIEN٨٣QGZ")]
        public void TheCheckDigitRefusesWhatIsNotFourteenLettersAndDigits(String Text)
        {

            Assert.Multiple(() => {
                Assert.That(EMAIdCheckDigit.TryCompute(Text, out _, out var error), Is.False);
                Assert.That(error, Is.Not.Null.And.Not.Empty);
                Assert.That(() => EMAIdCheckDigit.Of(Text), Throws.ArgumentException);
            });

        }

        #endregion

        #region AnEMAIdIsReadInEveryFormItIsWrittenIn()

        [TestCase("NL-TNM-C00122045-K")]
        [TestCase("NL-TNM-C00122045")]
        [TestCase("NLTNMC00122045K")]
        [TestCase("NLTNMC00122045")]
        [TestCase("nl-tnm-c00122045-k")]
        [TestCase("  NL-TNM-C00122045-K  ")]
        public void AnEMAIdIsReadInEveryFormItIsWrittenIn(String Text)
        {

            Assert.That(EMAId.TryParse(Text, out var emaId, out var error), Is.True, error);

            Assert.Multiple(() => {
                Assert.That(emaId!.CountryCode,  Is.EqualTo("NL"));
                Assert.That(emaId.ProviderId,    Is.EqualTo("TNM"));
                Assert.That(emaId.Instance,      Is.EqualTo("C00122045"));
                Assert.That(emaId.CheckDigit,    Is.EqualTo('K'));
                Assert.That(emaId.ToString(),    Is.EqualTo("NL-TNM-C00122045-K"));
                Assert.That(emaId.Compact,       Is.EqualTo("NLTNMC00122045K"));
                Assert.That(emaId.PartyId,       Is.EqualTo("NL-TNM"));
            });

        }

        #endregion

        #region AWrongCheckDigitIsRefused()

        [Test]
        public void AWrongCheckDigitIsRefused()
        {

            Assert.Multiple(() => {

                Assert.That(EMAId.TryParse("NL-TNM-C00122045-X", out _, out var error), Is.False);
                Assert.That(error, Does.Contain("\"K\""));

                Assert.That(EMAId.TryParse("NL-TNM-C0012204",     out _, out _), Is.False, "eight characters of instance");
                Assert.That(EMAId.TryParse("N-TNM-C00122045-K",   out _, out _), Is.False, "one letter of country");
                Assert.That(EMAId.TryParse("NL-TN|-C00122045",    out _, out _), Is.False, "a character that is not a letter or a digit");
                Assert.That(EMAId.TryParse(null,                  out _, out _), Is.False);

            });

        }

        #endregion

        #region ARandomEMAIdIsAContractOfTheProvider()

        [Test]
        public void ARandomEMAIdIsAContractOfTheProvider()
        {

            var first   = EMAId.Random("DE", "GDF");
            var second  = EMAId.Random("DE", "GDF");

            Assert.Multiple(() => {

                Assert.That(first.CountryCode,    Is.EqualTo("DE"));
                Assert.That(first.ProviderId,     Is.EqualTo("GDF"));
                Assert.That(first.Instance,       Does.StartWith("C").And.Length.EqualTo(9));
                Assert.That(first.Compact,        Has.Length.EqualTo(15));

                // What it writes, it reads back, check digit and all.
                Assert.That(EMAId.TryParse(first.ToString(), out var again, out var error), Is.True, error);
                Assert.That(again,                Is.EqualTo(first));

                Assert.That(second,               Is.Not.EqualTo(first), "Two random identifiers came out the same.");

            });

        }

        #endregion

        #region CreateCalculatesTheCheckDigit()

        [Test]
        public void CreateCalculatesTheCheckDigit()
        {

            var emaId = EMAId.Create("DE", "8AA", "001234567");

            Assert.Multiple(() => {
                Assert.That(emaId.CheckDigit,  Is.EqualTo('0'));
                Assert.That(emaId.ToString(),  Is.EqualTo("DE-8AA-001234567-0"));
                Assert.That(() => EMAId.Create("DE", "8AA", "12"), Throws.ArgumentException);
            });

        }

        #endregion

    }

}
