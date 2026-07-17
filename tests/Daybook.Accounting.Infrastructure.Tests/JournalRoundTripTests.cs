using Daybook.Accounting.Core;

using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// M10 slice 5 — closes out journal persistence: a single realistic mixed
/// history (several posted entries across two saves, a still-open draft,
/// and a reversal) round-tripped through <see cref="EfJournalStore"/> and
/// reloaded from a brand-new <see cref="DaybookDbContext"/> pointed at the
/// same file, simulating a fresh process opening the database. Closes with
/// <see cref="TrialBalance.Compute"/> on the reloaded journal — proof the
/// persisted picture is still a valid, balanced ledger, not just a set of
/// fields that happen to match.
/// </summary>
public sealed class JournalRoundTripTests : IDisposable
{
    private static readonly DateOnly EntryDate = new(2026, 7, 15);
    private static readonly DateTimeOffset PostedAtUtc = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostedByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly string _dbPath;

    public JournalRoundTripTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"daybook-test-{Guid.NewGuid()}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private DaybookDbContext AContext()
    {
        var options = new DbContextOptionsBuilder<DaybookDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        return new DaybookDbContext(options);
    }

    private static JournalLine ADebit(Guid accountId, decimal amount, string? memo = null) =>
        JournalLine.Create(accountId, Side.Debit, Money.Of(amount, Currency.Usd), memo).Value;

    private static JournalLine ACredit(Guid accountId, decimal amount, string? memo = null) =>
        JournalLine.Create(accountId, Side.Credit, Money.Of(amount, Currency.Usd), memo).Value;

    [Fact]
    public async Task A_mixed_history_of_drafts_posts_and_a_reversal_round_trips_and_still_balances()
    {
        var bookId = Guid.NewGuid();

        // Set up: a book with a small real-world-shaped chart.
        await using (var setupContext = AContext())
        {
            await setupContext.Database.MigrateAsync();
            var book = Book.Create(bookId, "Household", Basis.Cash, Currency.Usd, MonthDay.Create(1, 1).Value).Value;
            await new EfBookStore(setupContext).SaveAsync(book);
        }

        Guid checkingId, savingsId, salaryId, groceriesId;
        await using (var chartContext = AContext())
        {
            var chart = ChartOfAccounts.Empty();
            var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
            var savings = chart.AddRoot(Guid.NewGuid(), "Savings", AccountType.Asset).Value;
            var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
            var groceries = chart.AddRoot(Guid.NewGuid(), "Groceries", AccountType.Expense).Value;
            await new EfChartOfAccountsStore(chartContext).SaveAsync(bookId, chart);
            (checkingId, savingsId, salaryId, groceriesId) = (checking.Id, savings.Id, salary.Id, groceries.Id);
        }

        var groceriesEntryId = Guid.NewGuid();
        var reversalId = Guid.NewGuid();
        var draftId = Guid.NewGuid();

        // First session: post a paycheck and a (later-regretted) groceries run.
        await using (var firstContext = AContext())
        {
            var chart = await new EfChartOfAccountsStore(firstContext).LoadAsync(bookId);
            var journalStore = new EfJournalStore(firstContext);
            var journal = Journal.Empty(Currency.Usd);

            journal.CreateDraft(
                Guid.NewGuid(), EntryDate, "Paycheck", [ADebit(checkingId, 2000m), ACredit(salaryId, 2000m)]);
            journal.Post(journal.Drafts.Single().Id, chart, PostedAtUtc, PostedByUserId);

            journal.CreateDraft(
                groceriesEntryId, EntryDate, "Groceries run",
                [ADebit(groceriesId, 150m), ACredit(checkingId, 150m)]);
            journal.Post(groceriesEntryId, chart, PostedAtUtc, PostedByUserId);

            await journalStore.SaveAsync(bookId, journal);
        }

        // Second session: reload, transfer to savings, leave a draft open,
        // and reverse the groceries entry (wrong amount, corrected later).
        await using (var secondContext = AContext())
        {
            var chart = await new EfChartOfAccountsStore(secondContext).LoadAsync(bookId);
            var journalStore = new EfJournalStore(secondContext);
            var journal = await journalStore.LoadAsync(bookId, Currency.Usd);

            var transferId = Guid.NewGuid();
            journal.CreateDraft(
                transferId, EntryDate, "Transfer to savings",
                [ADebit(savingsId, 500m), ACredit(checkingId, 500m)]);
            journal.Post(transferId, chart, PostedAtUtc, PostedByUserId);

            journal.CreateDraft(
                draftId, EntryDate, "Pending - unconfirmed", [ADebit(groceriesId, 75m), ACredit(checkingId, 75m)]);

            journal.Reverse(
                groceriesEntryId, reversalId, chart, new DateOnly(2026, 7, 16),
                "Correcting groceries entry", PostedAtUtc, PostedByUserId);

            await journalStore.SaveAsync(bookId, journal);
        }

        // Third session: a fresh context/store, as if a new process opened
        // the same database file - proves this is real persistence, not an
        // artifact of reusing the same change-tracked context.
        await using var finalContext = AContext();
        var finalChart = await new EfChartOfAccountsStore(finalContext).LoadAsync(bookId);
        var finalJournal = await new EfJournalStore(finalContext).LoadAsync(bookId, Currency.Usd);

        finalJournal.PostedEntries.Select(e => e.SequenceNumber).Should().Equal(1, 2, 3, 4);
        finalJournal.Drafts.Should().ContainSingle().Which.Id.Should().Be(draftId);

        finalJournal.IsReversed(groceriesEntryId).Should().BeTrue();
        finalJournal.ReversalOf(groceriesEntryId).Should().Be(reversalId);
        var original = finalJournal.Find(groceriesEntryId)!;
        original.Lines.Should().Contain(l => l.Side == Side.Debit && l.AccountId == groceriesId && l.Amount == Money.Of(150m, Currency.Usd));
        var reversal = finalJournal.Find(reversalId)!;
        reversal.ReversesEntryId.Should().Be(groceriesEntryId);
        reversal.Lines.Should().Contain(l => l.Side == Side.Credit && l.AccountId == groceriesId && l.Amount == Money.Of(150m, Currency.Usd));
        reversal.Lines.Should().Contain(l => l.Side == Side.Debit && l.AccountId == checkingId && l.Amount == Money.Of(150m, Currency.Usd));

        var trialBalance = TrialBalance.Compute(finalChart, finalJournal);
        trialBalance.TotalDebits.Should().Be(trialBalance.TotalCredits);

        var checkingBalance = trialBalance.Lines.Single(l => l.AccountId == checkingId);
        // 2000 (paycheck) - 150 (groceries) - 500 (transfer) + 150 (reversal) = 1500, on Checking's normal (debit) side.
        checkingBalance.RolledUpBalance.Should().Be(Money.Of(1500m, Currency.Usd));

        var salaryBalance = trialBalance.Lines.Single(l => l.AccountId == salaryId);
        salaryBalance.RolledUpBalance.Should().Be(Money.Of(2000m, Currency.Usd));

        var savingsBalance = trialBalance.Lines.Single(l => l.AccountId == savingsId);
        savingsBalance.RolledUpBalance.Should().Be(Money.Of(500m, Currency.Usd));

        var groceriesBalance = trialBalance.Lines.Single(l => l.AccountId == groceriesId);
        groceriesBalance.RolledUpBalance.Should().Be(Money.Zero(Currency.Usd));
    }
}