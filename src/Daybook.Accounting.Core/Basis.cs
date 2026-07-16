namespace Daybook.Accounting.Core;

/// <summary>
/// A book's accounting basis (spec §4.1), fixed per book. Both bases are
/// double-entry; the difference is which accounts you use and when. Per
/// spec §6.5, this drives report presentation and validation guidance
/// only — there is no separate code path in the core posting/derivation
/// math, so this value is inert in Core beyond being stored.
/// </summary>
public enum Basis
{
    Cash,
    Accrual,
}