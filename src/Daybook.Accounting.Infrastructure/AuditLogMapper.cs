using Daybook.Accounting.Core;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// Explicit translation between an <see cref="AuditLogEntry"/> and its
/// <see cref="AuditLogEntryEntity"/> row.
/// </summary>
internal static class AuditLogMapper
{
    public static AuditLogEntryEntity ToEntity(AuditLogEntry entry) => new()
    {
        Id = entry.Id,
        EntryId = entry.EntryId,
        SequenceNumber = entry.SequenceNumber,
        ActingUserId = entry.ActingUserId,
        TimestampUtc = entry.TimestampUtc,
        BeforeStatus = entry.BeforeStatus,
        AfterStatus = entry.AfterStatus,
        CorrelationId = entry.CorrelationId,
    };

    public static AuditLogEntry ToDomain(AuditLogEntryEntity entity) => new(
        entity.Id,
        entity.EntryId,
        entity.SequenceNumber,
        entity.ActingUserId,
        entity.TimestampUtc,
        entity.BeforeStatus,
        entity.AfterStatus,
        entity.CorrelationId);
}