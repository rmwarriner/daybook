using System.Security.Cryptography;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// Envelope encryption for the database's data-encryption-key (spec
/// §13.5): a passphrase-derived key-encryption-key wraps a randomly
/// generated data-encryption-key using AES-GCM (authenticated encryption —
/// a wrong passphrase fails the tag check rather than silently producing
/// garbage), so rotating the passphrase only re-wraps the DEK, never a
/// full-database re-encryption.
/// </summary>
public static class DataKeyEnvelope
{
    private const int SaltSizeBytes = 16;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int DataKeySizeBytes = 32;

    private static readonly byte[] Purpose = "envelope-kek"u8.ToArray();

    public static byte[] GenerateDataKey() => RandomNumberGenerator.GetBytes(DataKeySizeBytes);

    public static WrappedDataKey Wrap(byte[] dataKey, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var kek = Argon2Kdf.DeriveKey(passphrase, salt, Purpose);

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[dataKey.Length];
        var tag = new byte[TagSizeBytes];
        using (var aesGcm = new AesGcm(kek, TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, dataKey, ciphertext, tag);
        }

        return new WrappedDataKey(
            salt, nonce, ciphertext, tag,
            Argon2Kdf.DefaultMemorySizeKb, Argon2Kdf.DefaultIterations, Argon2Kdf.DefaultDegreeOfParallelism);
    }

    /// <exception cref="CryptographicException">The passphrase is wrong (the AES-GCM authentication tag fails).</exception>
    public static byte[] Unwrap(WrappedDataKey wrapped, string passphrase)
    {
        var kek = Argon2Kdf.DeriveKey(
            passphrase, wrapped.Salt, Purpose,
            memorySizeKb: wrapped.MemorySizeKb, iterations: wrapped.Iterations,
            degreeOfParallelism: wrapped.DegreeOfParallelism);

        var dataKey = new byte[wrapped.Ciphertext.Length];
        using var aesGcm = new AesGcm(kek, TagSizeBytes);
        aesGcm.Decrypt(wrapped.Nonce, wrapped.Ciphertext, wrapped.Tag, dataKey);
        return dataKey;
    }

    /// <summary>Re-wraps the same data key under a new passphrase. The database itself is never touched.</summary>
    /// <exception cref="CryptographicException"><paramref name="oldPassphrase"/> is wrong.</exception>
    public static WrappedDataKey Rotate(WrappedDataKey wrapped, string oldPassphrase, string newPassphrase)
    {
        var dataKey = Unwrap(wrapped, oldPassphrase);
        return Wrap(dataKey, newPassphrase);
    }
}