using System.Globalization;

using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// Renders an already-computed <see cref="TrialBalance"/> as a Markdown
/// table (spec §12). Pure presentation over data Core already computed and
/// validated — no accounting logic lives here.
/// </summary>
public static class TrialBalanceMarkdownPresenter
{
    /// <exception cref="ArgumentNullException"><paramref name="trialBalance"/>, <paramref name="chart"/>, or <paramref name="title"/> is null.</exception>
    public static string Render(TrialBalance trialBalance, ChartOfAccounts chart, string title = "Trial Balance")
    {
        ArgumentNullException.ThrowIfNull(trialBalance);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(title);

        var rows = trialBalance.Lines
            .Select(line => (Path: chart.DisplayPathOf(line.AccountId).Value, Line: line))
            .OrderBy(r => r.Path, StringComparer.Ordinal);

        var lines = new List<string>
        {
            $"# {title}",
            string.Empty,
            "| Account | Debit | Credit |",
            "| --- | ---: | ---: |",
        };

        foreach (var (path, line) in rows)
        {
            var debit = line.NormalBalance == Side.Debit ? FormatAmount(line.RolledUpBalance) : string.Empty;
            var credit = line.NormalBalance == Side.Credit ? FormatAmount(line.RolledUpBalance) : string.Empty;
            lines.Add($"| {path} | {debit} | {credit} |");
        }

        lines.Add($"| **Total** | **{FormatAmount(trialBalance.TotalDebits)}** | **{FormatAmount(trialBalance.TotalCredits)}** |");

        return string.Join('\n', lines);
    }

    private static string FormatAmount(Money amount) => amount.Amount.ToString("N2", CultureInfo.InvariantCulture);
}