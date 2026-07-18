namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 13 — the <see cref="Reference"/> value object's own local
/// invariants (spec §4.3.1): a defined <see cref="ReferenceType"/> and a
/// required, trimmed <c>Value</c>.
/// </summary>
public class ReferenceTests
{
    [Fact]
    public void Create_with_valid_fields_succeeds()
    {
        var result = Reference.Create(ReferenceType.Check, "1234");

        result.IsSuccess.Should().BeTrue();
        var reference = result.Value;
        reference.Type.Should().Be(ReferenceType.Check);
        reference.Value.Should().Be("1234");
    }

    [Fact]
    public void Create_trims_value()
    {
        var reference = Reference.Create(ReferenceType.Check, "  1234  ").Value;

        reference.Value.Should().Be("1234");
    }

    [Fact]
    public void Create_rejects_an_undefined_reference_type()
    {
        var result = Reference.Create((ReferenceType)(-1), "1234");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reference.type.invalid");
    }

    [Fact]
    public void Create_rejects_null_value()
    {
        var act = () => Reference.Create(ReferenceType.Check, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_value(string blank)
    {
        var result = Reference.Create(ReferenceType.Check, blank);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reference.value.required");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
    }

    [Fact]
    public void References_with_the_same_type_and_value_are_equal()
    {
        var a = Reference.Create(ReferenceType.Check, "1234").Value;
        var b = Reference.Create(ReferenceType.Check, "1234").Value;

        a.Should().Be(b);
    }

    [Fact]
    public void References_with_different_types_are_not_equal_even_with_the_same_value()
    {
        var check = Reference.Create(ReferenceType.Check, "1234").Value;
        var ach = Reference.Create(ReferenceType.ACH, "1234").Value;

        check.Should().NotBe(ach);
    }
}