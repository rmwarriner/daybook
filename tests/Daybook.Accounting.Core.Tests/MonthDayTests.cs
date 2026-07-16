namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 6 — <see cref="MonthDay"/>: a recurring calendar date with no
/// year, needed for <see cref="Book"/>'s <c>FiscalYearStart</c> (spec §4.1,
/// "Month/day"). A general-purpose value type, not Book-specific.
/// </summary>
public class MonthDayTests
{
    [Fact]
    public void Create_with_a_valid_month_and_day_succeeds()
    {
        var result = MonthDay.Create(4, 6);

        result.IsSuccess.Should().BeTrue();
        result.Value.Month.Should().Be(4);
        result.Value.Day.Should().Be(6);
    }

    [Fact]
    public void Create_allows_february_29_as_a_recurring_date()
    {
        var result = MonthDay.Create(2, 29);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public void Create_rejects_an_out_of_range_month(int month)
    {
        var result = MonthDay.Create(month, 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("month_day.month.invalid");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
    }

    [Theory]
    [InlineData(4, 31)] // April has 30 days
    [InlineData(2, 30)] // February never has 30 days
    [InlineData(1, 0)]
    [InlineData(1, 32)]
    public void Create_rejects_a_day_that_does_not_exist_in_the_month(int month, int day)
    {
        var result = MonthDay.Create(month, day);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("month_day.day.invalid");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
    }

    [Fact]
    public void Equal_month_and_day_are_value_equal()
    {
        MonthDay.Create(4, 6).Value.Should().Be(MonthDay.Create(4, 6).Value);
    }
}