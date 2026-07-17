using Daybook.Accounting.Core;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// Explicit translation between a <see cref="ChartOfAccounts"/> and its
/// <see cref="AccountEntity"/> rows.
/// </summary>
internal static class ChartOfAccountsMapper
{
    public static IReadOnlyList<AccountEntity> ToEntities(Guid bookId, ChartOfAccounts chart) =>
        chart.Accounts.Select(a => new AccountEntity
        {
            Id = a.Id,
            BookId = bookId,
            Code = a.Code,
            Name = a.Name,
            Type = a.Type,
            ParentAccountId = a.ParentAccountId,
            IsPlaceholder = a.IsPlaceholder,
            IsActive = a.IsActive,
        }).ToList();

    /// <summary>
    /// Reconstructs a chart from rows that were originally written via
    /// <see cref="ChartOfAccounts"/>'s own Add methods, so they're already
    /// individually and collectively valid. Adds parent-before-child,
    /// since <see cref="ChartOfAccounts.AddChild"/> requires the parent to
    /// already be present in the chart being built.
    /// </summary>
    /// <exception cref="InvalidOperationException">A row references a parent that isn't among the rows — corrupted or tampered data.</exception>
    public static ChartOfAccounts ToDomain(IEnumerable<AccountEntity> entities)
    {
        var chart = ChartOfAccounts.Empty();
        var remaining = entities.ToList();

        var roots = remaining.Where(e => e.ParentAccountId is null).ToList();
        foreach (var root in roots)
        {
            _ = chart.AddRoot(root.Id, root.Name, root.Type, root.Code, root.IsPlaceholder, root.IsActive).Value;
        }

        remaining.RemoveAll(e => e.ParentAccountId is null);

        while (remaining.Count > 0)
        {
            var addable = remaining.Where(e => chart.Find(e.ParentAccountId!.Value) is not null).ToList();
            if (addable.Count == 0)
            {
                throw new InvalidOperationException(
                    "Persisted accounts reference a parent that doesn't exist in this chart — corrupted data.");
            }

            foreach (var entity in addable)
            {
                _ = chart.AddChild(
                    entity.Id, entity.ParentAccountId!.Value, entity.Name, entity.Type,
                    entity.Code, entity.IsPlaceholder, entity.IsActive).Value;
            }

            remaining.RemoveAll(addable.Contains);
        }

        return chart;
    }
}