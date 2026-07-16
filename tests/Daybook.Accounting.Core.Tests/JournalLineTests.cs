namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 3 — <see cref="JournalLine"/>'s own local invariants (spec
/// §4.4): a positive <c>Amount</c>, a defined <see cref="Side"/>, and an
/// optional, normalized <c>Memo</c>. Invariants that need the rest of the
/// entry or the chart of accounts (balance, account existence/activity,
/// currency) live in <c>JournalTests</c> instead — a single line cannot
/// check those alone.
/// </summary>
public class JournalLineTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Money OneHundred = Money.Of(100m, Currency.Usd);

    [Fact]
    public void Create_with_valid_fields_succeeds()
    {
        var result = JournalLine.Create(AccountId, Side.Debit, OneHundred, memo: "Groceries");

        result.IsSuccess.Should().BeTrue();
        var line = result.Value;
        line.AccountId.Should().Be(AccountId);
        line.Side.Should().Be(Side.Debit);
        line.Amount.Should().Be(OneHundred);
        line.Memo.Should().Be("Groceries");
    }

    [Fact]
    public void Create_defaults_memo_to_null()
    {
        JournalLine.Create(AccountId, Side.Debit, OneHundred).Value.Memo.Should().BeNull();
    }

    [Fact]
    public void Create_trims_memo_and_treats_blank_memo_as_none()
    {
        JournalLine.Create(AccountId, Side.Debit, OneHundred, memo: "  Groceries  ")
            .Value.Memo.Should().Be("Groceries");

        JournalLine.Create(AccountId, Side.Debit, OneHundred, memo: "   ")
            .Value.Memo.Should().BeNull();
    }

    [Fact]
    public void Create_rejects_empty_account_id()
    {
        var act = () => JournalLine.Create(Guid.Empty, Side.Debit, OneHundred);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_null_amount()
    {
        var act = () => JournalLine.Create(AccountId, Side.Debit, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_rejects_zero_amount()
    {
        var result = JournalLine.Create(AccountId, Side.Debit, Money.Zero(Currency.Usd));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.line.amount_invalid");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
    }

    [Fact]
    public void Create_rejects_negative_amount()
    {
        var result = JournalLine.Create(AccountId, Side.Debit, Money.Of(-1m, Currency.Usd));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.line.amount_invalid");
    }

    [Fact]
    public void Create_rejects_an_undefined_side()
    {
        var result = JournalLine.Create(AccountId, (Side)(-1), OneHundred);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.line.side_invalid");
    }
}