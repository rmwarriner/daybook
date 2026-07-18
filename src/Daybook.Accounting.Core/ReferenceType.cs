namespace Daybook.Accounting.Core;

/// <summary>
/// The kind of external instrument a <see cref="Reference"/> identifies
/// (spec §4.3.1) — extensible rather than a bespoke field per instrument.
/// </summary>
public enum ReferenceType
{
    Check,
    ACH,
    Wire,
    Invoice,
    Receipt,
    Card,
    Other,
}