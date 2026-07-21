using System.Data;

using Daybook.Accounting.Infrastructure;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Daybook.Accounting.Api.Auth;

/// <summary>
/// <c>/v1/auth/register</c> and <c>/v1/auth/login</c> (spec §8). Auth
/// orchestration lives directly here, in Api, not behind a new
/// Application-layer use case — CLAUDE.md's own architecture diagram
/// assigns "auth" to Api's job description, and ASP.NET Core
/// Identity's <see cref="UserManager{TUser}"/> is exactly the kind of
/// ASP.NET-specific plumbing that belongs at this layer.
/// </summary>
internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/auth/register", RegisterAsync);
        return endpoints;
    }

    /// <summary>
    /// Bootstrap-only: succeeds exactly once, for whoever gets there first
    /// while zero users exist. An open, always-available registration
    /// endpoint on a self-hosted financial API is a real vulnerability
    /// surface for no v1 benefit — v1 is "effectively single-user" (spec
    /// §8), so there is never a legitimate second registration.
    /// </summary>
    private static async Task<IResult> RegisterAsync(
        RegisterRequest request, UserManager<ApplicationUser> userManager, DaybookDbContext dbContext)
    {
        // Wraps the exists-check and the create in one transaction so
        // SQLite's single-writer lock serializes concurrent bootstrap
        // attempts, rather than letting two check-then-act sequences
        // interleave. Not a hard guarantee against every interleaving
        // (SQLite's default deferred transaction only takes a write lock
        // at the first write, not at the read) - cheap, worthwhile
        // insurance for a low-severity, single-household-scale race, not a
        // claim of full serializability.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        if (await userManager.Users.AnyAsync())
        {
            return Results.Conflict();
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return Results.BadRequest(result.Errors);
        }

        await transaction.CommitAsync();
        return Results.Created($"/v1/auth/register/{user.Id}", new RegisterResponse(user.Id));
    }
}

internal sealed record RegisterRequest(string Email, string Password);

internal sealed record RegisterResponse(Guid UserId);