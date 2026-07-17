using Daybook.Accounting.Application;
using Daybook.Accounting.Core;

using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure;

/// <summary>EF Core implementation of <see cref="IChartOfAccountsStore"/> (spec §7.1).</summary>
public sealed class EfChartOfAccountsStore(DaybookDbContext context) : IChartOfAccountsStore
{
    public async Task<ChartOfAccounts> LoadAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        var entities = await context.Accounts.Where(a => a.BookId == bookId).ToListAsync(cancellationToken);
        return ChartOfAccountsMapper.ToDomain(entities);
    }

    public async Task SaveAsync(Guid bookId, ChartOfAccounts chart, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var existingById = await context.Accounts
            .Where(a => a.BookId == bookId)
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        foreach (var updated in ChartOfAccountsMapper.ToEntities(bookId, chart))
        {
            if (existingById.TryGetValue(updated.Id, out var existing))
            {
                context.Entry(existing).CurrentValues.SetValues(updated);
            }
            else
            {
                context.Accounts.Add(updated);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}