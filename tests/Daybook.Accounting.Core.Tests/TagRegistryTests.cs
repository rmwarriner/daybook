namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 12 — <see cref="TagRegistry"/>, the per-book collection that
/// enforces the invariant a lone <see cref="Tag"/> cannot check by itself
/// (spec §4.8): unique <c>Name</c>, case-insensitive, trimmed.
/// </summary>
public class TagRegistryTests
{
    // ---- Empty / lookup -------------------------------------------------

    [Fact]
    public void Empty_registry_has_no_tags()
    {
        var registry = TagRegistry.Empty();

        registry.Tags.Should().BeEmpty();
        registry.Find(Guid.NewGuid()).Should().BeNull();
        registry.FindByName("Business").Should().BeNull();
    }

    // ---- Create -----------------------------------------------------------

    [Fact]
    public void Create_adds_a_findable_tag_by_id_and_name()
    {
        var registry = TagRegistry.Empty();
        var id = Guid.NewGuid();

        var result = registry.Create(id, "Business");

        result.IsSuccess.Should().BeTrue();
        registry.Find(id).Should().Be(result.Value);
        registry.FindByName("Business").Should().Be(result.Value);
    }

    [Fact]
    public void Create_finds_by_name_case_insensitively()
    {
        var registry = TagRegistry.Empty();
        registry.Create(Guid.NewGuid(), "Business");

        registry.FindByName("BUSINESS").Should().NotBeNull();
    }

    [Fact]
    public void Create_rejects_a_duplicate_id()
    {
        var registry = TagRegistry.Empty();
        var id = Guid.NewGuid();
        registry.Create(id, "Business");

        var result = registry.Create(id, "Personal");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("tag.id.duplicate");
    }

    [Fact]
    public void Create_rejects_a_name_already_used_case_insensitively()
    {
        var registry = TagRegistry.Empty();
        registry.Create(Guid.NewGuid(), "Business");

        var result = registry.Create(Guid.NewGuid(), "BUSINESS");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("tag.name.duplicate");
    }

    [Fact]
    public void Create_bubbles_up_tag_level_validation_failures()
    {
        var registry = TagRegistry.Empty();

        var result = registry.Create(Guid.NewGuid(), "   ");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("tag.name.required");
    }

    // ---- Rename -------------------------------------------------------------

    [Fact]
    public void Rename_updates_the_name_index_freeing_the_old_name()
    {
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;

        registry.Rename(tag.Id, "Personal").IsSuccess.Should().BeTrue();

        registry.FindByName("Business").Should().BeNull();
        registry.FindByName("Personal").Should().NotBeNull();
    }

    [Fact]
    public void Rename_to_its_own_current_value_is_not_treated_as_a_duplicate()
    {
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;

        var result = registry.Rename(tag.Id, "Business");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Rename_rejects_a_name_already_used_by_another_tag()
    {
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;
        registry.Create(Guid.NewGuid(), "Personal");

        var result = registry.Rename(tag.Id, "PERSONAL");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("tag.name.duplicate");
    }

    [Fact]
    public void Rename_rejects_an_unknown_tag()
    {
        var registry = TagRegistry.Empty();

        var result = registry.Rename(Guid.NewGuid(), "Business");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("tag.not_found");
    }

    // ---- Archive / Unarchive ------------------------------------------------

    [Fact]
    public void Archive_marks_the_tag_archived()
    {
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;

        var result = registry.Archive(tag.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsArchived.Should().BeTrue();
        registry.Find(tag.Id)!.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Unarchive_clears_the_archived_flag()
    {
        var registry = TagRegistry.Empty();
        var tag = registry.Create(Guid.NewGuid(), "Business").Value;
        registry.Archive(tag.Id);

        var result = registry.Unarchive(tag.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Archive_rejects_an_unknown_tag()
    {
        var registry = TagRegistry.Empty();

        var result = registry.Archive(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("tag.not_found");
    }
}
