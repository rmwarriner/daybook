namespace Daybook.Accounting.Core;

/// <summary>
/// A posted line's verification state against an external statement (spec
/// §4.4) — a separate, orthogonal axis from <see cref="JournalEntryStatus"/>.
/// Mirrors the familiar Quicken <c>c</c>/<c>R</c> distinction: <see cref="Cleared"/>
/// = "seen at the bank," <see cref="Reconciled"/> = "locked in a completed
/// statement reconciliation" (<see cref="Reconciliation"/>).
/// </summary>
public enum ReconciliationStatus
{
    Unreconciled,
    Cleared,
    Reconciled,
}