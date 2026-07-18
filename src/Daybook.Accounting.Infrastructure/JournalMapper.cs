using Daybook.Accounting.Core;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// Explicit translation between a <see cref="Journal"/> and its
/// <see cref="JournalEntryEntity"/>/<see cref="JournalLineEntity"/> rows.
/// </summary>
internal static class JournalMapper
{
    public static IReadOnlyList<JournalEntryEntity> ToEntryEntities(Guid bookId, Journal journal) =>
        AllEntries(journal).Select(e => new JournalEntryEntity
        {
            Id = e.Id,
            BookId = bookId,
            EntryDate = e.EntryDate,
            Description = e.Description,
            Status = e.Status,
            SequenceNumber = e.SequenceNumber,
            PostedAtUtc = e.PostedAtUtc,
            PostedByUserId = e.PostedByUserId,
            ReversesEntryId = e.ReversesEntryId,
            SchemaVersion = e.SchemaVersion,
        }).ToList();

    public static IReadOnlyList<JournalLineEntity> ToLineEntities(Journal journal) =>
        AllEntries(journal).SelectMany(e => e.Lines.Select((l, index) => new JournalLineEntity
        {
            Id = Guid.NewGuid(),
            EntryId = e.Id,
            LineNumber = index,
            AccountId = l.AccountId,
            Side = l.Side,
            Amount = l.Amount.Amount,
            Currency = l.Amount.Currency.Code,
            Memo = l.Memo,
        })).ToList();

    /// <summary>
    /// Reconstructs a journal from rows that were originally written via
    /// <see cref="ToEntryEntities"/>/<see cref="ToLineEntities"/>, so
    /// they're already known-valid — see <see cref="Journal.Rehydrate"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The rows are internally inconsistent — corrupted or tampered data.</exception>
    public static Journal ToDomain(
        Currency baseCurrency, IReadOnlyList<JournalEntryEntity> entryEntities, IReadOnlyList<JournalLineEntity> lineEntities)
    {
        var linesByEntryId = lineEntities.ToLookup(l => l.EntryId);

        var snapshots = entryEntities.Select(e => new JournalEntrySnapshot(
            e.Id,
            e.EntryDate,
            e.Description,
            linesByEntryId[e.Id]
                .OrderBy(l => l.LineNumber)
                .Select(l => JournalLine.Create(l.AccountId, l.Side, Money.Of(l.Amount, Currency.Of(l.Currency)), l.Memo).Value)
                .ToList(),
            e.Status,
            e.SequenceNumber,
            e.PostedAtUtc,
            e.PostedByUserId,
            e.ReversesEntryId,
            e.SchemaVersion));

        return Journal.Rehydrate(baseCurrency, snapshots);
    }

    private static IEnumerable<JournalEntry> AllEntries(Journal journal) => journal.Drafts.Concat(journal.PostedEntries);
}