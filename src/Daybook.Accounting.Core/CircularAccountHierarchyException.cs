namespace Daybook.Accounting.Core;

/// <summary>
/// Thrown when reparenting an account would make it its own ancestor (spec
/// §4.2). A caller precondition violation, not a user-facing business-rule
/// failure (see CLAUDE.md logging/errors).
/// </summary>
public sealed class CircularAccountHierarchyException : InvalidOperationException
{
    public Guid AccountId { get; }

    public CircularAccountHierarchyException(Guid accountId)
        : base($"Account '{accountId}' cannot be reparented under itself or one of its own descendants.")
    {
        AccountId = accountId;
    }
}