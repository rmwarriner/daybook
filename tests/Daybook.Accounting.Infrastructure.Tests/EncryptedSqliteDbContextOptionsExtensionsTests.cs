using Daybook.Accounting.Core;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// <see cref="EncryptedSqliteDbContextOptionsExtensions"/> — wires a real
/// SQLite3 Multiple Ciphers-encrypted connection into
/// <see cref="DaybookDbContext"/> (spec §8/§13.5), given an already-unwrapped
/// data-encryption-key. Deliberately takes the raw key as a parameter rather
/// than doing any passphrase/wrapped-key-file orchestration itself — that's
/// composition-root work, not built yet (see <see cref="DataKeyEnvelope"/>
/// for the piece that produces the raw key this class consumes).
/// </summary>
public sealed class EncryptedSqliteDbContextOptionsExtensionsTests : IDisposable
{
    private readonly string _dbPath;

    public EncryptedSqliteDbContextOptionsExtensionsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"daybook-encrypted-{Guid.NewGuid()}.db");
    }

    public void Dispose()
    {
        DeleteIfExists(_dbPath);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private DaybookDbContext AnEncryptedContext(byte[] key, string? dbPath = null) =>
        new(new DbContextOptionsBuilder<DaybookDbContext>().UseEncryptedSqlite(key, dbPath ?? _dbPath).Options);

    private DaybookDbContext APlainContext(string? dbPath = null) =>
        new(new DbContextOptionsBuilder<DaybookDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = dbPath ?? _dbPath, Pooling = false }.ToString())
            .Options);

    [Fact]
    public async Task UseEncryptedSqlite_options_construct_a_context_that_migrates_cleanly()
    {
        var key = DataKeyEnvelope.GenerateDataKey();
        await using var context = AnEncryptedContext(key);

        var act = async () => await context.Database.MigrateAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Round_trips_through_two_fresh_contexts_with_the_same_key()
    {
        var key = DataKeyEnvelope.GenerateDataKey();
        var bookId = Guid.NewGuid();
        Guid checkingId, salaryId;

        await using (var setupContext = AnEncryptedContext(key))
        {
            await setupContext.Database.MigrateAsync();
            var book = Book.Create(bookId, "Household", Basis.Cash, Currency.Usd, MonthDay.Create(1, 1).Value).Value;
            await new EfBookStore(setupContext).SaveAsync(book);

            var chart = ChartOfAccounts.Empty();
            var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
            var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
            await new EfChartOfAccountsStore(setupContext).SaveAsync(bookId, chart);
            (checkingId, salaryId) = (checking.Id, salary.Id);

            var journal = Journal.Empty(Currency.Usd);
            journal.CreateDraft(
                Guid.NewGuid(), new DateOnly(2026, 7, 19), "Paycheck",
                [JournalLine.Create(checkingId, Side.Debit, Money.Of(100m, Currency.Usd)).Value,
                 JournalLine.Create(salaryId, Side.Credit, Money.Of(100m, Currency.Usd)).Value]);
            journal.Post(journal.Drafts.Single().Id, chart, DateTimeOffset.UtcNow, Guid.NewGuid());
            await new EfJournalStore(setupContext).SaveAsync(bookId, journal);
        }

        await using var freshContext = AnEncryptedContext(key);
        var reloadedJournal = await new EfJournalStore(freshContext).LoadAsync(bookId, Currency.Usd);

        reloadedJournal.PostedEntries.Should().ContainSingle()
            .Which.Description.Should().Be("Paycheck");
    }

    [Fact]
    public async Task The_raw_database_file_does_not_contain_a_known_plaintext_value()
    {
        const string canary = "TotallyDistinctivePlaintextCanaryValue";
        var key = DataKeyEnvelope.GenerateDataKey();
        var plainDbPath = Path.Combine(Path.GetTempPath(), $"daybook-plain-{Guid.NewGuid()}.db");

        try
        {
            await WriteCanaryEntry(AnEncryptedContext(key), canary);
            await WriteCanaryEntry(APlainContext(plainDbPath), canary);

            var encryptedBytes = await File.ReadAllBytesAsync(_dbPath);
            var plainBytes = await File.ReadAllBytesAsync(plainDbPath);
            var needle = System.Text.Encoding.UTF8.GetBytes(canary);

            // Negative control: proves the byte-search technique itself would
            // actually catch the canary if encryption weren't engaged - without
            // this, the encrypted-side assertion could pass vacuously.
            plainBytes.AsSpan().IndexOf(needle).Should().BeGreaterThanOrEqualTo(0);
            encryptedBytes.AsSpan().IndexOf(needle).Should().Be(-1);

            // This project doesn't enable WAL mode anywhere, so no -wal/-shm
            // sidecar should outlive Dispose() - check anyway so this test
            // doesn't go silently stale if that ever changes.
            foreach (var sidecar in Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileName(_dbPath)}-*"))
            {
                var sidecarBytes = await File.ReadAllBytesAsync(sidecar);
                sidecarBytes.AsSpan().IndexOf(needle).Should().Be(-1);
            }
        }
        finally
        {
            DeleteIfExists(plainDbPath);
        }
    }

    private static async Task WriteCanaryEntry(DaybookDbContext context, string canary)
    {
        await using (context)
        {
            await context.Database.MigrateAsync();
            var bookId = Guid.NewGuid();
            var book = Book.Create(bookId, "Household", Basis.Cash, Currency.Usd, MonthDay.Create(1, 1).Value).Value;
            await new EfBookStore(context).SaveAsync(book);

            var chart = ChartOfAccounts.Empty();
            var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
            var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
            await new EfChartOfAccountsStore(context).SaveAsync(bookId, chart);

            var journal = Journal.Empty(Currency.Usd);
            journal.CreateDraft(
                Guid.NewGuid(), new DateOnly(2026, 7, 19), canary,
                [JournalLine.Create(checking.Id, Side.Debit, Money.Of(1m, Currency.Usd)).Value,
                 JournalLine.Create(salary.Id, Side.Credit, Money.Of(1m, Currency.Usd)).Value]);
            journal.Post(journal.Drafts.Single().Id, chart, DateTimeOffset.UtcNow, Guid.NewGuid());
            await new EfJournalStore(context).SaveAsync(bookId, journal);
        }
    }

    [Fact]
    public async Task The_configured_cipher_is_actually_active()
    {
        var key = DataKeyEnvelope.GenerateDataKey();
        await using var context = AnEncryptedContext(key);
        await context.Database.MigrateAsync();
        var connection = (SqliteConnection)context.Database.GetDbConnection();

        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA cipher;";
        var cipher = (string?)await command.ExecuteScalarAsync();

        cipher.Should().Be(EncryptedSqliteDbContextOptionsExtensions.DefaultCipher);
    }

    [Fact]
    public void An_unrecognized_cipher_name_is_rejected()
    {
        var key = DataKeyEnvelope.GenerateDataKey();

        var act = () => new DbContextOptionsBuilder<DaybookDbContext>()
            .UseEncryptedSqlite(key, _dbPath, cipher: "'; DROP TABLE x; --");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Opening_with_the_wrong_key_throws_and_the_correct_key_still_works_afterward()
    {
        var keyA = DataKeyEnvelope.GenerateDataKey();
        var keyB = DataKeyEnvelope.GenerateDataKey();

        await using (var setupContext = AnEncryptedContext(keyA))
        {
            await setupContext.Database.MigrateAsync();
        }

        await using (var wrongKeyContext = AnEncryptedContext(keyB))
        {
            var act = async () => await wrongKeyContext.Database.ExecuteSqlRawAsync("SELECT count(*) FROM sqlite_master;");
            await act.Should().ThrowAsync<SqliteException>();
        }

        // The correct key still works afterward - proves a wrong-keyed open
        // didn't leave a stale pooled connection behind for the next open.
        await using var correctKeyAgainContext = AnEncryptedContext(keyA);
        var act2 = async () => await correctKeyAgainContext.Database.ExecuteSqlRawAsync("SELECT count(*) FROM sqlite_master;");
        await act2.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Opening_an_encrypted_file_through_a_plain_unkeyed_connection_throws()
    {
        var key = DataKeyEnvelope.GenerateDataKey();
        await using (var setupContext = AnEncryptedContext(key))
        {
            await setupContext.Database.MigrateAsync();
        }

        await using var plainContext = APlainContext();
        var act = async () => await plainContext.Database.ExecuteSqlRawAsync("SELECT count(*) FROM sqlite_master;");

        await act.Should().ThrowAsync<SqliteException>();
    }
}