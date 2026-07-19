using Daybook.Accounting.Core;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// The EF Core row shape for an <see cref="AuditLogEntry"/>. Deliberately a
/// separate, simple, mutable type rather than mapping <see cref="AuditLogEntry"/>
/// itself — see <see cref="BookEntity"/>'s remarks for why.
/// <see cref="AuditLogMapper"/> translates explicitly, both directions.
/// </summary>
internal sealed class AuditLogEntryEntity
{
    public Guid Id { get; set; }

    public Guid EntryId { get; set; }

    public int SequenceNumber { get; set; }

    public Guid ActingUserId { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public JournalEntryStatus BeforeStatus { get; set; }

    public JournalEntryStatus AfterStatus { get; set; }

    public Guid CorrelationId { get; set; }
}