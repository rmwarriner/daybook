using System.Data.Common;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// Keys every physical connection SQLite3 Multiple Ciphers opens (spec
/// §13.5). Runs raw PRAGMAs via <see cref="ConnectionOpened"/> rather than
/// <see cref="SqliteConnectionStringBuilder.Password"/> — Microsoft.Data.Sqlite
/// funnels <c>Password</c> through SQLite's <c>quote()</c> on a TEXT-bound
/// parameter, which re-quotes a raw-hex key as a passphrase instead of
/// preserving it as key bytes. <c>PRAGMA hexkey</c> is always interpreted as
/// hex regardless of quoting, sidestepping that ambiguity entirely.
/// </summary>
internal sealed class SqliteEncryptionKeyInterceptor(byte[] dataKey, string cipher) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyKey((SqliteConnection)connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await ApplyKeyAsync((SqliteConnection)connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private void ApplyKey(SqliteConnection connection)
    {
        Execute(connection, $"PRAGMA cipher = '{cipher}';");
        Execute(connection, $"PRAGMA hexkey = '{Convert.ToHexStringLower(dataKey)}';");
        // hexkey/cipher "return ok" even for a wrong key - the key isn't
        // actually used until the next real read. Force one now, so a wrong
        // key throws immediately rather than on whatever query the caller
        // happens to run first.
        Execute(connection, "SELECT count(*) FROM sqlite_master;");
    }

    private async Task ApplyKeyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, $"PRAGMA cipher = '{cipher}';", cancellationToken);
        await ExecuteAsync(connection, $"PRAGMA hexkey = '{Convert.ToHexStringLower(dataKey)}';", cancellationToken);
        await ExecuteAsync(connection, "SELECT count(*) FROM sqlite_master;", cancellationToken);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}