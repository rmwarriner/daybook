namespace Daybook.Accounting.Core;

/// <summary>
/// A completed reconciliation session against an external statement (spec
/// §4.5) — reconciled lines link here rather than carrying a loose flag, so
/// the match is provable and re-openable. An entity — identity by
/// <see cref="Id"/>.
/// </summary>
/// <remarks>
/// Unlike <see cref="JournalEntry"/>, there is no draft/incremental-build
/// lifecycle: a session is created already-complete, so this type eagerly
/// validates <see cref="ClearedLines"/> is non-empty as a genuine local
/// invariant. Whether each line actually resolves to a posted line, belongs
/// to <see cref="AccountId"/>, and isn't already reconciled elsewhere needs
/// <see cref="Journal"/> context this type doesn't have — that's
/// <see cref="ReconciliationLedger"/>'s job, the one guarded door for
/// creating and re-opening sessions.
/// </remarks>
public sealed class Reconciliation : IEquatable<Reconciliation>
{
    public Guid Id { get; }

    /// <summary>The account being reconciled.</summary>
    public Guid AccountId { get; }

    /// <summary>The external statement's date.</summary>
    public DateOnly StatementDate { get; }

    public Money StatementEndingBalance { get; }

    public DateTimeOffset ReconciledAtUtc { get; }

    public Guid ReconciledByUserId { get; }

    /// <summary>The lines cleared in this session (spec: "the set of journal lines cleared").</summary>
    public IReadOnlySet<LineLocation> ClearedLines { get; }

    private Reconciliation(
        Guid id,
        Guid accountId,
        DateOnly statementDate,
        Money statementEndingBalance,
        DateTimeOffset reconciledAtUtc,
        Guid reconciledByUserId,
        IReadOnlySet<LineLocation> clearedLines)
    {
        Id = id;
        AccountId = accountId;
        StatementDate = statementDate;
        StatementEndingBalance = statementEndingBalance;
        ReconciledAtUtc = reconciledAtUtc;
        ReconciledByUserId = reconciledByUserId;
        ClearedLines = clearedLines;
    }

    /// <exception cref="ArgumentException"><paramref name="id"/> or <paramref name="accountId"/> is <see cref="Guid.Empty"/> — a caller bug, not a business rule.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="statementEndingBalance"/> or <paramref name="clearedLines"/> is null — a caller bug, not a business rule.</exception>
    public static Result<Reconciliation> Create(
        Guid id,
        Guid accountId,
        DateOnly statementDate,
        Money statementEndingBalance,
        DateTimeOffset reconciledAtUtc,
        Guid reconciledByUserId,
        IReadOnlySet<LineLocation> clearedLines)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Reconciliation id must not be Guid.Empty.", nameof(id));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("AccountId must not be Guid.Empty.", nameof(accountId));
        }

        ArgumentNullException.ThrowIfNull(statementEndingBalance);
        ArgumentNullException.ThrowIfNull(clearedLines);

        if (clearedLines.Count == 0)
        {
            return new Error(
                "reconciliation.cleared_lines.required",
                ErrorCategory.Validation,
                "A reconciliation session must cover at least one cleared line.",
                ["Include at least one cleared line."]);
        }

        return new Reconciliation(
            id, accountId, statementDate, statementEndingBalance, reconciledAtUtc, reconciledByUserId, clearedLines);
    }

    public bool Equals(Reconciliation? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as Reconciliation);

    public override int GetHashCode() => Id.GetHashCode();
}