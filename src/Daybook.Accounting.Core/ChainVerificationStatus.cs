namespace Daybook.Accounting.Core;

/// <summary>The outcome of walking a <see cref="Journal"/>'s HMAC hash chain (spec §15.3).</summary>
public enum ChainVerificationStatus
{
    /// <summary>Every posted entry's stored hash matches its recomputed value.</summary>
    Intact,

    /// <summary>This journal was constructed without a chain key — there is nothing to verify against.</summary>
    NoChainKeyConfigured,

    /// <summary>A posted entry has no stored hash — chaining wasn't enabled for its whole history.</summary>
    ChainNotFullyPopulated,

    /// <summary>A posted entry's stored hash doesn't match its recomputed value.</summary>
    Tampered,
}