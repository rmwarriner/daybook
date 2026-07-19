namespace Daybook.Accounting.Core;

/// <summary>
/// The result of <see cref="Journal.VerifyChain"/> — a plain descriptive
/// outcome rather than <see cref="Result{T}"/>, since this is a query, not a
/// fallible operation (same reasoning as <c>TrialBalance.Compute</c>).
/// <see cref="FirstAffectedEntryId"/>/<see cref="FirstAffectedSequenceNumber"/>
/// are set only for <see cref="ChainVerificationStatus.ChainNotFullyPopulated"/>
/// and <see cref="ChainVerificationStatus.Tampered"/>.
/// </summary>
public sealed record ChainVerificationResult(
    ChainVerificationStatus Status,
    Guid? FirstAffectedEntryId,
    int? FirstAffectedSequenceNumber);