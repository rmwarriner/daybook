namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 12 — the <see cref="Tag"/> entity's own local invariants (spec
/// §4.8): a required <c>Name</c>, and the archive/unarchive lifecycle.
/// Uniqueness of <c>Name</c> per book needs the rest of the registry, so
/// that lives in <c>TagRegistryTests</c> instead.
/// </summary>
public class TagTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_with_a_valid_name_succeeds()
    {
        var result = Tag.Create(Id, "Business");

        result.IsSuccess.Should().BeTrue();
        var tag = result.Value;
        tag.Id.Should().Be(Id);
        tag.Name.Should().Be("Business");
        tag.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Create_trims_name()
    {
        var tag = Tag.Create(Id, "  Business  ").Value;

        tag.Name.Should().Be("Business");
    }

    [Fact]
    public void Create_rejects_empty_id()
    {
        var act = () => Tag.Create(Guid.Empty, "Business");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_null_name()
    {
        var act = () => Tag.Create(Id, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string blank)
    {
        var result = Tag.Create(Id, blank);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("tag.name.required");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
    }

    [Fact]
    public void Rename_replaces_the_name()
    {
        var tag = Tag.Create(Id, "Business").Value;

        var renamed = tag.Rename("Personal").Value;

        renamed.Name.Should().Be("Personal");
        renamed.Id.Should().Be(tag.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_blank_name(string blank)
    {
        var tag = Tag.Create(Id, "Business").Value;

        var result = tag.Rename(blank);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("tag.name.required");
    }

    [Fact]
    public void Archive_marks_the_tag_archived()
    {
        var tag = Tag.Create(Id, "Business").Value;

        var archived = tag.Archive();

        archived.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Unarchive_clears_the_archived_flag()
    {
        var tag = Tag.Create(Id, "Business").Value.Archive();

        var unarchived = tag.Unarchive();

        unarchived.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Tags_with_the_same_id_are_equal_even_if_other_fields_differ()
    {
        var original = Tag.Create(Id, "Business").Value;
        var renamed = original.Rename("Personal").Value;

        renamed.Should().Be(original);
    }
}