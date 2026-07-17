using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// The port for persisting and retrieving a <see cref="Book"/> (spec §7.1)
/// — defined here, implemented by an EF Core adapter in Infrastructure.
/// </summary>
public interface IBookStore
{
    Task<Book?> FindAsync(Guid bookId, CancellationToken cancellationToken = default);

    Task SaveAsync(Book book, CancellationToken cancellationToken = default);
}