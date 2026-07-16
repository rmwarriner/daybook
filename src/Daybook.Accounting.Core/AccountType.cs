namespace Daybook.Accounting.Core;

/// <summary>
/// One of the five root chart-of-accounts types (spec §4.2). Each has a fixed
/// normal balance — see <see cref="Account.NormalBalance"/>.
/// </summary>
public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense,
}