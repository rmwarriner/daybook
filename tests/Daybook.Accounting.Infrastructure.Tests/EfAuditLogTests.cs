using Daybook.Accounting.Core;

using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// <see cref="EfAuditLog"/>: proves the EF Core mapping and round-trip
/// against a real, temp-file SQLite database per test (design-spec §9).
/// Append-only is structural — <see cref="IAuditLog"/> exposes no
/// update/delete method at all — so the one thing worth proving beyond a
/// plain round-trip is that a genuine duplicate-id append is rejected by
/// the table's own primary key, with no extra guard code needed.
/// </summary>
public sealed class EfAuditLogTests : IDisposable
{
    private static readonly DateOnly EntryDate = new(2026, 7, 15);
    private static readonly DateTimeOffset PostedAtUtc = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostedByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly string _dbPath;
    private readonly DaybookDbContext _context;
    private readonly EfBookStore _bookStore;
    private readonly EfChartOfAccountsStore _chartStore;
    private readonly EfJournalStore _journalStore;
    private readonly EfAuditLog _auditLog;

    public EfAuditLogTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"daybook-test-{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<DaybookDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _context = new DaybookDbContext(options);
        _context.Database.Migrate();
        _bookStore = new EfBookStore(_context);
        _chartStore = new EfChartOfAccountsStore(_context);
        _journalStore = new EfJournalStore(_context);
        _auditLog = new EfAuditLog(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static JournalLine ADebit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Debit, Money.Of(amount, Currency.Usd)).Value;

    private static JournalLine ACredit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Credit, Money.Of(amount, Currency.Usd)).Value;

    private async Task<Guid> APostedEntryAsync()
    {
        var bookId = Guid.NewGuid();
        var book = Book.Create(bookId, "Household", Basis.Cash, Currency.Usd, MonthDay.Create(1, 1).Value).Value;
        await _bookStore.SaveAsync(book);

        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        await _chartStore.SaveAsync(bookId, chart);

        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);
        await _journalStore.SaveAsync(bookId, journal);

        return id;
    }

    private static AuditLogEntry AnEntry(Guid entryId, int sequenceNumber) => new(
        Guid.NewGuid(),
        entryId,
        sequenceNumber,
        PostedByUserId,
        PostedAtUtc,
        JournalEntryStatus.Draft,
        JournalEntryStatus.Posted,
        Guid.NewGuid());

    [Fact]
    public async Task AppendAsync_then_GetForEntryAsync_round_trips_every_field()
    {
        var entryId = await APostedEntryAsync();
        var entry = AnEntry(entryId, 1);

        await _auditLog.AppendAsync(entry);
        var found = await _auditLog.GetForEntryAsync(entryId);

        found.Should().ContainSingle().Which.Should().Be(entry);
    }

    [Fact]
    public async Task GetForEntryAsync_only_returns_the_matching_entrys_rows()
    {
        var firstEntryId = await APostedEntryAsync();
        var secondEntryId = await APostedEntryAsync();
        await _auditLog.AppendAsync(AnEntry(firstEntryId, 1));
        await _auditLog.AppendAsync(AnEntry(secondEntryId, 1));

        var found = await _auditLog.GetForEntryAsync(firstEntryId);

        found.Should().ContainSingle().Which.EntryId.Should().Be(firstEntryId);
    }

    [Fact]
    public async Task GetForEntryAsync_for_an_entry_with_no_audit_history_returns_empty()
    {
        var entryId = await APostedEntryAsync();

        var found = await _auditLog.GetForEntryAsync(entryId);

        found.Should().BeEmpty();
    }

    [Fact]
    public async Task AppendAsync_rejects_a_duplicate_id()
    {
        // A fresh context for the second append is what makes this a real
        // test of the table's own primary key - reusing the tracking
        // _context here would just hit EF's in-memory identity-map
        // conflict (InvalidOperationException) before ever reaching the
        // database, proving nothing about the actual append-only guard.
        var entryId = await APostedEntryAsync();
        var id = Guid.NewGuid();
        await _auditLog.AppendAsync(AnEntry(entryId, 1) with { Id = id });

        await using var freshContext = new DaybookDbContext(
            new DbContextOptionsBuilder<DaybookDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var act = () => new EfAuditLog(freshContext).AppendAsync(AnEntry(entryId, 1) with { Id = id });

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}