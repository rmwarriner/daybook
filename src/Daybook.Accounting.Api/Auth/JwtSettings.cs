namespace Daybook.Accounting.Api.Auth;

/// <summary>
/// Shared between JWT issuance (<c>JwtTokenFactory</c>) and JWT validation
/// (<c>Program.cs</c>'s <c>AddJwtBearer</c> wiring) — issuer/audience must
/// match exactly between the two, so both sides read from here rather than
/// each hard-coding their own copy.
/// </summary>
/// <remarks>
/// Public, like <see cref="DaybookClaimTypes"/> — issuer/audience are part
/// of the issued token's external contract, not an internal detail.
/// </remarks>
public static class JwtSettings
{
    public const string Issuer = "daybook";
    public const string Audience = "daybook-api";
}