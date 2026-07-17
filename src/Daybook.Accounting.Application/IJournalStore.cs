using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// The port for persisting and retrieving a book's <see cref="Journal"/>
/// (spec §7.1) — defined here, implemented by an EF Core adapter in
/// Infrastructure.
/// </summary>
public interface IJournalStore
{
    /// <summary>
    /// Every entry for the book, reconstructed as a journal via
    /// <see cref="Journal.Rehydrate"/>. A book with no entries yet loads as
    /// an empty journal. <paramref name="baseCurrency"/> is supplied by the
    /// caller (from the owning <see cref="Book"/>) rather than looked up
    /// here — the same convention <see cref="Journal.Empty"/> and
    /// <see cref="Journal.Rehydrate"/> already use.
    /// </summary>
    Task<Journal> LoadAsync(Guid bookId, Currency baseCurrency, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid bookId, Journal journal, CancellationToken cancellationToken = default);
}