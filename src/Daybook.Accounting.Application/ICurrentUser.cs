using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// The acting user (CLAUDE.md's determinism rule — no ambient identity in
/// domain code). Core itself never depends on this: <see cref="Journal.Post"/>
/// and <see cref="Journal.Reverse"/> already take an explicit
/// <c>Guid postedByUserId</c> instead of reading one ambiently.
/// </summary>
/// <remarks>
/// No real implementation exists yet — "the current user" means "whoever
/// is authenticated for this request," which needs auth/HTTP-context
/// machinery this repo hasn't built. Only the port is defined here, so the
/// documented contract (CLAUDE.md: "Application... defines ports
/// (`IJournalStore`, `IClock`, `ICurrentUser`, …)") exists in code; a real
/// implementation is a future Api-layer concern.
/// </remarks>
public interface ICurrentUser
{
    Guid UserId { get; }
}