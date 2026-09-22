// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if !NET9_0_OR_GREATER
using System.Globalization;
using System.Numerics;

namespace OpenTelemetry.Configuration.Declarative;

/// <summary>
/// Rounds validated decimal YAML scalars to binary64 without relying on legacy runtime parsing.
/// </summary>
internal static class DecimalFloatParser
{
    // Binary64 rounding midpoints have at most 768 significant decimal digits.
    // Keeping 800 plus a sticky tail bounds the arithmetic without losing rounding information.
    private const int MaxSignificantDigits = 800;

    /// <summary>
    /// Rounds <paramref name="value"/> to the nearest binary64, ties to even.
    /// </summary>
    /// <remarks>
    /// <paramref name="value"/> must be a decimal number as recognised by
    /// <see cref="YamlScalarResolver.IsDecimalNumber(string)"/>. The <c>.inf</c> and <c>.nan</c>
    /// forms and hexadecimal and octal notation have no decimal digits and will throw here, so
    /// callers must convert those themselves.
    /// </remarks>
    /// <param name="value">The decimal scalar text to round.</param>
    /// <returns>The nearest <see cref="double"/>, saturating to zero or infinity out of range.</returns>
    internal static double Parse(string value)
    {
        var start = value[0] is '+' or '-' ? 1 : 0;
        var exponentIndex = value.AsSpan().IndexOf('e');
        if (exponentIndex < 0)
        {
            exponentIndex = value.AsSpan().IndexOf('E');
        }

        var end = exponentIndex < 0 ? value.Length : exponentIndex;
        var point = value.AsSpan().IndexOf('.');
        var fractionalDigits = point < 0 ? 0 : end - point - 1;
        var digits = value.Substring(start, end - start);
        if (point >= 0)
        {
            digits = digits.Remove(point - start, 1);
        }

        digits = digits.TrimStart('0');
        var result = digits.Length == 0
            ? 0.0
            : ParseMagnitude(digits, ReadExponent(value, exponentIndex) - fractionalDigits);

        return value[0] == '-' ? -result : result;
    }

    private static long ReadExponent(string value, int exponentIndex)
    {
        if (exponentIndex < 0)
        {
            return 0;
        }

        var index = exponentIndex + 1;
        var negative = value[index] == '-';
        if (value[index] is '+' or '-')
        {
            index++;
        }

        // Beyond this bound, even the entire mantissa cannot offset overflow or underflow.
        var limit = (long)value.Length + 400;
        var exponent = 0L;
        for (; index < value.Length; index++)
        {
            exponent = Math.Min(limit, (exponent * 10) + value[index] - '0');
        }

        return negative ? -exponent : exponent;
    }

    private static double ParseMagnitude(string digits, long decimalExponent)
    {
        var magnitude = digits.Length + decimalExponent;
        if (magnitude > 309)
        {
            return double.PositiveInfinity;
        }

        if (magnitude < -323)
        {
            return 0.0;
        }

        var trimmed = digits.TrimEnd('0');
        decimalExponent += digits.Length - trimmed.Length;
        digits = trimmed;

        var hasDiscardedNonzeroDigits = digits.Length > MaxSignificantDigits;
        if (hasDiscardedNonzeroDigits)
        {
            decimalExponent += digits.Length - MaxSignificantDigits;
            digits = digits.Substring(0, MaxSignificantDigits);
        }

        var numerator = BigInteger.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        var denominator = BigInteger.One;
        if (decimalExponent >= 0)
        {
            numerator *= BigInteger.Pow(10, (int)decimalExponent);
        }
        else
        {
            denominator = BigInteger.Pow(10, (int)-decimalExponent);
        }

        var binaryExponent = GetBitLength(numerator) - GetBitLength(denominator);
        if (binaryExponent >= 0
            ? numerator < (denominator << binaryExponent)
            : (numerator << -binaryExponent) < denominator)
        {
            binaryExponent--;
        }

        if (binaryExponent > 1023)
        {
            return double.PositiveInfinity;
        }

        // Normal numbers retain 53 bits; subnormals use the fixed 2^-1074 spacing.
        var shift = 52 - Math.Max(binaryExponent, -1022);
        if (shift >= 0)
        {
            numerator <<= shift;
        }
        else
        {
            denominator <<= -shift;
        }

        var significand = BigInteger.DivRem(numerator, denominator, out var remainder);
        var midpointComparison = (remainder << 1).CompareTo(denominator);
        if (midpointComparison > 0
            || (midpointComparison == 0 && (hasDiscardedNonzeroDigits || !significand.IsEven)))
        {
            significand++;
        }

        var bits = (long)significand;
        if (binaryExponent < -1022)
        {
            // Rounding the largest subnormal up also produces the smallest normal encoding.
            return BitConverter.Int64BitsToDouble(bits);
        }

        if (bits == (1L << 53))
        {
            bits >>= 1;
            binaryExponent++;
        }

        bits = ((long)(binaryExponent + 1023) << 52) | (bits & ((1L << 52) - 1));
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static int GetBitLength(BigInteger value)
    {
        var bytes = value.ToByteArray();
        var bits = (bytes.Length - 1) * 8;
        for (var high = bytes[bytes.Length - 1]; high != 0; high >>= 1)
        {
            bits++;
        }

        return bits;
    }
}
#endif
