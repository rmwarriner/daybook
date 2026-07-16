namespace Daybook.Accounting.Core;

/// <summary>
/// A side of a double-entry posting. Used both as an <see cref="Account"/>'s
/// normal balance and, later, as a journal line's explicit <c>Debit</c>/
/// <c>Credit</c> designation (spec §4.2, §4.4) — the same domain concept in
/// both places, so one enum serves both.
/// </summary>
public enum Side
{
    Debit,
    Credit,
}