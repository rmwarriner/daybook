using Daybook.Accounting.Core;

using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// The golden-journal regression fixture (design-spec.md §7.5/§11, GitHub
/// #16): a realistic history - one account of each <see cref="AccountType"/>,
/// a posted reversal, and a still-open draft - persisted, reloaded from a
/// brand-new <see cref="DaybookDbContext"/>, and checked against hard-coded
/// expected values for <see cref="JournalEntry.SchemaVersion"/>,
/// <see cref="TrialBalance"/>, and <see cref="BalanceSheet"/>. This is the
/// durability guarantee golden rule 4 calls for: proof that persisted data
/// still reads back and still derives identical reports under current code.
/// </summary>
/// <remarks>
/// This is the checked-in **v1** golden case - the only schema version that
/// has ever existed. When a real v2 schema change lands, this scenario must
/// not be modified: add a v2 counterpart (plus the upcaster this milestone
/// deliberately deferred) alongside it, so both keep passing.
/// </remarks>
public sealed class GoldenJournalTests : IDisposable
{
    private static readonly DateOnly EntryDate = new(2026, 7, 15);
    private static readonly DateTimeOffset PostedAtUtc = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostedByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly string _dbPath;

    public GoldenJournalTests()
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

    private static JournalLine ADebit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Debit, Money.Of(amount, Currency.Usd)).Value;

    private static JournalLine ACredit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Credit, Money.Of(amount, Currency.Usd)).Value;

    [Fact]
    public async Task A_v1_history_across_every_account_type_round_trips_and_derives_identical_reports()
    {
        var bookId = Guid.NewGuid();

        // Set up: a book with one account of each type.
        await using (var setupContext = AContext())
        {
            await setupContext.Database.MigrateAsync();
            var book = Book.Create(bookId, "Household", Basis.Cash, Currency.Usd, MonthDay.Create(1, 1).Value).Value;
            await new EfBookStore(setupContext).SaveAsync(book);
        }

        Guid checkingId, creditCardId, openingEquityId, salaryId, groceriesId;
        await using (var chartContext = AContext())
        {
            var chart = ChartOfAccounts.Empty();
            var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
            var creditCard = chart.AddRoot(Guid.NewGuid(), "Credit Card", AccountType.Liability).Value;
            var openingEquity = chart.AddRoot(Guid.NewGuid(), "Opening Equity", AccountType.Equity).Value;
            var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
            var groceries = chart.AddRoot(Guid.NewGuid(), "Groceries", AccountType.Expense).Value;
            await new EfChartOfAccountsStore(chartContext).SaveAsync(bookId, chart);
            (checkingId, creditCardId, openingEquityId, salaryId, groceriesId) =
                (checking.Id, creditCard.Id, openingEquity.Id, salary.Id, groceries.Id);
        }

        var groceriesEntryId = Guid.NewGuid();
        var reversalId = Guid.NewGuid();
        var correctedGroceriesId = Guid.NewGuid();
        var draftId = Guid.NewGuid();

        // First session: opening balance and a paycheck.
        await using (var firstContext = AContext())
        {
            var chart = await new EfChartOfAccountsStore(firstContext).LoadAsync(bookId);
            var journalStore = new EfJournalStore(firstContext);
            var journal = Journal.Empty(Currency.Usd);

            journal.CreateDraft(
                Guid.NewGuid(), EntryDate, "Opening balance",
                [ADebit(checkingId, 1000m), ACredit(openingEquityId, 1000m)]);
            journal.Post(journal.Drafts.Single().Id, chart, PostedAtUtc, PostedByUserId);

            journal.CreateDraft(
                Guid.NewGuid(), EntryDate, "Paycheck", [ADebit(checkingId, 2000m), ACredit(salaryId, 2000m)]);
            journal.Post(journal.Drafts.Single().Id, chart, PostedAtUtc, PostedByUserId);

            await journalStore.SaveAsync(bookId, journal);
        }

        // Second session: a groceries purchase on credit (wrong amount,
        // corrected via reversal + a fresh entry), plus a still-open draft.
        await using (var secondContext = AContext())
        {
            var chart = await new EfChartOfAccountsStore(secondContext).LoadAsync(bookId);
            var journalStore = new EfJournalStore(secondContext);
            var journal = await journalStore.LoadAsync(bookId, Currency.Usd);

            journal.CreateDraft(
                groceriesEntryId, EntryDate, "Groceries on credit card",
                [ADebit(groceriesId, 150m), ACredit(creditCardId, 150m)]);
            journal.Post(groceriesEntryId, chart, PostedAtUtc, PostedByUserId);

            journal.CreateDraft(
                draftId, EntryDate, "Pending - unconfirmed", [ADebit(groceriesId, 75m), ACredit(checkingId, 75m)]);

            journal.Reverse(
                groceriesEntryId, reversalId, chart, new DateOnly(2026, 7, 16),
                "Correcting groceries entry", PostedAtUtc, PostedByUserId);

            journal.CreateDraft(
                correctedGroceriesId, new DateOnly(2026, 7, 16), "Groceries on credit card (corrected)",
                [ADebit(groceriesId, 120m), ACredit(creditCardId, 120m)]);
            journal.Post(correctedGroceriesId, chart, PostedAtUtc, PostedByUserId);

            await journalStore.SaveAsync(bookId, journal);
        }

        // Final session: a fresh context/store, as if a new process opened
        // the database - the golden-journal fixture's actual assertion is
        // that this reload still derives identical reports under current
        // code, not that the same in-memory objects still look right.
        await using var finalContext = AContext();
        var finalChart = await new EfChartOfAccountsStore(finalContext).LoadAsync(bookId);
        var finalJournal = await new EfJournalStore(finalContext).LoadAsync(bookId, Currency.Usd);

        finalJournal.PostedEntries.Should().OnlyContain(e => e.SchemaVersion == JournalEntry.CurrentSchemaVersion);
        finalJournal.Drafts.Should().OnlyContain(e => e.SchemaVersion == JournalEntry.CurrentSchemaVersion);
        finalJournal.PostedEntries.Select(e => e.SequenceNumber).Should().Equal(1, 2, 3, 4, 5);
        finalJournal.IsReversed(groceriesEntryId).Should().BeTrue();

        var trialBalance = TrialBalance.Compute(finalChart, finalJournal);
        trialBalance.TotalDebits.Should().Be(trialBalance.TotalCredits);
        trialBalance.TotalDebits.Should().Be(Money.Of(3420m, Currency.Usd));

        Money RolledUp(Guid accountId) => trialBalance.Lines.Single(l => l.AccountId == accountId).RolledUpBalance;
        RolledUp(checkingId).Should().Be(Money.Of(3000m, Currency.Usd));
        RolledUp(creditCardId).Should().Be(Money.Of(120m, Currency.Usd));
        RolledUp(openingEquityId).Should().Be(Money.Of(1000m, Currency.Usd));
        RolledUp(salaryId).Should().Be(Money.Of(2000m, Currency.Usd));
        RolledUp(groceriesId).Should().Be(Money.Of(120m, Currency.Usd));

        var balanceSheet = BalanceSheet.Compute(finalChart, finalJournal);
        balanceSheet.TotalAssets.Should().Be(Money.Of(3000m, Currency.Usd));
        balanceSheet.TotalLiabilities.Should().Be(Money.Of(120m, Currency.Usd));
        balanceSheet.NetIncome.Should().Be(Money.Of(1880m, Currency.Usd));
        balanceSheet.TotalEquity.Should().Be(Money.Of(2880m, Currency.Usd));
        balanceSheet.TotalLiabilitiesAndEquity.Should().Be(balanceSheet.TotalAssets);
    }
}