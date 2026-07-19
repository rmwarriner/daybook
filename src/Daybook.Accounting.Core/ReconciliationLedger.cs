namespace Daybook.Accounting.Core;

/// <summary>
/// Owns both the completed <see cref="Reconciliation"/> sessions and each
/// line's current <see cref="ReconciliationStatus"/> (spec §4.4/§4.5), kept
/// together in one aggregate — not split into two — because they must
/// never drift apart, the same reasoning <see cref="Journal"/> already uses
/// for owning entries and reversal links together. A new, independent Core
/// aggregate, same tier as <see cref="TagRegistry"/>/<see cref="Journal"/>/
/// <see cref="ChartOfAccounts"/> — not nested inside any of them.
/// </summary>
public sealed class ReconciliationLedger
{
    private readonly Dictionary<Guid, Reconciliation> _sessionsById = [];
    private readonly Dictionary<LineLocation, ReconciliationStatus> _statusByLine = [];
    private readonly Dictionary<LineLocation, Guid> _reconciliationByLine = [];

    private ReconciliationLedger()
    {
    }

    public static ReconciliationLedger Empty() => new();

    /// <summary>Defaults to <see cref="ReconciliationStatus.Unreconciled"/> for anything never tracked.</summary>
    public ReconciliationStatus StatusOf(LineLocation location) =>
        _statusByLine.GetValueOrDefault(location, ReconciliationStatus.Unreconciled);

    /// <summary>The session that reconciled this line, or null if it isn't currently <see cref="ReconciliationStatus.Reconciled"/>.</summary>
    public Guid? ReconciliationOf(LineLocation location) =>
        _reconciliationByLine.TryGetValue(location, out var id) ? id : null;

    public Reconciliation? FindReconciliation(Guid reconciliationId) => _sessionsById.GetValueOrDefault(reconciliationId);

    /// <summary>Marks a line "seen at the bank," ahead of a full statement reconciliation. Fails once the line is already Reconciled.</summary>
    public Result<ReconciliationStatus> MarkCleared(LineLocation location, Journal journal)
    {
        var lineResult = ResolveLine(journal, location);
        if (lineResult.IsFailure)
        {
            return lineResult.Error;
        }

        if (StatusOf(location) == ReconciliationStatus.Reconciled)
        {
            return AlreadyReconciled(location);
        }

        _statusByLine[location] = ReconciliationStatus.Cleared;
        return ReconciliationStatus.Cleared;
    }

    /// <summary>Un-clears a line. Fails once the line is already Reconciled — reopen its session first.</summary>
    public Result<ReconciliationStatus> MarkUnreconciled(LineLocation location)
    {
        if (StatusOf(location) == ReconciliationStatus.Reconciled)
        {
            return AlreadyReconciled(location);
        }

        _statusByLine[location] = ReconciliationStatus.Unreconciled;
        return ReconciliationStatus.Unreconciled;
    }

    /// <summary>
    /// Creates a completed reconciliation session and promotes every one of
    /// <paramref name="clearedLines"/> to <see cref="ReconciliationStatus.Reconciled"/>,
    /// linked to it. Validates every line resolves to a posted line
    /// belonging to <paramref name="accountId"/> and isn't already
    /// reconciled elsewhere, before storing anything.
    /// </summary>
    public Result<Reconciliation> CreateReconciliation(
        Guid id,
        Guid accountId,
        DateOnly statementDate,
        Money statementEndingBalance,
        DateTimeOffset reconciledAtUtc,
        Guid reconciledByUserId,
        IReadOnlySet<LineLocation> clearedLines,
        Journal journal)
    {
        ArgumentNullException.ThrowIfNull(clearedLines);

        if (_sessionsById.ContainsKey(id))
        {
            return DuplicateId(id);
        }

        foreach (var location in clearedLines)
        {
            var lineResult = ResolveLine(journal, location);
            if (lineResult.IsFailure)
            {
                return lineResult.Error;
            }

            if (lineResult.Value.AccountId != accountId)
            {
                return AccountMismatch(location, accountId);
            }

            if (StatusOf(location) == ReconciliationStatus.Reconciled)
            {
                return AlreadyReconciled(location);
            }
        }

        var result = Reconciliation.Create(
            id, accountId, statementDate, statementEndingBalance, reconciledAtUtc, reconciledByUserId, clearedLines);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var reconciliation = result.Value;
        _sessionsById[id] = reconciliation;
        foreach (var location in clearedLines)
        {
            _statusByLine[location] = ReconciliationStatus.Reconciled;
            _reconciliationByLine[location] = id;
        }

        return reconciliation;
    }

    /// <summary>Re-opens a session: removes it and resets every one of its lines back to Unreconciled.</summary>
    public Result<Reconciliation> ReopenReconciliation(Guid reconciliationId)
    {
        var reconciliation = FindReconciliation(reconciliationId);
        if (reconciliation is null)
        {
            return ReconciliationNotFound(reconciliationId);
        }

        foreach (var location in reconciliation.ClearedLines)
        {
            _statusByLine[location] = ReconciliationStatus.Unreconciled;
            _reconciliationByLine.Remove(location);
        }

        _sessionsById.Remove(reconciliationId);
        return reconciliation;
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
        "reconciliation.line.entry_not_found",
        ErrorCategory.Validation,
        $"No journal entry with id '{entryId}' exists.",
        ["Check the EntryId."]);

    private static Error NotPosted(Guid entryId) => new(
        "reconciliation.line.not_posted",
        ErrorCategory.BusinessRule,
        $"Entry '{entryId}' is not posted; only posted lines can be reconciled.",
        ["Post the entry first."]);

    private static Error IndexOutOfRange(LineLocation location) => new(
        "reconciliation.line.index_out_of_range",
        ErrorCategory.Validation,
        $"Entry '{location.EntryId}' has no line at index {location.LineIndex}.",
        ["Check the LineIndex."]);

    private static Error AlreadyReconciled(LineLocation location) => new(
        "reconciliation.line.already_reconciled",
        ErrorCategory.BusinessRule,
        $"Line {location.LineIndex} of entry '{location.EntryId}' is already reconciled.",
        ["Reopen its reconciliation session first."]);

    private static Error AccountMismatch(LineLocation location, Guid accountId) => new(
        "reconciliation.line.account_mismatch",
        ErrorCategory.Validation,
        $"Line {location.LineIndex} of entry '{location.EntryId}' does not belong to account '{accountId}'.",
        ["Only include lines posted to the account being reconciled."]);

    private static Error DuplicateId(Guid id) => new(
        "reconciliation.id.duplicate",
        ErrorCategory.Conflict,
        $"A reconciliation with id '{id}' already exists.",
        ["Generate a new ReconciliationId; ids must be unique."]);

    private static Error ReconciliationNotFound(Guid id) => new(
        "reconciliation.not_found",
        ErrorCategory.Validation,
        $"No reconciliation with id '{id}' exists.",
        ["Check the ReconciliationId."]);
}