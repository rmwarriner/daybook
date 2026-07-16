namespace Daybook.Accounting.Core;

/// <summary>One row of a <see cref="TrialBalance"/> (spec §6.2): an account's normal balance and rolled-up total.</summary>
public sealed record TrialBalanceLine(Guid AccountId, Side NormalBalance, Money OwnBalance, Money RolledUpBalance);