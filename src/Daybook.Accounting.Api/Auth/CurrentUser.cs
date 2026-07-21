using Daybook.Accounting.Application;

namespace Daybook.Accounting.Api.Auth;

/// <summary>
/// The real <see cref="ICurrentUser"/> (spec §8) — first port
/// implementation in this codebase to live in Api rather than
/// Infrastructure, deliberately: <see cref="IHttpContextAccessor"/> is a
/// web-framework type, and Infrastructure's job description (CLAUDE.md:
/// "EF Core/SQLite, Serilog, crypto") has no web-framework item. Auth is
/// explicitly Api's job per CLAUDE.md's architecture diagram.
/// </summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid UserId
    {
        get
        {
            var claim = httpContextAccessor.HttpContext?.User.FindFirst(DaybookClaimTypes.UserId)
                ?? throw new InvalidOperationException("No authenticated user on the current request.");
            return Guid.Parse(claim.Value);
        }
    }
}