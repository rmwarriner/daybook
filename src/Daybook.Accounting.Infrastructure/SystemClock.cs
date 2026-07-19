using Daybook.Accounting.Application;

namespace Daybook.Accounting.Infrastructure;

/// <summary>EF/SQLite-free implementation of <see cref="IClock"/> — the real wall clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}