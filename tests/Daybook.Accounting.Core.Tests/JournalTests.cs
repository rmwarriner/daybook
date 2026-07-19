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

    // ---- AddReference / RemoveReference (spec §4.3.1) --------------------

    [Fact]
    public void AddReference_updates_the_stored_entry()
    {
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Groceries", []);
        var reference = Reference.Create(ReferenceType.Check, "1234").Value;

        var result = journal.AddReference(id, reference);

        result.IsSuccess.Should().BeTrue();
        journal.Find(id)!.References.Should().Equal(reference);
    }

    [Fact]
    public void AddReference_rejects_an_unknown_entry()
    {
        var journal = Journal.Empty(Currency.Usd);

        var result = journal.AddReference(Guid.NewGuid(), Reference.Create(ReferenceType.Check, "1234").Value);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.not_found");
    }

    [Fact]
    public void RemoveReference_updates_the_stored_entry()
    {
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Groceries", []);
        var reference = Reference.Create(ReferenceType.Check, "1234").Value;
        journal.AddReference(id, reference);

        var result = journal.RemoveReference(id, reference);

        result.IsSuccess.Should().BeTrue();
        journal.Find(id)!.References.Should().BeEmpty();
    }

    [Fact]
    public void RemoveReference_rejects_an_unknown_entry()
    {
        var journal = Journal.Empty(Currency.Usd);

        var result = journal.RemoveReference(Guid.NewGuid(), Reference.Create(ReferenceType.Check, "1234").Value);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.not_found");
    }

    // ---- FindByReference (spec §4.3.1 - duplicate lookup) -----------------

    [Fact]
    public void FindByReference_finds_posted_entries_carrying_a_matching_reference()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);
        var reference = Reference.Create(ReferenceType.Check, "1234").Value;
        // References are frozen at post, so attach before posting via a fresh draft instead.
        var secondId = Guid.NewGuid();
        journal.CreateDraft(secondId, EntryDate, "Reimbursement", [ADebit(checking.Id, 50m), ACredit(salary.Id, 50m)]);
        journal.AddReference(secondId, reference);
        journal.Post(secondId, chart, PostedAtUtc, PostedByUserId);

        var found = journal.FindByReference(ReferenceType.Check, "1234");

        found.Should().ContainSingle().Which.Id.Should().Be(secondId);
    }

    [Fact]
    public void FindByReference_returns_empty_when_nothing_matches()
    {
        var journal = Journal.Empty(Currency.Usd);

        journal.FindByReference(ReferenceType.Check, "1234").Should().BeEmpty();
    }

    [Fact]
    public void FindByReference_ignores_drafts()
    {
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Groceries", []);
        journal.AddReference(id, Reference.Create(ReferenceType.Check, "1234").Value);

        journal.FindByReference(ReferenceType.Check, "1234").Should().BeEmpty();
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
        posted.SchemaVersion.Should().Be(JournalEntry.CurrentSchemaVersion);
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

    [Fact]
    public void A_posted_entrys_references_cannot_be_added()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 10m), ACredit(salary.Id, 10m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        var result = journal.Find(id)!.AddReference(Reference.Create(ReferenceType.Check, "1234").Value);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.posted.immutable");
    }

    [Fact]
    public void A_posted_entrys_references_cannot_be_removed()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 10m), ACredit(salary.Id, 10m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        var result = journal.Find(id)!.RemoveReference(Reference.Create(ReferenceType.Check, "1234").Value);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.posted.immutable");
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
        reversal.SchemaVersion.Should().Be(JournalEntry.CurrentSchemaVersion);

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

    // ---- Rehydrate --------------------------------------------------------

    private static JournalEntrySnapshot ADraftSnapshot(Guid id, IReadOnlyList<JournalLine> lines) =>
        new(id, EntryDate, "A draft", lines, JournalEntryStatus.Draft, null, null, null, null);

    private static JournalEntrySnapshot APostedSnapshot(
        Guid id, int sequenceNumber, IReadOnlyList<JournalLine> lines, Guid? reversesEntryId = null) =>
        new(id, EntryDate, "A posted entry", lines, JournalEntryStatus.Posted,
            sequenceNumber, PostedAtUtc, PostedByUserId, reversesEntryId);

    [Fact]
    public void Rehydrate_of_no_entries_yields_an_empty_journal()
    {
        var journal = Journal.Rehydrate(Currency.Usd, []);

        journal.Drafts.Should().BeEmpty();
        journal.PostedEntries.Should().BeEmpty();
    }

    [Fact]
    public void Rehydrate_restores_a_snapshots_stamped_schema_version_verbatim()
    {
        // Not a real historical version - there's only ever been one - just
        // a stand-in to prove Rehydrate carries forward whatever value was
        // loaded instead of silently re-stamping it as CurrentSchemaVersion.
        const int notCurrentVersion = JournalEntry.CurrentSchemaVersion + 1;
        var (_, checking, salary) = AChart();
        var id = Guid.NewGuid();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var snapshot = ADraftSnapshot(id, lines) with { SchemaVersion = notCurrentVersion };

        var journal = Journal.Rehydrate(Currency.Usd, [snapshot]);

        journal.Find(id)!.SchemaVersion.Should().Be(notCurrentVersion);
    }

    [Fact]
    public void Rehydrate_restores_a_snapshots_references()
    {
        var (_, checking, salary) = AChart();
        var id = Guid.NewGuid();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var reference = Reference.Create(ReferenceType.Check, "1234").Value;
        var snapshot = ADraftSnapshot(id, lines) with { References = [reference] };

        var journal = Journal.Rehydrate(Currency.Usd, [snapshot]);

        journal.Find(id)!.References.Should().Equal(reference);
    }

    [Fact]
    public void Rehydrate_restores_a_draft_entry()
    {
        var (_, checking, salary) = AChart();
        var id = Guid.NewGuid();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };

        var journal = Journal.Rehydrate(Currency.Usd, [ADraftSnapshot(id, lines)]);

        var entry = journal.Find(id);
        entry.Should().NotBeNull();
        entry!.Status.Should().Be(JournalEntryStatus.Draft);
        entry.Description.Should().Be("A draft");
        entry.Lines.Should().BeEquivalentTo(lines);
        journal.Drafts.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Fact]
    public void Rehydrate_restores_posted_entries_with_their_original_sequence_numbers()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        var journal = Journal.Rehydrate(
            Currency.Usd, [APostedSnapshot(firstId, 1, lines), APostedSnapshot(secondId, 2, lines)]);

        journal.PostedEntries.Select(e => e.Id).Should().Equal(firstId, secondId);
        var first = journal.Find(firstId)!;
        first.Status.Should().Be(JournalEntryStatus.Posted);
        first.SequenceNumber.Should().Be(1);
        first.PostedAtUtc.Should().Be(PostedAtUtc);
        first.PostedByUserId.Should().Be(PostedByUserId);
    }

    [Fact]
    public void Rehydrate_seeds_the_next_sequence_number_past_loaded_history()
    {
        var (chart, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var journal = Journal.Rehydrate(Currency.Usd, [APostedSnapshot(Guid.NewGuid(), 1, lines), APostedSnapshot(Guid.NewGuid(), 2, lines)]);
        var newId = Guid.NewGuid();
        journal.CreateDraft(newId, EntryDate, "A new entry", lines);

        var result = journal.Post(newId, chart, PostedAtUtc, PostedByUserId);

        result.Value.SequenceNumber.Should().Be(3);
    }

    [Fact]
    public void Rehydrate_restores_reversal_links_both_directions()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var originalId = Guid.NewGuid();
        var reversalId = Guid.NewGuid();

        var journal = Journal.Rehydrate(
            Currency.Usd,
            [
                APostedSnapshot(originalId, 1, lines),
                APostedSnapshot(reversalId, 2, lines, reversesEntryId: originalId),
            ]);

        journal.ReversalOf(originalId).Should().Be(reversalId);
        journal.IsReversed(originalId).Should().BeTrue();
        journal.Find(reversalId)!.ReversesEntryId.Should().Be(originalId);
    }

    [Fact]
    public void Rehydrate_restores_a_mixed_set_of_drafts_and_posted_entries()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var draftId = Guid.NewGuid();
        var postedId = Guid.NewGuid();

        var journal = Journal.Rehydrate(
            Currency.Usd, [ADraftSnapshot(draftId, lines), APostedSnapshot(postedId, 1, lines)]);

        journal.Drafts.Should().ContainSingle().Which.Id.Should().Be(draftId);
        journal.PostedEntries.Should().ContainSingle().Which.Id.Should().Be(postedId);
    }

    [Fact]
    public void Rehydrate_rejects_a_null_currency()
    {
        var act = () => Journal.Rehydrate(null!, []);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rehydrate_rejects_a_null_entries_sequence()
    {
        var act = () => Journal.Rehydrate(Currency.Usd, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Rehydrate_rejects_a_duplicate_id()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var id = Guid.NewGuid();

        var act = () => Journal.Rehydrate(Currency.Usd, [ADraftSnapshot(id, lines), ADraftSnapshot(id, lines)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rehydrate_rejects_a_draft_carrying_a_sequence_number()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var corrupt = new JournalEntrySnapshot(
            Guid.NewGuid(), EntryDate, "Corrupt", lines, JournalEntryStatus.Draft,
            SequenceNumber: 1, PostedAtUtc: null, PostedByUserId: null, ReversesEntryId: null);

        var act = () => Journal.Rehydrate(Currency.Usd, [corrupt]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rehydrate_rejects_a_draft_carrying_a_reverses_entry_id()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var corrupt = new JournalEntrySnapshot(
            Guid.NewGuid(), EntryDate, "Corrupt", lines, JournalEntryStatus.Draft,
            SequenceNumber: null, PostedAtUtc: null, PostedByUserId: null, ReversesEntryId: Guid.NewGuid());

        var act = () => Journal.Rehydrate(Currency.Usd, [corrupt]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rehydrate_rejects_a_posted_entry_missing_its_sequence_number()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var corrupt = new JournalEntrySnapshot(
            Guid.NewGuid(), EntryDate, "Corrupt", lines, JournalEntryStatus.Posted,
            SequenceNumber: null, PostedAtUtc: PostedAtUtc, PostedByUserId: PostedByUserId, ReversesEntryId: null);

        var act = () => Journal.Rehydrate(Currency.Usd, [corrupt]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rehydrate_rejects_a_posted_entry_missing_its_posted_stamps()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var corrupt = new JournalEntrySnapshot(
            Guid.NewGuid(), EntryDate, "Corrupt", lines, JournalEntryStatus.Posted,
            SequenceNumber: 1, PostedAtUtc: null, PostedByUserId: null, ReversesEntryId: null);

        var act = () => Journal.Rehydrate(Currency.Usd, [corrupt]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rehydrate_rejects_a_gap_in_the_sequence_numbers()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };

        var act = () => Journal.Rehydrate(
            Currency.Usd, [APostedSnapshot(Guid.NewGuid(), 1, lines), APostedSnapshot(Guid.NewGuid(), 3, lines)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rehydrate_rejects_duplicate_sequence_numbers()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };

        var act = () => Journal.Rehydrate(
            Currency.Usd, [APostedSnapshot(Guid.NewGuid(), 1, lines), APostedSnapshot(Guid.NewGuid(), 1, lines)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rehydrate_rejects_a_reversal_that_points_outside_the_set()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };

        var act = () => Journal.Rehydrate(
            Currency.Usd, [APostedSnapshot(Guid.NewGuid(), 1, lines, reversesEntryId: Guid.NewGuid())]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rehydrate_rejects_a_reversal_that_points_to_a_draft()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var draftId = Guid.NewGuid();

        var act = () => Journal.Rehydrate(
            Currency.Usd,
            [ADraftSnapshot(draftId, lines), APostedSnapshot(Guid.NewGuid(), 1, lines, reversesEntryId: draftId)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rehydrate_rejects_two_entries_claiming_to_reverse_the_same_original()
    {
        var (_, checking, salary) = AChart();
        var lines = new[] { ADebit(checking.Id, 10m), ACredit(salary.Id, 10m) };
        var originalId = Guid.NewGuid();

        var act = () => Journal.Rehydrate(
            Currency.Usd,
            [
                APostedSnapshot(originalId, 1, lines),
                APostedSnapshot(Guid.NewGuid(), 2, lines, reversesEntryId: originalId),
                APostedSnapshot(Guid.NewGuid(), 3, lines, reversesEntryId: originalId),
            ]);

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- Hash chain (spec §15.3) ------------------------------------------

    [Fact]
    public void Post_leaves_entry_hash_null_without_a_chain_key()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);

        var posted = journal.Post(id, chart, PostedAtUtc, PostedByUserId).Value;

        posted.EntryHash.Should().BeNull();
    }

    [Fact]
    public void Post_computes_an_entry_hash_when_constructed_with_a_chain_key()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd, "chain-key"u8.ToArray());
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);

        var posted = journal.Post(id, chart, PostedAtUtc, PostedByUserId).Value;

        posted.EntryHash.Should().NotBeNull();
        posted.EntryHash.Should().HaveCount(32);
    }

    [Fact]
    public void Reverse_computes_a_chained_entry_hash()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd, "chain-key"u8.ToArray());
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        var original = journal.Post(id, chart, PostedAtUtc, PostedByUserId).Value;

        var reversal = journal.Reverse(id, Guid.NewGuid(), chart, EntryDate, "Reversal", PostedAtUtc, PostedByUserId).Value;

        reversal.EntryHash.Should().NotBeNull();
        reversal.EntryHash.Should().NotEqual(original.EntryHash);
    }

    [Fact]
    public void An_entrys_hash_depends_on_the_previous_entrys_hash_not_just_its_own_content()
    {
        // Isolates the actual chaining property: two journals whose first
        // entries differ (so their hashes differ), then the exact same
        // second entry (same id, lines, accounts, sequence number) posted
        // into each. If the second entry's hash still differs between the
        // two journals, that difference can only come from _headHash -
        // proof this is a real chain, not independent per-entry hashing.
        var chainKey = "chain-key"u8.ToArray();
        var checkingId = Guid.NewGuid();
        var salaryId = Guid.NewGuid();

        ChartOfAccounts AChartWithFixedIds()
        {
            var chart = ChartOfAccounts.Empty();
            chart.AddRoot(checkingId, "Checking", AccountType.Asset);
            chart.AddRoot(salaryId, "Salary", AccountType.Income);
            return chart;
        }

        var chartA = AChartWithFixedIds();
        var journalA = Journal.Empty(Currency.Usd, chainKey);
        var firstIdA = Guid.NewGuid();
        journalA.CreateDraft(firstIdA, EntryDate, "First A", [ADebit(checkingId, 10m), ACredit(salaryId, 10m)]);
        journalA.Post(firstIdA, chartA, PostedAtUtc, PostedByUserId);

        var chartB = AChartWithFixedIds();
        var journalB = Journal.Empty(Currency.Usd, chainKey);
        var firstIdB = Guid.NewGuid();
        journalB.CreateDraft(firstIdB, EntryDate, "First B", [ADebit(checkingId, 20m), ACredit(salaryId, 20m)]);
        journalB.Post(firstIdB, chartB, PostedAtUtc, PostedByUserId);

        var secondId = Guid.NewGuid();
        journalA.CreateDraft(secondId, EntryDate, "Second", [ADebit(checkingId, 5m), ACredit(salaryId, 5m)]);
        var secondEntryA = journalA.Post(secondId, chartA, PostedAtUtc, PostedByUserId).Value;

        journalB.CreateDraft(secondId, EntryDate, "Second", [ADebit(checkingId, 5m), ACredit(salaryId, 5m)]);
        var secondEntryB = journalB.Post(secondId, chartB, PostedAtUtc, PostedByUserId).Value;

        secondEntryA.EntryHash.Should().NotEqual(secondEntryB.EntryHash);
    }

    [Fact]
    public void Rehydrate_seeds_the_head_hash_so_a_newly_posted_entry_continues_the_same_chain()
    {
        var chainKey = "chain-key"u8.ToArray();
        var (chart, checking, salary) = AChart();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        // Continuous: both entries posted in one journal, no reload.
        var continuous = Journal.Empty(Currency.Usd, chainKey);
        continuous.CreateDraft(firstId, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        continuous.Post(firstId, chart, PostedAtUtc, PostedByUserId);
        continuous.CreateDraft(secondId, EntryDate, "Rent", [ADebit(checking.Id, 50m), ACredit(salary.Id, 50m)]);
        var continuousSecond = continuous.Post(secondId, chart, PostedAtUtc, PostedByUserId).Value;

        // Reloaded: post the first entry, capture it as a snapshot exactly
        // as a store would, rehydrate fresh, then post the second entry.
        var firstSession = Journal.Empty(Currency.Usd, chainKey);
        firstSession.CreateDraft(firstId, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        var firstEntry = firstSession.Post(firstId, chart, PostedAtUtc, PostedByUserId).Value;
        var snapshot = new JournalEntrySnapshot(
            firstEntry.Id, firstEntry.EntryDate, firstEntry.Description, firstEntry.Lines, firstEntry.Status,
            firstEntry.SequenceNumber, firstEntry.PostedAtUtc, firstEntry.PostedByUserId, firstEntry.ReversesEntryId,
            firstEntry.SchemaVersion, firstEntry.References, firstEntry.EntryHash);

        var reloaded = Journal.Rehydrate(Currency.Usd, [snapshot], chainKey);
        reloaded.CreateDraft(secondId, EntryDate, "Rent", [ADebit(checking.Id, 50m), ACredit(salary.Id, 50m)]);
        var reloadedSecond = reloaded.Post(secondId, chart, PostedAtUtc, PostedByUserId).Value;

        reloadedSecond.EntryHash.Should().Equal(continuousSecond.EntryHash);
    }

    [Fact]
    public void VerifyChain_reports_intact_for_a_healthy_chained_journal()
    {
        var chainKey = "chain-key"u8.ToArray();
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd, chainKey);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        journal.CreateDraft(firstId, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(firstId, chart, PostedAtUtc, PostedByUserId);
        journal.CreateDraft(secondId, EntryDate, "Rent", [ADebit(checking.Id, 50m), ACredit(salary.Id, 50m)]);
        journal.Post(secondId, chart, PostedAtUtc, PostedByUserId);

        var result = journal.VerifyChain();

        result.Status.Should().Be(ChainVerificationStatus.Intact);
        result.FirstAffectedEntryId.Should().BeNull();
        result.FirstAffectedSequenceNumber.Should().BeNull();
    }

    [Fact]
    public void VerifyChain_reports_no_chain_key_configured_without_one()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);

        var result = journal.VerifyChain();

        result.Status.Should().Be(ChainVerificationStatus.NoChainKeyConfigured);
    }

    [Fact]
    public void VerifyChain_reports_chain_not_fully_populated_when_a_posted_entry_has_no_stored_hash()
    {
        // A journal rehydrated from history that predates chaining being
        // enabled: posted, but no EntryHash was ever stored for it.
        var chainKey = "chain-key"u8.ToArray();
        var (_, checking, salary) = AChart();
        var id = Guid.NewGuid();
        var snapshot = new JournalEntrySnapshot(
            id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)],
            JournalEntryStatus.Posted, 1, PostedAtUtc, PostedByUserId, null);
        var journal = Journal.Rehydrate(Currency.Usd, [snapshot], chainKey);

        var result = journal.VerifyChain();

        result.Status.Should().Be(ChainVerificationStatus.ChainNotFullyPopulated);
        result.FirstAffectedEntryId.Should().Be(id);
        result.FirstAffectedSequenceNumber.Should().Be(1);
    }

    [Fact]
    public void VerifyChain_reports_tampered_when_a_stored_hash_does_not_match_its_recomputed_value()
    {
        var chainKey = "chain-key"u8.ToArray();
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd, chainKey);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        var posted = journal.Post(id, chart, PostedAtUtc, PostedByUserId).Value;

        // Same technique as the append-only guard's tampered-snapshot tests:
        // construct a snapshot with a deliberately wrong stored hash, as if
        // the row had been altered on disk after posting.
        var tamperedHash = new byte[32];
        Array.Fill(tamperedHash, (byte)0xFF);
        var tamperedSnapshot = new JournalEntrySnapshot(
            posted.Id, posted.EntryDate, posted.Description, posted.Lines, posted.Status,
            posted.SequenceNumber, posted.PostedAtUtc, posted.PostedByUserId, posted.ReversesEntryId,
            posted.SchemaVersion, posted.References, tamperedHash);
        var reloaded = Journal.Rehydrate(Currency.Usd, [tamperedSnapshot], chainKey);

        var result = reloaded.VerifyChain();

        result.Status.Should().Be(ChainVerificationStatus.Tampered);
        result.FirstAffectedEntryId.Should().Be(posted.Id);
        result.FirstAffectedSequenceNumber.Should().Be(posted.SequenceNumber);
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