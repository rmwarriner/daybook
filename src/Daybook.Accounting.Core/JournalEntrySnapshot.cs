namespace Daybook.Accounting.Core;

/// <summary>
/// The full recorded state of one <see cref="JournalEntry"/>, as previously
/// posted or drafted — the shape <see cref="Journal.Rehydrate"/> needs to
/// reconstruct a <see cref="Journal"/> from storage. Never produced by
/// ordinary posting; only ever fed back in.
/// </summary>
public sealed record JournalEntrySnapshot(
    Guid Id,
    DateOnly EntryDate,
    string Description,
    IReadOnlyList<JournalLine> Lines,
    JournalEntryStatus Status,
    int? SequenceNumber,
    DateTimeOffset? PostedAtUtc,
    Guid? PostedByUserId,
    Guid? ReversesEntryId,
    int SchemaVersion = JournalEntry.CurrentSchemaVersion,
    IReadOnlyList<Reference>? References = null);