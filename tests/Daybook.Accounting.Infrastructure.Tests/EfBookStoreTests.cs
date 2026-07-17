using Daybook.Accounting.Core;

using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// Milestone 9 — <see cref="EfBookStore"/>: proves the EF Core mapping and
/// round-trip against a real, temp-file SQLite database per test (per
/// CLAUDE.md's testing convention — no mocks).
/// </summary>
public sealed class EfBookStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DaybookDbContext _context;
    private readonly EfBookStore _store;

    public EfBookStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"daybook-test-{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<DaybookDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _context = new DaybookDbContext(options);
        _context.Database.Migrate();
        _store = new EfBookStore(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public async Task Saving_then_finding_a_book_round_trips_all_fields()
    {
        var id = Guid.NewGuid();
        var fiscalYearStart = MonthDay.Create(1, 1).Value;
        var book = Book.Create(id, "Household", Basis.Cash, Currency.Usd, fiscalYearStart).Value;

        await _store.SaveAsync(book);
        var found = await _store.FindAsync(id);

        found.Should().NotBeNull();
        found!.Id.Should().Be(id);
        found.Name.Should().Be("Household");
        found.Basis.Should().Be(Basis.Cash);
        found.BaseCurrency.Should().Be(Currency.Usd);
        found.FiscalYearStart.Should().Be(fiscalYearStart);
        found.Status.Should().Be(BookStatus.Open);
    }

    [Fact]
    public async Task Finding_an_unknown_book_returns_null()
    {
        var found = await _store.FindAsync(Guid.NewGuid());

        found.Should().BeNull();
    }

    [Fact]
    public async Task Saving_an_already_persisted_book_updates_it_in_place()
    {
        var id = Guid.NewGuid();
        var book = Book.Create(id, "Household", Basis.Cash, Currency.Usd, MonthDay.Create(1, 1).Value).Value;
        await _store.SaveAsync(book);

        var renamed = book.Rename("Family Household").Value.Archive();
        await _store.SaveAsync(renamed);

        var found = await _store.FindAsync(id);
        found!.Name.Should().Be("Family Household");
        found.Status.Should().Be(BookStatus.Archived);
    }

    [Fact]
    public async Task Round_trips_a_non_default_fiscal_year_start_and_accrual_basis()
    {
        var id = Guid.NewGuid();
        var fiscalYearStart = MonthDay.Create(4, 6).Value;
        var book = Book.Create(id, "Household", Basis.Accrual, Currency.Usd, fiscalYearStart).Value;

        await _store.SaveAsync(book);
        var found = await _store.FindAsync(id);

        found!.Basis.Should().Be(Basis.Accrual);
        found.FiscalYearStart.Should().Be(fiscalYearStart);
    }
}