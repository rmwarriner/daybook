using Daybook.Accounting.Core;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// Explicit translation between <see cref="Book"/> and <see cref="BookEntity"/>.
/// </summary>
internal static class BookMapper
{
    public static BookEntity ToEntity(Book book) => new()
    {
        Id = book.Id,
        Name = book.Name,
        Basis = book.Basis,
        BaseCurrency = book.BaseCurrency.Code,
        FiscalYearStartMonth = book.FiscalYearStart.Month,
        FiscalYearStartDay = book.FiscalYearStart.Day,
        Status = book.Status,
    };

    /// <summary>
    /// Reconstructs a <see cref="Book"/> from a row that was originally
    /// written via <see cref="Book.Create"/>, so it's already valid.
    /// </summary>
    /// <exception cref="InvalidOperationException">The row fails domain validation — corrupted or tampered data.</exception>
    public static Book ToDomain(BookEntity entity)
    {
        var fiscalYearStart = MonthDay.Create(entity.FiscalYearStartMonth, entity.FiscalYearStartDay).Value;
        return Book.Create(
            entity.Id, entity.Name, entity.Basis, Currency.Of(entity.BaseCurrency), fiscalYearStart, entity.Status).Value;
    }
}