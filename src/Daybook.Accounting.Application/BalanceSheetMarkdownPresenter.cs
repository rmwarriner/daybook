using System.Globalization;

using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// Renders an already-computed <see cref="BalanceSheet"/> as Markdown
/// (spec §12): Assets / Liabilities / Equity sections, each with a
/// subtotal, plus the final Assets = Liabilities + Equity summary.
/// </summary>
public static class BalanceSheetMarkdownPresenter
{
    /// <exception cref="ArgumentNullException"><paramref name="balanceSheet"/>, <paramref name="chart"/>, or <paramref name="title"/> is null.</exception>
    public static string Render(BalanceSheet balanceSheet, ChartOfAccounts chart, string title = "Balance Sheet")
    {
        ArgumentNullException.ThrowIfNull(balanceSheet);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(title);

        var lines = new List<string> { $"# {title}", string.Empty };

        lines.AddRange(Section("Assets", balanceSheet.Assets, chart));
        lines.Add($"**Total Assets: {FormatAmount(balanceSheet.TotalAssets)}**");
        lines.Add(string.Empty);

        lines.AddRange(Section("Liabilities", balanceSheet.Liabilities, chart));
        lines.Add($"**Total Liabilities: {FormatAmount(balanceSheet.TotalLiabilities)}**");
        lines.Add(string.Empty);

        lines.AddRange(Section("Equity", balanceSheet.Equity, chart));
        lines.Add($"| Net Income | {FormatAmount(balanceSheet.NetIncome)} |");
        lines.Add($"**Total Equity: {FormatAmount(balanceSheet.TotalEquity)}**");
        lines.Add(string.Empty);

        lines.Add($"**Total Liabilities + Equity: {FormatAmount(balanceSheet.TotalLiabilitiesAndEquity)}**");

        return string.Join('\n', lines);
    }

    private static List<string> Section(string heading, IReadOnlyList<AccountBalance> accounts, ChartOfAccounts chart)
    {
        var lines = new List<string>
        {
            $"## {heading}",
            string.Empty,
            "| Account | Amount |",
            "| --- | ---: |",
        };

        var rows = accounts
            .Select(a => (Path: chart.DisplayPathOf(a.AccountId).Value, Balance: a.RolledUpBalance))
            .OrderBy(r => r.Path, StringComparer.Ordinal);

        foreach (var (path, balance) in rows)
        {
            lines.Add($"| {path} | {FormatAmount(balance)} |");
        }

        return lines;
    }

    private static string FormatAmount(Money amount) => amount.Amount.ToString("N2", CultureInfo.InvariantCulture);
}