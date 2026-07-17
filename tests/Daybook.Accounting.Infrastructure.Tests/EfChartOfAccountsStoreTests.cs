using Daybook.Accounting.Core;

using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// Milestone 9 — <see cref="EfChartOfAccountsStore"/>: proves the EF Core
/// mapping and round-trip against a real, temp-file SQLite database per
/// test. The hierarchy-reconstruction logic (parent-before-child) lives in
/// the internal <c>ChartOfAccountsMapper</c> and has no public surface of
/// its own, so it's covered here through the store, the same pattern
/// already used for <c>HtmlReportDocument</c> in Milestone 8.
/// </summary>
public sealed class EfChartOfAccountsStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DaybookDbContext _context;
    private readonly EfBookStore _bookStore;
    private readonly EfChartOfAccountsStore _store;

    public EfChartOfAccountsStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"daybook-test-{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<DaybookDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _context = new DaybookDbContext(options);
        _context.Database.Migrate();
        _bookStore = new EfBookStore(_context);
        _store = new EfChartOfAccountsStore(_context);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    /// <summary>
    /// Accounts.BookId has an enforced foreign key to Books.Id (spec §7.2)
    /// — a chart never stands alone in the schema, so every test needs a
    /// real persisted Book behind whichever bookId it uses.
    /// </summary>
    private async Task<Guid> ABookAsync()
    {
        var id = Guid.NewGuid();
        var book = Book.Create(id, "Household", Basis.Cash, Currency.Usd, MonthDay.Create(1, 1).Value).Value;
        await _bookStore.SaveAsync(book);
        return id;
    }

    [Fact]
    public async Task Loading_an_unknown_book_returns_an_empty_chart()
    {
        var chart = await _store.LoadAsync(Guid.NewGuid());

        chart.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task Saving_then_loading_round_trips_a_flat_set_of_root_accounts()
    {
        var bookId = await ABookAsync();
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset, code: "1000").Value;
        var savings = chart.AddRoot(Guid.NewGuid(), "Savings", AccountType.Asset, isActive: false).Value;

        await _store.SaveAsync(bookId, chart);
        var loaded = await _store.LoadAsync(bookId);

        var loadedChecking = loaded.Find(checking.Id);
        loadedChecking.Should().NotBeNull();
        loadedChecking!.Name.Should().Be("Checking");
        loadedChecking.Type.Should().Be(AccountType.Asset);
        loadedChecking.Code.Should().Be("1000");
        loadedChecking.IsActive.Should().BeTrue();
        loadedChecking.ParentAccountId.Should().BeNull();

        var loadedSavings = loaded.Find(savings.Id);
        loadedSavings!.Code.Should().BeNull();
        loadedSavings.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Saving_then_loading_round_trips_a_hierarchy()
    {
        var bookId = await ABookAsync();
        var chart = ChartOfAccounts.Empty();
        var utilities = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense, isPlaceholder: true).Value;
        var electric = chart.AddChild(Guid.NewGuid(), utilities.Id, "Electric", AccountType.Expense).Value;
        var gas = chart.AddChild(Guid.NewGuid(), utilities.Id, "Gas", AccountType.Expense).Value;

        await _store.SaveAsync(bookId, chart);
        var loaded = await _store.LoadAsync(bookId);

        loaded.Find(electric.Id)!.ParentAccountId.Should().Be(utilities.Id);
        loaded.Find(gas.Id)!.ParentAccountId.Should().Be(utilities.Id);
        loaded.Find(utilities.Id)!.IsPlaceholder.Should().BeTrue();
        loaded.Children(utilities.Id).Select(a => a.Id).Should().BeEquivalentTo([electric.Id, gas.Id]);
        loaded.DisplayPathOf(electric.Id).Value.Should().Be("Utilities:Electric");
    }

    [Fact]
    public async Task Charts_for_different_books_are_isolated()
    {
        var bookA = await ABookAsync();
        var bookB = await ABookAsync();
        var chartA = ChartOfAccounts.Empty();
        chartA.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset);
        var chartB = ChartOfAccounts.Empty();
        chartB.AddRoot(Guid.NewGuid(), "Savings", AccountType.Asset);

        await _store.SaveAsync(bookA, chartA);
        await _store.SaveAsync(bookB, chartB);

        var loadedA = await _store.LoadAsync(bookA);
        loadedA.Accounts.Should().ContainSingle().Which.Name.Should().Be("Checking");
    }

    [Fact]
    public async Task Resaving_a_modified_chart_persists_the_update()
    {
        var bookId = await ABookAsync();
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        await _store.SaveAsync(bookId, chart);

        chart.Rename(checking.Id, "Primary Checking");
        chart.Deactivate(checking.Id);
        await _store.SaveAsync(bookId, chart);

        var loaded = await _store.LoadAsync(bookId);
        var loadedChecking = loaded.Find(checking.Id)!;
        loadedChecking.Name.Should().Be("Primary Checking");
        loadedChecking.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Two_different_books_can_reuse_the_same_code()
    {
        var bookA = await ABookAsync();
        var bookB = await ABookAsync();
        var chartA = ChartOfAccounts.Empty();
        chartA.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset, code: "1000");
        var chartB = ChartOfAccounts.Empty();
        chartB.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset, code: "1000");

        await _store.SaveAsync(bookA, chartA);
        var act = async () => await _store.SaveAsync(bookB, chartB);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_unique_code_constraint_is_enforced_per_book_even_across_independently_loaded_charts()
    {
        var bookId = await ABookAsync();
        var firstChart = ChartOfAccounts.Empty();
        firstChart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset, code: "1000");
        await _store.SaveAsync(bookId, firstChart);

        // A second, independently-built chart unaware of the first's committed
        // "1000" - simulates two processes that both loaded before either saved.
        var secondChart = ChartOfAccounts.Empty();
        secondChart.AddRoot(Guid.NewGuid(), "Savings", AccountType.Asset, code: "1000");

        var act = async () => await _store.SaveAsync(bookId, secondChart);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}