using System.Text.Json;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// A data-encryption-key wrapped by a passphrase-derived
/// key-encryption-key (spec §13.5), plus the Argon2id parameters used to
/// derive that KEK. Stored alongside the wrapped output — not read from
/// today's <see cref="Argon2Kdf"/> defaults at unwrap time — so a future
/// recalibration of those defaults can't silently break reproducing an
/// already-derived key.
/// </summary>
/// <remarks>
/// This is what lives on disk next to the encrypted database file, never
/// inside it — the database can't hold the key that opens it.
/// </remarks>
public sealed record WrappedDataKey(
    byte[] Salt,
    byte[] Nonce,
    byte[] Ciphertext,
    byte[] Tag,
    int MemorySizeKb,
    int Iterations,
    int DegreeOfParallelism)
{
    public bool Equals(WrappedDataKey? other) =>
        other is not null &&
        Salt.SequenceEqual(other.Salt) &&
        Nonce.SequenceEqual(other.Nonce) &&
        Ciphertext.SequenceEqual(other.Ciphertext) &&
        Tag.SequenceEqual(other.Tag) &&
        MemorySizeKb == other.MemorySizeKb &&
        Iterations == other.Iterations &&
        DegreeOfParallelism == other.DegreeOfParallelism;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Salt);
        hash.AddBytes(Nonce);
        hash.AddBytes(Ciphertext);
        hash.AddBytes(Tag);
        hash.Add(MemorySizeKb);
        hash.Add(Iterations);
        hash.Add(DegreeOfParallelism);
        return hash.ToHashCode();
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this));

    public static WrappedDataKey Load(string path) =>
        JsonSerializer.Deserialize<WrappedDataKey>(File.ReadAllText(path))!;
}