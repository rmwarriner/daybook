using System.Security.Cryptography;

using Daybook.Accounting.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Daybook.Accounting.Api.Tests;

/// <summary>
/// A fresh, isolated host per test — own temp DB file, own temp JWT
/// signing-key file — mirroring
/// <c>EncryptedSqliteDbContextOptionsExtensionsTests</c>'s fresh-temp-file-
/// per-test pattern. Deliberately not a shared <c>IClassFixture</c>: the
/// registration tests are inherently stateful ("first succeeds," "second is
/// rejected"), so sharing one instance across a class risks order-dependent
/// flakiness.
/// </summary>
public sealed class DaybookWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"daybook-api-{Guid.NewGuid()}.db");
    private readonly string _signingKeyPath = Path.Combine(Path.GetTempPath(), $"daybook-jwt-key-{Guid.NewGuid()}.txt");

    public DaybookWebApplicationFactory()
    {
        File.WriteAllText(_signingKeyPath, Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
    }

    public async Task MigrateAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DaybookDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Daybook:DatabasePath"] = _dbPath,
            ["Daybook:JwtSigningKeyFilePath"] = _signingKeyPath,
        }));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        if (File.Exists(_signingKeyPath))
        {
            File.Delete(_signingKeyPath);
        }
    }
}