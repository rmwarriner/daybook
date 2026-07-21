using System.Security.Cryptography;
using System.Text;

using Daybook.Accounting.Api.Auth;
using Daybook.Accounting.Infrastructure;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

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

    /// <summary>
    /// Mints a token signed with this host's real signing key but already
    /// expired — for proving expiry specifically causes rejection, without
    /// waiting on the wall clock. Deliberately not <c>JwtTokenFactory</c>
    /// itself (internal to Api, no InternalsVisibleTo in this codebase by
    /// convention) — mirrors it directly instead.
    /// </summary>
    public string CreateExpiredToken(Guid userId)
    {
        var signingKeyBytes = Encoding.UTF8.GetBytes(File.ReadAllText(_signingKeyPath).Trim());
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = JwtSettings.Issuer,
            Audience = JwtSettings.Audience,
            Expires = DateTime.UtcNow.AddMinutes(-1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(signingKeyBytes), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { [DaybookClaimTypes.UserId] = userId.ToString() },
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
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