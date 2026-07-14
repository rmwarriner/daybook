namespace Daybook.Accounting.Application.Tests;

/// <summary>Milestone 0 smoke test: proves the Application test harness is green.</summary>
public class HarnessSmokeTests
{
    [Fact]
    public void Harness_is_wired_up()
    {
        true.Should().BeTrue();
    }
}