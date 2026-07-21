using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// <see cref="DaybookDbContext"/>'s Identity storage (spec §8): backs the
/// real <c>ICurrentUser</c> implementation with ASP.NET Core Identity user
/// records, added via <see cref="ApplicationUser"/> and the
/// <c>AddIdentity</c> migration. Deliberately no role tables (see
/// <see cref="ApplicationUser"/>'s remarks) — the spec's future per-book
/// Owner/Editor/Viewer model doesn't map onto Identity's global roles.
/// </summary>
public class DaybookDbContextIdentityTests
{
    [Fact]
    public async Task AddIdentity_migration_applies_cleanly_to_a_fresh_database()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"daybook-identity-migrate-{Guid.NewGuid()}.db");
        try
        {
            var options = new DbContextOptionsBuilder<DaybookDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            await using var context = new DaybookDbContext(options);

            var act = async () => await context.Database.MigrateAsync();

            await act.Should().NotThrowAsync();
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }

    [Fact]
    public async Task An_ApplicationUser_round_trips_through_EF()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"daybook-identity-roundtrip-{Guid.NewGuid()}.db");
        try
        {
            var userId = Guid.NewGuid();

            var options = new DbContextOptionsBuilder<DaybookDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using (var setupContext = new DaybookDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                setupContext.Set<ApplicationUser>().Add(new ApplicationUser
                {
                    Id = userId,
                    UserName = "household@example.test",
                    NormalizedUserName = "HOUSEHOLD@EXAMPLE.TEST",
                    Email = "household@example.test",
                    NormalizedEmail = "HOUSEHOLD@EXAMPLE.TEST",
                });
                await setupContext.SaveChangesAsync();
            }

            await using var freshContext = new DaybookDbContext(options);
            var reloaded = await freshContext.Set<ApplicationUser>().FindAsync(userId);

            reloaded.Should().NotBeNull();
            reloaded!.UserName.Should().Be("household@example.test");
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}