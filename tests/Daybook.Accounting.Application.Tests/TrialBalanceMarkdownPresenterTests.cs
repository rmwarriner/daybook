using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application.Tests;

/// <summary>
/// Milestone 7 — <see cref="TrialBalanceMarkdownPresenter"/> (spec §12):
/// renders an already-computed <see cref="TrialBalance"/> as a Markdown
/// table. Pure presentation — no accounting logic lives here, only display
/// formatting over data Core already computed and validated.
/// </summary>
public class TrialBalanceMarkdownPresenterTests
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

    [Fact]
    public void Renders_a_default_title_heading()
    {
        var chart = ChartOfAccounts.Empty();
        var journal = Journal.Empty(Currency.Usd);
        var trialBalance = TrialBalance.Compute(chart, journal);

        var markdown = TrialBalanceMarkdownPresenter.Render(trialBalance, chart);

        markdown.Should().StartWith("# Trial Balance");
    }

    [Fact]
    public void Accepts_a_custom_title()
    {
        var chart = ChartOfAccounts.Empty();
        var journal = Journal.Empty(Currency.Usd);
        var trialBalance = TrialBalance.Compute(chart, journal);

        var markdown = TrialBalanceMarkdownPresenter.Render(trialBalance, chart, title: "Household — Trial Balance");

        markdown.Should().StartWith("# Household — Trial Balance");
    }

    [Fact]
    public void Includes_the_table_header_and_alignment_row()
    {
        var chart = ChartOfAccounts.Empty();
        var journal = Journal.Empty(Currency.Usd);
        var trialBalance = TrialBalance.Compute(chart, journal);

        var markdown = TrialBalanceMarkdownPresenter.Render(trialBalance, chart);

        markdown.Should().Contain("| Account | Debit | Credit |");
        markdown.Should().Contain("| --- | ---: | ---: |");
    }

    [Fact]
    public void Shows_each_accounts_rolled_up_balance_on_its_normal_side()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var rent = chart.AddRoot(Guid.NewGuid(), "Rent", AccountType.Expense).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 500m), ACredit(salary.Id, 500m));
        Post(journal, chart, ADebit(rent.Id, 120m), ACredit(checking.Id, 120m));
        var trialBalance = TrialBalance.Compute(chart, journal);

        var markdown = TrialBalanceMarkdownPresenter.Render(trialBalance, chart);

        markdown.Should().Contain("| Checking | 380.00 |  |");
        markdown.Should().Contain("| Rent | 120.00 |  |");
        markdown.Should().Contain("| Salary |  | 500.00 |");
    }

    [Fact]
    public void Shows_a_bold_total_row_using_the_trial_balances_own_totals()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 500m), ACredit(salary.Id, 500m));
        var trialBalance = TrialBalance.Compute(chart, journal);

        var markdown = TrialBalanceMarkdownPresenter.Render(trialBalance, chart);

        markdown.Should().Contain("| **Total** | **500.00** | **500.00** |");
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

        var markdown = TrialBalanceMarkdownPresenter.Render(trialBalance, chart);

        markdown.Should().Contain("Utilities:Electric");
    }

    [Fact]
    public void Orders_rows_alphabetically_by_display_path()
    {
        var chart = ChartOfAccounts.Empty();
        chart.AddRoot(Guid.NewGuid(), "Zebra", AccountType.Asset);
        chart.AddRoot(Guid.NewGuid(), "Apple", AccountType.Asset);
        var journal = Journal.Empty(Currency.Usd);
        var trialBalance = TrialBalance.Compute(chart, journal);

        var markdown = TrialBalanceMarkdownPresenter.Render(trialBalance, chart);

        markdown.IndexOf("Apple", StringComparison.Ordinal)
            .Should().BeLessThan(markdown.IndexOf("Zebra", StringComparison.Ordinal));
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

        var markdown = TrialBalanceMarkdownPresenter.Render(trialBalance, chart);

        markdown.Should().Contain("12,345.60");
    }

    [Fact]
    public void Escapes_pipe_characters_in_account_names_so_the_table_stays_valid()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Rent | Utilities", AccountType.Asset).Value;
        var equity = chart.AddRoot(Guid.NewGuid(), "Owner's Equity", AccountType.Equity).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 100m), ACredit(equity.Id, 100m));
        var trialBalance = TrialBalance.Compute(chart, journal);

        var markdown = TrialBalanceMarkdownPresenter.Render(trialBalance, chart);

        markdown.Should().Contain("| Rent \\| Utilities | 100.00 |  |");
        markdown.Split('\n').Count(l => l.StartsWith('|')).Should().Be(5); // header, alignment, 2 account rows, total row
    }

    [Fact]
    public void Escapes_newlines_in_account_names_so_the_table_stays_valid()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Rent\nUtilities", AccountType.Asset).Value;
        var equity = chart.AddRoot(Guid.NewGuid(), "Owner's Equity", AccountType.Equity).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, ADebit(checking.Id, 100m), ACredit(equity.Id, 100m));
        var trialBalance = TrialBalance.Compute(chart, journal);

        var markdown = TrialBalanceMarkdownPresenter.Render(trialBalance, chart);

        markdown.Should().Contain("| Rent Utilities | 100.00 |  |");
        markdown.Split('\n').Count(l => l.StartsWith('|')).Should().Be(5);
    }

    [Fact]
    public void Rejects_a_null_trial_balance()
    {
        var act = () => TrialBalanceMarkdownPresenter.Render(null!, ChartOfAccounts.Empty());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rejects_a_null_chart()
    {
        var trialBalance = TrialBalance.Compute(ChartOfAccounts.Empty(), Journal.Empty(Currency.Usd));

        var act = () => TrialBalanceMarkdownPresenter.Render(trialBalance, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}