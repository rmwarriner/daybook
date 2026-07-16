namespace Daybook.Accounting.Core;

/// <summary>
/// The derivation engine's shared foundation (spec §6.1): every account's
/// own and hierarchical-rollup balance, folded from <see cref="Journal"/>'s
/// posted entries only — drafts never count. Every other report (trial
/// balance, register, balance sheet) is built on top of this snapshot.
/// </summary>
public sealed class AccountBalances
{
    private readonly IReadOnlyDictionary<Guid, AccountBalance> _byAccountId;

    private AccountBalances(IReadOnlyDictionary<Guid, AccountBalance> byAccountId)
    {
        _byAccountId = byAccountId;
    }

    /// <exception cref="ArgumentNullException"><paramref name="chart"/> or <paramref name="journal"/> is null.</exception>
    public static AccountBalances Compute(ChartOfAccounts chart, Journal journal)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(journal);

        var ownBalances = chart.Accounts.ToDictionary(a => a.Id, _ => Money.Zero(Currency.Usd));
        foreach (var entry in journal.PostedEntries)
        {
            foreach (var line in entry.Lines)
            {
                var account = chart.Find(line.AccountId)!;
                var signed = line.Side == account.NormalBalance ? line.Amount : line.Amount.Negate();
                ownBalances[line.AccountId] += signed;
            }
        }

        var rolledUpBalances = new Dictionary<Guid, Money>();
        Money RollUp(Guid accountId)
        {
            if (rolledUpBalances.TryGetValue(accountId, out var cached))
            {
                return cached;
            }

            var total = chart.Children(accountId).Aggregate(ownBalances[accountId], (sum, child) => sum + RollUp(child.Id));
            rolledUpBalances[accountId] = total;
            return total;
        }

        var byAccountId = chart.Accounts.ToDictionary(
            a => a.Id,
            a => new AccountBalance(a.Id, ownBalances[a.Id], RollUp(a.Id)));

        return new AccountBalances(byAccountId);
    }

    public AccountBalance? Find(Guid accountId) => _byAccountId.GetValueOrDefault(accountId);

    public IReadOnlyCollection<AccountBalance> All => _byAccountId.Values.ToList();
}