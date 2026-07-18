namespace Daybook.Accounting.Core;

/// <summary>
/// A chart-of-accounts entry (spec §4.2). An entity, not a value object —
/// two <see cref="Account"/> instances are the same account when their
/// <see cref="Id"/> matches, regardless of what else has since changed;
/// accounts are mutable config, edited over time via reversal-free updates.
/// </summary>
/// <remarks>
/// This type enforces only the invariants a single account can check on its
/// own: a non-empty <see cref="Name"/>, a non-blank <see cref="Code"/> when
/// one is supplied, and a defined <see cref="AccountType"/>. Invariants that
/// depend on the rest of the tree — unique-when-present <c>Code</c>, type
/// inheritance from the parent, no cycles — are enforced by
/// <see cref="ChartOfAccounts"/>, the only place that has that context.
/// </remarks>
public sealed class Account : IEquatable<Account>
{
    public Guid Id { get; }

    /// <summary>Optional free-form human identifier. Unique-when-present is enforced by <see cref="ChartOfAccounts"/>.</summary>
    public string? Code { get; }

    public string Name { get; }

    public AccountType Type { get; }

    /// <summary>The enforced normal balance for <see cref="Type"/> (spec §4.2 table).</summary>
    public Side NormalBalance => Type.NormalBalance();

    /// <summary>Nullable self-reference; the hierarchy lives here, never in the name.</summary>
    public Guid? ParentAccountId { get; }

    /// <summary>A roll-up-only node that rejects direct postings.</summary>
    public bool IsPlaceholder { get; }

    /// <summary>Inactive accounts reject new postings but keep history.</summary>
    public bool IsActive { get; }

    /// <summary>
    /// Tag-ids classifying this account (spec §4.8). Flows down the tree as
    /// part of every descendant's effective tags (<see cref="ChartOfAccounts.EffectiveTagsOf"/>)
    /// — never validated here, since tag-existence is cross-aggregate
    /// context a lone <see cref="Account"/> doesn't have.
    /// </summary>
    public IReadOnlySet<Guid> Tags { get; }

    private Account(
        Guid id,
        string? code,
        string name,
        AccountType type,
        Guid? parentAccountId,
        bool isPlaceholder,
        bool isActive,
        IReadOnlySet<Guid> tags)
    {
        Id = id;
        Code = code;
        Name = name;
        Type = type;
        ParentAccountId = parentAccountId;
        IsPlaceholder = isPlaceholder;
        IsActive = isActive;
        Tags = tags;
    }

    /// <exception cref="ArgumentException"><paramref name="id"/> is <see cref="Guid.Empty"/> — a caller bug, not a business rule.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null — a caller bug, not a business rule.</exception>
    public static Result<Account> Create(
        Guid id,
        string name,
        AccountType type,
        string? code = null,
        Guid? parentAccountId = null,
        bool isPlaceholder = false,
        bool isActive = true)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Account id must not be Guid.Empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(name);

        if (!Enum.IsDefined(type))
        {
            return InvalidType(type);
        }

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
        {
            return nameResult.Error;
        }

        var codeResult = ValidateCode(code);
        if (codeResult.IsFailure)
        {
            return codeResult.Error;
        }

        return new Account(
            id, codeResult.Value, nameResult.Value, type, parentAccountId, isPlaceholder, isActive, new HashSet<Guid>());
    }

    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public Result<Account> Rename(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
        {
            return nameResult.Error;
        }

        return new Account(Id, Code, nameResult.Value, Type, ParentAccountId, IsPlaceholder, IsActive, Tags);
    }

    /// <summary>Sets the code, or clears it when <paramref name="code"/> is null.</summary>
    public Result<Account> SetCode(string? code)
    {
        var codeResult = ValidateCode(code);
        if (codeResult.IsFailure)
        {
            return codeResult.Error;
        }

        return new Account(Id, codeResult.Value, Name, Type, ParentAccountId, IsPlaceholder, IsActive, Tags);
    }

    public Account Activate() => new(Id, Code, Name, Type, ParentAccountId, IsPlaceholder, isActive: true, Tags);

    public Account Deactivate() => new(Id, Code, Name, Type, ParentAccountId, IsPlaceholder, isActive: false, Tags);

    public Account MarkAsPlaceholder() =>
        new(Id, Code, Name, Type, ParentAccountId, isPlaceholder: true, IsActive, Tags);

    public Account ClearPlaceholder() =>
        new(Id, Code, Name, Type, ParentAccountId, isPlaceholder: false, IsActive, Tags);

    /// <summary>
    /// Raw reparent with no tree-context validation. Internal because type
    /// inheritance and no-cycle checks need the rest of the tree; only
    /// <see cref="ChartOfAccounts"/> may call this, after checking those.
    /// </summary>
    internal Account WithParent(Guid? parentAccountId) =>
        new(Id, Code, Name, Type, parentAccountId, IsPlaceholder, IsActive, Tags);

    /// <summary>Adds a tag-id. Not validated against any registry — see <see cref="Tags"/>.</summary>
    public Account AddTag(Guid tagId) =>
        new(Id, Code, Name, Type, ParentAccountId, IsPlaceholder, IsActive, new HashSet<Guid>(Tags) { tagId });

    /// <summary>Removes a tag-id. A no-op if it wasn't present.</summary>
    public Account RemoveTag(Guid tagId)
    {
        var tags = new HashSet<Guid>(Tags);
        tags.Remove(tagId);
        return new(Id, Code, Name, Type, ParentAccountId, IsPlaceholder, IsActive, tags);
    }

    private static Result<string> ValidateName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return new Error(
                "account.name.required",
                ErrorCategory.Validation,
                "Account name must not be empty.",
                ["Provide a non-empty Name."]);
        }

        return trimmed;
    }

    private static Result<string?> ValidateCode(string? code)
    {
        if (code is null)
        {
            return Result<string?>.Success(null);
        }

        var trimmed = code.Trim();
        if (trimmed.Length == 0)
        {
            return new Error(
                "account.code.blank",
                ErrorCategory.Validation,
                "Account code was provided but is blank.",
                ["Omit Code entirely, or supply a non-blank value."]);
        }

        return Result<string?>.Success(trimmed);
    }

    private static Error InvalidType(AccountType type) => new(
        "account.type.invalid",
        ErrorCategory.Validation,
        $"'{type}' is not a recognized account type.",
        ["Use one of: Asset, Liability, Equity, Income, Expense."]);

    public bool Equals(Account? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as Account);

    public override int GetHashCode() => Id.GetHashCode();
}