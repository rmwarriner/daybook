namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 3 — the <see cref="JournalEntry"/> entity's own local
/// invariants (spec §4.3/§4.7): a required <c>Description</c>, and a Draft
/// that can be freely replaced. What happens once an entry is
/// <em>posted</em> — immutability, sequence assignment, and the full §5
/// posting checklist — can only happen through <see cref="Journal"/>, the
/// one guarded write door, so those behaviors live in
/// <c>JournalTests</c> instead.
/// </summary>
public class JournalEntryTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly EntryDate = new(2026, 7, 15);

    private static JournalLine ALine() =>
        JournalLine.Create(AccountId, Side.Debit, Money.Of(100m, Currency.Usd)).Value;

    [Fact]
    public void CreateDraft_with_valid_fields_succeeds()
    {
        var lines = new[] { ALine() };

        var result = JournalEntry.CreateDraft(Id, EntryDate, "Groceries", lines);

        result.IsSuccess.Should().BeTrue();
        var entry = result.Value;
        entry.Id.Should().Be(Id);
        entry.EntryDate.Should().Be(EntryDate);
        entry.Description.Should().Be("Groceries");
        entry.Lines.Should().Equal(lines);
        entry.Status.Should().Be(JournalEntryStatus.Draft);
        entry.SequenceNumber.Should().BeNull();
        entry.PostedAtUtc.Should().BeNull();
        entry.PostedByUserId.Should().BeNull();
    }

    [Fact]
    public void CreateDraft_allows_fewer_than_two_lines_a_draft_may_be_incomplete()
    {
        JournalEntry.CreateDraft(Id, EntryDate, "Half-entered", []).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CreateDraft_trims_description()
    {
        var entry = JournalEntry.CreateDraft(Id, EntryDate, "  Groceries  ", []).Value;

        entry.Description.Should().Be("Groceries");
    }

    [Fact]
    public void CreateDraft_defensively_copies_lines()
    {
        var lines = new List<JournalLine> { ALine() };
        var entry = JournalEntry.CreateDraft(Id, EntryDate, "Groceries", lines).Value;

        lines.Add(ALine());

        entry.Lines.Should().HaveCount(1);
    }

    [Fact]
    public void CreateDraft_rejects_empty_id()
    {
        var act = () => JournalEntry.CreateDraft(Guid.Empty, EntryDate, "Groceries", []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateDraft_rejects_null_description()
    {
        var act = () => JournalEntry.CreateDraft(Id, EntryDate, null!, []);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CreateDraft_rejects_null_lines()
    {
        var act = () => JournalEntry.CreateDraft(Id, EntryDate, "Groceries", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDraft_rejects_blank_description(string blank)
    {
        var result = JournalEntry.CreateDraft(Id, EntryDate, blank, []);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.description.required");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
    }

    // ---- UpdateDraft (while still a Draft) ---------------------------------

    [Fact]
    public void UpdateDraft_replaces_date_description_and_lines()
    {
        var entry = JournalEntry.CreateDraft(Id, EntryDate, "Groceries", []).Value;
        var newDate = new DateOnly(2026, 7, 16);
        var newLines = new[] { ALine() };

        var updated = entry.UpdateDraft(newDate, "Corrected groceries", newLines).Value;

        updated.EntryDate.Should().Be(newDate);
        updated.Description.Should().Be("Corrected groceries");
        updated.Lines.Should().Equal(newLines);
        updated.Id.Should().Be(entry.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateDraft_rejects_blank_description(string blank)
    {
        var entry = JournalEntry.CreateDraft(Id, EntryDate, "Groceries", []).Value;

        var result = entry.UpdateDraft(EntryDate, blank, []);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("entry.description.required");
    }

    // ---- Identity equality (entity, not value object) ----------------------

    [Fact]
    public void Entries_with_the_same_id_are_equal_even_if_other_fields_differ()
    {
        var original = JournalEntry.CreateDraft(Id, EntryDate, "Groceries", []).Value;
        var updated = original.UpdateDraft(EntryDate, "Corrected groceries", []).Value;

        updated.Should().Be(original);
    }
}