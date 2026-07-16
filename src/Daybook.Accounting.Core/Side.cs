namespace Daybook.Accounting.Core;

/// <summary>
/// Which side of a double-entry a journal line falls on, and which side an
/// account's normal balance sits on. Explicit rather than a signed amount —
/// see spec §4.2 and §4.4.
/// </summary>
public enum Side
{
    Debit,
    Credit,
}