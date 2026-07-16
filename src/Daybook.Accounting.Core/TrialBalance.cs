namespace Daybook.Accounting.Core;

/// <summary>
/// The trial balance (spec §6.2): every account's rolled-up balance, plus
/// the book-wide total-debits-equals-total-credits check.
/// </summary>
/// <remarks>
/// <see cref="TotalDebits"/>/<see cref="TotalCredits"/> are folded directly
/// from every posted line in the journal, not from summed rolled-up
/// balances — summing rolled-up balances across all accounts would
/// double-count a parent's total against its own descendants'.
/// </remarks>
public sealed class TrialBalance
{
    public IReadOnlyList<TrialBalanceLine> Lines { get; }

    public Money TotalDebits { get; }

    public Money TotalCredits { get; }

    private TrialBalance(IReadOnlyList<TrialBalanceLine> lines, Money totalDebits, Money totalCredits)
    {
        Lines = lines;
        TotalDebits = totalDebits;
        TotalCredits = totalCredits;
    }

    /// <exception cref="ArgumentNullException"><paramref name="chart"/> or <paramref name="journal"/> is null.</exception>
    /// <exception cref="LedgerIntegrityException">Total debits do not equal total credits — should be impossible given spec §5.</exception>
    public static TrialBalance Compute(ChartOfAccounts chart, Journal journal)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(journal);

        var balances = AccountBalances.Compute(chart, journal);
        var lines = chart.Accounts
            .Select(a =>
            {
                var balance = balances.Find(a.Id)!;
                return new TrialBalanceLine(a.Id, a.NormalBalance, balance.OwnBalance, balance.RolledUpBalance);
            })
            .ToList();

        var totalDebits = Money.Zero(Currency.Usd);
        var totalCredits = Money.Zero(Currency.Usd);
        foreach (var line in journal.PostedEntries.SelectMany(e => e.Lines))
        {
            if (line.Side == Side.Debit)
            {
                totalDebits += line.Amount;
            }
            else
            {
                totalCredits += line.Amount;
            }
        }

        if (totalDebits != totalCredits)
        {
            throw new LedgerIntegrityException(
                $"Trial balance does not balance: total debits {totalDebits} vs total credits {totalCredits}. " +
                "This should be impossible given spec §5; if it happens, the engine has a bug.",
                totalDebits,
                totalCredits);
        }

        return new TrialBalance(lines, totalDebits, totalCredits);
    }
}