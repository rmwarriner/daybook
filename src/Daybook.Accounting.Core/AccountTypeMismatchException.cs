namespace Daybook.Accounting.Core;

/// <summary>
/// Thrown when an account is created or reparented under a parent of a
/// different root <see cref="AccountType"/>. A sub-account must share its
/// parent's type (spec §4.2) — a caller precondition violation, not a
/// user-facing business-rule failure (see CLAUDE.md logging/errors).
/// </summary>
public sealed class AccountTypeMismatchException : InvalidOperationException
{
    public AccountType ParentType { get; }

    public AccountType ChildType { get; }

    public AccountTypeMismatchException(AccountType parentType, AccountType childType)
        : base(
            $"Account type '{childType}' does not match parent account type '{parentType}'. " +
            "A sub-account must share its parent's type.")
    {
        ParentType = parentType;
        ChildType = childType;
    }
}