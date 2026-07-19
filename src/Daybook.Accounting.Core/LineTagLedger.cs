namespace Daybook.Accounting.Core;

/// <summary>
/// Tracks explicit tag-ids per line (spec §4.4/§4.8) and derives
/// <c>effective(line) = explicit line tags ∪ the line's account's effective
/// tags</c>. A new, independent Core aggregate, same tier as
/// <see cref="ReconciliationLedger"/>/<see cref="TagRegistry"/>/
/// <see cref="Journal"/>/<see cref="ChartOfAccounts"/> — not nested inside
/// any of them.
/// </summary>
/// <remarks>
/// Line tagging is **posted-only**: <see cref="LineLocation"/> addressing
/// is only stable once an entry is posted (its own remarks say so) — a
/// draft's lines can be freely reordered/replaced by
/// <see cref="Journal.UpdateDraft"/>, so tagging a draft line by position
/// would silently point at a different line after the next edit.
/// </remarks>
public sealed class LineTagLedger
{
    private readonly Dictionary<LineLocation, HashSet<Guid>> _tagsByLine = [];

    private LineTagLedger()
    {
    }

    public static LineTagLedger Empty() => new();

    /// <summary>Explicit tags only, defaulting to empty for anything never tagged.</summary>
    public IReadOnlySet<Guid> TagsOf(LineLocation location) =>
        _tagsByLine.TryGetValue(location, out var tags) ? tags : new HashSet<Guid>();

    /// <summary>Tags a posted line. Validates the location resolves and that the tag exists in <paramref name="registry"/>.</summary>
    public Result<IReadOnlySet<Guid>> AddTag(LineLocation location, Guid tagId, Journal journal, TagRegistry registry)
    {
        var lineResult = ResolveLine(journal, location);
        if (lineResult.IsFailure)
        {
            return lineResult.Error;
        }

        if (registry.Find(tagId) is null)
        {
            return TagNotFound(tagId);
        }

        if (!_tagsByLine.TryGetValue(location, out var tags))
        {
            tags = [];
            _tagsByLine[location] = tags;
        }

        tags.Add(tagId);
        return tags;
    }

    /// <summary>Untags a line. A no-op if the tag/line was never tracked.</summary>
    public IReadOnlySet<Guid> RemoveTag(LineLocation location, Guid tagId)
    {
        if (_tagsByLine.TryGetValue(location, out var tags))
        {
            tags.Remove(tagId);
        }

        return TagsOf(location);
    }

    /// <summary>The spec §4.8 formula: <see cref="TagsOf"/> unioned with the line's account's effective tags.</summary>
    public Result<IReadOnlySet<Guid>> EffectiveTagsOf(LineLocation location, Journal journal, ChartOfAccounts chart)
    {
        var lineResult = ResolveLine(journal, location);
        if (lineResult.IsFailure)
        {
            return lineResult.Error;
        }

        var accountTagsResult = chart.EffectiveTagsOf(lineResult.Value.AccountId);
        if (accountTagsResult.IsFailure)
        {
            return accountTagsResult.Error;
        }

        var combined = new HashSet<Guid>(TagsOf(location));
        combined.UnionWith(accountTagsResult.Value);
        return combined;
    }

    /// <summary>
    /// Spec's "apply to all lines" convenience for the common case.
    /// Validates the entry exists/is posted and the tag exists before
    /// tagging anything.
    /// </summary>
    public Result<IReadOnlyList<LineLocation>> AddTagToAllLines(Guid entryId, Guid tagId, Journal journal, TagRegistry registry)
    {
        var entry = journal.Find(entryId);
        if (entry is null)
        {
            return EntryNotFound(entryId);
        }

        if (entry.Status != JournalEntryStatus.Posted)
        {
            return NotPosted(entryId);
        }

        if (registry.Find(tagId) is null)
        {
            return TagNotFound(tagId);
        }

        var locations = new List<LineLocation>();
        for (var i = 0; i < entry.Lines.Count; i++)
        {
            var location = new LineLocation(entryId, i);
            AddTag(location, tagId, journal, registry);
            locations.Add(location);
        }

        return locations;
    }

    private static Result<JournalLine> ResolveLine(Journal journal, LineLocation location)
    {
        var entry = journal.Find(location.EntryId);
        if (entry is null)
        {
            return EntryNotFound(location.EntryId);
        }

        if (entry.Status != JournalEntryStatus.Posted)
        {
            return NotPosted(location.EntryId);
        }

        if (location.LineIndex < 0 || location.LineIndex >= entry.Lines.Count)
        {
            return IndexOutOfRange(location);
        }

        return entry.Lines[location.LineIndex];
    }

    private static Error EntryNotFound(Guid entryId) => new(
        "line_tag.entry_not_found",
        ErrorCategory.Validation,
        $"No journal entry with id '{entryId}' exists.",
        ["Check the EntryId."]);

    private static Error NotPosted(Guid entryId) => new(
        "line_tag.not_posted",
        ErrorCategory.BusinessRule,
        $"Entry '{entryId}' is not posted; only posted lines can be tagged.",
        ["Post the entry first."]);

    private static Error IndexOutOfRange(LineLocation location) => new(
        "line_tag.index_out_of_range",
        ErrorCategory.Validation,
        $"Entry '{location.EntryId}' has no line at index {location.LineIndex}.",
        ["Check the LineIndex."]);

    private static Error TagNotFound(Guid tagId) => new(
        "line_tag.tag_not_found",
        ErrorCategory.Validation,
        $"No tag with id '{tagId}' exists in the given registry.",
        ["Check the tag id, or create the tag first."]);
}