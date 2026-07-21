namespace Daybook.Accounting.Api.Auth;

/// <summary>
/// <see cref="Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions.MapInboundClaims"/>
/// defaults to <c>true</c> and silently remaps well-known short claim names
/// (e.g. <c>"sub"</c>) to legacy WS-* URIs on the way in — a claim issued
/// under <c>"sub"</c> would round-trip under a different key than the one
/// it was issued with. Using a private, non-standard claim name sidesteps
/// that entirely rather than fighting the remap.
/// </summary>
/// <remarks>
/// Public, unlike most of this folder — the claim name is part of the
/// issued token's external contract (any future client inspecting a JWT
/// needs it), not an internal implementation detail.
/// </remarks>
public static class DaybookClaimTypes
{
    public const string UserId = "daybook:user_id";
}