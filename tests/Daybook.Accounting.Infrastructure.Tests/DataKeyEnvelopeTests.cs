using System.Security.Cryptography;

namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// <see cref="DataKeyEnvelope"/> — envelope encryption for the database's
/// data-encryption-key (spec §13.5): a passphrase-derived
/// key-encryption-key wraps a randomly-generated data-encryption-key, so
/// rotating the passphrase only re-wraps the DEK, never a full-database
/// re-encryption.
/// </summary>
public class DataKeyEnvelopeTests
{
    [Fact]
    public void GenerateDataKey_returns_a_256_bit_key()
    {
        DataKeyEnvelope.GenerateDataKey().Should().HaveCount(32);
    }

    [Fact]
    public void GenerateDataKey_returns_a_fresh_key_each_time()
    {
        DataKeyEnvelope.GenerateDataKey().Should().NotEqual(DataKeyEnvelope.GenerateDataKey());
    }

    [Fact]
    public void Wrap_then_Unwrap_round_trips_the_original_data_key()
    {
        var dataKey = DataKeyEnvelope.GenerateDataKey();

        var wrapped = DataKeyEnvelope.Wrap(dataKey, "correct horse battery staple");
        var unwrapped = DataKeyEnvelope.Unwrap(wrapped, "correct horse battery staple");

        unwrapped.Should().Equal(dataKey);
    }

    [Fact]
    public void Unwrap_with_the_wrong_passphrase_throws()
    {
        var dataKey = DataKeyEnvelope.GenerateDataKey();
        var wrapped = DataKeyEnvelope.Wrap(dataKey, "correct horse battery staple");

        var act = () => DataKeyEnvelope.Unwrap(wrapped, "wrong passphrase");

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Rotate_unwraps_with_the_new_passphrase_to_the_same_data_key()
    {
        var dataKey = DataKeyEnvelope.GenerateDataKey();
        var wrapped = DataKeyEnvelope.Wrap(dataKey, "old passphrase");

        var rotated = DataKeyEnvelope.Rotate(wrapped, "old passphrase", "new passphrase");

        DataKeyEnvelope.Unwrap(rotated, "new passphrase").Should().Equal(dataKey);
    }

    [Fact]
    public void Rotate_makes_the_old_passphrase_no_longer_work()
    {
        var dataKey = DataKeyEnvelope.GenerateDataKey();
        var wrapped = DataKeyEnvelope.Wrap(dataKey, "old passphrase");

        var rotated = DataKeyEnvelope.Rotate(wrapped, "old passphrase", "new passphrase");

        var act = () => DataKeyEnvelope.Unwrap(rotated, "old passphrase");
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Rotate_rejects_the_wrong_old_passphrase()
    {
        var dataKey = DataKeyEnvelope.GenerateDataKey();
        var wrapped = DataKeyEnvelope.Wrap(dataKey, "old passphrase");

        var act = () => DataKeyEnvelope.Rotate(wrapped, "wrong passphrase", "new passphrase");

        act.Should().Throw<CryptographicException>();
    }
}