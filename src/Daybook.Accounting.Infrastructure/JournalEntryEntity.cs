using Daybook.Accounting.Core;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// The EF Core row shape for a <see cref="JournalEntry"/>. Deliberately a
/// separate, simple, mutable type rather than mapping <see cref="JournalEntry"/>
/// itself — see <see cref="BookEntity"/>'s remarks for why.
/// <see cref="JournalMapper"/> translates explicitly, both directions.
/// </summary>
internal sealed class JournalEntryEntity
{
    public Guid Id { get; set; }

    public Guid BookId { get; set; }

    public DateOnly EntryDate { get; set; }

    public string Description { get; set; } = "";

    public JournalEntryStatus Status { get; set; }

    public int? SequenceNumber { get; set; }

    public DateTimeOffset? PostedAtUtc { get; set; }

    public Guid? PostedByUserId { get; set; }

    public Guid? ReversesEntryId { get; set; }
}