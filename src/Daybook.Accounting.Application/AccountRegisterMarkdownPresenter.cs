using System.Globalization;

using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// Renders an already-computed <see cref="AccountRegister"/> as a Markdown
/// table (spec §12) — "the account register a household user reads day to
/// day." Every line carries its own account column (via display path), so
/// a whole-subtree register (<c>includeDescendants: true</c>) stays
/// disambiguated even though its lines span several accounts.
/// </summary>
public static class AccountRegisterMarkdownPresenter
{
    /// <exception cref="ArgumentNullException"><paramref name="register"/> or <paramref name="chart"/> is null.</exception>
    public static string Render(AccountRegister register, ChartOfAccounts chart, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(chart);

        var heading = title ?? $"Account Register: {chart.DisplayPathOf(register.AccountId).Value}";

        var lines = new List<string>
        {
            $"# {heading}",
            string.Empty,
            "| Date | Account | Description | Debit | Credit | Balance |",
            "| --- | --- | --- | ---: | ---: | ---: |",
        };

        foreach (var line in register.Lines)
        {
            var debit = line.Side == Side.Debit ? FormatAmount(line.Amount) : string.Empty;
            var credit = line.Side == Side.Credit ? FormatAmount(line.Amount) : string.Empty;
            var accountPath = chart.DisplayPathOf(line.AccountId).Value;
            var date = line.EntryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            lines.Add($"| {date} | {accountPath} | {line.Description} | {debit} | {credit} | {FormatAmount(line.RunningBalance)} |");
        }

        return string.Join('\n', lines);
    }

    private static string FormatAmount(Money amount) => amount.Amount.ToString("N2", CultureInfo.InvariantCulture);
}