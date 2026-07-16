using CsCheck;

namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestones 2 (issue #3) and 3 (issue #4) — the <see cref="Account"/>
/// entity. Written test-first (red -> green -> refactor). Covers spec §4.2:
/// five root types with an enforced normal balance, plus hierarchy (type
/// inheritance, no-cycle, reparenting) and active/inactive status. Optional
/// code, display path, and tags are separate slices (issues #5-#7) and are
/// out of scope here.
/// </summary>
public class AccountTests
{
    private static readonly Gen<AccountType> GenAccountType =
        Gen.Int[0, 4].Select(i => (AccountType)i);

    // ---- Construction --------------------------------------------------

    [Fact]
    public void Create_sets_name_and_type()
    {
        var account = Account.Create("Checking", AccountType.Asset);

        account.Name.Should().Be("Checking");
        account.Type.Should().Be(AccountType.Asset);
    }

    [Fact]
    public void Create_assigns_a_unique_stable_account_id()
    {
        var first = Account.Create("Checking", AccountType.Asset);
        var second = Account.Create("Savings", AccountType.Asset);

        first.AccountId.Should().NotBe(Guid.Empty);
        first.AccountId.Should().NotBe(second.AccountId);
    }

    [Fact]
    public void Create_trims_surrounding_whitespace_from_name()
    {
        var account = Account.Create("  Checking  ", AccountType.Asset);

        account.Name.Should().Be("Checking");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string? name)
    {
        var act = () => Account.Create(name!, AccountType.Asset);

        act.Should().Throw<ArgumentException>();
    }

    // ---- Normal balance (spec §4.2) -------------------------------------

    [Theory]
    [InlineData(AccountType.Asset, Side.Debit)]
    [InlineData(AccountType.Expense, Side.Debit)]
    [InlineData(AccountType.Liability, Side.Credit)]
    [InlineData(AccountType.Equity, Side.Credit)]
    [InlineData(AccountType.Income, Side.Credit)]
    public void NormalBalance_is_enforced_per_root_type(AccountType type, Side expected)
    {
        var account = Account.Create("Some account", type);

        account.NormalBalance.Should().Be(expected);
    }

    // ---- Type immutability (spec §4.2: "immutable once any entry references it") ----

    [Fact]
    public void Type_has_no_public_setter()
    {
        typeof(Account).GetProperty(nameof(Account.Type))!.SetMethod.Should().BeNull();
    }

    // ---- Hierarchy: parent, type inheritance (spec §4.2) -----------------

    [Fact]
    public void Create_without_a_parent_is_a_root_account()
    {
        var account = Account.Create("Checking", AccountType.Asset);

        account.Parent.Should().BeNull();
        account.ParentAccountId.Should().BeNull();
    }

    [Theory]
    [InlineData(AccountType.Asset)]
    [InlineData(AccountType.Liability)]
    [InlineData(AccountType.Equity)]
    [InlineData(AccountType.Income)]
    [InlineData(AccountType.Expense)]
    public void Create_with_a_parent_of_the_same_type_succeeds(AccountType type)
    {
        var parent = Account.Create("Parent", type);

        var child = Account.Create("Child", type, parent);

        child.Parent.Should().Be(parent);
        child.ParentAccountId.Should().Be(parent.AccountId);
    }

    [Theory]
    [InlineData(AccountType.Asset, AccountType.Liability)]
    [InlineData(AccountType.Liability, AccountType.Equity)]
    [InlineData(AccountType.Equity, AccountType.Income)]
    [InlineData(AccountType.Income, AccountType.Expense)]
    [InlineData(AccountType.Expense, AccountType.Asset)]
    public void Create_with_a_parent_of_a_different_type_throws(AccountType parentType, AccountType childType)
    {
        var parent = Account.Create("Parent", parentType);

        var act = () => Account.Create("Child", childType, parent);

        act.Should().Throw<AccountTypeMismatchException>();
    }

    [Fact]
    public void Property_a_child_can_be_created_under_a_parent_iff_the_types_match()
    {
        Gen.Select(GenAccountType, GenAccountType, (parentType, childType) =>
        {
            var parent = Account.Create("Parent", parentType);

            if (parentType == childType)
            {
                var child = Account.Create("Child", childType, parent);
                return child.Type == childType && child.Parent == parent;
            }

            try
            {
                Account.Create("Child", childType, parent);
                return false;
            }
            catch (AccountTypeMismatchException)
            {
                return true;
            }
        }).Sample(ok => ok);
    }

    // ---- Hierarchy: reparenting (spec §4.2) -------------------------------

    [Fact]
    public void Reparent_moves_the_account_under_a_new_parent_of_the_same_type()
    {
        var oldParent = Account.Create("Old Parent", AccountType.Asset);
        var newParent = Account.Create("New Parent", AccountType.Asset);
        var account = Account.Create("Checking", AccountType.Asset, oldParent);

        account.Reparent(newParent);

        account.Parent.Should().Be(newParent);
        account.ParentAccountId.Should().Be(newParent.AccountId);
    }

    [Fact]
    public void Reparent_to_null_makes_the_account_a_root()
    {
        var parent = Account.Create("Parent", AccountType.Asset);
        var account = Account.Create("Checking", AccountType.Asset, parent);

        account.Reparent(null);

        account.Parent.Should().BeNull();
        account.ParentAccountId.Should().BeNull();
    }

    [Fact]
    public void Reparent_to_a_parent_of_a_different_type_throws_and_leaves_the_account_unmoved()
    {
        var originalParent = Account.Create("Assets", AccountType.Asset);
        var wrongTypeParent = Account.Create("Rent", AccountType.Expense);
        var account = Account.Create("Checking", AccountType.Asset, originalParent);

        var act = () => account.Reparent(wrongTypeParent);

        act.Should().Throw<AccountTypeMismatchException>();
        account.Parent.Should().Be(originalParent);
    }

    // ---- Hierarchy: no cycles (spec §4.2) ----------------------------------

    [Fact]
    public void Reparent_to_self_throws()
    {
        var account = Account.Create("Checking", AccountType.Asset);

        var act = () => account.Reparent(account);

        act.Should().Throw<CircularAccountHierarchyException>();
    }

    [Fact]
    public void Reparent_to_a_descendant_throws_and_leaves_the_account_unmoved()
    {
        var root = Account.Create("Root", AccountType.Asset);
        var child = Account.Create("Child", AccountType.Asset, root);
        var grandchild = Account.Create("Grandchild", AccountType.Asset, child);

        var act = () => root.Reparent(grandchild);

        act.Should().Throw<CircularAccountHierarchyException>();
        root.Parent.Should().BeNull();
    }

    [Fact]
    public void Property_reparenting_an_account_under_its_own_descendant_always_throws()
    {
        Gen.Int[1, 8].Sample(depth =>
        {
            var root = Account.Create("Root", AccountType.Asset);
            var deepest = root;
            for (var i = 0; i < depth; i++)
            {
                deepest = Account.Create($"Descendant {i}", AccountType.Asset, deepest);
            }

            try
            {
                root.Reparent(deepest);
                return false;
            }
            catch (CircularAccountHierarchyException)
            {
                return true;
            }
        });
    }

    // ---- Active / inactive (spec §4.2: deactivate rather than delete) -----

    [Fact]
    public void Create_defaults_to_active()
    {
        var account = Account.Create("Checking", AccountType.Asset);

        account.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_marks_the_account_inactive()
    {
        var account = Account.Create("Checking", AccountType.Asset);

        account.Deactivate();

        account.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_is_idempotent()
    {
        var account = Account.Create("Checking", AccountType.Asset);

        account.Deactivate();
        account.Deactivate();

        account.IsActive.Should().BeFalse();
    }
}