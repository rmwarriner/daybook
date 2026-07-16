namespace Daybook.Accounting.Core;

/// <summary>
/// The posting lifecycle of a <see cref="JournalEntry"/> (spec §4.7). A
/// posted entry's <c>ReversedByEntryId</c> may later be set, but its status
/// stays <see cref="Posted"/> — reversal creates a new entry, it does not
/// change this one's status.
/// </summary>
public enum JournalEntryStatus
{
    Draft,
    Posted,
}