namespace Daybook.Accounting.Core;

/// <summary>
/// Addresses one line within a posted <see cref="JournalEntry"/>, by
/// position rather than a stored identity — <see cref="JournalLine"/> has
/// none, and doesn't need one: a posted entry's <see cref="JournalEntry.Lines"/>
/// order is permanent (golden rule 2 plus the append-only guard), so
/// <see cref="LineIndex"/> into it is already a stable, zero-cost address.
/// </summary>
public readonly record struct LineLocation(Guid EntryId, int LineIndex);