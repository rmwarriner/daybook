using Daybook.Accounting.Core;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// The EF Core row shape for an <see cref="Account"/>. Deliberately a
/// separate, simple, mutable type rather than mapping <see cref="Account"/>
/// itself — see <see cref="BookEntity"/>'s remarks for why.
/// <see cref="ChartOfAccountsMapper"/> translates explicitly, both directions.
/// </summary>
internal sealed class AccountEntity
{
    public Guid Id { get; set; }

    public Guid BookId { get; set; }

    public string? Code { get; set; }

    public string Name { get; set; } = "";

    public AccountType Type { get; set; }

    public Guid? ParentAccountId { get; set; }

    public bool IsPlaceholder { get; set; }

    public bool IsActive { get; set; }
}