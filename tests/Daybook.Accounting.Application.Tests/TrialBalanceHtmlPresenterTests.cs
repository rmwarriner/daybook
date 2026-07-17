using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application.Tests;

/// <summary>
/// Milestone 8 — <see cref="TrialBalanceHtmlPresenter"/> and, indirectly,
/// the shared <c>HtmlReportDocument</c> wrapper it's built on (spec §12):
/// renders an already-computed <see cref="TrialBalance"/> as a full,
/// toner-friendly HTML document. The wrapper is internal and has no public
/// surface of its own, so its document-level concerns (doctype, CSS,
/// title escaping) are covered here rather than in a dedicated test file —
/// the other two HTML presenters reuse it untested-directly, trusting this
/// coverage, the same way Journal.ValidateAndBalance is only tested via
/// Post and trusted by Reverse.
/// </summary>
public class TrialBalanceHtmlPresenterTests
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

    // ---- Document-level concerns (HtmlReportDocument, via this presenter) ----

    [Fact]
    public void Renders_a_valid_html_document()
    {
        var trialBalance = TrialBalance.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var html = TrialBalanceHtmlPresenter.Render(trialBalance, ChartOfAccounts.Empty());

        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().Contain("<html lang=\"en\">");
        html.Should().Contain("<meta charset=\"utf-8\">");
        html.Should().Contain("</html>");
    }

    [Fact]
    public void Includes_the_default_title_in_head_and_body()
    {
        var trialBalance = TrialBalance.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var html = TrialBalanceHtmlPresenter.Render(trialBalance, ChartOfAccounts.Empty());

        html.Should().Contain("<title>Trial Balance</title>");
        html.Should().Contain("<h1>Trial Balance</h1>");
    }

    [Fact]
    public void Accepts_a_custom_title()
    {
        var trialBalance = TrialBalance.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var html = TrialBalanceHtmlPresenter.Render(
            trialBalance, ChartOfAccounts.Empty(), title: "Household — Trial Balance");

        html.Should().Contain("<title>Household — Trial Balance</title>");
        html.Should().Contain("<h1>Household — Trial Balance</h1>");
    }

    [Fact]
    public void Includes_toner_friendly_print_rules_in_the_embedded_css()
    {
        var trialBalance = TrialBalance.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var html = TrialBalanceHtmlPresenter.Render(trialBalance, ChartOfAccounts.Empty());

        html.Should().Contain("<style>");
        html.Should().Contain("@page");
        html.Should().Contain("size: letter");
        html.Should().Contain("color: #000");
        html.Should().Contain("background: #fff");
        html.Should().Contain("font-variant-numeric: tabular-nums");
        html.Should().NotContain("background-color");
    }

    [Fact]
    public void Escapes_html_special_characters_in_the_title()
    {
        var trialBalance = TrialBalance.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var html = TrialBalanceHtmlPresenter.Render(
            trialBalance, ChartOfAccounts.Empty(), title: "Alice & Bob's <Fund>");

        html.Should().Contain("Alice &amp; Bob&#39;s &lt;Fund&gt;");
        html.Should().NotContain("<Fund>");
    }

    // ---- Trial-balance-specific body content -------------------------------

    [Fact]
    public void Includes_a_table_header_row()
    {
        var trialBalance = TrialBalance.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var html = TrialBalanceHtmlPresenter.Render(trialBalance, ChartOfAccounts.Empty());

        html.Should().Contain("<th>Account</th>");
        html.Should().Contain("<th class=\"amount\">Debit</th>");
        html.Should().Contain("<th class=\"amount\">Credit</th>");
    }

    [Fact]
    public void Shows_each_accounts_rolled_up_balance_on_its_normal_side()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 500m), ACredit(salary.Id, 500m));
        var trialBalance = TrialBalance.Compute(chart, journal);

        var html = TrialBalanceHtmlPresenter.Render(trialBalance, chart);

        html.Should().Contain("<td>Checking</td><td class=\"amount\">500.00</td><td class=\"amount\"></td>");
        html.Should().Contain("<td>Salary</td><td class=\"amount\"></td><td class=\"amount\">500.00</td>");
    }

    [Fact]
    public void Shows_the_total_row_in_a_table_footer()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 500m), ACredit(salary.Id, 500m));
        var trialBalance = TrialBalance.Compute(chart, journal);

        var html = TrialBalanceHtmlPresenter.Render(trialBalance, chart);

        html.Should().Contain("<tfoot>");
        html.Should().Contain("<td>Total</td><td class=\"amount\">500.00</td><td class=\"amount\">500.00</td>");
    }

    [Fact]
    public void Uses_the_derived_display_path_for_nested_accounts()
    {
        var chart = ChartOfAccounts.Empty();
        var utilities = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense, isPlaceholder: true).Value;
        var electric = chart.AddChild(Guid.NewGuid(), utilities.Id, "Electric", AccountType.Expense).Value;
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(electric.Id, 50m), ACredit(checking.Id, 50m));
        var trialBalance = TrialBalance.Compute(chart, journal);

        var html = TrialBalanceHtmlPresenter.Render(trialBalance, chart);

        html.Should().Contain("Utilities:Electric");
    }

    [Fact]
    public void Orders_rows_alphabetically_by_display_path()
    {
        var chart = ChartOfAccounts.Empty();
        chart.AddRoot(Guid.NewGuid(), "Zebra", AccountType.Asset);
        chart.AddRoot(Guid.NewGuid(), "Apple", AccountType.Asset);
        var trialBalance = TrialBalance.Compute(chart, Journal.Empty(Currency.Usd));

        var html = TrialBalanceHtmlPresenter.Render(trialBalance, chart);

        html.IndexOf(">Apple<", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf(">Zebra<", StringComparison.Ordinal));
    }

    [Fact]
    public void Formats_amounts_with_thousands_separators_and_two_decimals()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var equity = chart.AddRoot(Guid.NewGuid(), "Owner's Equity", AccountType.Equity).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 12345.6m), ACredit(equity.Id, 12345.6m));
        var trialBalance = TrialBalance.Compute(chart, journal);

        var html = TrialBalanceHtmlPresenter.Render(trialBalance, chart);

        html.Should().Contain("12,345.60");
    }

    [Fact]
    public void Escapes_html_special_characters_in_account_names()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Alice & Bob's <Fund>", AccountType.Asset).Value;
        var equity = chart.AddRoot(Guid.NewGuid(), "Owner's Equity", AccountType.Equity).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 100m), ACredit(equity.Id, 100m));
        var trialBalance = TrialBalance.Compute(chart, journal);

        var html = TrialBalanceHtmlPresenter.Render(trialBalance, chart);

        html.Should().Contain("Alice &amp; Bob&#39;s &lt;Fund&gt;");
        html.Should().NotContain("<Fund>");
    }

    [Fact]
    public void Rejects_a_null_trial_balance()
    {
        var act = () => TrialBalanceHtmlPresenter.Render(null!, ChartOfAccounts.Empty());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rejects_a_null_chart()
    {
        var trialBalance = TrialBalance.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var act = () => TrialBalanceHtmlPresenter.Render(trialBalance, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}