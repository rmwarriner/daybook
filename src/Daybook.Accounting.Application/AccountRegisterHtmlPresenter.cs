using System.Globalization;
using System.Text;

using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// Renders an already-computed <see cref="AccountRegister"/> as a full,
/// toner-friendly HTML document (spec §12). Content shape mirrors
/// <see cref="AccountRegisterMarkdownPresenter"/>; only the markup differs.
/// </summary>
public static class AccountRegisterHtmlPresenter
{
    /// <exception cref="ArgumentNullException"><paramref name="register"/> or <paramref name="chart"/> is null.</exception>
    public static string Render(AccountRegister register, ChartOfAccounts chart, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(chart);

        var heading = title ?? $"Account Register: {chart.DisplayPathOf(register.AccountId).Value}";

        var body = new StringBuilder();
        body.Append("<table>\n<thead>\n<tr><th>Date</th><th>Account</th><th>Description</th>" +
            "<th class=\"amount\">Debit</th><th class=\"amount\">Credit</th><th class=\"amount\">Balance</th></tr>\n" +
            "</thead>\n<tbody>\n");

        foreach (var line in register.Lines)
        {
            var debit = line.Side == Side.Debit ? FormatAmount(line.Amount) : string.Empty;
            var credit = line.Side == Side.Credit ? FormatAmount(line.Amount) : string.Empty;
            var accountPath = HtmlReportDocument.Escape(chart.DisplayPathOf(line.AccountId).Value);
            var description = HtmlReportDocument.Escape(line.Description);
            var date = line.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            body.Append(
                $"<tr><td>{date}</td><td>{accountPath}</td><td>{description}</td>" +
                $"<td class=\"amount\">{debit}</td><td class=\"amount\">{credit}</td>" +
                $"<td class=\"amount\">{FormatAmount(line.RunningBalance)}</td></tr>\n");
        }

        body.Append("</tbody>\n</table>");

        return HtmlReportDocument.Wrap(heading, body.ToString());
    }

    private static string FormatAmount(Money amount) => amount.Amount.ToString("N2", CultureInfo.InvariantCulture);
}