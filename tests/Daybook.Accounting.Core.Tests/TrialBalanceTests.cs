using CsCheck;

namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 5 — <see cref="TrialBalance"/> (spec §6.2): a rolled-up line
/// per account, plus the total-debits-equals-total-credits integrity check
/// spec names as impossible to fail given §5 — computed directly from
/// posted lines (not from summed rolled-up balances, which would
/// double-count a parent's own total against its descendants').
/// </summary>
public class TrialBalanceTests
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
    public void Includes_a_line_per_account_with_side_own_and_rolled_up_balance()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty();
        Post(journal, chart, Guid.NewGuid(), ADebit(checking.Id, 500m), ACredit(salary.Id, 500m));

        var trialBalance = TrialBalance.Compute(chart, journal);

        trialBalance.Lines.Should().Contain(l =>
            l.AccountId == checking.Id &&
            l.NormalBalance == Side.Debit &&
            l.OwnBalance == Money.Of(500m, Currency.Usd) &&
            l.RolledUpBalance == Money.Of(500m, Currency.Usd));
    }

    [Fact]
    public void Total_debits_equals_total_credits()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var rent = chart.AddRoot(Guid.NewGuid(), "Rent", AccountType.Expense).Value;
        var journal = Journal.Empty();
        Post(journal, chart, Guid.NewGuid(), ADebit(checking.Id, 500m), ACredit(salary.Id, 500m));
        Post(journal, chart, Guid.NewGuid(), ADebit(rent.Id, 120m), ACredit(checking.Id, 120m));

        var trialBalance = TrialBalance.Compute(chart, journal);

        trialBalance.TotalDebits.Should().Be(trialBalance.TotalCredits);
        trialBalance.TotalDebits.Should().Be(Money.Of(620m, Currency.Usd));
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

        var trialBalance = TrialBalance.Compute(chart, journal);

        trialBalance.TotalDebits.Should().Be(Money.Zero(Currency.Usd));
        trialBalance.TotalCredits.Should().Be(Money.Zero(Currency.Usd));
    }

    [Fact]
    public void An_empty_journal_balances_at_zero()
    {
        var trialBalance = TrialBalance.Compute(ChartOfAccounts.Empty(), Journal.Empty());

        trialBalance.Lines.Should().BeEmpty();
        trialBalance.TotalDebits.Should().Be(Money.Zero(Currency.Usd));
        trialBalance.TotalCredits.Should().Be(Money.Zero(Currency.Usd));
    }

    // ---- Property-based: spec's own named example ("trial balance always balances") ----

    [Fact]
    public void Property_the_trial_balance_always_balances()
    {
        Gen.Int[1, 1000].Sample(seed =>
        {
            var random = new Random(seed);
            var chart = ChartOfAccounts.Empty();
            var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
            var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
            var journal = Journal.Empty();

            var entryCount = random.Next(1, 6);
            for (var i = 0; i < entryCount; i++)
            {
                var amount = (decimal)random.Next(1, 100_000) / 100m;
                Post(journal, chart, Guid.NewGuid(), ADebit(checking.Id, amount), ACredit(salary.Id, amount));
            }

            var act = () => TrialBalance.Compute(chart, journal);

            act.Should().NotThrow();
            var trialBalance = act();
            trialBalance.TotalDebits.Should().Be(trialBalance.TotalCredits);
        });
    }
}