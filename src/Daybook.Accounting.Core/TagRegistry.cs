namespace Daybook.Accounting.Core;

/// <summary>
/// The per-book tag collection (spec §4.8) — where the one invariant a lone
/// <see cref="Tag"/> cannot check by itself lives: unique <c>Name</c>,
/// case-insensitive, trimmed. A separate aggregate from
/// <see cref="ChartOfAccounts"/>, not nested inside it — tags are referenced
/// by accounts today and will be referenced by journal lines later, so their
/// lifecycle can't be coupled to the chart's.
/// </summary>
public sealed class TagRegistry
{
    private readonly Dictionary<Guid, Tag> _byId = [];
    private readonly Dictionary<string, Guid> _byName = new(StringComparer.OrdinalIgnoreCase);

    private TagRegistry()
    {
    }

    public static TagRegistry Empty() => new();

    public IReadOnlyCollection<Tag> Tags => _byId.Values;

    public Tag? Find(Guid id) => _byId.GetValueOrDefault(id);

    public Tag? FindByName(string name) => _byName.TryGetValue(name, out var id) ? _byId[id] : null;

    public Result<Tag> Create(Guid id, string name)
    {
        if (_byId.ContainsKey(id))
        {
            return DuplicateId(id);
        }

        var tagResult = Tag.Create(id, name);
        if (tagResult.IsFailure)
        {
            return tagResult.Error;
        }

        var tag = tagResult.Value;
        if (_byName.ContainsKey(tag.Name))
        {
            return DuplicateName(tag.Name);
        }

        _byId[id] = tag;
        _byName[tag.Name] = id;
        return tag;
    }

    public Result<Tag> Rename(Guid id, string name)
    {
        var tag = Find(id);
        if (tag is null)
        {
            return TagNotFound(id);
        }

        var result = tag.Rename(name);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var renamed = result.Value;
        if (_byName.TryGetValue(renamed.Name, out var owner) && owner != id)
        {
            return DuplicateName(renamed.Name);
        }

        _byName.Remove(tag.Name);
        _byName[renamed.Name] = id;
        _byId[id] = renamed;
        return renamed;
    }

    public Result<Tag> Archive(Guid id) => Mutate(id, t => t.Archive());

    public Result<Tag> Unarchive(Guid id) => Mutate(id, t => t.Unarchive());

    private Result<Tag> Mutate(Guid id, Func<Tag, Tag> mutate)
    {
        var tag = Find(id);
        if (tag is null)
        {
            return TagNotFound(id);
        }

        var updated = mutate(tag);
        _byId[id] = updated;
        return updated;
    }

    private static Error TagNotFound(Guid id) => new(
        "tag.not_found",
        ErrorCategory.Validation,
        $"No tag with id '{id}' exists in this registry.",
        ["Check the tag id, or create the tag first."]);

    private static Error DuplicateId(Guid id) => new(
        "tag.id.duplicate",
        ErrorCategory.Conflict,
        $"A tag with id '{id}' already exists in this registry.",
        ["Generate a new TagId; ids must be unique."]);

    private static Error DuplicateName(string name) => new(
        "tag.name.duplicate",
        ErrorCategory.Conflict,
        $"Tag name '{name}' is already in use in this registry (case-insensitive).",
        ["Choose a different Name."]);
}
