namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Line-level tags (spec §4.4/§4.8), deferred out of the account-tags
/// milestone until <see cref="LineLocation"/> existed. Tracks explicit
/// tag-ids per line and derives <c>effective(line) = explicit line tags ∪
/// the line's account's effective tags</c> — the account-tree half of that
/// formula is already thoroughly proven by
/// <c>ChartOfAccountsTests.Property_effective_tags_is_always_a_superset_of_every_ancestors_own_tags</c>,
/// so this only needs direct tests proving the union with the line's own
/// explicit tags.
/// </summary>
public class LineTagLedgerTests
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

    private static (Journal Journal, Guid EntryId) APostedEntry(ChartOfAccounts chart, Account checking, Account salary)
    {
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);
        return (journal, id);
    }

    // ---- AddTag / RemoveTag ------------------------------------------------

    [Fact]
    public void TagsOf_an_untracked_location_is_empty()
    {
        var ledger = LineTagLedger.Empty();

        ledger.TagsOf(new LineLocation(Guid.NewGuid(), 0)).Should().BeEmpty();
    }

    [Fact]
    public void AddTag_updates_the_tag_set_of_a_real_posted_line()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;
        var ledger = LineTagLedger.Empty();
        var location = new LineLocation(entryId, 0);

        var result = ledger.AddTag(location, tag.Id, journal, registry);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(tag.Id);
        ledger.TagsOf(location).Should().Equal(tag.Id);
    }

    [Fact]
    public void AddTag_is_a_set_adding_the_same_tag_twice_does_not_duplicate()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;
        var ledger = LineTagLedger.Empty();
        var location = new LineLocation(entryId, 0);
        ledger.AddTag(location, tag.Id, journal, registry);

        var result = ledger.AddTag(location, tag.Id, journal, registry);

        result.Value.Should().Equal(tag.Id);
    }

    [Fact]
    public void AddTag_rejects_an_unknown_entry()
    {
        var journal = Journal.Empty(Currency.Usd);
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;
        var ledger = LineTagLedger.Empty();

        var result = ledger.AddTag(new LineLocation(Guid.NewGuid(), 0), tag.Id, journal, registry);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("line_tag.entry_not_found");
    }

    [Fact]
    public void AddTag_rejects_a_still_draft_entry()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;
        var ledger = LineTagLedger.Empty();

        var result = ledger.AddTag(new LineLocation(id, 0), tag.Id, journal, registry);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("line_tag.not_posted");
    }

    [Fact]
    public void AddTag_rejects_an_out_of_range_line_index()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;
        var ledger = LineTagLedger.Empty();

        var result = ledger.AddTag(new LineLocation(entryId, 5), tag.Id, journal, registry);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("line_tag.index_out_of_range");
    }

    [Fact]
    public void AddTag_rejects_a_tag_not_in_the_registry()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var registry = TagRegistry.Empty();
        var ledger = LineTagLedger.Empty();

        var result = ledger.AddTag(new LineLocation(entryId, 0), Guid.NewGuid(), journal, registry);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("line_tag.tag_not_found");
    }

    [Fact]
    public void RemoveTag_removes_from_the_tag_set()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;
        var ledger = LineTagLedger.Empty();
        var location = new LineLocation(entryId, 0);
        ledger.AddTag(location, tag.Id, journal, registry);

        var result = ledger.RemoveTag(location, tag.Id);

        result.Should().BeEmpty();
        ledger.TagsOf(location).Should().BeEmpty();
    }

    [Fact]
    public void RemoveTag_of_an_untracked_location_is_a_no_op()
    {
        var ledger = LineTagLedger.Empty();

        var result = ledger.RemoveTag(new LineLocation(Guid.NewGuid(), 0), Guid.NewGuid());

        result.Should().BeEmpty();
    }

    // ---- EffectiveTagsOf ----------------------------------------------------

    [Fact]
    public void EffectiveTagsOf_is_just_the_explicit_line_tags_when_the_account_has_none()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var registry = TagRegistry.Empty();
        var lineTag = registry.Create(Guid.NewGuid(), "Business").Value;
        var ledger = LineTagLedger.Empty();
        var location = new LineLocation(entryId, 0);
        ledger.AddTag(location, lineTag.Id, journal, registry);

        var result = ledger.EffectiveTagsOf(location, journal, chart);

        result.Value.Should().Equal(lineTag.Id);
    }

    [Fact]
    public void EffectiveTagsOf_is_just_the_accounts_effective_tags_when_the_line_has_none_explicit()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var registry = TagRegistry.Empty();
        var accountTag = registry.Create(Guid.NewGuid(), "Personal").Value;
        chart.AddTag(checking.Id, accountTag.Id, registry);
        var ledger = LineTagLedger.Empty();

        var result = ledger.EffectiveTagsOf(new LineLocation(entryId, 0), journal, chart);

        result.Value.Should().Equal(accountTag.Id);
    }

    [Fact]
    public void EffectiveTagsOf_is_the_union_of_explicit_line_tags_and_the_accounts_effective_tags()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var registry = TagRegistry.Empty();
        var lineTag = registry.Create(Guid.NewGuid(), "Business").Value;
        var accountTag = registry.Create(Guid.NewGuid(), "Personal").Value;
        chart.AddTag(checking.Id, accountTag.Id, registry);
        var ledger = LineTagLedger.Empty();
        var location = new LineLocation(entryId, 0);
        ledger.AddTag(location, lineTag.Id, journal, registry);

        var result = ledger.EffectiveTagsOf(location, journal, chart);

        result.Value.Should().BeEquivalentTo([lineTag.Id, accountTag.Id]);
    }

    [Fact]
    public void EffectiveTagsOf_dedupes_a_tag_shared_by_the_line_and_the_account()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var registry = TagRegistry.Empty();
        var sharedTag = registry.Create(Guid.NewGuid(), "Shared").Value;
        chart.AddTag(checking.Id, sharedTag.Id, registry);
        var ledger = LineTagLedger.Empty();
        var location = new LineLocation(entryId, 0);
        ledger.AddTag(location, sharedTag.Id, journal, registry);

        var result = ledger.EffectiveTagsOf(location, journal, chart);

        result.Value.Should().Equal(sharedTag.Id);
    }

    [Fact]
    public void EffectiveTagsOf_rejects_an_unresolvable_location()
    {
        var (chart, checking, salary) = AChart();
        var (journal, _) = APostedEntry(chart, checking, salary);
        var ledger = LineTagLedger.Empty();

        var result = ledger.EffectiveTagsOf(new LineLocation(Guid.NewGuid(), 0), journal, chart);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("line_tag.entry_not_found");
    }

    // ---- AddTagToAllLines ---------------------------------------------------

    [Fact]
    public void AddTagToAllLines_tags_every_line_in_the_entry()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;
        var ledger = LineTagLedger.Empty();

        var result = ledger.AddTagToAllLines(entryId, tag.Id, journal, registry);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo([new LineLocation(entryId, 0), new LineLocation(entryId, 1)]);
        ledger.TagsOf(new LineLocation(entryId, 0)).Should().Equal(tag.Id);
        ledger.TagsOf(new LineLocation(entryId, 1)).Should().Equal(tag.Id);
    }

    [Fact]
    public void AddTagToAllLines_rejects_an_unknown_entry_before_tagging_anything()
    {
        var journal = Journal.Empty(Currency.Usd);
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;
        var ledger = LineTagLedger.Empty();

        var result = ledger.AddTagToAllLines(Guid.NewGuid(), tag.Id, journal, registry);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("line_tag.entry_not_found");
    }

    [Fact]
    public void AddTagToAllLines_rejects_a_tag_not_in_the_registry_before_tagging_anything()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var registry = TagRegistry.Empty();
        var ledger = LineTagLedger.Empty();

        var result = ledger.AddTagToAllLines(entryId, Guid.NewGuid(), journal, registry);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("line_tag.tag_not_found");
        ledger.TagsOf(new LineLocation(entryId, 0)).Should().BeEmpty();
    }
}