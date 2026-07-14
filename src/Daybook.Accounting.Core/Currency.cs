namespace Daybook.Accounting.Core;

/// <summary>
/// An ISO 4217 alpha-3 currency code (e.g. <c>USD</c>). A value object: two
/// currencies are equal when their <see cref="Code"/> is equal.
/// </summary>
/// <remarks>
/// v1 of the engine only *transacts* in USD (spec §4.6/§5), but the type is
/// deliberately currency-aware so multi-currency is an additive change later.
/// The USD-only restriction is enforced at the posting/book boundary, not here.
/// </remarks>
public sealed record Currency
{
    /// <summary>The v1 base currency.</summary>
    public static Currency Usd { get; } = new("USD");

    /// <summary>The three-letter, upper-case ISO 4217 code.</summary>
    public string Code { get; }

    private Currency(string code) => Code = code;

    /// <summary>
    /// Creates a currency from a three-letter alphabetic code. The code is
    /// trimmed and upper-cased; anything that is not exactly three ASCII
    /// letters is rejected.
    /// </summary>
    /// <exception cref="ArgumentException">The code is not three ASCII letters.</exception>
    public static Currency Of(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetter))
        {
            throw new ArgumentException(
                $"Currency code must be three ASCII letters (ISO 4217), but was '{code}'.",
                nameof(code));
        }

        return new Currency(normalized);
    }

    public override string ToString() => Code;
}