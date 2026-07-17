using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// Spec §7.5: "every EF migration ships with a test that applies it to a
/// fresh DB." One test suffices to cover the whole chain — <c>Migrate()</c>
/// applies every migration in order, so proving the fresh-DB case proves
/// every migration added so far still applies cleanly.
/// </summary>
public class MigrationsTests
{
    [Fact]
    public async Task Migrations_apply_cleanly_to_a_fresh_database()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"daybook-migrate-{Guid.NewGuid()}.db");
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
}