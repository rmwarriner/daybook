namespace Daybook.Accounting.Core;

/// <summary>
/// A recurring calendar date with no year — e.g. "April 6" for a UK-style
/// fiscal year, or "January 1" for a calendar-year book. General-purpose,
/// not tied to any one field; <see cref="Book"/>'s <c>FiscalYearStart</c>
/// (spec §4.1) is its first use.
/// </summary>
public sealed record MonthDay
{
    /// <summary>A leap year, used only to validate February 29 as a legitimate recurring date.</summary>
    private const int LeapYearReference = 2024;

    public int Month { get; }

    public int Day { get; }

    private MonthDay(int month, int day)
    {
        Month = month;
        Day = day;
    }

    public static Result<MonthDay> Create(int month, int day)
    {
        if (month is < 1 or > 12)
        {
            return new Error(
                "month_day.month.invalid",
                ErrorCategory.Validation,
                $"Month must be between 1 and 12, but was {month}.",
                ["Provide a month between 1 (January) and 12 (December)."]);
        }

        var daysInMonth = DateTime.DaysInMonth(LeapYearReference, month);
        if (day < 1 || day > daysInMonth)
        {
            return new Error(
                "month_day.day.invalid",
                ErrorCategory.Validation,
                $"Day {day} does not exist in month {month}, which has at most {daysInMonth} days.",
                [$"Provide a day between 1 and {daysInMonth}."]);
        }

        return new MonthDay(month, day);
    }
}