namespace Daybook.Accounting.Core;

/// <summary>
/// The balance sheet (spec §6.4): Assets = Liabilities + Equity, where
/// Equity is equity-account balances plus <see cref="NetIncome"/>.
/// </summary>
/// <remarks>
/// <see cref="NetIncome"/> is life-to-date (Σ Income − Σ Expense), not
/// scoped to a fiscal period: v1 has no period-close/closing-entries
/// mechanism (deliberately out of scope), so income/expense accounts are
/// never zeroed into retained earnings, and "life-to-date" is the only
/// coherent v1 reading of "current-period earnings."
///
/// Only root accounts (no <c>ParentAccountId</c>) appear in
/// <see cref="Assets"/>/<see cref="Liabilities"/>/<see cref="Equity"/>,
/// each with its rolled-up balance — listing every account at every level
/// would double-count a parent's total against its own descendants'.
/// </remarks>
public sealed class BalanceSheet
{
    public IReadOnlyList<AccountBalance> Assets { get; }

    public IReadOnlyList<AccountBalance> Liabilities { get; }

    public IReadOnlyList<AccountBalance> Equity { get; }

    public Money NetIncome { get; }

    public Money TotalAssets { get; }

    public Money TotalLiabilitiesAndEquity { get; }

    private BalanceSheet(
        IReadOnlyList<AccountBalance> assets,
        IReadOnlyList<AccountBalance> liabilities,
        IReadOnlyList<AccountBalance> equity,
        Money netIncome,
        Money totalAssets,
        Money totalLiabilitiesAndEquity)
    {
        Assets = assets;
        Liabilities = liabilities;
        Equity = equity;
        NetIncome = netIncome;
        TotalAssets = totalAssets;
        TotalLiabilitiesAndEquity = totalLiabilitiesAndEquity;
    }

    /// <exception cref="ArgumentNullException"><paramref name="chart"/> or <paramref name="journal"/> is null.</exception>
    /// <exception cref="LedgerIntegrityException">Assets do not equal liabilities plus equity — should be impossible given spec §5.</exception>
    public static BalanceSheet Compute(ChartOfAccounts chart, Journal journal)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(journal);

        var balances = AccountBalances.Compute(chart, journal);

        IReadOnlyList<AccountBalance> RootBalancesOfType(AccountType type) => chart.Accounts
            .Where(a => a.Type == type && a.ParentAccountId is null)
            .Select(a => balances.Find(a.Id)!)
            .ToList();

        Money Total(IEnumerable<AccountBalance> accountBalances) =>
            accountBalances.Aggregate(Money.Zero(journal.BaseCurrency), (sum, b) => sum + b.RolledUpBalance);

        var assets = RootBalancesOfType(AccountType.Asset);
        var liabilities = RootBalancesOfType(AccountType.Liability);
        var equity = RootBalancesOfType(AccountType.Equity);
        var income = RootBalancesOfType(AccountType.Income);
        var expense = RootBalancesOfType(AccountType.Expense);

        var netIncome = Total(income) - Total(expense);
        var totalAssets = Total(assets);
        var totalLiabilitiesAndEquity = Total(liabilities) + Total(equity) + netIncome;

        if (totalAssets != totalLiabilitiesAndEquity)
        {
            throw new LedgerIntegrityException(
                $"Balance sheet does not balance: total assets {totalAssets} vs total liabilities+equity " +
                $"{totalLiabilitiesAndEquity}. This should be impossible given spec §5; if it happens, the engine has a bug.",
                totalAssets,
                totalLiabilitiesAndEquity);
        }

        return new BalanceSheet(assets, liabilities, equity, netIncome, totalAssets, totalLiabilitiesAndEquity);
    }
}