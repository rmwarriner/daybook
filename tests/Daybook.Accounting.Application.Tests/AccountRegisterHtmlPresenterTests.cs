using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application.Tests;

/// <summary>
/// Milestone 8 — <see cref="AccountRegisterHtmlPresenter"/> (spec §12):
/// renders an already-computed <see cref="AccountRegister"/> as HTML.
/// Content shape mirrors <see cref="AccountRegisterMarkdownPresenter"/>;
/// document-level concerns (doctype, CSS, escaping) are already covered by
/// <c>TrialBalanceHtmlPresenterTests</c> against the shared wrapper, so
/// this file focuses on the register-specific body content.
/// </summary>
public class AccountRegisterHtmlPresenterTests
{
    private static readonly DateTimeOffset PostedAtUtc = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostedByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static JournalLine ADebit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Debit, Money.Of(amount, Currency.Usd)).Value;

    private static JournalLine ACredit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Credit, Money.Of(amount, Currency.Usd)).Value;

    private static void Post(
        Journal journal, ChartOfAccounts chart, DateOnly entryDate, string description, params JournalLine[] lines)
    {
        var id = Guid.NewGuid();
        journal.CreateDraft(id, entryDate, description, lines);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Renders_a_default_title_using_the_accounts_display_path()
    {
        var chart = ChartOfAccounts.Empty();
        var utilities = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense, isPlaceholder: true).Value;
        var electric = chart.AddChild(Guid.NewGuid(), utilities.Id, "Electric", AccountType.Expense).Value;
        var register = AccountRegister.Compute(electric.Id, chart, Journal.Empty(Currency.Usd)).Value;

        var html = AccountRegisterHtmlPresenter.Render(register, chart);

        html.Should().Contain("<title>Account Register: Utilities:Electric</title>");
        html.Should().Contain("<h1>Account Register: Utilities:Electric</h1>");
    }

    [Fact]
    public void Accepts_a_custom_title()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var register = AccountRegister.Compute(checking.Id, chart, Journal.Empty(Currency.Usd)).Value;

        var html = AccountRegisterHtmlPresenter.Render(register, chart, title: "July Checking");

        html.Should().Contain("<h1>July Checking</h1>");
    }

    [Fact]
    public void Includes_the_table_header_row()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var register = AccountRegister.Compute(checking.Id, chart, Journal.Empty(Currency.Usd)).Value;

        var html = AccountRegisterHtmlPresenter.Render(register, chart);

        html.Should().Contain("<th>Date</th><th>Account</th><th>Description</th>" +
            "<th class=\"amount\">Debit</th><th class=\"amount\">Credit</th><th class=\"amount\">Balance</th>");
    }

    [Fact]
    public void Shows_each_line_with_iso_date_account_description_side_and_running_balance()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, new DateOnly(2026, 7, 1), "Paycheck", ADebit(checking.Id, 1000m), ACredit(salary.Id, 1000m));
        var register = AccountRegister.Compute(checking.Id, chart, journal).Value;

        var html = AccountRegisterHtmlPresenter.Render(register, chart);

        html.Should().Contain(
            "<td>2026-07-01</td><td>Checking</td><td>Paycheck</td>" +
            "<td class=\"amount\">1,000.00</td><td class=\"amount\"></td><td class=\"amount\">1,000.00</td>");
    }

    [Fact]
    public void Labels_each_line_with_its_own_account_in_a_subtree_register()
    {
        var chart = ChartOfAccounts.Empty();
        var utilities = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense, isPlaceholder: true).Value;
        var electric = chart.AddChild(Guid.NewGuid(), utilities.Id, "Electric", AccountType.Expense).Value;
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, new DateOnly(2026, 7, 5), "Electric bill", ADebit(electric.Id, 50m), ACredit(checking.Id, 50m));
        var register = AccountRegister.Compute(utilities.Id, chart, journal, includeDescendants: true).Value;

        var html = AccountRegisterHtmlPresenter.Render(register, chart);

        html.Should().Contain("<td>Utilities:Electric</td>");
    }

    [Fact]
    public void Escapes_html_special_characters_in_account_names_and_descriptions()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Alice & Bob's <Fund>", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(
            journal, chart, new DateOnly(2026, 7, 1), "Pay < Bonus>",
            ADebit(checking.Id, 100m), ACredit(salary.Id, 100m));
        var register = AccountRegister.Compute(checking.Id, chart, journal).Value;

        var html = AccountRegisterHtmlPresenter.Render(register, chart);

        html.Should().Contain("Alice &amp; Bob&#39;s &lt;Fund&gt;");
        html.Should().Contain("Pay &lt; Bonus&gt;");
        html.Should().NotContain("<Fund>");
        html.Should().NotContain("< Bonus>");
    }

    [Fact]
    public void Rejects_a_null_register()
    {
        var act = () => AccountRegisterHtmlPresenter.Render(null!, ChartOfAccounts.Empty());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rejects_a_null_chart()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var register = AccountRegister.Compute(checking.Id, chart, Journal.Empty(Currency.Usd)).Value;

        var act = () => AccountRegisterHtmlPresenter.Render(register, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}