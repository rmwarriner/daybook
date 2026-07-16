namespace Daybook.Accounting.Core;

/// <summary>
/// A daybook record (spec §4.3): a header plus lines, moving from
/// <see cref="JournalEntryStatus.Draft"/> (freely editable) to
/// <see cref="JournalEntryStatus.Posted"/> (immutable, per golden rule 2).
/// An entity — identity equality by <see cref="Id"/>, not structural.
/// </summary>
/// <remarks>
/// This type enforces only what it can check on its own: a required
/// <see cref="Description"/>, and that a posted entry has no mutators. The
/// full §5 posting checklist (balance, line count, account existence and
/// activity, currency) needs the rest of the journal and the chart of
/// accounts, so it is enforced by <see cref="Journal"/> — the one guarded
/// write door — at post time.
/// </remarks>
public sealed class JournalEntry : IEquatable<JournalEntry>
{
    public Guid Id { get; }

    public DateOnly EntryDate { get; }

    /// <summary>Narrative memo. Frozen at post — corrected by reversal, not edit.</summary>
    public string Description { get; }

    public IReadOnlyList<JournalLine> Lines { get; }

    public JournalEntryStatus Status { get; }

    /// <summary>Gapless, per-book, assigned at post time. Null until posted.</summary>
    public int? SequenceNumber { get; }

    public DateTimeOffset? PostedAtUtc { get; }

    public Guid? PostedByUserId { get; }

    private JournalEntry(
        Guid id,
        DateOnly entryDate,
        string description,
        IReadOnlyList<JournalLine> lines,
        JournalEntryStatus status,
        int? sequenceNumber,
        DateTimeOffset? postedAtUtc,
        Guid? postedByUserId)
    {
        Id = id;
        EntryDate = entryDate;
        Description = description;
        Lines = lines;
        Status = status;
        SequenceNumber = sequenceNumber;
        PostedAtUtc = postedAtUtc;
        PostedByUserId = postedByUserId;
    }

    /// <summary>
    /// Creates a new draft. A draft may have fewer than two lines, or lines
    /// that don't yet balance — the §5 posting checklist only applies at
    /// post time, so a draft can be legitimately incomplete while edited.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="id"/> is <see cref="Guid.Empty"/> — a caller bug, not a business rule.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> or <paramref name="lines"/> is null — a caller bug, not a business rule.</exception>
    public static Result<JournalEntry> CreateDraft(
        Guid id,
        DateOnly entryDate,
        string description,
        IReadOnlyList<JournalLine> lines)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Entry id must not be Guid.Empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(lines);

        var descriptionResult = ValidateDescription(description);
        if (descriptionResult.IsFailure)
        {
            return descriptionResult.Error;
        }

        return new JournalEntry(
            id,
            entryDate,
            descriptionResult.Value,
            lines.ToList(),
            JournalEntryStatus.Draft,
            sequenceNumber: null,
            postedAtUtc: null,
            postedByUserId: null);
    }

    /// <summary>Replaces the date, description, and lines of a draft. Fails once the entry is posted.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> or <paramref name="lines"/> is null.</exception>
    public Result<JournalEntry> UpdateDraft(DateOnly entryDate, string description, IReadOnlyList<JournalLine> lines)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(lines);

        if (Status != JournalEntryStatus.Draft)
        {
            return PostedImmutable();
        }

        var descriptionResult = ValidateDescription(description);
        if (descriptionResult.IsFailure)
        {
            return descriptionResult.Error;
        }

        return new JournalEntry(
            Id,
            entryDate,
            descriptionResult.Value,
            lines.ToList(),
            Status,
            SequenceNumber,
            PostedAtUtc,
            PostedByUserId);
    }

    /// <summary>
    /// Raw transition to Posted with no §5 validation. Internal because
    /// that validation needs the chart of accounts and the journal's
    /// sequence counter; only <see cref="Journal"/> may call this, after
    /// checking those.
    /// </summary>
    internal JournalEntry MarkPosted(int sequenceNumber, DateTimeOffset postedAtUtc, Guid postedByUserId) =>
        new(Id, EntryDate, Description, Lines, JournalEntryStatus.Posted, sequenceNumber, postedAtUtc, postedByUserId);

    internal static Error PostedImmutable() => new(
        "entry.posted.immutable",
        ErrorCategory.BusinessRule,
        "A posted entry cannot be edited; corrections happen by posting a reversing entry.",
        ["Post a reversing entry, then post a fresh corrected entry."]);

    private static Result<string> ValidateDescription(string description)
    {
        var trimmed = description.Trim();
        if (trimmed.Length == 0)
        {
            return new Error(
                "entry.description.required",
                ErrorCategory.Validation,
                "Entry description must not be empty.",
                ["Provide a non-empty Description."]);
        }

        return trimmed;
    }

    public bool Equals(JournalEntry? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as JournalEntry);

    public override int GetHashCode() => Id.GetHashCode();
}