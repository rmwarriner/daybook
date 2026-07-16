using CsCheck;

namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 3 — <see cref="Journal"/>, the one guarded write door (golden
/// rule 5) that owns the full §5 posting checklist, gapless sequence
/// assignment, and posted-entry immutability.
/// </summary>
public class JournalTests
{
    private static readonly DateOnly EntryDate = new(2026, 7, 15);
    private static readonly DateTimeOffset PostedAtUtc = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostedByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static (ChartOfAccounts Chart, Account Checking, Account Salary) AChart()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        return (chart, checking, salary);
    }

    private static JournalLine ADebit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Debit, Money.Of(amount, Currency.Usd)).Value;

    private static JournalLine ACredit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Credit, Money.Of(amount, Currency.Usd)).Value;

    // ---- Empty / lookup -------------------------------------------------

    [Fact]
    public void Empty_journal_has_no_entries()
    {
        var journal = Journal.Empty(Currency.Usd);

        journal.Find(Guid.NewGuid()).Should().BeNull();
        journal.Drafts.Should().BeEmpty();
        journal.PostedEntries.Should().BeEmpty();
    }

    [Fact]
    public void Empty_stores_the_currency_it_was_constructed_with()
    {
        Journal.Empty(Currency.Usd).BaseCurrency.Should().Be(Currency.Usd);
        Journal.Empty(Currency.Of("EUR")).BaseCurrency.Should().Be(Currency.Of("EUR"));
    }

    [Fact]
    public void Empty_rejects_a_null_currency()
    {
        var act = () => Journal.Empty(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Posting_uses_the_journals_own_currency_not_a_hardcoded_one()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty(Currency.Of("EUR"));
        var id = Guid.NewGuid();
        var eurDebit = JournalLine.Create(checking.Id, Side.Debit, Money.Of(100m, Currency.Of("EUR"))).Value;
        var eurCredit = JournalLine.Create(salary.Id, Side.Credit, Money.Of(100m, Currency.Of("EUR"))).Value;
        journal.CreateDraft(id, EntryDate, "Paycheck in EUR", [eurDebit, eurCredit]);

        var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Posting_a_usd_line_against_a_eur_journal_is_a_currency_mismatch()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        var journal = Journal.Empty(Currency.Of("EUR"));
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Wrong currency", [ADebit(checking.Id, 100m), ADebit(salary.Id, 100m)]);

        var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.line.currency_mismatch");
        result.Error.Message.Should().Contain("EUR");
    }

    // ---- CreateDraft ------------------------------------------------------

    [Fact]
    public void CreateDraft_adds_a_findable_draft()
    {
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();

        var result = journal.CreateDraft(id, EntryDate, "Groceries", []);

        result.IsSuccess.Should().BeTrue();
        journal.Find(id).Should().Be(result.Value);
        journal.Drafts.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Fact]
    public void CreateDraft_rejects_a_duplicate_id()
    {
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Groceries", []);

        var result = journal.CreateDraft(id, EntryDate, "Something else", []);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.id.duplicate");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
    }

    [Fact]
    public void CreateDraft_bubbles_up_entry_level_validation_failures()
    {
        var journal = Journal.Empty(Currency.Usd);

        var result = journal.CreateDraft(Guid.NewGuid(), EntryDate, "   ", []);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.description.required");
        journal.Drafts.Should().BeEmpty();
    }

    // ---- UpdateDraft / DeleteDraft ------------------------------------

    [Fact]
    public void UpdateDraft_updates_the_stored_entry()
    {
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Groceries", []);

        var result = journal.UpdateDraft(id, EntryDate, "Corrected groceries", []);

        result.IsSuccess.Should().BeTrue();
        journal.Find(id)!.Description.Should().Be("Corrected groceries");
    }

    [Fact]
    public void UpdateDraft_rejects_an_unknown_entry()
    {
        var journal = Journal.Empty(Currency.Usd);

        var result = journal.UpdateDraft(Guid.NewGuid(), EntryDate, "Whatever", []);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.not_found");
    }

    [Fact]
    public void DeleteDraft_removes_a_draft()
    {
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Groceries", []);

        var result = journal.DeleteDraft(id);

        result.IsSuccess.Should().BeTrue();
        journal.Find(id).Should().BeNull();
    }

    [Fact]
    public void DeleteDraft_rejects_an_unknown_entry()
    {
        var journal = Journal.Empty(Currency.Usd);

        var result = journal.DeleteDraft(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.not_found");
    }

    // ---- Post: happy path ---------------------------------------------

    [Fact]
    public void Post_a_valid_balanced_entry_succeeds_and_stamps_the_entry()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 1000m), ACredit(salary.Id, 1000m)]);

        var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        result.IsSuccess.Should().BeTrue();
        var posted = result.Value;
        posted.Status.Should().Be(JournalEntryStatus.Posted);
        posted.SequenceNumber.Should().Be(1);
        posted.PostedAtUtc.Should().Be(PostedAtUtc);
        posted.PostedByUserId.Should().Be(PostedByUserId);
        journal.PostedEntries.Should().ContainSingle().Which.Id.Should().Be(id);
        journal.Drafts.Should().BeEmpty();
    }

    [Fact]
    public void Post_assigns_gapless_increasing_sequence_numbers_across_multiple_posts()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);

        var sequenceNumbers = new List<int?>();
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid();
            journal.CreateDraft(id, EntryDate, $"Entry {i}", [ADebit(checking.Id, 10m), ACredit(salary.Id, 10m)]);
            sequenceNumbers.Add(journal.Post(id, chart, PostedAtUtc, PostedByUserId).Value.SequenceNumber);
        }

        sequenceNumbers.Should().Equal(1, 2, 3);
    }

    // ---- Post: §5 checklist failures ------------------------------------

    [Fact]
    public void Post_rejects_an_unknown_entry()
    {
        var (chart, _, _) = AChart();
        var journal = Journal.Empty(Currency.Usd);

        var result = journal.Post(Guid.NewGuid(), chart, PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.not_found");
    }

    [Fact]
    public void Post_rejects_an_already_posted_entry()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 10m), ACredit(salary.Id, 10m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.already_posted");
    }

    [Fact]
    public void Post_rejects_fewer_than_two_lines()
    {
        var (chart, checking, _) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Incomplete", [ADebit(checking.Id, 10m)]);

        var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.lines.insufficient");
    }

    [Fact]
    public void Post_rejects_an_unbalanced_entry_and_reports_the_totals()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Lopsided", [ADebit(checking.Id, 100m), ACredit(salary.Id, 60m)]);

        var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.unbalanced");
        result.Error.Message.Should().Contain("100").And.Contain("60");
    }

    [Fact]
    public void Post_rejects_a_line_referencing_an_unknown_account()
    {
        var (chart, checking, _) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Ghost account", [ADebit(checking.Id, 10m), ACredit(Guid.NewGuid(), 10m)]);

        var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.line.account_not_found");
    }

    [Fact]
    public void Post_rejects_a_line_on_an_inactive_account()
    {
        var (chart, checking, salary) = AChart();
        chart.Deactivate(checking.Id);
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Inactive", [ADebit(checking.Id, 10m), ACredit(salary.Id, 10m)]);

        var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.line.account_inactive");
        result.Error.Category.Should().Be(ErrorCategory.BusinessRule);
    }

    [Fact]
    public void Post_rejects_a_line_on_a_placeholder_account()
    {
        var (chart, checking, salary) = AChart();
        chart.MarkAsPlaceholder(checking.Id);
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Roll-up only", [ADebit(checking.Id, 10m), ACredit(salary.Id, 10m)]);

        var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.line.account_placeholder");
        result.Error.Category.Should().Be(ErrorCategory.BusinessRule);
    }

    [Fact]
    public void Post_rejects_a_line_in_a_non_base_currency()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        var eurLine = JournalLine.Create(salary.Id, Side.Credit, Money.Of(10m, Currency.Of("EUR"))).Value;
        journal.CreateDraft(id, EntryDate, "Wrong currency", [ADebit(checking.Id, 10m), eurLine]);

        var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.line.currency_mismatch");
    }

    // ---- Posted immutability --------------------------------------------

    [Fact]
    public void A_posted_entry_cannot_be_updated()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 10m), ACredit(salary.Id, 10m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        var result = journal.UpdateDraft(id, EntryDate, "Tampered", []);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.posted.immutable");
        journal.Find(id)!.Description.Should().Be("Paycheck");
    }

    [Fact]
    public void A_posted_entry_cannot_be_deleted()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 10m), ACredit(salary.Id, 10m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        var result = journal.DeleteDraft(id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.posted.immutable");
        journal.Find(id).Should().NotBeNull();
    }

    // ---- Reverse --------------------------------------------------------

    [Fact]
    public void Reverse_creates_a_new_entry_with_flipped_sides_and_links_both_directions()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var originalId = Guid.NewGuid();
        var reversalId = Guid.NewGuid();
        journal.CreateDraft(originalId, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(originalId, chart, PostedAtUtc, PostedByUserId);

        var reversalDate = new DateOnly(2026, 7, 20);
        var result = journal.Reverse(
            originalId, reversalId, chart, reversalDate, "Reversing paycheck", PostedAtUtc, PostedByUserId);

        result.IsSuccess.Should().BeTrue();
        var reversal = result.Value;
        reversal.Id.Should().Be(reversalId);
        reversal.EntryDate.Should().Be(reversalDate);
        reversal.Description.Should().Be("Reversing paycheck");
        reversal.Status.Should().Be(JournalEntryStatus.Posted);
        reversal.ReversesEntryId.Should().Be(originalId);
        reversal.Lines.Should().Contain(l =>
            l.AccountId == checking.Id && l.Side == Side.Credit && l.Amount == Money.Of(100m, Currency.Usd));
        reversal.Lines.Should().Contain(l =>
            l.AccountId == salary.Id && l.Side == Side.Debit && l.Amount == Money.Of(100m, Currency.Usd));

        journal.ReversalOf(originalId).Should().Be(reversalId);
        journal.IsReversed(originalId).Should().BeTrue();
    }

    [Fact]
    public void Reverse_assigns_the_next_gapless_sequence_number()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var originalId = Guid.NewGuid();
        journal.CreateDraft(originalId, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(originalId, chart, PostedAtUtc, PostedByUserId).Value.SequenceNumber.Should().Be(1);

        var result = journal.Reverse(
            originalId, Guid.NewGuid(), chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId);

        result.Value.SequenceNumber.Should().Be(2);
    }

    [Fact]
    public void Reverse_rejects_an_unknown_original_entry()
    {
        var (chart, _, _) = AChart();
        var journal = Journal.Empty(Currency.Usd);

        var result = journal.Reverse(
            Guid.NewGuid(), Guid.NewGuid(), chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.not_found");
    }

    [Fact]
    public void Reverse_rejects_a_draft_entry()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);

        var result = journal.Reverse(id, Guid.NewGuid(), chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.reversal.not_posted");
    }

    [Fact]
    public void Reverse_rejects_an_entry_that_has_already_been_reversed()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);
        journal.Reverse(id, Guid.NewGuid(), chart, EntryDate, "First reversal", PostedAtUtc, PostedByUserId);

        var result = journal.Reverse(id, Guid.NewGuid(), chart, EntryDate, "Second reversal", PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.reversal.already_reversed");
    }

    [Fact]
    public void Reverse_rejects_a_duplicate_reversal_id()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);
        var otherId = Guid.NewGuid();
        journal.CreateDraft(otherId, EntryDate, "Something else", []);

        var result = journal.Reverse(id, otherId, chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.id.duplicate");
    }

    [Fact]
    public void Reverse_bubbles_up_a_blank_description_on_the_reversal()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        var result = journal.Reverse(id, Guid.NewGuid(), chart, EntryDate, "   ", PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.description.required");
        journal.ReversalOf(id).Should().BeNull();
    }

    [Fact]
    public void Reverse_rejects_if_an_account_was_deactivated_since_the_original_was_posted()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        chart.Deactivate(checking.Id);

        var result = journal.Reverse(id, Guid.NewGuid(), chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.line.account_inactive");
        journal.ReversalOf(id).Should().BeNull();
    }

    [Fact]
    public void A_reversal_is_itself_reversible()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);
        var reversalId = Guid.NewGuid();
        journal.Reverse(id, reversalId, chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId);

        var result = journal.Reverse(
            reversalId, Guid.NewGuid(), chart, EntryDate, "Reversal of the reversal", PostedAtUtc, PostedByUserId);

        result.IsSuccess.Should().BeTrue();
    }

    // ---- Property-based: the central accounting invariant -------------

    [Fact]
    public void Property_any_balanced_entry_posts_and_remains_balanced()
    {
        Gen.Int[1, 1000].Sample(seed =>
        {
            var random = new Random(seed);
            var (chart, checking, salary) = AChart();
            var journal = Journal.Empty(Currency.Usd);
            var id = Guid.NewGuid();

            var debitCount = random.Next(1, 5);
            var debitAmounts = Enumerable.Range(0, debitCount)
                .Select(_ => (decimal)random.Next(1, 100_000) / 100m)
                .ToList();
            var total = debitAmounts.Sum();

            var lines = debitAmounts.Select(a => ADebit(checking.Id, a))
                .Append(ACredit(salary.Id, total))
                .ToArray();

            journal.CreateDraft(id, EntryDate, "Random balanced entry", lines);

            var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

            result.IsSuccess.Should().BeTrue();
            var debitTotal = result.Value.Lines.Where(l => l.Side == Side.Debit)
                .Aggregate(Money.Zero(Currency.Usd), (acc, l) => acc + l.Amount);
            var creditTotal = result.Value.Lines.Where(l => l.Side == Side.Credit)
                .Aggregate(Money.Zero(Currency.Usd), (acc, l) => acc + l.Amount);
            debitTotal.Should().Be(creditTotal);
        });
    }

    [Fact]
    public void Property_any_unbalanced_entry_always_fails_to_post()
    {
        Gen.Int[1, 1000].Sample(seed =>
        {
            var random = new Random(seed);
            var (chart, checking, salary) = AChart();
            var journal = Journal.Empty(Currency.Usd);
            var id = Guid.NewGuid();

            var debitAmount = (decimal)random.Next(100, 100_000) / 100m;
            var skew = (decimal)random.Next(1, 1000) / 100m;

            journal.CreateDraft(
                id,
                EntryDate,
                "Random unbalanced entry",
                [ADebit(checking.Id, debitAmount), ACredit(salary.Id, debitAmount + skew)]);

            var result = journal.Post(id, chart, PostedAtUtc, PostedByUserId);

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("entry.unbalanced");
        });
    }

    [Fact]
    public void Property_reversing_an_entry_nets_every_affected_account_to_zero()
    {
        Gen.Int[1, 1000].Sample(seed =>
        {
            var random = new Random(seed);
            var (chart, checking, salary) = AChart();
            var journal = Journal.Empty(Currency.Usd);
            var id = Guid.NewGuid();

            var debitCount = random.Next(1, 5);
            var debitAmounts = Enumerable.Range(0, debitCount)
                .Select(_ => (decimal)random.Next(1, 100_000) / 100m)
                .ToList();
            var total = debitAmounts.Sum();

            var lines = debitAmounts.Select(a => ADebit(checking.Id, a))
                .Append(ACredit(salary.Id, total))
                .ToArray();

            journal.CreateDraft(id, EntryDate, "Random entry", lines);
            var original = journal.Post(id, chart, PostedAtUtc, PostedByUserId).Value;

            var reversal = journal
                .Reverse(id, Guid.NewGuid(), chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId)
                .Value;

            var netByAccount = original.Lines.Concat(reversal.Lines)
                .GroupBy(l => l.AccountId)
                .Select(g => g.Sum(l => l.Side == Side.Debit ? l.Amount.Amount : -l.Amount.Amount));

            netByAccount.Should().OnlyContain(net => net == 0m);
        });
    }
}