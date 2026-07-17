using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// Lets <c>dotnet ef</c> construct a <see cref="DaybookDbContext"/> at
/// design time (for <c>migrations add</c> etc.), since the real
/// constructor needs options that only exist once a host is wired up. Not
/// used at runtime by the application itself.
/// </summary>
internal sealed class DaybookDbContextFactory : IDesignTimeDbContextFactory<DaybookDbContext>
{
    public DaybookDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DaybookDbContext>()
            .UseSqlite("Data Source=daybook-design.db")
            .Options;
        return new DaybookDbContext(options);
    }
}