using CsCheck;

namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 5 — <see cref="BalanceSheet"/> (spec §6.4): Assets = Liabilities
/// + Equity, where Equity is equity-account balances plus net income. Net
/// income is life-to-date (Σ Income − Σ Expense), not scoped to a fiscal
/// period — v1 has no period-close/closing-entries mechanism (deliberately
/// out of scope), so income/expense accounts are never zeroed into retained
/// earnings; "life-to-date" is the only coherent v1 reading.
/// </summary>
public class BalanceSheetTests
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
    public void Assets_equal_liabilities_plus_equity_plus_net_income()
    {
        var (chart, checking, equity, loan, salary, rent) = AFullChart();
        var journal = Journal.Empty();
        Post(journal, chart, ADebit(checking.Id, 1000m), ACredit(equity.Id, 1000m));
        Post(journal, chart, ADebit(checking.Id, 500m), ACredit(loan.Id, 500m));
        Post(journal, chart, ADebit(checking.Id, 300m), ACredit(salary.Id, 300m));
        Post(journal, chart, ADebit(rent.Id, 200m), ACredit(checking.Id, 200m));

        var balanceSheet = BalanceSheet.Compute(chart, journal);

        balanceSheet.Assets.Should().ContainSingle(a => a.AccountId == checking.Id)
            .Which.RolledUpBalance.Should().Be(Money.Of(1600m, Currency.Usd));
        balanceSheet.Liabilities.Should().ContainSingle(a => a.AccountId == loan.Id)
            .Which.RolledUpBalance.Should().Be(Money.Of(500m, Currency.Usd));
        balanceSheet.Equity.Should().ContainSingle(a => a.AccountId == equity.Id)
            .Which.RolledUpBalance.Should().Be(Money.Of(1000m, Currency.Usd));
        balanceSheet.NetIncome.Should().Be(Money.Of(100m, Currency.Usd));
        balanceSheet.TotalAssets.Should().Be(Money.Of(1600m, Currency.Usd));
        balanceSheet.TotalLiabilitiesAndEquity.Should().Be(Money.Of(1600m, Currency.Usd));
    }

    [Fact]
    public void Net_income_is_life_to_date_income_minus_expense()
    {
        var (chart, checking, _, _, salary, rent) = AFullChart();
        var journal = Journal.Empty();
        Post(journal, chart, ADebit(checking.Id, 300m), ACredit(salary.Id, 300m));
        Post(journal, chart, ADebit(rent.Id, 120m), ACredit(checking.Id, 120m));

        var balanceSheet = BalanceSheet.Compute(chart, journal);

        balanceSheet.NetIncome.Should().Be(Money.Of(180m, Currency.Usd));
    }

    [Fact]
    public void Only_root_accounts_appear_avoiding_double_counting_a_subtree()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var pettyCash = chart.AddChild(Guid.NewGuid(), checking.Id, "Petty Cash", AccountType.Asset).Value;
        var equity = chart.AddRoot(Guid.NewGuid(), "Owner's Equity", AccountType.Equity).Value;
        var journal = Journal.Empty();
        Post(journal, chart, ADebit(pettyCash.Id, 50m), ACredit(equity.Id, 50m));

        var balanceSheet = BalanceSheet.Compute(chart, journal);

        balanceSheet.Assets.Should().ContainSingle().Which.AccountId.Should().Be(checking.Id);
        balanceSheet.Assets[0].RolledUpBalance.Should().Be(Money.Of(50m, Currency.Usd));
        balanceSheet.TotalAssets.Should().Be(Money.Of(50m, Currency.Usd));
    }

    [Fact]
    public void Draft_entries_are_ignored()
    {
        var (chart, checking, equity, _, _, _) = AFullChart();
        var journal = Journal.Empty();
        journal.CreateDraft(
            Guid.NewGuid(), EntryDate, "Unposted", [ADebit(checking.Id, 1000m), ACredit(equity.Id, 1000m)]);

        var balanceSheet = BalanceSheet.Compute(chart, journal);

        balanceSheet.TotalAssets.Should().Be(Money.Zero(Currency.Usd));
    }

    [Fact]
    public void An_empty_chart_balances_at_zero()
    {
        var balanceSheet = BalanceSheet.Compute(ChartOfAccounts.Empty(), Journal.Empty());

        balanceSheet.Assets.Should().BeEmpty();
        balanceSheet.Liabilities.Should().BeEmpty();
        balanceSheet.Equity.Should().BeEmpty();
        balanceSheet.NetIncome.Should().Be(Money.Zero(Currency.Usd));
        balanceSheet.TotalAssets.Should().Be(Money.Zero(Currency.Usd));
        balanceSheet.TotalLiabilitiesAndEquity.Should().Be(Money.Zero(Currency.Usd));
    }

    // ---- Property-based: the fundamental accounting identity --------------

    [Fact]
    public void Property_assets_always_equal_liabilities_plus_equity()
    {
        Gen.Int[1, 1000].Sample(seed =>
        {
            var random = new Random(seed);
            var (chart, checking, equity, loan, salary, rent) = AFullChart();
            var journal = Journal.Empty();

            Post(journal, chart, ADebit(checking.Id, 1000m), ACredit(equity.Id, 1000m));

            var entryCount = random.Next(1, 6);
            for (var i = 0; i < entryCount; i++)
            {
                var amount = (decimal)random.Next(1, 10_000) / 100m;
                switch (random.Next(3))
                {
                    case 0:
                        Post(journal, chart, ADebit(checking.Id, amount), ACredit(loan.Id, amount));
                        break;
                    case 1:
                        Post(journal, chart, ADebit(checking.Id, amount), ACredit(salary.Id, amount));
                        break;
                    default:
                        Post(journal, chart, ADebit(rent.Id, amount), ACredit(checking.Id, amount));
                        break;
                }
            }

            var act = () => BalanceSheet.Compute(chart, journal);

            act.Should().NotThrow();
            var balanceSheet = act();
            balanceSheet.TotalAssets.Should().Be(balanceSheet.TotalLiabilitiesAndEquity);
        });
    }
}