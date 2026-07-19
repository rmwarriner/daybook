using System.Text;

using Konscious.Security.Cryptography;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// Derives a key from a passphrase using Argon2id (spec §13.5) — the raw
/// passphrase is never used directly as a cryptographic key.
/// </summary>
/// <remarks>
/// Parameters (128 MiB memory, 3 iterations, degree of parallelism 1) are
/// OWASP's cited "enhanced" profile (their baseline is 19 MiB/t=2/p=1).
/// Justified here because this KDF gates a single household's entire
/// financial history and runs rarely (at startup, at passphrase rotation)
/// — there's no concurrent-request-volume pressure to trade security for
/// throughput.
/// </remarks>
public static class Argon2Kdf
{
    public const int DefaultMemorySizeKb = 128 * 1024;
    public const int DefaultIterations = 3;
    public const int DefaultDegreeOfParallelism = 1;

    /// <summary>
    /// Derives a key. <paramref name="purpose"/> is a domain-separation
    /// label (e.g. <c>"envelope-kek"</c>) so the same passphrase and salt
    /// can derive multiple, cryptographically distinct keys for different
    /// jobs — reusing one raw key across different cryptographic
    /// primitives is a well-known anti-pattern this avoids.
    /// </summary>
    /// <remarks>
    /// The cost parameters are explicit, defaulting to today's chosen
    /// values, rather than hardcoded internally — a caller persisting the
    /// output (see <see cref="WrappedDataKey"/>) stores whichever
    /// parameters were actually used alongside it, so a future
    /// recalibration of the defaults can't silently break reproducing an
    /// already-derived key.
    /// </remarks>
    public static byte[] DeriveKey(
        string passphrase,
        byte[] salt,
        byte[] purpose,
        int outputLength = 32,
        int memorySizeKb = DefaultMemorySizeKb,
        int iterations = DefaultIterations,
        int degreeOfParallelism = DefaultDegreeOfParallelism)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(passphrase))
        {
            Salt = salt,
            AssociatedData = purpose,
            MemorySize = memorySizeKb,
            Iterations = iterations,
            DegreeOfParallelism = degreeOfParallelism,
        };

        return argon2.GetBytes(outputLength);
    }
}