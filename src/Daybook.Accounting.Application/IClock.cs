using Daybook.Accounting.Core;

namespace Daybook.Accounting.Application;

/// <summary>
/// The current time (CLAUDE.md's determinism rule — no wall-clock access
/// in domain code). Core itself never depends on this: <see cref="Journal.Post"/>
/// and <see cref="Journal.Reverse"/> already take an explicit
/// <c>DateTimeOffset</c> instead of reading one ambiently. This port exists
/// for whatever calls them — so a use case can supply a real reading in
/// production and a controlled one in tests.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}