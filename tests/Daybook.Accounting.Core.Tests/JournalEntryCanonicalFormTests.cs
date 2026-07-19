namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// <see cref="JournalEntryCanonicalForm"/> — a deterministic byte
/// representation of a <see cref="JournalEntry"/>'s frozen-at-post content,
/// for the HMAC hash chain (spec §15.3). Deliberately excludes anything
/// tracked in <c>LineTagLedger</c>/<c>ReconciliationLedger</c> (mutable
/// after post, never accounting truth) and <see cref="JournalEntry.EntryHash"/>
/// itself (can't hash something that includes its own hash).
/// </summary>
public class JournalEntryCanonicalFormTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly EntryDate = new(2026, 7, 15);
    private static readonly DateTimeOffset PostedAtUtc = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostedByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static JournalLine ALine(decimal amount = 100m, Side side = Side.Debit, string? memo = null) =>
        JournalLine.Create(AccountId, side, Money.Of(amount, Currency.Usd), memo).Value;

    private static JournalEntry ADraft(string description = "Groceries", decimal amount = 100m, Side side = Side.Debit, string? memo = null) =>
        JournalEntry.CreateDraft(Id, EntryDate, description, [ALine(amount, side, memo)]).Value;

    // MarkPosted is internal to Journal, so a posted entry for these
    // serialization tests comes via the public Rehydrate API instead -
    // same technique JournalTests.cs already uses for "tampered snapshot"
    // tests. Rehydrate demands a gapless sequence starting at 1, so
    // reaching a given sequenceNumber means filling in the gap with dummy
    // entries first.
    private static JournalEntry APostedEntry(int sequenceNumber = 1)
    {
        var snapshots = new List<JournalEntrySnapshot>();
        for (var seq = 1; seq < sequenceNumber; seq++)
        {
            snapshots.Add(new JournalEntrySnapshot(
                Guid.NewGuid(), EntryDate, "Filler", [ALine()], JournalEntryStatus.Posted, seq, PostedAtUtc, PostedByUserId, null));
        }

        snapshots.Add(new JournalEntrySnapshot(
            Id, EntryDate, "Groceries", [ALine()], JournalEntryStatus.Posted, sequenceNumber, PostedAtUtc, PostedByUserId, null));

        return Journal.Rehydrate(Currency.Usd, snapshots).Find(Id)!;
    }

    [Fact]
    public void Serialize_is_deterministic_for_the_same_entry()
    {
        var entry = ADraft();

        JournalEntryCanonicalForm.Serialize(entry).Should().Equal(JournalEntryCanonicalForm.Serialize(entry));
    }

    [Fact]
    public void Serialize_changes_when_the_description_changes()
    {
        var a = JournalEntryCanonicalForm.Serialize(ADraft(description: "Groceries"));
        var b = JournalEntryCanonicalForm.Serialize(ADraft(description: "Rent"));

        a.Should().NotEqual(b);
    }

    [Fact]
    public void Serialize_changes_when_a_lines_amount_changes()
    {
        var a = JournalEntryCanonicalForm.Serialize(ADraft(amount: 100m));
        var b = JournalEntryCanonicalForm.Serialize(ADraft(amount: 200m));

        a.Should().NotEqual(b);
    }

    [Fact]
    public void Serialize_changes_when_a_lines_side_changes()
    {
        var a = JournalEntryCanonicalForm.Serialize(ADraft(side: Side.Debit));
        var b = JournalEntryCanonicalForm.Serialize(ADraft(side: Side.Credit));

        a.Should().NotEqual(b);
    }

    [Fact]
    public void Serialize_changes_when_a_lines_memo_changes()
    {
        var a = JournalEntryCanonicalForm.Serialize(ADraft(memo: "Weekly shop"));
        var b = JournalEntryCanonicalForm.Serialize(ADraft(memo: null));

        a.Should().NotEqual(b);
    }

    [Fact]
    public void Serialize_changes_when_references_differ()
    {
        var withReference = ADraft().AddReference(Reference.Create(ReferenceType.Check, "1234").Value).Value;

        JournalEntryCanonicalForm.Serialize(withReference).Should().NotEqual(JournalEntryCanonicalForm.Serialize(ADraft()));
    }

    [Fact]
    public void Serialize_changes_once_posted()
    {
        JournalEntryCanonicalForm.Serialize(ADraft()).Should().NotEqual(JournalEntryCanonicalForm.Serialize(APostedEntry()));
    }

    [Fact]
    public void Serialize_changes_when_the_sequence_number_differs()
    {
        var first = JournalEntryCanonicalForm.Serialize(APostedEntry(sequenceNumber: 1));
        var second = JournalEntryCanonicalForm.Serialize(APostedEntry(sequenceNumber: 2));

        first.Should().NotEqual(second);
    }

    [Fact]
    public void Serialize_changes_when_reverses_entry_id_differs()
    {
        // Rehydrate requires ReversesEntryId to point at a real posted
        // entry in the same snapshot set, so the reversal case needs a
        // companion "original" snapshot alongside it.
        var originalId = Guid.NewGuid();
        var original = new JournalEntrySnapshot(
            originalId, EntryDate, "Groceries", [ALine()], JournalEntryStatus.Posted, 1, PostedAtUtc, PostedByUserId, null);
        var reversalSnapshot = new JournalEntrySnapshot(
            Id, EntryDate, "Groceries", [ALine()], JournalEntryStatus.Posted, 2, PostedAtUtc, PostedByUserId, originalId);
        var journal = Journal.Rehydrate(Currency.Usd, [original, reversalSnapshot]);

        var plain = JournalEntryCanonicalForm.Serialize(APostedEntry(sequenceNumber: 2));
        var reversal = JournalEntryCanonicalForm.Serialize(journal.Find(Id)!);

        plain.Should().NotEqual(reversal);
    }
}