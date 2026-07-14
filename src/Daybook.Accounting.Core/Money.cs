using System.Globalization;

namespace Daybook.Accounting.Core;

/// <summary>
/// An immutable, currency-aware monetary amount backed by <see cref="decimal"/>
/// at scale 4 (i.e. <c>decimal(19,4)</c>). Never <c>float</c>/<c>double</c>.
/// </summary>
/// <remarks>
/// Rounding is banker's rounding (<see cref="MidpointRounding.ToEven"/>) and is
/// applied only at defined boundaries — construction (<see cref="Of"/>) and
/// scalar multiplication — never silently mid-calculation. Addition,
/// subtraction and negation of scale-4 amounts are exact and are not rounded.
/// Value equality is by amount and currency (ignoring decimal scale, so
/// <c>1.5</c> equals <c>1.5000</c>).
/// </remarks>
public sealed record Money
{
    /// <summary>Number of decimal places retained — the "4" in decimal(19,4).</summary>
    public const int Scale = 4;

    /// <summary>The amount, rounded to <see cref="Scale"/> decimal places.</summary>
    public decimal Amount { get; }

    /// <summary>The currency this amount is denominated in.</summary>
    public Currency Currency { get; }

    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>
    /// Creates a <see cref="Money"/>, rounding <paramref name="amount"/> to
    /// <see cref="Scale"/> places with banker's rounding.
    /// </summary>
    public static Money Of(decimal amount, Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        return new Money(Round(amount), currency);
    }

    /// <summary>The additive identity (0) in the given currency.</summary>
    public static Money Zero(Currency currency) => Of(0m, currency);

    /// <summary>Adds two amounts of the same currency (exact, no rounding).</summary>
    /// <exception cref="CurrencyMismatchException">Currencies differ.</exception>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>Subtracts an amount of the same currency (exact, no rounding).</summary>
    /// <exception cref="CurrencyMismatchException">Currencies differ.</exception>
    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    /// <summary>Returns this amount with its sign flipped (exact).</summary>
    public Money Negate() => new(-Amount, Currency);

    /// <summary>
    /// Multiplies by a dimensionless scalar, rounding the result to
    /// <see cref="Scale"/> places (a defined boundary).
    /// </summary>
    public Money Multiply(decimal factor) => new(Round(Amount * factor), Currency);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public static Money operator -(Money value) => value.Negate();

    public static Money operator *(Money money, decimal factor) => money.Multiply(factor);

    public static Money operator *(decimal factor, Money money) => money.Multiply(factor);

    public override string ToString() =>
        $"{Amount.ToString("0.0000", CultureInfo.InvariantCulture)} {Currency.Code}";

    private void EnsureSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency)
        {
            throw new CurrencyMismatchException(Currency, other.Currency);
        }
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, Scale, MidpointRounding.ToEven);
}