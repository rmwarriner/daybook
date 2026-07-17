using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// The port for persisting and retrieving a book's <see cref="ChartOfAccounts"/>
/// (spec §7.1) — defined here, implemented by an EF Core adapter in Infrastructure.
/// </summary>
public interface IChartOfAccountsStore
{
    /// <summary>Every account for the book, reconstructed as a chart. A book with no accounts yet loads as an empty chart.</summary>
    Task<ChartOfAccounts> LoadAsync(Guid bookId, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid bookId, ChartOfAccounts chart, CancellationToken cancellationToken = default);
}