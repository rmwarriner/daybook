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

    /// <summary>
    /// Writes every draft and newly-posted entry back to storage. Never
    /// touches a row that's already <see cref="JournalEntryStatus.Posted"/>
    /// in the database — no UPDATE, no DELETE, not even a rewrite with
    /// identical values — regardless of what <paramref name="journal"/>
    /// claims for that id (spec §7.4's append-only enforcement). Under
    /// normal usage this can never come up, since Core exposes no way to
    /// construct a modified <c>Posted</c> entry; the guard exists for the
    /// bug/rogue-write case that normal usage can't reach.
    /// </summary>
    public async Task SaveAsync(Guid bookId, Journal journal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var updatedEntries = JournalMapper.ToEntryEntities(bookId, journal);
        var currentIds = updatedEntries.Select(e => e.Id).ToHashSet();

        var existingEntries = await context.JournalEntries
            .Where(e => e.BookId == bookId)
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var writableIds = updatedEntries
            .Where(e => !existingEntries.TryGetValue(e.Id, out var existing) || existing.Status != JournalEntryStatus.Posted)
            .Select(e => e.Id)
            .ToHashSet();

        foreach (var stale in existingEntries.Values
            .Where(e => !currentIds.Contains(e.Id) && e.Status != JournalEntryStatus.Posted))
        {
            context.JournalEntries.Remove(stale);
        }

        foreach (var updated in updatedEntries.Where(e => writableIds.Contains(e.Id)))
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
            .Where(l => writableIds.Contains(l.EntryId))
            .ToListAsync(cancellationToken);
        context.JournalLines.RemoveRange(existingLines);
        context.JournalLines.AddRange(JournalMapper.ToLineEntities(journal).Where(l => writableIds.Contains(l.EntryId)));

        await context.SaveChangesAsync(cancellationToken);
    }
}