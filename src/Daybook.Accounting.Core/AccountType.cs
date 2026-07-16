namespace Daybook.Accounting.Core;

/// <summary>
/// The five root chart-of-accounts types (spec §4.2). Every account's
/// <see cref="AccountType"/> fixes its normal balance and, transitively
/// (via type inheritance), the type of every account beneath it.
/// </summary>
public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense,
}