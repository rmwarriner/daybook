namespace Daybook.Accounting.Core;

/// <summary>
/// Thrown when a derived report's core accounting identity doesn't hold —
/// e.g. a trial balance's total debits don't equal total credits, or a
/// balance sheet's assets don't equal liabilities plus equity. Both are
/// guaranteed by spec §5's posting invariant and are not reachable through
/// the public <see cref="Journal"/>/<see cref="ChartOfAccounts"/> API, so
/// this signals a bug in the engine (or, later, tampered persisted data),
/// not a business-rule violation a caller can trigger.
/// </summary>
public sealed class LedgerIntegrityException : InvalidOperationException
{
    public Money Left { get; }

    public Money Right { get; }

    public LedgerIntegrityException(string message, Money left, Money right)
        : base(message)
    {
        Left = left;
        Right = right;
    }
}