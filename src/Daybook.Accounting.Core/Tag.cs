namespace Daybook.Accounting.Core;

/// <summary>
/// A per-book cross-cutting classification (spec §4.8) — a second reporting
/// axis alongside accounts (a trip, a project, "tax-deductible"). An entity,
/// not a value object — referenced by id from accounts (and, later, journal
/// lines) precisely so renaming/archiving never disturbs those references.
/// </summary>
/// <remarks>
/// This type enforces only what it can check on its own: a non-blank
/// <see cref="Name"/>. Uniqueness of <see cref="Name"/> per book needs the
/// rest of the collection, so that's enforced by <see cref="TagRegistry"/>.
/// </remarks>
public sealed class Tag : IEquatable<Tag>
{
    public Guid Id { get; }

    public string Name { get; }

    /// <summary>Archived tags stay referenceable by history but shouldn't be offered for new assignment.</summary>
    public bool IsArchived { get; }

    private Tag(Guid id, string name, bool isArchived)
    {
        Id = id;
        Name = name;
        IsArchived = isArchived;
    }

    /// <exception cref="ArgumentException"><paramref name="id"/> is <see cref="Guid.Empty"/> — a caller bug, not a business rule.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null — a caller bug, not a business rule.</exception>
    public static Result<Tag> Create(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tag id must not be Guid.Empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(name);

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
        {
            return nameResult.Error;
        }

        return new Tag(id, nameResult.Value, isArchived: false);
    }

    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public Result<Tag> Rename(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
        {
            return nameResult.Error;
        }

        return new Tag(Id, nameResult.Value, IsArchived);
    }

    public Tag Archive() => new(Id, Name, isArchived: true);

    public Tag Unarchive() => new(Id, Name, isArchived: false);

    private static Result<string> ValidateName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return new Error(
                "tag.name.required",
                ErrorCategory.Validation,
                "Tag name must not be empty.",
                ["Provide a non-empty Name."]);
        }

        return trimmed;
    }

    public bool Equals(Tag? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as Tag);

    public override int GetHashCode() => Id.GetHashCode();
}