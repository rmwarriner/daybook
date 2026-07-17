using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application.Tests;

/// <summary>
/// Milestone 8 — <see cref="BalanceSheetHtmlPresenter"/> (spec §12):
/// renders an already-computed <see cref="BalanceSheet"/> as HTML. Content
/// shape mirrors <see cref="BalanceSheetMarkdownPresenter"/>; document-level
/// concerns are already covered by <c>TrialBalanceHtmlPresenterTests</c>.
/// </summary>
public class BalanceSheetHtmlPresenterTests
{
    private static readonly DateOnly EntryDate = new(2026, 7, 15);
    private static readonly DateTimeOffset PostedAtUtc = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostedByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static JournalLine ADebit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Debit, Money.Of(amount, Currency.Usd)).Value;

    private static JournalLine ACredit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Credit, Money.Of(amount, Currency.Usd)).Value;

    private static void Post(Journal journal, ChartOfAccounts chart, params JournalLine[] lines)
    {
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Test entry", lines);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId).IsSuccess.Should().BeTrue();
    }

    private static (ChartOfAccounts Chart, Account Checking, Account Equity, Account Loan, Account Salary, Account Rent)
        AFullChart()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var equity = chart.AddRoot(Guid.NewGuid(), "Owner's Equity", AccountType.Equity).Value;
        var loan = chart.AddRoot(Guid.NewGuid(), "Loan Payable", AccountType.Liability).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var rent = chart.AddRoot(Guid.NewGuid(), "Rent", AccountType.Expense).Value;
        return (chart, checking, equity, loan, salary, rent);
    }

    [Fact]
    public void Accepts_a_custom_title()
    {
        var balanceSheet = BalanceSheet.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var html = BalanceSheetHtmlPresenter.Render(
            balanceSheet, ChartOfAccounts.Empty(), title: "Household — Balance Sheet");

        html.Should().Contain("<h1>Household — Balance Sheet</h1>");
    }

    [Fact]
    public void Renders_a_full_balance_sheet_with_sections_subtotals_and_the_final_identity()
    {
        var (chart, checking, equity, loan, salary, rent) = AFullChart();
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 1000m), ACredit(equity.Id, 1000m));
        Post(journal, chart, ADebit(checking.Id, 500m), ACredit(loan.Id, 500m));
        Post(journal, chart, ADebit(checking.Id, 300m), ACredit(salary.Id, 300m));
        Post(journal, chart, ADebit(rent.Id, 200m), ACredit(checking.Id, 200m));
        var balanceSheet = BalanceSheet.Compute(chart, journal);

        var html = BalanceSheetHtmlPresenter.Render(balanceSheet, chart);

        html.Should().Contain("<h2>Assets</h2>");
        html.Should().Contain("<td>Checking</td><td class=\"amount\">1,600.00</td>");
        html.Should().Contain("<p class=\"total\">Total Assets: 1,600.00</p>");

        html.Should().Contain("<h2>Liabilities</h2>");
        html.Should().Contain("<td>Loan Payable</td><td class=\"amount\">500.00</td>");
        html.Should().Contain("<p class=\"total\">Total Liabilities: 500.00</p>");

        html.Should().Contain("<h2>Equity</h2>");
        html.Should().Contain("<td>Owner&#39;s Equity</td><td class=\"amount\">1,000.00</td>");
        html.Should().Contain("<td>Net Income</td><td class=\"amount\">100.00</td>");
        html.Should().Contain("<p class=\"total\">Total Equity: 1,100.00</p>");

        html.Should().Contain("<p class=\"total\">Total Liabilities + Equity: 1,600.00</p>");
    }

    [Fact]
    public void The_net_income_row_is_inside_the_equity_table_not_a_separate_table()
    {
        var (chart, checking, equity, _, salary, _) = AFullChart();
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 1000m), ACredit(equity.Id, 1000m));
        Post(journal, chart, ADebit(checking.Id, 300m), ACredit(salary.Id, 300m));
        var balanceSheet = BalanceSheet.Compute(chart, journal);

        var html = BalanceSheetHtmlPresenter.Render(balanceSheet, chart);

        var equitySection = html[html.IndexOf("<h2>Equity</h2>", StringComparison.Ordinal)..];
        var tableEnd = equitySection.IndexOf("</table>", StringComparison.Ordinal);
        equitySection[..tableEnd].Should().Contain("Net Income");
    }

    [Fact]
    public void Orders_accounts_alphabetically_within_a_section()
    {
        var chart = ChartOfAccounts.Empty();
        chart.AddRoot(Guid.NewGuid(), "Zebra Savings", AccountType.Asset);
        chart.AddRoot(Guid.NewGuid(), "Apple Checking", AccountType.Asset);
        var balanceSheet = BalanceSheet.Compute(chart, Journal.Empty(Currency.Usd));

        var html = BalanceSheetHtmlPresenter.Render(balanceSheet, chart);

        html.IndexOf(">Apple Checking<", StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf(">Zebra Savings<", StringComparison.Ordinal));
    }

    [Fact]
    public void Renders_an_empty_section_with_a_zero_subtotal()
    {
        var chart = ChartOfAccounts.Empty();
        chart.AddRoot(Guid.NewGuid(), "Owner's Equity", AccountType.Equity);
        var balanceSheet = BalanceSheet.Compute(chart, Journal.Empty(Currency.Usd));

        var html = BalanceSheetHtmlPresenter.Render(balanceSheet, chart);

        html.Should().Contain("<h2>Liabilities</h2>");
        html.Should().Contain("<p class=\"total\">Total Liabilities: 0.00</p>");
    }

    [Fact]
    public void Escapes_html_special_characters_in_account_names()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Alice & Bob's <Fund>", AccountType.Asset).Value;
        var equity = chart.AddRoot(Guid.NewGuid(), "Owner's Equity", AccountType.Equity).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 100m), ACredit(equity.Id, 100m));
        var balanceSheet = BalanceSheet.Compute(chart, journal);

        var html = BalanceSheetHtmlPresenter.Render(balanceSheet, chart);

        html.Should().Contain("Alice &amp; Bob&#39;s &lt;Fund&gt;");
        html.Should().NotContain("<Fund>");
    }

    [Fact]
    public void Rejects_a_null_balance_sheet()
    {
        var act = () => BalanceSheetHtmlPresenter.Render(null!, ChartOfAccounts.Empty());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rejects_a_null_chart()
    {
        var balanceSheet = BalanceSheet.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var act = () => BalanceSheetHtmlPresenter.Render(balanceSheet, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}