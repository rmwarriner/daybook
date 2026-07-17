using System.Globalization;
using System.Text;

using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// Renders an already-computed <see cref="TrialBalance"/> as a full,
/// toner-friendly HTML document (spec §12). Content shape mirrors
/// <see cref="TrialBalanceMarkdownPresenter"/>; only the markup differs.
/// </summary>
public static class TrialBalanceHtmlPresenter
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

        var body = new StringBuilder();
        body.Append("<table>\n<thead>\n<tr><th>Account</th><th class=\"amount\">Debit</th><th class=\"amount\">Credit</th></tr>\n</thead>\n<tbody>\n");

        foreach (var (path, line) in rows)
        {
            var debit = line.NormalBalance == Side.Debit ? FormatAmount(line.RolledUpBalance) : string.Empty;
            var credit = line.NormalBalance == Side.Credit ? FormatAmount(line.RolledUpBalance) : string.Empty;
            body.Append(
                $"<tr><td>{HtmlReportDocument.Escape(path)}</td><td class=\"amount\">{debit}</td>" +
                $"<td class=\"amount\">{credit}</td></tr>\n");
        }

        body.Append("</tbody>\n<tfoot>\n");
        body.Append(
            $"<tr><td>Total</td><td class=\"amount\">{FormatAmount(trialBalance.TotalDebits)}</td>" +
            $"<td class=\"amount\">{FormatAmount(trialBalance.TotalCredits)}</td></tr>\n");
        body.Append("</tfoot>\n</table>");

        return HtmlReportDocument.Wrap(title, body.ToString());
    }

    private static string FormatAmount(Money amount) => amount.Amount.ToString("N2", CultureInfo.InvariantCulture);
}