using Daybook.Accounting.Core;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// The EF Core row shape for a <see cref="Book"/>. Deliberately a separate,
/// simple, mutable type rather than mapping <see cref="Book"/> itself —
/// Core stays completely unaware persistence exists, not even indirectly
/// via a constructor shape EF happens to like. <see cref="BookMapper"/>
/// translates explicitly, both directions.
/// </summary>
internal sealed class BookEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public Basis Basis { get; set; }

    public string BaseCurrency { get; set; } = "";

    public int FiscalYearStartMonth { get; set; }

    public int FiscalYearStartDay { get; set; }

    public BookStatus Status { get; set; }
}