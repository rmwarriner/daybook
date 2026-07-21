using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Daybook.Accounting.Api.Auth;

/// <summary>
/// Issues a real, inspectable JWT for a user id (spec §8) — signed with the
/// same HMAC key <c>Program.cs</c>'s JWT Bearer validation reads via
/// <see cref="Daybook.Accounting.Infrastructure.PassphraseFile"/>. Symmetric
/// signing is correct here: the Api is both sole issuer and sole verifier,
/// same trust boundary as the DB passphrase.
/// </summary>
internal sealed class JwtTokenFactory(byte[] signingKeyBytes)
{
    /// <summary>
    /// A session-length lifetime is enough for an "effectively single-user,
    /// self-hosted household" threat model (spec §8) — refresh tokens are
    /// deliberately not built; re-running login on expiry is an acceptable
    /// manual step.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private static readonly JsonWebTokenHandler Handler = new();

    public string CreateToken(Guid userId)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = JwtSettings.Issuer,
            Audience = JwtSettings.Audience,
            Expires = DateTime.UtcNow.Add(Lifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(signingKeyBytes), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { [DaybookClaimTypes.UserId] = userId.ToString() },
        };

        return Handler.CreateToken(descriptor);
    }
}