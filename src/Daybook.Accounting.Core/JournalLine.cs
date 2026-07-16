namespace Daybook.Accounting.Core;

/// <summary>
/// One side of a <see cref="JournalEntry"/> (spec §4.4): the account, which
/// side it posts to, the amount, and an optional memo. A value object —
/// two lines with the same fields are interchangeable, unlike the entities
/// that hold them.
/// </summary>
/// <remarks>
/// This type enforces only the invariants a single line can check on its
/// own: a positive <see cref="Amount"/> and a defined <see cref="Side"/>.
/// Invariants that need the rest of the entry or the chart of accounts —
/// the entry balances, referenced accounts exist and are postable, the
/// currency matches the book — are enforced by <see cref="Journal"/> at
/// post time, the only place that has that context.
/// </remarks>
public sealed record JournalLine
{
    public Guid AccountId { get; }

    public Side Side { get; }

    public Money Amount { get; }

    /// <summary>Optional per-line note. Blank input is normalized to no memo.</summary>
    public string? Memo { get; }

    private JournalLine(Guid accountId, Side side, Money amount, string? memo)
    {
        AccountId = accountId;
        Side = side;
        Amount = amount;
        Memo = memo;
    }

    /// <exception cref="ArgumentException"><paramref name="accountId"/> is <see cref="Guid.Empty"/> — a caller bug, not a business rule.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="amount"/> is null — a caller bug, not a business rule.</exception>
    public static Result<JournalLine> Create(Guid accountId, Side side, Money amount, string? memo = null)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("AccountId must not be Guid.Empty.", nameof(accountId));
        }

        ArgumentNullException.ThrowIfNull(amount);

        if (!Enum.IsDefined(side))
        {
            return new Error(
                "entry.line.side_invalid",
                ErrorCategory.Validation,
                $"'{side}' is not a recognized side.",
                ["Use Side.Debit or Side.Credit."]);
        }

        if (amount.Amount <= 0m)
        {
            return new Error(
                "entry.line.amount_invalid",
                ErrorCategory.Validation,
                $"Line amount must be positive, but was {amount}.",
                ["Provide an amount greater than zero."]);
        }

        var trimmedMemo = memo?.Trim();
        return new JournalLine(accountId, side, amount, string.IsNullOrEmpty(trimmedMemo) ? null : trimmedMemo);
    }
}