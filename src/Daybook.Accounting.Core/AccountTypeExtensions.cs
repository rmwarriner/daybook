namespace Daybook.Accounting.Core;

public static class AccountTypeExtensions
{
    /// <summary>The enforced normal balance for a root account type (spec §4.2 table).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a defined value.</exception>
    public static Side NormalBalance(this AccountType type) => type switch
    {
        AccountType.Asset => Side.Debit,
        AccountType.Liability => Side.Credit,
        AccountType.Equity => Side.Credit,
        AccountType.Income => Side.Credit,
        AccountType.Expense => Side.Debit,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown AccountType."),
    };
}