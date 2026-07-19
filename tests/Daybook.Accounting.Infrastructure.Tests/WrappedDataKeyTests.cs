namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// <see cref="WrappedDataKey"/> — a wrapped data-encryption-key plus the
/// Argon2id parameters used to derive its key-encryption-key (spec §13.5),
/// stored alongside the wrapped output so a future recalibration of the
/// defaults can't silently break reproducing an already-derived key. This
/// is what lives on disk next to the encrypted database file, never inside
/// it - the DB can't hold its own key.
/// </summary>
public sealed class WrappedDataKeyTests : IDisposable
{
    private readonly string _path;

    public WrappedDataKeyTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"daybook-test-{Guid.NewGuid()}.key.json");
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static WrappedDataKey AWrappedDataKey() => new(
        Salt: [1, 2, 3],
        Nonce: [4, 5, 6],
        Ciphertext: [7, 8, 9, 10],
        Tag: [11, 12, 13],
        MemorySizeKb: Argon2Kdf.DefaultMemorySizeKb,
        Iterations: Argon2Kdf.DefaultIterations,
        DegreeOfParallelism: Argon2Kdf.DefaultDegreeOfParallelism);

    [Fact]
    public void Wrapped_data_keys_with_the_same_field_values_are_equal()
    {
        AWrappedDataKey().Should().Be(AWrappedDataKey());
    }

    [Fact]
    public void Wrapped_data_keys_with_a_different_ciphertext_are_not_equal()
    {
        var a = AWrappedDataKey();
        var b = a with { Ciphertext = [99, 99, 99, 99] };

        a.Should().NotBe(b);
    }

    [Fact]
    public void Save_then_Load_round_trips_every_field()
    {
        var wrapped = AWrappedDataKey();

        wrapped.Save(_path);
        var loaded = WrappedDataKey.Load(_path);

        loaded.Should().Be(wrapped);
    }
}