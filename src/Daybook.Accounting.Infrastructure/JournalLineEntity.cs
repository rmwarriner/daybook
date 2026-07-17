using Daybook.Accounting.Core;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// The EF Core row shape for a <see cref="JournalLine"/>. Has its own
/// surrogate <see cref="Id"/> since <see cref="JournalLine"/> is a value
/// object with no identity of its own; <see cref="LineNumber"/> preserves
/// the original line order (not otherwise recoverable from a set of rows).
/// </summary>
internal sealed class JournalLineEntity
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public int LineNumber { get; set; }

    public Guid AccountId { get; set; }

    public Side Side { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = "";

    public string? Memo { get; set; }
}