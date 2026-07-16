namespace Daybook.Accounting.Core;

/// <summary>
/// A chart-of-accounts entry (spec §4.2): one of five root types, each with a
/// fixed normal balance. This slice covers identity, name, type, and normal
/// balance only — hierarchy, optional code, display path, and tags are
/// separate slices (issues #4-#7).
/// </summary>
public sealed class Account
{
    /// <summary>Stable identifier.</summary>
    public Guid AccountId { get; }

    public string Name { get; }

    public AccountType Type { get; }

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

    private Account(Guid accountId, string name, AccountType type)
    {
        AccountId = accountId;
        Name = name;
        Type = type;
    }

    /// <summary>
    /// Creates a new account. <see cref="Type"/> has no setter — v1 has no
    /// reclassification API, so the spec's "immutable once referenced" rule
    /// (§4.2) holds trivially.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a defined <see cref="AccountType"/>.</exception>
    public static Account Create(string name, AccountType type)
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

        return new Account(Guid.NewGuid(), name.Trim(), type);
    }
}