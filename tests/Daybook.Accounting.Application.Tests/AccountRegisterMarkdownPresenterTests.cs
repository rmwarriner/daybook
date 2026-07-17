using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application.Tests;

/// <summary>
/// Milestone 7 — <see cref="AccountRegisterMarkdownPresenter"/> (spec §12):
/// renders an already-computed <see cref="AccountRegister"/> as a Markdown
/// table — "the account register a household user reads day to day."
/// </summary>
public class AccountRegisterMarkdownPresenterTests
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
        var journal = Journal.Empty(Currency.Usd);
        var register = AccountRegister.Compute(electric.Id, chart, journal).Value;

        var markdown = AccountRegisterMarkdownPresenter.Render(register, chart);

        markdown.Should().StartWith("# Account Register: Utilities:Electric");
    }

    [Fact]
    public void Accepts_a_custom_title()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var journal = Journal.Empty(Currency.Usd);
        var register = AccountRegister.Compute(checking.Id, chart, journal).Value;

        var markdown = AccountRegisterMarkdownPresenter.Render(register, chart, title: "July Checking");

        markdown.Should().StartWith("# July Checking");
    }

    [Fact]
    public void Includes_the_table_header_and_alignment_row()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var journal = Journal.Empty(Currency.Usd);
        var register = AccountRegister.Compute(checking.Id, chart, journal).Value;

        var markdown = AccountRegisterMarkdownPresenter.Render(register, chart);

        markdown.Should().Contain("| Date | Account | Description | Debit | Credit | Balance |");
        markdown.Should().Contain("| --- | --- | --- | ---: | ---: | ---: |");
    }

    [Fact]
    public void Shows_each_line_with_iso_date_account_description_side_and_running_balance()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var rent = chart.AddRoot(Guid.NewGuid(), "Rent", AccountType.Expense).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, new DateOnly(2026, 7, 1), "Paycheck", ADebit(checking.Id, 1000m), ACredit(salary.Id, 1000m));
        Post(journal, chart, new DateOnly(2026, 7, 5), "Rent", ADebit(rent.Id, 400m), ACredit(checking.Id, 400m));
        var register = AccountRegister.Compute(checking.Id, chart, journal).Value;

        var markdown = AccountRegisterMarkdownPresenter.Render(register, chart);

        markdown.Should().Contain("| 2026-07-01 | Checking | Paycheck | 1,000.00 |  | 1,000.00 |");
        markdown.Should().Contain("| 2026-07-05 | Checking | Rent |  | 400.00 | 600.00 |");
    }

    [Fact]
    public void Labels_each_line_with_its_own_account_in_a_subtree_register()
    {
        var chart = ChartOfAccounts.Empty();
        var utilities = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense, isPlaceholder: true).Value;
        var electric = chart.AddChild(Guid.NewGuid(), utilities.Id, "Electric", AccountType.Expense).Value;
        var gas = chart.AddChild(Guid.NewGuid(), utilities.Id, "Gas", AccountType.Expense).Value;
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var journal = Journal.Empty(Currency.Usd);
        Post(journal, chart, new DateOnly(2026, 7, 1), "Gas bill", ADebit(gas.Id, 30m), ACredit(checking.Id, 30m));
        Post(journal, chart, new DateOnly(2026, 7, 5), "Electric bill", ADebit(electric.Id, 50m), ACredit(checking.Id, 50m));
        var register = AccountRegister.Compute(utilities.Id, chart, journal, includeDescendants: true).Value;

        var markdown = AccountRegisterMarkdownPresenter.Render(register, chart);

        markdown.Should().Contain("| 2026-07-01 | Utilities:Gas | Gas bill | 30.00 |  | 30.00 |");
        markdown.Should().Contain("| 2026-07-05 | Utilities:Electric | Electric bill | 50.00 |  | 80.00 |");
    }

    [Fact]
    public void Renders_just_the_header_when_there_are_no_lines()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var journal = Journal.Empty(Currency.Usd);
        var register = AccountRegister.Compute(checking.Id, chart, journal).Value;

        var markdown = AccountRegisterMarkdownPresenter.Render(register, chart);

        markdown.Should().Contain("| Date | Account | Description | Debit | Credit | Balance |");
        markdown.Should().NotContain("|  |  |  |  |  |  |");
    }

    [Fact]
    public void Rejects_a_null_register()
    {
        var act = () => AccountRegisterMarkdownPresenter.Render(null!, ChartOfAccounts.Empty());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rejects_a_null_chart()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var register = AccountRegister.Compute(checking.Id, chart, Journal.Empty(Currency.Usd)).Value;

        var act = () => AccountRegisterMarkdownPresenter.Render(register, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}