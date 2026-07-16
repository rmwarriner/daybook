namespace Daybook.Accounting.Core;

/// <summary>
/// Per-account posted lines in <c>(EntryDate, SequenceNumber)</c> order
/// with a running balance (spec §6.3) — "the account register a household
/// user reads day to day."
/// </summary>
public sealed class AccountRegister
{
    public Guid AccountId { get; }

    public IReadOnlyList<RegisterLine> Lines { get; }

    private AccountRegister(Guid accountId, IReadOnlyList<RegisterLine> lines)
    {
        AccountId = accountId;
        Lines = lines;
    }

    /// <summary>
    /// Computes the register for <paramref name="accountId"/>. By default
    /// only its own lines are included; <paramref name="includeDescendants"/>
    /// merges the whole subtree in the same date order — valid because type
    /// inheritance (spec §4.2) guarantees every descendant shares the same
    /// normal balance as <paramref name="accountId"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="chart"/> or <paramref name="journal"/> is null.</exception>
    public static Result<AccountRegister> Compute(
        Guid accountId,
        ChartOfAccounts chart,
        Journal journal,
        bool includeDescendants = false)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(journal);

        var account = chart.Find(accountId);
        if (account is null)
        {
            return new Error(
                "account.not_found",
                ErrorCategory.Validation,
                $"No account with id '{accountId}' exists in this chart.",
                ["Check the account id, or create the account first."]);
        }

        var accountIds = new HashSet<Guid> { accountId };
        if (includeDescendants)
        {
            CollectDescendants(accountId, chart, accountIds);
        }

        var ordered = journal.PostedEntries
            .SelectMany(e => e.Lines.Where(l => accountIds.Contains(l.AccountId)).Select(l => (Entry: e, Line: l)))
            .OrderBy(x => x.Entry.EntryDate)
            .ThenBy(x => x.Entry.SequenceNumber);

        var running = Money.Zero(journal.BaseCurrency);
        var lines = new List<RegisterLine>();
        foreach (var (entry, line) in ordered)
        {
            var signed = line.Side == account.NormalBalance ? line.Amount : line.Amount.Negate();
            running += signed;
            lines.Add(new RegisterLine(
                entry.Id,
                entry.SequenceNumber!.Value,
                entry.EntryDate,
                entry.Description,
                line.AccountId,
                line.Side,
                line.Amount,
                running));
        }

        return new AccountRegister(accountId, lines);
    }

    private static void CollectDescendants(Guid accountId, ChartOfAccounts chart, HashSet<Guid> into)
    {
        foreach (var child in chart.Children(accountId))
        {
            into.Add(child.Id);
            CollectDescendants(child.Id, chart, into);
        }
    }
}