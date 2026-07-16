namespace Daybook.Accounting.Core;

/// <summary>
/// The chart of accounts for a book (spec §4.2) — a mutable collection of
/// <see cref="Account"/> entities. This is where the tree-context
/// invariants a lone <see cref="Account"/> cannot check by itself live:
/// unique-when-present <c>Code</c>, type inheritance from the parent, no
/// cycles, and the derived display path.
/// </summary>
public sealed class ChartOfAccounts
{
    private readonly Dictionary<Guid, Account> _byId = [];
    private readonly Dictionary<string, Guid> _byCode = new(StringComparer.Ordinal);

    private ChartOfAccounts()
    {
    }

    public static ChartOfAccounts Empty() => new();

    public IReadOnlyCollection<Account> Accounts => _byId.Values;

    public Account? Find(Guid id) => _byId.GetValueOrDefault(id);

    public Account? FindByCode(string code) =>
        _byCode.TryGetValue(code, out var id) ? _byId[id] : null;

    public IReadOnlyCollection<Account> Children(Guid parentAccountId) =>
        _byId.Values.Where(a => a.ParentAccountId == parentAccountId).ToList();

    /// <summary>Adds a new account with no parent.</summary>
    public Result<Account> AddRoot(
        Guid id,
        string name,
        AccountType type,
        string? code = null,
        bool isPlaceholder = false,
        bool isActive = true) =>
        Add(id, name, type, code, parentAccountId: null, isPlaceholder, isActive);

    /// <summary>
    /// Adds a new account under an existing parent. <paramref name="type"/>
    /// must match the parent's type (type inheritance, spec §4.2).
    /// </summary>
    public Result<Account> AddChild(
        Guid id,
        Guid parentAccountId,
        string name,
        AccountType type,
        string? code = null,
        bool isPlaceholder = false,
        bool isActive = true)
    {
        var parent = Find(parentAccountId);
        if (parent is null)
        {
            return ParentNotFound(parentAccountId);
        }

        if (type != parent.Type)
        {
            return TypeMismatch(type, parent.Type);
        }

        return Add(id, name, type, code, parentAccountId, isPlaceholder, isActive);
    }

    /// <summary>
    /// Moves an account to a new parent (or to the root, when
    /// <paramref name="newParentAccountId"/> is null). Rejects a move that
    /// would violate type inheritance or create a cycle.
    /// </summary>
    public Result<Account> Reparent(Guid accountId, Guid? newParentAccountId)
    {
        var account = Find(accountId);
        if (account is null)
        {
            return AccountNotFound(accountId);
        }

        if (newParentAccountId is not { } newParentId)
        {
            return Store(account.WithParent(null));
        }

        if (newParentId == accountId)
        {
            return Cycle(accountId, newParentId);
        }

        var newParent = Find(newParentId);
        if (newParent is null)
        {
            return ParentNotFound(newParentId);
        }

        if (newParent.Type != account.Type)
        {
            return TypeMismatch(account.Type, newParent.Type);
        }

        if (IsDescendant(accountId, newParentId))
        {
            return Cycle(accountId, newParentId);
        }

        return Store(account.WithParent(newParentId));
    }

    public Result<Account> Rename(Guid accountId, string name)
    {
        var account = Find(accountId);
        if (account is null)
        {
            return AccountNotFound(accountId);
        }

        var result = account.Rename(name);
        return result.IsFailure ? result.Error : Store(result.Value);
    }

    public Result<Account> SetCode(Guid accountId, string? code)
    {
        var account = Find(accountId);
        if (account is null)
        {
            return AccountNotFound(accountId);
        }

        var result = account.SetCode(code);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var updated = result.Value;
        if (updated.Code is not null &&
            _byCode.TryGetValue(updated.Code, out var owner) &&
            owner != accountId)
        {
            return DuplicateCode(updated.Code);
        }

        if (account.Code is not null)
        {
            _byCode.Remove(account.Code);
        }

        if (updated.Code is not null)
        {
            _byCode[updated.Code] = accountId;
        }

        _byId[accountId] = updated;
        return updated;
    }

    public Result<Account> Activate(Guid accountId) => Mutate(accountId, a => a.Activate());

    public Result<Account> Deactivate(Guid accountId) => Mutate(accountId, a => a.Deactivate());

    public Result<Account> MarkAsPlaceholder(Guid accountId) => Mutate(accountId, a => a.MarkAsPlaceholder());

    public Result<Account> ClearPlaceholder(Guid accountId) => Mutate(accountId, a => a.ClearPlaceholder());

    /// <summary>The derived, readable path (e.g. <c>Utilities:Electric</c>) from the root down to this account.</summary>
    public Result<string> DisplayPathOf(Guid accountId)
    {
        var account = Find(accountId);
        if (account is null)
        {
            return AccountNotFound(accountId);
        }

        var segments = new List<string>();
        Account? current = account;
        while (current is not null)
        {
            segments.Add(current.Name);
            current = current.ParentAccountId is { } parentId ? Find(parentId) : null;
        }

        segments.Reverse();
        return string.Join(':', segments);
    }

    private Result<Account> Add(
        Guid id,
        string name,
        AccountType type,
        string? code,
        Guid? parentAccountId,
        bool isPlaceholder,
        bool isActive)
    {
        if (_byId.ContainsKey(id))
        {
            return DuplicateId(id);
        }

        var accountResult = Account.Create(id, name, type, code, parentAccountId, isPlaceholder, isActive);
        if (accountResult.IsFailure)
        {
            return accountResult.Error;
        }

        var account = accountResult.Value;
        if (account.Code is not null && _byCode.ContainsKey(account.Code))
        {
            return DuplicateCode(account.Code);
        }

        _byId[id] = account;
        if (account.Code is not null)
        {
            _byCode[account.Code] = id;
        }

        return account;
    }

    private Result<Account> Mutate(Guid accountId, Func<Account, Account> mutate)
    {
        var account = Find(accountId);
        return account is null ? AccountNotFound(accountId) : Store(mutate(account));
    }

    private Account Store(Account account)
    {
        _byId[account.Id] = account;
        return account;
    }

    /// <summary>True when <paramref name="candidateDescendantId"/> is somewhere in the subtree rooted at <paramref name="ancestorId"/>.</summary>
    private bool IsDescendant(Guid ancestorId, Guid candidateDescendantId)
    {
        var current = Find(candidateDescendantId);
        while (current?.ParentAccountId is { } parentId)
        {
            if (parentId == ancestorId)
            {
                return true;
            }

            current = Find(parentId);
        }

        return false;
    }

    private static Error AccountNotFound(Guid id) => new(
        "account.not_found",
        ErrorCategory.Validation,
        $"No account with id '{id}' exists in this chart.",
        ["Check the account id, or create the account first."]);

    private static Error ParentNotFound(Guid id) => new(
        "account.parent.not_found",
        ErrorCategory.Validation,
        $"No account with id '{id}' exists to use as a parent.",
        ["Check the ParentAccountId, or create the parent account first."]);

    private static Error DuplicateId(Guid id) => new(
        "account.id.duplicate",
        ErrorCategory.Conflict,
        $"An account with id '{id}' already exists in this chart.",
        ["Generate a new AccountId; ids must be unique."]);

    private static Error DuplicateCode(string code) => new(
        "account.code.duplicate",
        ErrorCategory.Conflict,
        $"Account code '{code}' is already in use in this chart.",
        ["Choose a different Code, or omit it."]);

    private static Error TypeMismatch(AccountType childType, AccountType parentType) => new(
        "account.parent.type_mismatch",
        ErrorCategory.BusinessRule,
        $"Account type '{childType}' does not match its parent's type '{parentType}'.",
        [
            "A sub-account must share its parent's root type.",
            $"Choose a parent of type '{childType}', or a different account to reparent.",
        ]);

    private static Error Cycle(Guid accountId, Guid newParentId) => new(
        "account.hierarchy.cycle",
        ErrorCategory.BusinessRule,
        $"Making '{newParentId}' the parent of '{accountId}' would create a cycle in the account hierarchy.",
        ["Choose a parent that is not this account or one of its own descendants."]);
}