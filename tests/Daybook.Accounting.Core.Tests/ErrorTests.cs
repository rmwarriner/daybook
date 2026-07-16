namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 2 — the <see cref="Error"/> value object that backs
/// <see cref="Result{T}"/>. Covers spec §10: every business-rule failure
/// carries a stable machine-readable code, a category, a human message, and
/// actionable recovery options — never a bare "an error occurred."
/// </summary>
public class ErrorTests
{
    [Fact]
    public void Carries_code_category_message_and_recovery()
    {
        var error = new Error(
            "account.name.required",
            ErrorCategory.Validation,
            "Account name must not be empty.",
            ["Provide a non-empty Name."]);

        error.Code.Should().Be("account.name.required");
        error.Category.Should().Be(ErrorCategory.Validation);
        error.Message.Should().Be("Account name must not be empty.");
        error.Recovery.Should().ContainSingle().Which.Should().Be("Provide a non-empty Name.");
    }

    [Fact]
    public void Recovery_defaults_to_empty_when_not_supplied()
    {
        var error = new Error("account.not_found", ErrorCategory.Validation, "No such account.");

        error.Recovery.Should().BeEmpty();
    }

    [Fact]
    public void Equal_field_values_are_value_equal()
    {
        var a = new Error("x.y", ErrorCategory.Conflict, "msg", ["fix it"]);
        var b = new Error("x.y", ErrorCategory.Conflict, "msg", ["fix it"]);

        a.Should().Be(b);
    }
}