using Daybook.Accounting.Core;

using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// M10 slice 2 — <see cref="EfJournalStore"/>: proves the EF Core mapping
/// and round-trip against a real, temp-file SQLite database per test.
/// Scope for this slice is the draft lifecycle only (create/update/delete a
/// round trip) — Post/Reverse persistence and the append-only guard are
/// M10 slices 3 and 4.
/// </summary>
public sealed class EfJournalStoreTests : IDisposable
{
    private static readonly DateOnly EntryDate = new(2026, 7, 15);

    private readonly string _dbPath;
    private readonly DaybookDbContext _context;
    private readonly EfBookStore _bookStore;
    private readonly EfChartOfAccountsStore _chartStore;
    private readonly EfJournalStore _store;

    public EfJournalStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"daybook-test-{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<DaybookDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _context = new DaybookDbContext(options);
        _context.Database.Migrate();
        _bookStore = new EfBookStore(_context);
        _chartStore = new EfChartOfAccountsStore(_context);
        _store = new EfJournalStore(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private async Task<(Guid BookId, Account Checking, Account Salary)> ABookWithAccountsAsync()
    {
        var bookId = Guid.NewGuid();
        var book = Book.Create(bookId, "Household", Basis.Cash, Currency.Usd, MonthDay.Create(1, 1).Value).Value;
        await _bookStore.SaveAsync(book);

        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        await _chartStore.SaveAsync(bookId, chart);

        return (bookId, checking, salary);
    }

    [Fact]
    public async Task Loading_an_unknown_book_returns_an_empty_journal()
    {
        var journal = await _store.LoadAsync(Guid.NewGuid(), Currency.Usd);

        journal.Drafts.Should().BeEmpty();
        journal.PostedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Saving_then_loading_round_trips_a_draft_entry()
    {
        var (bookId, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        var debit = JournalLine.Create(checking.Id, Side.Debit, Money.Of(42.50m, Currency.Usd), "Weekly shop").Value;
        var credit = JournalLine.Create(salary.Id, Side.Credit, Money.Of(42.50m, Currency.Usd)).Value;
        journal.CreateDraft(id, EntryDate, "Groceries", [debit, credit]);

        await _store.SaveAsync(bookId, journal);
        var loaded = await _store.LoadAsync(bookId, Currency.Usd);

        var entry = loaded.Find(id);
        entry.Should().NotBeNull();
        entry!.Status.Should().Be(JournalEntryStatus.Draft);
        entry.EntryDate.Should().Be(EntryDate);
        entry.Description.Should().Be("Groceries");
        entry.SequenceNumber.Should().BeNull();
        entry.Lines.Should().HaveCount(2);
        entry.Lines.Should().Contain(l =>
            l.AccountId == checking.Id && l.Side == Side.Debit &&
            l.Amount == Money.Of(42.50m, Currency.Usd) && l.Memo == "Weekly shop");
        entry.Lines.Should().Contain(l =>
            l.AccountId == salary.Id && l.Side == Side.Credit &&
            l.Amount == Money.Of(42.50m, Currency.Usd) && l.Memo == null);
    }

    [Fact]
    public async Task Draft_line_order_is_preserved()
    {
        var (bookId, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        var lines = new[]
        {
            JournalLine.Create(checking.Id, Side.Debit, Money.Of(10m, Currency.Usd), "First").Value,
            JournalLine.Create(checking.Id, Side.Debit, Money.Of(20m, Currency.Usd), "Second").Value,
            JournalLine.Create(salary.Id, Side.Credit, Money.Of(30m, Currency.Usd), "Third").Value,
        };
        journal.CreateDraft(id, EntryDate, "Ordered lines", lines);

        await _store.SaveAsync(bookId, journal);
        var loaded = await _store.LoadAsync(bookId, Currency.Usd);

        loaded.Find(id)!.Lines.Select(l => l.Memo).Should().Equal("First", "Second", "Third");
    }

    [Fact]
    public async Task Resaving_an_updated_draft_persists_the_change()
    {
        var (bookId, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(
            id, EntryDate, "Groceries",
            [JournalLine.Create(checking.Id, Side.Debit, Money.Of(10m, Currency.Usd)).Value,
             JournalLine.Create(salary.Id, Side.Credit, Money.Of(10m, Currency.Usd)).Value]);
        await _store.SaveAsync(bookId, journal);

        journal.UpdateDraft(
            id, new DateOnly(2026, 7, 16), "Corrected groceries",
            [JournalLine.Create(checking.Id, Side.Debit, Money.Of(99m, Currency.Usd)).Value,
             JournalLine.Create(salary.Id, Side.Credit, Money.Of(99m, Currency.Usd)).Value]);
        await _store.SaveAsync(bookId, journal);

        var loaded = await _store.LoadAsync(bookId, Currency.Usd);
        var entry = loaded.Find(id)!;
        entry.Description.Should().Be("Corrected groceries");
        entry.EntryDate.Should().Be(new DateOnly(2026, 7, 16));
        entry.Lines.Should().OnlyContain(l => l.Amount == Money.Of(99m, Currency.Usd));
    }

    [Fact]
    public async Task Deleting_a_draft_then_resaving_removes_it()
    {
        var (bookId, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(
            id, EntryDate, "Groceries",
            [JournalLine.Create(checking.Id, Side.Debit, Money.Of(10m, Currency.Usd)).Value,
             JournalLine.Create(salary.Id, Side.Credit, Money.Of(10m, Currency.Usd)).Value]);
        await _store.SaveAsync(bookId, journal);

        journal.DeleteDraft(id);
        await _store.SaveAsync(bookId, journal);

        var loaded = await _store.LoadAsync(bookId, Currency.Usd);
        loaded.Find(id).Should().BeNull();
    }

    [Fact]
    public async Task Multiple_drafts_round_trip_independently()
    {
        var (bookId, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        journal.CreateDraft(
            firstId, EntryDate, "First",
            [JournalLine.Create(checking.Id, Side.Debit, Money.Of(10m, Currency.Usd)).Value,
             JournalLine.Create(salary.Id, Side.Credit, Money.Of(10m, Currency.Usd)).Value]);
        journal.CreateDraft(
            secondId, EntryDate, "Second",
            [JournalLine.Create(checking.Id, Side.Debit, Money.Of(20m, Currency.Usd)).Value,
             JournalLine.Create(salary.Id, Side.Credit, Money.Of(20m, Currency.Usd)).Value]);

        await _store.SaveAsync(bookId, journal);
        var loaded = await _store.LoadAsync(bookId, Currency.Usd);

        loaded.Drafts.Select(e => e.Id).Should().BeEquivalentTo([firstId, secondId]);
        loaded.Find(firstId)!.Description.Should().Be("First");
        loaded.Find(secondId)!.Description.Should().Be("Second");
    }

    [Fact]
    public async Task Journals_for_different_books_are_isolated()
    {
        var (bookA, checkingA, salaryA) = await ABookWithAccountsAsync();
        var (bookB, checkingB, salaryB) = await ABookWithAccountsAsync();
        var journalA = Journal.Empty(Currency.Usd);
        journalA.CreateDraft(
            Guid.NewGuid(), EntryDate, "In book A",
            [JournalLine.Create(checkingA.Id, Side.Debit, Money.Of(10m, Currency.Usd)).Value,
             JournalLine.Create(salaryA.Id, Side.Credit, Money.Of(10m, Currency.Usd)).Value]);
        var journalB = Journal.Empty(Currency.Usd);
        journalB.CreateDraft(
            Guid.NewGuid(), EntryDate, "In book B",
            [JournalLine.Create(checkingB.Id, Side.Debit, Money.Of(20m, Currency.Usd)).Value,
             JournalLine.Create(salaryB.Id, Side.Credit, Money.Of(20m, Currency.Usd)).Value]);

        await _store.SaveAsync(bookA, journalA);
        await _store.SaveAsync(bookB, journalB);

        var loadedA = await _store.LoadAsync(bookA, Currency.Usd);
        loadedA.Drafts.Should().ContainSingle().Which.Description.Should().Be("In book A");
    }
}