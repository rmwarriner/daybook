namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 2 — the five root <see cref="AccountType"/>s and their
/// enforced normal balance (spec §4.2 table).
/// </summary>
public class AccountTypeTests
{
    [Theory]
    [InlineData(AccountType.Asset, Side.Debit)]
    [InlineData(AccountType.Liability, Side.Credit)]
    [InlineData(AccountType.Equity, Side.Credit)]
    [InlineData(AccountType.Income, Side.Credit)]
    [InlineData(AccountType.Expense, Side.Debit)]
    public void Each_root_type_has_its_spec_defined_normal_balance(AccountType type, Side expected)
    {
        type.NormalBalance().Should().Be(expected);
    }

    [Fact]
    public void Unknown_type_value_throws_when_asked_for_a_normal_balance()
    {
        var invalid = (AccountType)(-1);

        var act = () => invalid.NormalBalance();

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}