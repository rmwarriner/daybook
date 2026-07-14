namespace Daybook.Accounting.Core;

/// <summary>
/// Thrown when arithmetic is attempted between <see cref="Money"/> values of
/// different currencies. This is a precondition violation (a caller bug —
/// adding apples to oranges), not a user-facing business-rule failure, so it
/// is an exception rather than a <c>Result</c> (see CLAUDE.md logging/errors).
/// </summary>
public sealed class CurrencyMismatchException : InvalidOperationException
{
    public Currency Left { get; }

    public Currency Right { get; }

    public CurrencyMismatchException(Currency left, Currency right)
        : base($"Cannot combine Money values of differing currencies: {left.Code} and {right.Code}.")
    {
        Left = left;
        Right = right;
    }
}