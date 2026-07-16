namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 5 — <see cref="AccountRegister"/> (spec §6.3): posted lines for
/// an account (or its whole subtree) in <c>(EntryDate, SequenceNumber)</c>
/// order with a running balance signed onto the account's normal side —
/// "the account register a household user reads day to day."
/// </summary>
public class AccountRegisterTests
{
    private static readonly DateTimeOffset PostedAtUtc = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostedByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static JournalLine ADebit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Debit, Money.Of(amount, Currency.Usd)).Value;

    private static JournalLine ACredit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Credit, Money.Of(amount, Currency.Usd)).Value;

    private static JournalEntry Post(
        Journal journal, ChartOfAccounts chart, DateOnly entryDate, string description, params JournalLine[] lines)
    {
        var id = Guid.NewGuid();
        journal.CreateDraft(id, entryDate, description, lines);
        return journal.Post(id, chart, PostedAtUtc, PostedByUserId).Value;
    }

    [Fact]
    public void Returns_own_lines_with_a_running_balance_signed_onto_the_normal_side()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var rent = chart.AddRoot(Guid.NewGuid(), "Rent", AccountType.Expense).Value;
        var journal = Journal.Empty();
        var paycheck = Post(
            journal, chart, new DateOnly(2026, 7, 1), "Paycheck", ADebit(checking.Id, 1000m), ACredit(salary.Id, 1000m));
        var rentPayment = Post(
            journal, chart, new DateOnly(2026, 7, 5), "Rent", ADebit(rent.Id, 400m), ACredit(checking.Id, 400m));

        var register = AccountRegister.Compute(checking.Id, chart, journal).Value;

        register.AccountId.Should().Be(checking.Id);
        register.Lines.Should().HaveCount(2);
        register.Lines[0].EntryId.Should().Be(paycheck.Id);
        register.Lines[0].SequenceNumber.Should().Be(paycheck.SequenceNumber);
        register.Lines[0].EntryDate.Should().Be(new DateOnly(2026, 7, 1));
        register.Lines[0].Description.Should().Be("Paycheck");
        register.Lines[0].Side.Should().Be(Side.Debit);
        register.Lines[0].Amount.Should().Be(Money.Of(1000m, Currency.Usd));
        register.Lines[0].RunningBalance.Should().Be(Money.Of(1000m, Currency.Usd));

        register.Lines[1].EntryId.Should().Be(rentPayment.Id);
        register.Lines[1].Side.Should().Be(Side.Credit);
        register.Lines[1].RunningBalance.Should().Be(Money.Of(600m, Currency.Usd));
    }

    [Fact]
    public void Running_balance_for_a_credit_normal_account_credits_increase_it()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty();
        Post(journal, chart, new DateOnly(2026, 7, 1), "Paycheck", ADebit(checking.Id, 1000m), ACredit(salary.Id, 1000m));

        var register = AccountRegister.Compute(salary.Id, chart, journal).Value;

        register.Lines.Should().ContainSingle().Which.RunningBalance.Should().Be(Money.Of(1000m, Currency.Usd));
    }

    [Fact]
    public void Orders_lines_by_entry_date_then_sequence_number_regardless_of_post_order()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty();
        var later = Post(
            journal, chart, new DateOnly(2026, 7, 20), "Later", ADebit(checking.Id, 20m), ACredit(salary.Id, 20m));
        var earlier = Post(
            journal, chart, new DateOnly(2026, 7, 1), "Earlier", ADebit(checking.Id, 10m), ACredit(salary.Id, 10m));

        var register = AccountRegister.Compute(checking.Id, chart, journal).Value;

        register.Lines.Select(l => l.EntryId).Should().Equal(earlier.Id, later.Id);
    }

    [Fact]
    public void Excludes_descendants_by_default()
    {
        var chart = ChartOfAccounts.Empty();
        var utilities = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense, isPlaceholder: true).Value;
        var electric = chart.AddChild(Guid.NewGuid(), utilities.Id, "Electric", AccountType.Expense).Value;
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var journal = Journal.Empty();
        Post(journal, chart, new DateOnly(2026, 7, 1), "Electric bill", ADebit(electric.Id, 50m), ACredit(checking.Id, 50m));

        var register = AccountRegister.Compute(utilities.Id, chart, journal).Value;

        register.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Including_descendants_merges_the_whole_subtree_in_order()
    {
        var chart = ChartOfAccounts.Empty();
        var utilities = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense, isPlaceholder: true).Value;
        var electric = chart.AddChild(Guid.NewGuid(), utilities.Id, "Electric", AccountType.Expense).Value;
        var gas = chart.AddChild(Guid.NewGuid(), utilities.Id, "Gas", AccountType.Expense).Value;
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var journal = Journal.Empty();
        var gasBill = Post(
            journal, chart, new DateOnly(2026, 7, 1), "Gas bill", ADebit(gas.Id, 30m), ACredit(checking.Id, 30m));
        var electricBill = Post(
            journal, chart, new DateOnly(2026, 7, 5), "Electric bill", ADebit(electric.Id, 50m), ACredit(checking.Id, 50m));

        var register = AccountRegister.Compute(utilities.Id, chart, journal, includeDescendants: true).Value;

        register.Lines.Select(l => l.EntryId).Should().Equal(gasBill.Id, electricBill.Id);
        register.Lines[^1].RunningBalance.Should().Be(Money.Of(80m, Currency.Usd));
    }

    [Fact]
    public void Draft_entries_are_ignored()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty();
        journal.CreateDraft(
            Guid.NewGuid(), new DateOnly(2026, 7, 1), "Unposted", [ADebit(checking.Id, 1000m), ACredit(salary.Id, 1000m)]);

        var register = AccountRegister.Compute(checking.Id, chart, journal).Value;

        register.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_an_unknown_account()
    {
        var result = AccountRegister.Compute(Guid.NewGuid(), ChartOfAccounts.Empty(), Journal.Empty());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.not_found");
    }
}