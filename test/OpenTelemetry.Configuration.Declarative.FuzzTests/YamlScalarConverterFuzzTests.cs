// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Numerics;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace OpenTelemetry.Configuration.Declarative.FuzzTests;

public class YamlScalarConverterFuzzTests
{
    private const int MaxTests = 500;

    // Long enough to cross the 800 significant digit truncation in DecimalFloatParser.
    private const int MaxDecimalDigits = 900;

    private const string DigitAlphabet = "0123456789abcdef";

    private static readonly BigInteger Two53 = BigInteger.One << 53;

    private static readonly Arbitrary<string> ScalarStringArbitrary = Gen.Sized(size =>
        Gen.ArrayOf(
            Gen.Choose(0, 127).Select(c => (char)c),
            Math.Min(size + 1, 256))
        .Select(chars => new string(chars))).ToArbitrary();

    private static readonly Arbitrary<string> HexDigitsArbitrary = DigitStringGen(16, Gen.Choose(1, 70)).ToArbitrary();

    private static readonly Arbitrary<string> OctalDigitsArbitrary = DigitStringGen(8, Gen.Choose(1, 90)).ToArbitrary();

    private static readonly Arbitrary<DecimalCase> DecimalCaseArbitrary = DecimalCaseGen().ToArbitrary();

    [Property(MaxTest = MaxTests)]
    public Property ResolveAndConvertNeverThrowsForArbitraryPlainScalar() =>
        Prop.ForAll(
            ScalarStringArbitrary,
            value =>
            {
                var node = new YamlScalarNode(value) { Style = ScalarStyle.Plain };
                var resolved = YamlScalarResolver.Resolve(node, value);
                _ = YamlScalarConverter.Convert(resolved);
            });

    [Property(MaxTest = MaxTests)]
    public Property HexadecimalIntegerNotationRoundsToNearestDouble() =>
        Prop.ForAll(
            HexDigitsArbitrary,
            digits => AssertRoundsToNearest("0x" + digits, ToBigInteger(digits, 16), BigInteger.One, negative: false));

    [Property(MaxTest = MaxTests)]
    public Property OctalIntegerNotationRoundsToNearestDouble() =>
        Prop.ForAll(
            OctalDigitsArbitrary,
            digits => AssertRoundsToNearest("0o" + digits, ToBigInteger(digits, 8), BigInteger.One, negative: false));

    // Worth running on every framework even though only .NET Framework and .NET 8 reach
    // DecimalFloatParser: on .NET 9 and later this holds the runtime parser to the same contract,
    // which is what makes the two branches of the conversion interchangeable.
    [Property(MaxTest = MaxTests)]
    public Property DecimalNotationRoundsToNearestDouble() =>
        Prop.ForAll(
            DecimalCaseArbitrary,
            testCase =>
            {
                var mantissa = BigInteger.Parse(testCase.Digits, NumberStyles.None, CultureInfo.InvariantCulture);
                var scale = (int)(testCase.Exponent - testCase.FractionLength);

                AssertRoundsToNearest(
                    Render(testCase),
                    scale >= 0 ? mantissa * BigInteger.Pow(10, scale) : mantissa,
                    scale >= 0 ? BigInteger.One : BigInteger.Pow(10, -scale),
                    testCase.Negative);
            });

    /// <summary>
    /// Rounds the exact value <paramref name="numerator"/> / <paramref name="denominator"/> to the
    /// nearest binary64, ties to even.
    /// </summary>
    /// <remarks>
    /// Every finite binary64 is <c>significand * 2^(quantum - 1074)</c> for some significand below
    /// 2^53 and some quantum of zero (the subnormal grid) or more, so this finds the smallest
    /// quantum whose significand fits and rounds the exact remainder against it. It deliberately
    /// takes none of the shortcuts the code under test relies on - no digit truncation, no sticky
    /// bit, no decimal magnitude estimate, no exponent clamp - so the two agree only when both are
    /// right.
    /// </remarks>
    /// <param name="numerator">The non-negative numerator of the exact value.</param>
    /// <param name="denominator">The positive denominator of the exact value.</param>
    /// <returns>The nearest <see cref="double"/>, or infinity when the value is out of range.</returns>
    private static double RoundToNearest(BigInteger numerator, BigInteger denominator)
    {
        if (numerator.IsZero)
        {
            return 0.0;
        }

        // The bit length difference is within one of log2(numerator / denominator), so this lands
        // at or just below the answer and the loop settles the rest in a step or two.
        var scaled = numerator << 1074;
        var quantum = Math.Max(0, BitLength(scaled) - BitLength(denominator) - 53);

        while (true)
        {
            var divisor = denominator << quantum;
            var significand = BigInteger.DivRem(scaled, divisor, out var remainder);
            if (significand >= Two53)
            {
                quantum++;
                continue;
            }

            var midpointComparison = (remainder << 1).CompareTo(divisor);
            if (midpointComparison > 0 || (midpointComparison == 0 && !significand.IsEven))
            {
                significand++;
            }

            if (significand == Two53)
            {
                // Rounding up carried into the next binade; redo it one quantum wider.
                quantum++;
                continue;
            }

            var exponent = quantum - 1074;
            return exponent > 971
                ? double.PositiveInfinity
                : (double)significand * Math.Pow(2, exponent);
        }
    }

    private static void AssertRoundsToNearest(string text, BigInteger numerator, BigInteger denominator, bool negative)
    {
        var magnitude = RoundToNearest(numerator, denominator);
        var expected = BitConverter.DoubleToInt64Bits(negative ? -magnitude : magnitude);
        var actual = BitConverter.DoubleToInt64Bits(
            YamlScalarConverter.Convert(new(text, YamlScalarKind.Float)).AsDouble());

        if (expected != actual)
        {
            Assert.Fail($"'{text}' converted to 0x{actual:x16}, but 0x{expected:x16} is the nearest binary64.");
        }
    }

    private static string Render(DecimalCase testCase)
    {
        var text = new StringBuilder();
        if (testCase.Negative)
        {
            text.Append('-');
        }

        var integerLength = testCase.Digits.Length - testCase.FractionLength;
        text.Append(testCase.Digits, 0, integerLength);
        if (testCase.FractionLength > 0)
        {
            text.Append('.').Append(testCase.Digits, integerLength, testCase.FractionLength);
        }

        // A zero exponent is left off, which is what produces the plain "123.45" and "12345" forms.
        if (testCase.Exponent != 0)
        {
            text.Append('e').Append(testCase.Exponent.ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    // Most cases are pushed to a chosen power of ten so that the whole representable range,
    // including both saturation edges, gets covered. A quarter of the short ones keep the magnitude
    // their digits already have, which renders without an exponent.
    private static Gen<DecimalCase> DecimalCaseGen() =>
        from digits in DecimalDigitStringGen()
        from fractionLength in Gen.Choose(0, digits.Length)
        from magnitude in MagnitudeGen()
        from keepNaturalMagnitude in Gen.Choose(0, 3)
        from negative in Gen.Elements(true, false)
        let exponent = keepNaturalMagnitude == 0 && digits.Length <= 40
            ? 0L
            : magnitude - digits.Length + fractionLength
        select new DecimalCase(digits, fractionLength, exponent, negative);

    // The power of ten the value is placed at. Three quarters of the cases sit in the two narrow
    // windows where the result changes shape - the frontier between zero and the subnormals, and
    // the frontier between the largest finite value and infinity - because a uniform spread over
    // 640 decades visits either one far too rarely to be worth anything.
    private static Gen<int> MagnitudeGen() =>
        from bucket in Gen.Choose(0, 3)
        from magnitude in bucket switch
        {
            0 => Gen.Choose(-325, -318),
            1 => Gen.Choose(-323, -305),
            2 => Gen.Choose(307, 310),
            _ => Gen.Choose(-330, 310),
        }
        select magnitude;

    // The leading digit is never zero, so the chosen magnitude survives the parser's leading-zero
    // trim and puts the value where MagnitudeGen intended. Leading-zero forms are pinned by the
    // example-based tests instead.
    private static Gen<string> DecimalDigitStringGen() =>
        from length in DecimalLengthGen()
        from first in Gen.Choose(1, 9)
        from rest in Gen.ArrayOf(DigitGen(10), length - 1)
        select DigitAlphabet[first] + new string(rest);

    // Short strings dominate, but a fifth are long enough to force the 800 digit truncation.
    private static Gen<int> DecimalLengthGen() =>
        from bucket in Gen.Choose(0, 4)
        from length in bucket == 0 ? Gen.Choose(700, MaxDecimalDigits) : Gen.Choose(1, 40)
        select length;

    private static Gen<string> DigitStringGen(int numberBase, Gen<int> lengthGen) =>
        from length in lengthGen
        from digits in Gen.ArrayOf(DigitGen(numberBase), length)
        select new string(digits);

    // Digits are biased towards zero and the largest digit: runs of those are what put a value on
    // or beside a rounding boundary, which uniformly random digits practically never do.
    private static Gen<char> DigitGen(int numberBase) =>
        Gen.Choose(0, (numberBase * 2) - 1).Select(choice => choice switch
        {
            _ when choice < numberBase => DigitAlphabet[choice],
            _ when choice % 2 == 0 => '0',
            _ => DigitAlphabet[numberBase - 1],
        });

    private static BigInteger ToBigInteger(string digits, int numberBase)
    {
        var value = BigInteger.Zero;
        foreach (var digit in digits)
        {
            value = (value * numberBase) + (digit <= '9' ? digit - '0' : digit - 'a' + 10);
        }

        return value;
    }

    // BigInteger.GetBitLength is unavailable on .NET Framework.
    private static int BitLength(BigInteger value)
    {
        var bytes = value.ToByteArray();
        var bits = (bytes.Length - 1) * 8;
        for (var high = bytes[bytes.Length - 1]; high != 0; high >>= 1)
        {
            bits++;
        }

        return bits;
    }

    // A decimal scalar held as its parts, so that the rendered text and the exact value it denotes
    // are both derived here rather than by re-parsing the text with the logic under test.
    private sealed class DecimalCase
    {
        public DecimalCase(string digits, int fractionLength, long exponent, bool negative)
        {
            this.Digits = digits;
            this.FractionLength = fractionLength;
            this.Exponent = exponent;
            this.Negative = negative;
        }

        public string Digits { get; }

        public int FractionLength { get; }

        public long Exponent { get; }

        public bool Negative { get; }

        public override string ToString() =>
            $"digits={this.Digits} fractionLength={this.FractionLength} exponent={this.Exponent} negative={this.Negative}";
    }
}
