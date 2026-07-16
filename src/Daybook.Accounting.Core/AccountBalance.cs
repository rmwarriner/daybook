namespace Daybook.Accounting.Core;

/// <summary>
/// One account's balance (spec §6.1), both presented on its normal side:
/// <see cref="OwnBalance"/> from its own direct postings only, and
/// <see cref="RolledUpBalance"/> including every descendant. A placeholder
/// account always has a zero <see cref="OwnBalance"/> — it rejects direct
/// postings by construction — but may have a non-zero rolled-up one.
/// </summary>
public sealed record AccountBalance(Guid AccountId, Money OwnBalance, Money RolledUpBalance);