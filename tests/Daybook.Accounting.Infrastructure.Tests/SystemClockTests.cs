namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// <see cref="SystemClock"/> — the one real <c>IClock</c> implementation
/// (CLAUDE.md's determinism ports). Wall-clock access, so this is a tight
/// bound rather than an exact-value assertion, but not flaky: real time
/// only ever moves forward, so the reading must fall between a timestamp
/// taken immediately before and one taken immediately after.
/// </summary>
public class SystemClockTests
{
    [Fact]
    public void UtcNow_reflects_the_real_clock()
    {
        var before = DateTimeOffset.UtcNow;

        var reading = new SystemClock().UtcNow;

        var after = DateTimeOffset.UtcNow;
        reading.Should().BeOnOrAfter(before);
        reading.Should().BeOnOrBefore(after);
    }
}