namespace Daybook.Accounting.Core;

/// <summary>
/// A faithful record of a completed post or reversal (spec §9) — the
/// operational complement to the immutable journal, never redacted, only
/// ever appended. Unlike <see cref="Tag"/>/<see cref="Reference"/>/
/// <see cref="Reconciliation"/>, this carries no validation: it is only
/// ever constructed from a <see cref="Journal.Post"/>/<see cref="Journal.Reverse"/>
/// call that has already succeeded, so there is nothing left to check —
/// same reasoning as <see cref="JournalEntrySnapshot"/>, also an
/// unvalidated positional record of something that already happened.
/// </summary>
/// <remarks>
/// A reversal produces exactly this same shape — from the audit log's
/// perspective it is just another entry transitioning
/// <see cref="JournalEntryStatus.Draft"/> to <see cref="JournalEntryStatus.Posted"/>
/// at a new <see cref="SequenceNumber"/>. That it reverses something is
/// already recoverable from that entry's own persisted
/// <see cref="JournalEntry.ReversesEntryId"/>, so this type doesn't
/// duplicate it.
/// </remarks>
public sealed record AuditLogEntry(
    Guid Id,
    Guid EntryId,
    int SequenceNumber,
    Guid ActingUserId,
    DateTimeOffset TimestampUtc,
    JournalEntryStatus BeforeStatus,
    JournalEntryStatus AfterStatus,
    Guid CorrelationId);