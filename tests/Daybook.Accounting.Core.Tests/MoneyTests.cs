using System.Globalization;

using CsCheck;

namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 1 — the <see cref="Money"/> value object.
/// Written test-first (red → green → refactor). Covers the spec §4.6 contract:
/// decimal(19,4) backing, currency-awareness, currency-checked arithmetic,
/// immutability, value equality, banker's rounding at defined boundaries only.
/// </summary>
public class MoneyTests
{
    private static readonly Currency Usd = Currency.Usd;
    private static readonly Currency Eur = Currency.Of("EUR");

    // A generator of USD amounts with 4 decimal places, bounded well away from
    // decimal overflow so algebraic properties can be checked exactly.
    // n ∈ [-1e13, 1e13] ⇒ amount ∈ [-1e9, 1e9] at scale 4.
    private static readonly Gen<Money> GenMoney =
        Gen.Long[-10_000_000_000_000L, 10_000_000_000_000L]
           .Select(n => Money.Of(n / 10_000m, Usd));

    // ---- Construction & representation -------------------------------------

    [Fact]
    public void Of_carries_amount_and_currency()
    {
        var money = Money.Of(12.34m, Usd);

        money.Amount.Should().Be(12.34m);
        money.Currency.Should().Be(Usd);
    }

    [Fact]
    public void Usd_currency_is_available_as_the_v1_currency()
    {
        Currency.Usd.Code.Should().Be("USD");
    }

    [Fact]
    public void Zero_is_the_additive_identity_value()
    {
        Money.Zero(Usd).Amount.Should().Be(0m);
        Money.Zero(Usd).Currency.Should().Be(Usd);
    }

    // ---- Rounding happens only at construction/multiply boundaries ----------

    [Theory]
    [InlineData("1.00005", "1.0000")] // midpoint → round to even (0)
    [InlineData("1.00015", "1.0002")] // midpoint → round to even (2)
    [InlineData("1.00025", "1.0002")] // midpoint → round to even (2)
    [InlineData("1.23456", "1.2346")] // > midpoint → round up
    [InlineData("1.23454", "1.2345")] // < midpoint → round down
    public void Of_rounds_to_four_places_using_bankers_rounding(string input, string expected)
    {
        var amount = decimal.Parse(input, CultureInfo.InvariantCulture);
        var expectedAmount = decimal.Parse(expected, CultureInfo.InvariantCulture);

        Money.Of(amount, Usd).Amount.Should().Be(expectedAmount);
    }

    // ---- Value equality & immutability -------------------------------------

    [Fact]
    public void Equal_amount_and_currency_are_value_equal_regardless_of_scale()
    {
        Money.Of(1.5m, Usd).Should().Be(Money.Of(1.5000m, Usd));
        (Money.Of(1.5m, Usd) == Money.Of(1.5000m, Usd)).Should().BeTrue();
    }

    [Fact]
    public void Differing_currency_makes_values_unequal()
    {
        Money.Of(1m, Usd).Should().NotBe(Money.Of(1m, Eur));
    }

    [Fact]
    public void Arithmetic_returns_new_instances_and_leaves_operands_unchanged()
    {
        var a = Money.Of(10m, Usd);
        var b = Money.Of(3m, Usd);

        _ = a + b;

        a.Amount.Should().Be(10m);
        b.Amount.Should().Be(3m);
    }

    // ---- Arithmetic (same currency) ----------------------------------------

    [Fact]
    public void Add_sums_same_currency_amounts()
    {
        (Money.Of(10.25m, Usd) + Money.Of(5.75m, Usd)).Should().Be(Money.Of(16.00m, Usd));
    }

    [Fact]
    public void Subtract_differences_same_currency_amounts()
    {
        (Money.Of(10.00m, Usd) - Money.Of(2.50m, Usd)).Should().Be(Money.Of(7.50m, Usd));
    }

    [Fact]
    public void Negate_flips_sign()
    {
        (-Money.Of(4.20m, Usd)).Should().Be(Money.Of(-4.20m, Usd));
    }

    [Fact]
    public void Multiply_by_scalar_scales_amount_and_keeps_currency()
    {
        (Money.Of(2.00m, Usd) * 3).Should().Be(Money.Of(6.00m, Usd));
        (Money.Of(1.50m, Usd) * 0.5m).Should().Be(Money.Of(0.75m, Usd));
    }

    [Theory]
    [InlineData("0.0001", "0.5", "0.0000")] // 0.00005 → even → 0.0000
    [InlineData("0.0003", "0.5", "0.0002")] // 0.00015 → even → 0.0002
    public void Multiply_rounds_result_with_bankers_rounding_at_the_boundary(
        string amount, string factor, string expected)
    {
        var money = Money.Of(decimal.Parse(amount, CultureInfo.InvariantCulture), Usd);
        var scalar = decimal.Parse(factor, CultureInfo.InvariantCulture);
        var expectedAmount = decimal.Parse(expected, CultureInfo.InvariantCulture);

        (money * scalar).Should().Be(Money.Of(expectedAmount, Usd));
    }

    // ---- Currency mismatch throws ------------------------------------------

    [Fact]
    public void Add_across_currencies_throws()
    {
        var act = () => Money.Of(1m, Usd) + Money.Of(1m, Eur);

        act.Should().Throw<CurrencyMismatchException>()
           .Which.Message.Should().Contain("USD").And.Contain("EUR");
    }

    [Fact]
    public void Subtract_across_currencies_throws()
    {
        var act = () => Money.Of(1m, Usd) - Money.Of(1m, Eur);

        act.Should().Throw<CurrencyMismatchException>();
    }

    // ---- ToString -----------------------------------------------------------

    [Fact]
    public void ToString_shows_four_decimals_and_currency_code_invariantly()
    {
        Money.Of(1234.5m, Usd).ToString().Should().Be("1234.5000 USD");
        Money.Of(-0.05m, Usd).ToString().Should().Be("-0.0500 USD");
    }

    // ---- Property-based algebraic laws --------------------------------------

    [Fact]
    public void Property_additive_identity()
    {
        GenMoney.Sample(a =>
            a + Money.Zero(a.Currency) == a &&
            Money.Zero(a.Currency) + a == a);
    }

    [Fact]
    public void Property_addition_is_commutative()
    {
        Gen.Select(GenMoney, GenMoney, (a, b) => a + b == b + a)
           .Sample(ok => ok);
    }

    [Fact]
    public void Property_addition_is_associative()
    {
        Gen.Select(GenMoney, GenMoney, GenMoney, (a, b, c) => (a + b) + c == a + (b + c))
           .Sample(ok => ok);
    }

    [Fact]
    public void Property_negate_round_trips()
    {
        GenMoney.Sample(a => -(-a) == a);
    }

    [Fact]
    public void Property_value_plus_its_negation_is_zero()
    {
        GenMoney.Sample(a => a + (-a) == Money.Zero(a.Currency));
    }

    [Fact]
    public void Property_subtract_is_add_of_negation()
    {
        Gen.Select(GenMoney, GenMoney, (a, b) => a - b == a + (-b))
           .Sample(ok => ok);
    }
}