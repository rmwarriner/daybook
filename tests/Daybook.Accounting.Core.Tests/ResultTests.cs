namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 2 — <see cref="Result{T}"/>, the success/failure wrapper Core
/// and Application use instead of throwing for expected business-rule
/// failures (spec §10, CLAUDE.md golden rule 6).
/// </summary>
public class ResultTests
{
    private static readonly Error SampleError =
        new("some.error", ErrorCategory.Validation, "Something was wrong.", ["Fix the thing."]);

    [Fact]
    public void Success_carries_the_value_and_no_error()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Failure_carries_the_error_and_no_value()
    {
        var result = Result<int>.Failure(SampleError);

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SampleError);
    }

    [Fact]
    public void Value_on_a_failed_result_throws()
    {
        var result = Result<int>.Failure(SampleError);

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Error_on_a_successful_result_throws()
    {
        var result = Result<int>.Success(1);

        var act = () => result.Error;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Implicitly_converts_from_a_value_to_success()
    {
        Result<int> result = 7;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(7);
    }

    [Fact]
    public void Implicitly_converts_from_an_error_to_failure()
    {
        Result<int> result = SampleError;

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SampleError);
    }
}