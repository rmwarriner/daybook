namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 2 — the <see cref="Account"/> entity's own, local invariants
/// (spec §4.2): required <c>Name</c>, optional-but-non-blank <c>Code</c>, a
/// valid <see cref="AccountType"/>, and the placeholder/active flags.
/// Invariants that need the rest of the tree (unique code, type inheritance,
/// no cycles) live in <c>ChartOfAccountsTests</c> instead — a single
/// <see cref="Account"/> cannot check those alone.
/// </summary>
public class AccountTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ParentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ---- Construction --------------------------------------------------

    [Fact]
    public void Create_with_valid_fields_succeeds()
    {
        var result = Account.Create(Id, "Checking", AccountType.Asset, code: "1000", parentAccountId: ParentId);

        result.IsSuccess.Should().BeTrue();
        var account = result.Value;
        account.Id.Should().Be(Id);
        account.Name.Should().Be("Checking");
        account.Type.Should().Be(AccountType.Asset);
        account.Code.Should().Be("1000");
        account.ParentAccountId.Should().Be(ParentId);
        account.NormalBalance.Should().Be(Side.Debit);
    }

    [Fact]
    public void Create_defaults_code_to_null_parent_to_null_placeholder_false_active_true()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;

        account.Code.Should().BeNull();
        account.ParentAccountId.Should().BeNull();
        account.IsPlaceholder.Should().BeFalse();
        account.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_trims_name_and_code()
    {
        var account = Account.Create(Id, "  Checking  ", AccountType.Asset, code: "  1000  ").Value;

        account.Name.Should().Be("Checking");
        account.Code.Should().Be("1000");
    }

    [Fact]
    public void Create_rejects_null_id()
    {
        var act = () => Account.Create(Guid.Empty, "Checking", AccountType.Asset);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_null_name()
    {
        var act = () => Account.Create(Id, null!, AccountType.Asset);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string blank)
    {
        var result = Account.Create(Id, blank, AccountType.Asset);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.name.required");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
        result.Error.Recovery.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_code_when_provided(string blank)
    {
        var result = Account.Create(Id, "Checking", AccountType.Asset, code: blank);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.code.blank");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
    }

    [Fact]
    public void Create_rejects_undefined_account_type()
    {
        var result = Account.Create(Id, "Checking", (AccountType)(-1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.type.invalid");
    }

    [Theory]
    [InlineData(AccountType.Asset, Side.Debit)]
    [InlineData(AccountType.Liability, Side.Credit)]
    [InlineData(AccountType.Equity, Side.Credit)]
    [InlineData(AccountType.Income, Side.Credit)]
    [InlineData(AccountType.Expense, Side.Debit)]
    public void NormalBalance_matches_the_accounts_type(AccountType type, Side expected)
    {
        Account.Create(Id, "Some Account", type).Value.NormalBalance.Should().Be(expected);
    }

    // ---- Rename ----------------------------------------------------------

    [Fact]
    public void Rename_trims_and_replaces_the_name_leaving_everything_else()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset, code: "1000").Value;

        var renamed = account.Rename("  Primary Checking  ").Value;

        renamed.Name.Should().Be("Primary Checking");
        renamed.Code.Should().Be("1000");
        renamed.Type.Should().Be(AccountType.Asset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_blank_name(string blank)
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;

        var result = account.Rename(blank);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.name.required");
    }

    // ---- SetCode -----------------------------------------------------------

    [Fact]
    public void SetCode_trims_and_replaces_the_code()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;

        account.SetCode("  2000  ").Value.Code.Should().Be("2000");
    }

    [Fact]
    public void SetCode_null_clears_the_code()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset, code: "1000").Value;

        account.SetCode(null).Value.Code.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SetCode_rejects_blank_code(string blank)
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;

        var result = account.SetCode(blank);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.code.blank");
    }

    // ---- Active / placeholder flags ----------------------------------------

    [Fact]
    public void Deactivate_and_Activate_toggle_IsActive_only()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;

        var inactive = account.Deactivate();
        inactive.IsActive.Should().BeFalse();
        inactive.Name.Should().Be(account.Name);

        inactive.Activate().IsActive.Should().BeTrue();
    }

    [Fact]
    public void MarkAsPlaceholder_and_ClearPlaceholder_toggle_IsPlaceholder_only()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;

        var placeholder = account.MarkAsPlaceholder();
        placeholder.IsPlaceholder.Should().BeTrue();
        placeholder.Name.Should().Be(account.Name);

        placeholder.ClearPlaceholder().IsPlaceholder.Should().BeFalse();
    }

    // ---- Tags (spec §4.8) ---------------------------------------------------

    [Fact]
    public void Create_defaults_to_no_tags()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;

        account.Tags.Should().BeEmpty();
    }

    [Fact]
    public void AddTag_adds_to_the_tag_set_leaving_everything_else()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;
        var tagId = Guid.NewGuid();

        var tagged = account.AddTag(tagId);

        tagged.Tags.Should().Equal(tagId);
        tagged.Name.Should().Be(account.Name);
    }

    [Fact]
    public void AddTag_is_a_set_adding_the_same_tag_twice_does_not_duplicate()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;
        var tagId = Guid.NewGuid();

        var tagged = account.AddTag(tagId).AddTag(tagId);

        tagged.Tags.Should().Equal(tagId);
    }

    [Fact]
    public void RemoveTag_removes_from_the_tag_set()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;
        var tagId = Guid.NewGuid();
        var tagged = account.AddTag(tagId);

        var untagged = tagged.RemoveTag(tagId);

        untagged.Tags.Should().BeEmpty();
    }

    [Fact]
    public void RemoveTag_of_a_tag_not_present_is_a_no_op()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;

        var result = account.RemoveTag(Guid.NewGuid());

        result.Tags.Should().BeEmpty();
    }

    [Fact]
    public void AddTag_leaves_the_original_instance_untouched()
    {
        var account = Account.Create(Id, "Checking", AccountType.Asset).Value;

        account.AddTag(Guid.NewGuid());

        account.Tags.Should().BeEmpty();
    }

    // ---- Identity equality (entity, not value object) ----------------------

    [Fact]
    public void Accounts_with_the_same_id_are_equal_even_if_other_fields_differ()
    {
        var original = Account.Create(Id, "Checking", AccountType.Asset).Value;
        var renamed = original.Rename("Primary Checking").Value;

        renamed.Should().Be(original);
    }

    [Fact]
    public void Accounts_with_different_ids_are_not_equal_even_with_identical_fields()
    {
        var a = Account.Create(Id, "Checking", AccountType.Asset).Value;
        var b = Account.Create(ParentId, "Checking", AccountType.Asset).Value;

        a.Should().NotBe(b);
    }
}