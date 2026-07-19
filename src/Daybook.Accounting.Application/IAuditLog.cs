using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// The append-only audit trail port (spec §9) — who did what, when, for
/// every post and reversal. No update/delete method exists on this
/// interface at all; append-only is structural, not a convention to
/// remember.
/// </summary>
public interface IAuditLog
{
    Task AppendAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Every audit entry recorded for <paramref name="entryId"/>, or empty if none.</summary>
    Task<IReadOnlyList<AuditLogEntry>> GetForEntryAsync(Guid entryId, CancellationToken cancellationToken = default);
}