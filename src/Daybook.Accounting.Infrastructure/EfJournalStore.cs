using Daybook.Accounting.Application;
using Daybook.Accounting.Core;

using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure;

/// <summary>EF Core implementation of <see cref="IJournalStore"/> (spec §7.1).</summary>
public sealed class EfJournalStore(DaybookDbContext context) : IJournalStore
{
    public async Task<Journal> LoadAsync(Guid bookId, Currency baseCurrency, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseCurrency);

        var entries = await context.JournalEntries.Where(e => e.BookId == bookId).ToListAsync(cancellationToken);
        var entryIds = entries.Select(e => e.Id).ToHashSet();
        var lines = await context.JournalLines.Where(l => entryIds.Contains(l.EntryId)).ToListAsync(cancellationToken);

        return JournalMapper.ToDomain(baseCurrency, entries, lines);
    }

    public async Task SaveAsync(Guid bookId, Journal journal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var updatedEntries = JournalMapper.ToEntryEntities(bookId, journal);
        var currentIds = updatedEntries.Select(e => e.Id).ToHashSet();

        var existingEntries = await context.JournalEntries
            .Where(e => e.BookId == bookId)
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        foreach (var stale in existingEntries.Values.Where(e => !currentIds.Contains(e.Id)))
        {
            context.JournalEntries.Remove(stale);
        }

        foreach (var updated in updatedEntries)
        {
            if (existingEntries.TryGetValue(updated.Id, out var existing))
            {
                context.Entry(existing).CurrentValues.SetValues(updated);
            }
            else
            {
                context.JournalEntries.Add(updated);
            }
        }

        var existingLines = await context.JournalLines
            .Where(l => currentIds.Contains(l.EntryId))
            .ToListAsync(cancellationToken);
        context.JournalLines.RemoveRange(existingLines);
        context.JournalLines.AddRange(JournalMapper.ToLineEntities(journal));

        await context.SaveChangesAsync(cancellationToken);
    }
}