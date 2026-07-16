namespace Daybook.Accounting.Core;

/// <summary>
/// A chart-of-accounts entry (spec §4.2): one of five root types, each with a
/// fixed normal balance, arranged in a hierarchy via <see cref="Parent"/>.
/// This slice covers identity, name, type, normal balance, hierarchy
/// (type inheritance, no-cycle, reparenting), and active/inactive status.
/// Optional code, derived display path, and tags are separate slices
/// (issues #5-#7).
/// </summary>
public sealed class Account
{
    /// <summary>Stable identifier.</summary>
    public Guid AccountId { get; }

    public string Name { get; }

    public AccountType Type { get; }

    /// <summary>
    /// The parent in the account hierarchy, or <see langword="null"/> for a
    /// root account. The hierarchy lives here, never in the name.
    /// </summary>
    public Account? Parent { get; private set; }

    /// <summary>Convenience accessor for <see cref="Parent"/>'s id.</summary>
    public Guid? ParentAccountId => Parent?.AccountId;

    /// <summary>
    /// Inactive accounts reject new postings but keep their history. There is
    /// no delete operation in v1 — only <see cref="Deactivate"/> — so a
    /// child's <see cref="Parent"/> link can never be orphaned by a parent
    /// disappearing.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// The side this account's <see cref="Type"/> normally carries a balance on.
    /// </summary>
    public Side NormalBalance => Type switch
    {
        AccountType.Asset => Side.Debit,
        AccountType.Expense => Side.Debit,
        AccountType.Liability => Side.Credit,
        AccountType.Equity => Side.Credit,
        AccountType.Income => Side.Credit,
        _ => throw new ArgumentOutOfRangeException(nameof(Type), Type, "Unknown account type."),
    };

    private Account(Guid accountId, string name, AccountType type, Account? parent)
    {
        AccountId = accountId;
        Name = name;
        Type = type;
        Parent = parent;
    }

    /// <summary>
    /// Creates a new account, optionally as a child of <paramref name="parent"/>.
    /// <see cref="Type"/> has no setter — v1 has no reclassification API, so
    /// the spec's "immutable once referenced" rule (§4.2) holds trivially.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a defined <see cref="AccountType"/>.</exception>
    /// <exception cref="AccountTypeMismatchException"><paramref name="parent"/>'s type differs from <paramref name="type"/>.</exception>
    public static Account Create(string name, AccountType type, Account? parent = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Account name must not be blank.", nameof(name));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown account type.");
        }

        var accountId = Guid.NewGuid();
        EnsureValidParent(parent, accountId, type);

        return new Account(accountId, name.Trim(), type, parent);
    }

    /// <summary>
    /// Moves this account under <paramref name="newParent"/>, or to the root
    /// when <see langword="null"/>. Accounts are mutable config, not journal
    /// entries, so a subtree can be moved freely subject to the type and
    /// no-cycle rules (spec §4.2).
    /// </summary>
    /// <exception cref="AccountTypeMismatchException"><paramref name="newParent"/>'s type differs from this account's type.</exception>
    /// <exception cref="CircularAccountHierarchyException">This account is <paramref name="newParent"/> or one of its own ancestors.</exception>
    public void Reparent(Account? newParent)
    {
        EnsureValidParent(newParent, AccountId, Type);
        Parent = newParent;
    }

    /// <summary>
    /// Marks the account inactive. Inactive accounts reject new postings but
    /// keep their history — idempotent, and there is no way back in v1.
    /// </summary>
    public void Deactivate() => IsActive = false;

    private static void EnsureValidParent(Account? parent, Guid childAccountId, AccountType childType)
    {
        if (parent is null)
        {
            return;
        }

        if (parent.Type != childType)
        {
            throw new AccountTypeMismatchException(parent.Type, childType);
        }

        for (var ancestor = parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.AccountId == childAccountId)
            {
                throw new CircularAccountHierarchyException(childAccountId);
            }
        }
    }
}