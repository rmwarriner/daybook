namespace Daybook.Accounting.Core;

/// <summary>One posted line in an <see cref="AccountRegister"/> (spec §6.3), with its running balance.</summary>
public sealed record RegisterLine(
    Guid EntryId,
    int SequenceNumber,
    DateOnly EntryDate,
    string Description,
    Guid AccountId,
    Side Side,
    Money Amount,
    Money RunningBalance);