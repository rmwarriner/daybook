using Daybook.Accounting.Application;
using Daybook.Accounting.Core;

using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure;

/// <summary>EF Core implementation of <see cref="IBookStore"/> (spec §7.1).</summary>
public sealed class EfBookStore(DaybookDbContext context) : IBookStore
{
    public async Task<Book?> FindAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        var entity = await context.Books.FindAsync([bookId], cancellationToken);
        return entity is null ? null : BookMapper.ToDomain(entity);
    }

    public async Task SaveAsync(Book book, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);

        var updated = BookMapper.ToEntity(book);
        var existing = await context.Books.FindAsync([book.Id], cancellationToken);
        if (existing is null)
        {
            context.Books.Add(updated);
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(updated);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}