using System.Text;

namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// <see cref="Argon2Kdf"/> — the passphrase key-derivation function (spec
/// §13.5). No timing/determinism concerns beyond "same inputs, same
/// output" - Argon2id itself is well-tested upstream; this only proves the
/// wrapper wires salt/purpose/passphrase/output-length through correctly.
/// </summary>
public class Argon2KdfTests
{
    private static byte[] ASalt() => Encoding.UTF8.GetBytes("0123456789abcdef");

    [Fact]
    public void DeriveKey_is_deterministic_for_the_same_inputs()
    {
        var salt = ASalt();
        var purpose = "envelope-kek"u8.ToArray();

        var first = Argon2Kdf.DeriveKey("correct horse battery staple", salt, purpose);
        var second = Argon2Kdf.DeriveKey("correct horse battery staple", salt, purpose);

        first.Should().Equal(second);
    }

    [Fact]
    public void DeriveKey_returns_the_requested_output_length()
    {
        var key = Argon2Kdf.DeriveKey("passphrase", ASalt(), "purpose"u8.ToArray(), outputLength: 16);

        key.Should().HaveCount(16);
    }

    [Fact]
    public void DeriveKey_defaults_to_a_256_bit_key()
    {
        var key = Argon2Kdf.DeriveKey("passphrase", ASalt(), "purpose"u8.ToArray());

        key.Should().HaveCount(32);
    }

    [Fact]
    public void DeriveKey_differs_for_a_different_passphrase()
    {
        var salt = ASalt();
        var purpose = "envelope-kek"u8.ToArray();

        var first = Argon2Kdf.DeriveKey("passphrase-one", salt, purpose);
        var second = Argon2Kdf.DeriveKey("passphrase-two", salt, purpose);

        first.Should().NotEqual(second);
    }

    [Fact]
    public void DeriveKey_differs_for_a_different_salt()
    {
        var purpose = "envelope-kek"u8.ToArray();

        var first = Argon2Kdf.DeriveKey("passphrase", ASalt(), purpose);
        var second = Argon2Kdf.DeriveKey("passphrase", Encoding.UTF8.GetBytes("fedcba9876543210"), purpose);

        first.Should().NotEqual(second);
    }

    [Fact]
    public void DeriveKey_differs_for_a_different_purpose_even_with_the_same_passphrase_and_salt()
    {
        var salt = ASalt();

        var kek = Argon2Kdf.DeriveKey("passphrase", salt, "envelope-kek"u8.ToArray());
        var hmacKey = Argon2Kdf.DeriveKey("passphrase", salt, "hash-chain-hmac"u8.ToArray());

        kek.Should().NotEqual(hmacKey);
    }
}