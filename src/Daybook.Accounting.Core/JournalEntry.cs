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
    /// <summary>
    /// The journal-entry wire/disk-format version this entry was originally
    /// written under (golden rule 4). Stamped once, at <see cref="CreateDraft"/>
    /// time, and carried forward unchanged by every other operation — an
    /// entry written under schema v1 stays v1 forever, even across edits,
    /// posting, or reload.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

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

    /// <summary>
    /// Non-null when this entry is itself a reversal. Set once, at
    /// creation, like any other field — unlike the reverse direction
    /// ("what reversed this entry"), which isn't stored here at all; that
    /// would mean mutating an already-posted entry. <see cref="Journal"/>
    /// tracks that separately, as a link the original entry never carries.
    /// </summary>
    public Guid? ReversesEntryId { get; }

    /// <summary>See <see cref="CurrentSchemaVersion"/>.</summary>
    public int SchemaVersion { get; }

    private JournalEntry(
        Guid id,
        DateOnly entryDate,
        string description,
        IReadOnlyList<JournalLine> lines,
        JournalEntryStatus status,
        int? sequenceNumber,
        DateTimeOffset? postedAtUtc,
        Guid? postedByUserId,
        Guid? reversesEntryId,
        int schemaVersion)
    {
        Id = id;
        EntryDate = entryDate;
        Description = description;
        Lines = lines;
        Status = status;
        SequenceNumber = sequenceNumber;
        PostedAtUtc = postedAtUtc;
        PostedByUserId = postedByUserId;
        ReversesEntryId = reversesEntryId;
        SchemaVersion = schemaVersion;
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
        IReadOnlyList<JournalLine> lines) =>
        CreateDraft(id, entryDate, description, lines, CurrentSchemaVersion);

    /// <summary>
    /// Same as the public <see cref="CreateDraft(Guid,DateOnly,string,IReadOnlyList{JournalLine})"/>,
    /// but lets <see cref="Journal.Rehydrate"/> restore a snapshot's exact
    /// originally-stamped <paramref name="schemaVersion"/> instead of
    /// stamping today's <see cref="CurrentSchemaVersion"/>. Internal so
    /// nothing outside Core can inject an arbitrary version.
    /// </summary>
    internal static Result<JournalEntry> CreateDraft(
        Guid id,
        DateOnly entryDate,
        string description,
        IReadOnlyList<JournalLine> lines,
        int schemaVersion)
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
            postedByUserId: null,
            reversesEntryId: null,
            schemaVersion);
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
            PostedByUserId,
            ReversesEntryId,
            SchemaVersion);
    }

    /// <summary>
    /// Raw transition to Posted with no §5 validation. Internal because
    /// that validation needs the chart of accounts and the journal's
    /// sequence counter; only <see cref="Journal"/> may call this, after
    /// checking those. <paramref name="reversesEntryId"/> is set when this
    /// transition is posting a reversal, per <see cref="ReversesEntryId"/>.
    /// </summary>
    internal JournalEntry MarkPosted(
        int sequenceNumber,
        DateTimeOffset postedAtUtc,
        Guid postedByUserId,
        Guid? reversesEntryId = null) =>
        new(Id, EntryDate, Description, Lines, JournalEntryStatus.Posted, sequenceNumber, postedAtUtc, postedByUserId, reversesEntryId, SchemaVersion);

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