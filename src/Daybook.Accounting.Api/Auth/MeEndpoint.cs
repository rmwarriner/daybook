using Daybook.Accounting.Application;

namespace Daybook.Accounting.Api.Auth;

/// <summary>
/// <c>GET /v1/me</c> (spec §8) — a "whoami" check proving the full chain
/// (JWT → <c>ClaimsPrincipal</c> → <see cref="ICurrentUser"/>) resolves for
/// real, and a genuinely useful endpoint for the eventual CLI reference
/// client beyond that. A real, permanent endpoint from the moment it ships
/// (CLAUDE.md's additive-only versioning discipline), not test scaffolding.
/// </summary>
internal static class MeEndpoint
{
    public static IEndpointRouteBuilder MapMeEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("/v1/me", (ICurrentUser currentUser) => Results.Ok(new MeResponse(currentUser.UserId)))
            .RequireAuthorization();
        return endpoints;
    }
}

internal sealed record MeResponse(Guid UserId);