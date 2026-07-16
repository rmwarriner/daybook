namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 5 — the shared derivation foundation (spec §6.1): own and
/// hierarchical-rollup balance per account, signed onto its normal side.
/// Every other report (trial balance, register, balance sheet) is built on
/// top of this.
/// </summary>
public class AccountBalancesTests
{
    private static readonly DateOnly EntryDate = new(2026, 7, 15);
    private static readonly DateTimeOffset PostedAtUtc = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostedByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static JournalLine ADebit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Debit, Money.Of(amount, Currency.Usd)).Value;

    private static JournalLine ACredit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Credit, Money.Of(amount, Currency.Usd)).Value;

    private static void Post(Journal journal, ChartOfAccounts chart, Guid id, params JournalLine[] lines)
    {
        journal.CreateDraft(id, EntryDate, "Test entry", lines);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Own_balance_of_a_debit_normal_account_is_debits_minus_credits()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty();
        Post(journal, chart, Guid.NewGuid(), ADebit(checking.Id, 500m), ACredit(salary.Id, 500m));
        Post(journal, chart, Guid.NewGuid(), ADebit(salary.Id, 120m), ACredit(checking.Id, 120m));

        var balances = AccountBalances.Compute(chart, journal);

        balances.Find(checking.Id)!.OwnBalance.Should().Be(Money.Of(380m, Currency.Usd));
    }

    [Fact]
    public void Own_balance_of_a_credit_normal_account_is_credits_minus_debits()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty();
        Post(journal, chart, Guid.NewGuid(), ADebit(checking.Id, 500m), ACredit(salary.Id, 500m));

        var balances = AccountBalances.Compute(chart, journal);

        balances.Find(salary.Id)!.OwnBalance.Should().Be(Money.Of(500m, Currency.Usd));
    }

    [Fact]
    public void Rolled_up_balance_includes_all_descendants()
    {
        var chart = ChartOfAccounts.Empty();
        var utilities = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense, isPlaceholder: true).Value;
        var electric = chart.AddChild(Guid.NewGuid(), utilities.Id, "Electric", AccountType.Expense).Value;
        var gas = chart.AddChild(Guid.NewGuid(), utilities.Id, "Gas", AccountType.Expense).Value;
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var journal = Journal.Empty();
        Post(journal, chart, Guid.NewGuid(), ADebit(electric.Id, 50m), ACredit(checking.Id, 50m));
        Post(journal, chart, Guid.NewGuid(), ADebit(gas.Id, 30m), ACredit(checking.Id, 30m));

        var balances = AccountBalances.Compute(chart, journal);

        balances.Find(utilities.Id)!.RolledUpBalance.Should().Be(Money.Of(80m, Currency.Usd));
    }

    [Fact]
    public void A_placeholder_account_has_zero_own_balance_but_nonzero_rolled_up()
    {
        var chart = ChartOfAccounts.Empty();
        var utilities = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense, isPlaceholder: true).Value;
        var electric = chart.AddChild(Guid.NewGuid(), utilities.Id, "Electric", AccountType.Expense).Value;
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var journal = Journal.Empty();
        Post(journal, chart, Guid.NewGuid(), ADebit(electric.Id, 50m), ACredit(checking.Id, 50m));

        var balances = AccountBalances.Compute(chart, journal);

        balances.Find(utilities.Id)!.OwnBalance.Should().Be(Money.Zero(Currency.Usd));
        balances.Find(utilities.Id)!.RolledUpBalance.Should().Be(Money.Of(50m, Currency.Usd));
    }

    [Fact]
    public void An_account_with_no_postings_has_zero_own_and_rolled_up_balance()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;

        var balances = AccountBalances.Compute(chart, Journal.Empty());

        balances.Find(checking.Id)!.OwnBalance.Should().Be(Money.Zero(Currency.Usd));
        balances.Find(checking.Id)!.RolledUpBalance.Should().Be(Money.Zero(Currency.Usd));
    }

    [Fact]
    public void Draft_entries_are_ignored()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty();
        journal.CreateDraft(
            Guid.NewGuid(), EntryDate, "Unposted", [ADebit(checking.Id, 500m), ACredit(salary.Id, 500m)]);

        var balances = AccountBalances.Compute(chart, journal);

        balances.Find(checking.Id)!.OwnBalance.Should().Be(Money.Zero(Currency.Usd));
    }

    [Fact]
    public void Reversing_an_entry_nets_the_account_back_to_zero()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty();
        var entryId = Guid.NewGuid();
        Post(journal, chart, entryId, ADebit(checking.Id, 500m), ACredit(salary.Id, 500m));
        journal.Reverse(entryId, Guid.NewGuid(), chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId);

        var balances = AccountBalances.Compute(chart, journal);

        balances.Find(checking.Id)!.OwnBalance.Should().Be(Money.Zero(Currency.Usd));
        balances.Find(salary.Id)!.OwnBalance.Should().Be(Money.Zero(Currency.Usd));
    }

    [Fact]
    public void Find_returns_null_for_an_unknown_account()
    {
        var balances = AccountBalances.Compute(ChartOfAccounts.Empty(), Journal.Empty());

        balances.Find(Guid.NewGuid()).Should().BeNull();
    }
}