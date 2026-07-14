using CsCheck;

namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 0 smoke tests: prove the test harness (xUnit + AwesomeAssertions +
/// CsCheck) is wired up and green before any domain code exists.
/// </summary>
public class HarnessSmokeTests
{
    [Fact]
    public void Xunit_and_AwesomeAssertions_are_wired_up()
    {
        var sum = 2 + 2;

        sum.Should().Be(4);
    }

    [Fact]
    public void CsCheck_property_library_is_wired_up()
    {
        // Trivial algebraic property: integer addition is commutative.
        Gen.Int.Select(Gen.Int).Sample((a, b) => a + b == b + a);
    }
}