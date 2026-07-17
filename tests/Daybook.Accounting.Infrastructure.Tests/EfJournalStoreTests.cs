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
    private static readonly DateTimeOffset PostedAtUtc = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostedByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

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

    private async Task<(Guid BookId, ChartOfAccounts Chart, Account Checking, Account Salary)> ABookWithAccountsAsync()
    {
        var bookId = Guid.NewGuid();
        var book = Book.Create(bookId, "Household", Basis.Cash, Currency.Usd, MonthDay.Create(1, 1).Value).Value;
        await _bookStore.SaveAsync(book);

        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        await _chartStore.SaveAsync(bookId, chart);

        return (bookId, chart, checking, salary);
    }

    private static JournalLine ADebit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Debit, Money.Of(amount, Currency.Usd)).Value;

    private static JournalLine ACredit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Credit, Money.Of(amount, Currency.Usd)).Value;

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
        var (bookId, _, checking, salary) = await ABookWithAccountsAsync();
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
        var (bookId, _, checking, salary) = await ABookWithAccountsAsync();
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
        var (bookId, _, checking, salary) = await ABookWithAccountsAsync();
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
        var (bookId, _, checking, salary) = await ABookWithAccountsAsync();
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
        var (bookId, _, checking, salary) = await ABookWithAccountsAsync();
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

    // ---- Post: sequence numbers survive the round trip ------------------

    [Fact]
    public async Task Posting_persists_the_sequence_number_and_posting_stamps()
    {
        var (bookId, chart, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 1000m), ACredit(salary.Id, 1000m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        await _store.SaveAsync(bookId, journal);
        var loaded = await _store.LoadAsync(bookId, Currency.Usd);

        var entry = loaded.Find(id)!;
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.SequenceNumber.Should().Be(1);
        entry.PostedAtUtc.Should().Be(PostedAtUtc);
        entry.PostedByUserId.Should().Be(PostedByUserId);
        loaded.PostedEntries.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Fact]
    public async Task Reloading_then_posting_again_continues_the_gapless_sequence()
    {
        var (bookId, chart, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var firstId = Guid.NewGuid();
        journal.CreateDraft(firstId, EntryDate, "First", [ADebit(checking.Id, 10m), ACredit(salary.Id, 10m)]);
        journal.Post(firstId, chart, PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, journal);

        var reloaded = await _store.LoadAsync(bookId, Currency.Usd);
        var secondId = Guid.NewGuid();
        reloaded.CreateDraft(secondId, EntryDate, "Second", [ADebit(checking.Id, 20m), ACredit(salary.Id, 20m)]);
        var result = reloaded.Post(secondId, chart, PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, reloaded);

        result.Value.SequenceNumber.Should().Be(2);
        var finalLoad = await _store.LoadAsync(bookId, Currency.Usd);
        finalLoad.PostedEntries.Select(e => e.SequenceNumber).Should().Equal(1, 2);
    }

    // ---- Append-only guard (spec §7.4) -----------------------------------

    [Fact]
    public async Task Saving_never_overwrites_an_already_posted_entrys_row()
    {
        var (bookId, chart, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Original", [ADebit(checking.Id, 10m), ACredit(salary.Id, 10m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, journal);

        // Core's own API can never produce a modified Posted entry - the
        // only way to construct one sharing this id is to bypass normal
        // flow via Rehydrate, simulating a bug or a rogue write.
        var tampered = Journal.Rehydrate(
            Currency.Usd,
            [new JournalEntrySnapshot(
                id, EntryDate, "Tampered", [ADebit(checking.Id, 999m), ACredit(salary.Id, 999m)],
                JournalEntryStatus.Posted, SequenceNumber: 1, PostedAtUtc: PostedAtUtc,
                PostedByUserId: PostedByUserId, ReversesEntryId: null)]);

        await _store.SaveAsync(bookId, tampered);

        var loaded = await _store.LoadAsync(bookId, Currency.Usd);
        var entry = loaded.Find(id)!;
        entry.Description.Should().Be("Original");
        entry.Lines.Should().OnlyContain(l => l.Amount == Money.Of(10m, Currency.Usd));
    }

    [Fact]
    public async Task Saving_never_deletes_an_already_posted_entry_missing_from_the_incoming_journal()
    {
        var (bookId, chart, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 10m), ACredit(salary.Id, 10m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, journal);

        // A caller mistakenly saving a fresh Journal.Empty() instead of one
        // it loaded first must not be able to wipe out existing history.
        var forgotToLoad = Journal.Empty(Currency.Usd);
        await _store.SaveAsync(bookId, forgotToLoad);

        var loaded = await _store.LoadAsync(bookId, Currency.Usd);
        loaded.Find(id).Should().NotBeNull();
    }

    // ---- Reverse ----------------------------------------------------------

    [Fact]
    public async Task Reversing_a_posted_entry_round_trips_the_reversal()
    {
        var (bookId, chart, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var originalId = Guid.NewGuid();
        journal.CreateDraft(originalId, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(originalId, chart, PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, journal);

        var reversalId = Guid.NewGuid();
        var reversalDate = new DateOnly(2026, 7, 20);
        journal.Reverse(originalId, reversalId, chart, reversalDate, "Reversing paycheck", PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, journal);

        var loaded = await _store.LoadAsync(bookId, Currency.Usd);
        var reversal = loaded.Find(reversalId)!;
        reversal.Status.Should().Be(JournalEntryStatus.Posted);
        reversal.EntryDate.Should().Be(reversalDate);
        reversal.Description.Should().Be("Reversing paycheck");
        reversal.SequenceNumber.Should().Be(2);
        reversal.ReversesEntryId.Should().Be(originalId);
        reversal.Lines.Should().Contain(l =>
            l.AccountId == checking.Id && l.Side == Side.Credit && l.Amount == Money.Of(100m, Currency.Usd));
        reversal.Lines.Should().Contain(l =>
            l.AccountId == salary.Id && l.Side == Side.Debit && l.Amount == Money.Of(100m, Currency.Usd));
    }

    [Fact]
    public async Task Reload_restores_the_reversal_link_both_directions()
    {
        var (bookId, chart, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var originalId = Guid.NewGuid();
        journal.CreateDraft(originalId, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(originalId, chart, PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, journal);
        var reversalId = Guid.NewGuid();
        journal.Reverse(originalId, reversalId, chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, journal);

        var loaded = await _store.LoadAsync(bookId, Currency.Usd);

        loaded.ReversalOf(originalId).Should().Be(reversalId);
        loaded.IsReversed(originalId).Should().BeTrue();
    }

    [Fact]
    public async Task Saving_a_reversal_never_touches_the_original_entrys_row()
    {
        var (bookId, chart, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var originalId = Guid.NewGuid();
        journal.CreateDraft(originalId, EntryDate, "Original paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(originalId, chart, PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, journal);

        journal.Reverse(originalId, Guid.NewGuid(), chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, journal);

        var loaded = await _store.LoadAsync(bookId, Currency.Usd);
        var original = loaded.Find(originalId)!;
        original.Description.Should().Be("Original paycheck");
        original.SequenceNumber.Should().Be(1);
        original.Lines.Should().Contain(l => l.Side == Side.Debit && l.Amount == Money.Of(100m, Currency.Usd));
    }

    [Fact]
    public async Task Posting_and_reversing_before_the_first_save_round_trips_both_rows()
    {
        var (bookId, chart, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var originalId = Guid.NewGuid();
        journal.CreateDraft(originalId, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(originalId, chart, PostedAtUtc, PostedByUserId);
        var reversalId = Guid.NewGuid();
        journal.Reverse(originalId, reversalId, chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId);

        await _store.SaveAsync(bookId, journal);

        var loaded = await _store.LoadAsync(bookId, Currency.Usd);
        loaded.PostedEntries.Select(e => e.SequenceNumber).Should().Equal(1, 2);
        loaded.ReversalOf(originalId).Should().Be(reversalId);
    }

    [Fact]
    public async Task A_reversal_is_itself_reversible_across_reloads()
    {
        var (bookId, chart, checking, salary) = await ABookWithAccountsAsync();
        var journal = Journal.Empty(Currency.Usd);
        var originalId = Guid.NewGuid();
        journal.CreateDraft(originalId, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(originalId, chart, PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, journal);

        var reloaded1 = await _store.LoadAsync(bookId, Currency.Usd);
        var reversalId = Guid.NewGuid();
        reloaded1.Reverse(originalId, reversalId, chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, reloaded1);

        var reloaded2 = await _store.LoadAsync(bookId, Currency.Usd);
        var reversalOfReversalId = Guid.NewGuid();
        var result = reloaded2.Reverse(
            reversalId, reversalOfReversalId, chart, EntryDate, "Reversal of reversal", PostedAtUtc, PostedByUserId);
        await _store.SaveAsync(bookId, reloaded2);

        result.IsSuccess.Should().BeTrue();
        var final = await _store.LoadAsync(bookId, Currency.Usd);
        final.PostedEntries.Select(e => e.SequenceNumber).Should().Equal(1, 2, 3);
        final.ReversalOf(reversalId).Should().Be(reversalOfReversalId);
    }

    [Fact]
    public async Task Journals_for_different_books_are_isolated()
    {
        var (bookA, _, checkingA, salaryA) = await ABookWithAccountsAsync();
        var (bookB, _, checkingB, salaryB) = await ABookWithAccountsAsync();
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