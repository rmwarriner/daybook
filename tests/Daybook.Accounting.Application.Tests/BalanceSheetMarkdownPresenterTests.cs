using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application.Tests;

/// <summary>
/// Milestone 7 — <see cref="BalanceSheetMarkdownPresenter"/> (spec §12):
/// renders an already-computed <see cref="BalanceSheet"/> as Markdown —
/// Assets / Liabilities / Equity sections, each with a subtotal, plus the
/// final Assets = Liabilities + Equity summary.
/// </summary>
public class BalanceSheetMarkdownPresenterTests
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
    public void Renders_a_default_title_heading()
    {
        var balanceSheet = BalanceSheet.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var markdown = BalanceSheetMarkdownPresenter.Render(balanceSheet, ChartOfAccounts.Empty());

        markdown.Should().StartWith("# Balance Sheet");
    }

    [Fact]
    public void Accepts_a_custom_title()
    {
        var balanceSheet = BalanceSheet.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var markdown = BalanceSheetMarkdownPresenter.Render(
            balanceSheet, ChartOfAccounts.Empty(), title: "Household — Balance Sheet");

        markdown.Should().StartWith("# Household — Balance Sheet");
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

        var markdown = BalanceSheetMarkdownPresenter.Render(balanceSheet, chart);

        markdown.Should().Contain("## Assets");
        markdown.Should().Contain("| Account | Amount |");
        markdown.Should().Contain("| --- | ---: |");
        markdown.Should().Contain("| Checking | 1,600.00 |");
        markdown.Should().Contain("**Total Assets: 1,600.00**");

        markdown.Should().Contain("## Liabilities");
        markdown.Should().Contain("| Loan Payable | 500.00 |");
        markdown.Should().Contain("**Total Liabilities: 500.00**");

        markdown.Should().Contain("## Equity");
        markdown.Should().Contain("| Owner's Equity | 1,000.00 |");
        markdown.Should().Contain("| Net Income | 100.00 |");
        markdown.Should().Contain("**Total Equity: 1,100.00**");

        markdown.Should().Contain("**Total Liabilities + Equity: 1,600.00**");
    }

    [Fact]
    public void Orders_accounts_alphabetically_within_a_section()
    {
        var chart = ChartOfAccounts.Empty();
        chart.AddRoot(Guid.NewGuid(), "Zebra Savings", AccountType.Asset);
        chart.AddRoot(Guid.NewGuid(), "Apple Checking", AccountType.Asset);
        var balanceSheet = BalanceSheet.Compute(chart, Journal.Empty(Currency.Usd));

        var markdown = BalanceSheetMarkdownPresenter.Render(balanceSheet, chart);

        markdown.IndexOf("Apple Checking", StringComparison.Ordinal)
            .Should().BeLessThan(markdown.IndexOf("Zebra Savings", StringComparison.Ordinal));
    }

    [Fact]
    public void Escapes_pipe_characters_in_account_names_so_the_table_stays_valid()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking | Savings", AccountType.Asset).Value;
        var equity = chart.AddRoot(Guid.NewGuid(), "Owner's Equity", AccountType.Equity).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 100m), ACredit(equity.Id, 100m));
        var balanceSheet = BalanceSheet.Compute(chart, journal);

        var markdown = BalanceSheetMarkdownPresenter.Render(balanceSheet, chart);

        markdown.Should().Contain("| Checking \\| Savings | 100.00 |");
    }

    [Fact]
    public void Renders_an_empty_section_with_a_zero_subtotal()
    {
        var chart = ChartOfAccounts.Empty();
        chart.AddRoot(Guid.NewGuid(), "Owner's Equity", AccountType.Equity);
        var balanceSheet = BalanceSheet.Compute(chart, Journal.Empty(Currency.Usd));

        var markdown = BalanceSheetMarkdownPresenter.Render(balanceSheet, chart);

        markdown.Should().Contain("## Liabilities");
        markdown.Should().Contain("**Total Liabilities: 0.00**");
    }

    [Fact]
    public void Rejects_a_null_balance_sheet()
    {
        var act = () => BalanceSheetMarkdownPresenter.Render(null!, ChartOfAccounts.Empty());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rejects_a_null_chart()
    {
        var balanceSheet = BalanceSheet.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var act = () => BalanceSheetMarkdownPresenter.Render(balanceSheet, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}