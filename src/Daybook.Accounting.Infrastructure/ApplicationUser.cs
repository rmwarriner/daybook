using Microsoft.AspNetCore.Identity;

namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// The real user record backing <c>ICurrentUser</c> (spec §8). No custom
/// properties yet — v1 is "effectively single-user," so nothing beyond
/// Identity's own base fields is needed.
/// </summary>
/// <remarks>
/// Deliberately not paired with <c>IdentityRole</c>: ASP.NET Identity's
/// roles are global, but the spec's future authorization model is
/// per-<b>book</b> roles (Owner/Editor/Viewer scoped to a specific book) —
/// structurally incompatible with Identity's role tables. That model will
/// be a bespoke join entity (book id + user id + role) when it's built, not
/// <c>AspNetRoles</c>/<c>AspNetUserRoles</c>. See
/// <see cref="DaybookDbContext"/>'s use of <c>IdentityUserContext</c>
/// rather than <c>IdentityDbContext</c>.
/// </remarks>
public sealed class ApplicationUser : IdentityUser<Guid>;