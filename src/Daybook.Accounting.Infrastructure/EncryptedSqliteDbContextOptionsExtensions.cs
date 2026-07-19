using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// Wires a <see cref="DbContextOptionsBuilder{DaybookDbContext}"/> to an
/// SQLite3 Multiple Ciphers-encrypted file (spec §8/§13.5), given an
/// already-unwrapped data-encryption-key. Pure adapter, like the Ef*Store
/// classes: takes exactly what it needs as parameters and does no key
/// custody orchestration of its own — deciding whether a
/// <see cref="WrappedDataKey"/> exists yet, generating one, or reading a
/// passphrase is composition-root work, not built here.
/// </summary>
public static class EncryptedSqliteDbContextOptionsExtensions
{
    /// <summary>SQLite3MC's current recommended scheme (ChaCha20-Poly1305).</summary>
    public const string DefaultCipher = "chacha20";

    private const int DataKeySizeBytes = 32;

    public static DbContextOptionsBuilder<DaybookDbContext> UseEncryptedSqlite(
        this DbContextOptionsBuilder<DaybookDbContext> optionsBuilder,
        byte[] dataKey,
        string databasePath,
        string cipher = DefaultCipher)
    {
        ArgumentNullException.ThrowIfNull(dataKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(cipher);

        if (dataKey.Length != DataKeySizeBytes)
        {
            throw new ArgumentException($"Data key must be {DataKeySizeBytes} bytes, was {dataKey.Length}.", nameof(dataKey));
        }

        if (!cipher.All(char.IsAsciiLetterOrDigit))
        {
            // Defense in depth: cipher is interpolated into raw SQL text by
            // SqliteEncryptionKeyInterceptor.
            throw new ArgumentException($"'{cipher}' is not a recognized SQLite3MC cipher name.", nameof(cipher));
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Every Open() must produce a genuinely fresh native handle so
            // the interceptor's per-open PRAGMAs never run against a pooled
            // handle that might carry a different key from an earlier open
            // - hexkey/cipher "return ok" even when wrong, so a stale pooled
            // key wouldn't necessarily fail loudly.
            Pooling = false,
        }.ToString();

        return optionsBuilder
            .UseSqlite(connectionString)
            .AddInterceptors(new SqliteEncryptionKeyInterceptor(dataKey, cipher));
    }
}