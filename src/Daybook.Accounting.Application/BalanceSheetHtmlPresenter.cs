using System.Globalization;
using System.Text;

using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// Renders an already-computed <see cref="BalanceSheet"/> as a full,
/// toner-friendly HTML document (spec §12). Content shape mirrors
/// <see cref="BalanceSheetMarkdownPresenter"/>; only the markup differs.
/// </summary>
public static class BalanceSheetHtmlPresenter
{
    /// <exception cref="ArgumentNullException"><paramref name="balanceSheet"/>, <paramref name="chart"/>, or <paramref name="title"/> is null.</exception>
    public static string Render(BalanceSheet balanceSheet, ChartOfAccounts chart, string title = "Balance Sheet")
    {
        ArgumentNullException.ThrowIfNull(balanceSheet);
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(title);

        var body = new StringBuilder();

        body.Append(Section("Assets", Rows(balanceSheet.Assets, chart)));
        body.Append($"<p class=\"total\">Total Assets: {FormatAmount(balanceSheet.TotalAssets)}</p>\n");

        body.Append(Section("Liabilities", Rows(balanceSheet.Liabilities, chart)));
        body.Append($"<p class=\"total\">Total Liabilities: {FormatAmount(balanceSheet.TotalLiabilities)}</p>\n");

        var equityRows = Rows(balanceSheet.Equity, chart) +
            $"<tr><td>Net Income</td><td class=\"amount\">{FormatAmount(balanceSheet.NetIncome)}</td></tr>\n";
        body.Append(Section("Equity", equityRows));
        body.Append($"<p class=\"total\">Total Equity: {FormatAmount(balanceSheet.TotalEquity)}</p>\n");

        body.Append($"<p class=\"total\">Total Liabilities + Equity: {FormatAmount(balanceSheet.TotalLiabilitiesAndEquity)}</p>");

        return HtmlReportDocument.Wrap(title, body.ToString());
    }

    private static string Rows(IReadOnlyList<AccountBalance> accounts, ChartOfAccounts chart)
    {
        var rows = accounts
            .Select(a => (Path: chart.DisplayPathOf(a.AccountId).Value, Balance: a.RolledUpBalance))
            .OrderBy(r => r.Path, StringComparer.Ordinal);

        var sb = new StringBuilder();
        foreach (var (path, balance) in rows)
        {
            sb.Append($"<tr><td>{HtmlReportDocument.Escape(path)}</td><td class=\"amount\">{FormatAmount(balance)}</td></tr>\n");
        }

        return sb.ToString();
    }

    private static string Section(string heading, string rowsHtml) =>
        $"<h2>{HtmlReportDocument.Escape(heading)}</h2>\n<table>\n<thead>\n" +
        "<tr><th>Account</th><th class=\"amount\">Amount</th></tr>\n</thead>\n<tbody>\n" +
        $"{rowsHtml}</tbody>\n</table>\n";

    private static string FormatAmount(Money amount) => amount.Amount.ToString("N2", CultureInfo.InvariantCulture);
}