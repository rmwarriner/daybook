using Daybook.Accounting.Application;
using Daybook.Accounting.Core;

using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// EF Core implementation of <see cref="IAuditLog"/> (spec §9). Always
/// inserts, never updates — a genuine duplicate <see cref="AuditLogEntry.Id"/>
/// is rejected by the table's own primary key on <c>SaveChangesAsync</c>,
/// so no extra append-only guard code is needed here.
/// </summary>
public sealed class EfAuditLog(DaybookDbContext context) : IAuditLog
{
    public async Task AppendAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.AuditLogEntries.Add(AuditLogMapper.ToEntity(entry));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetForEntryAsync(
        Guid entryId, CancellationToken cancellationToken = default)
    {
        var entities = await context.AuditLogEntries
            .Where(e => e.EntryId == entryId)
            .ToListAsync(cancellationToken);

        return entities.Select(AuditLogMapper.ToDomain).ToList();
    }
}