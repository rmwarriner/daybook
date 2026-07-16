using CsCheck;

namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 2 — <see cref="ChartOfAccounts"/>, the aggregate that enforces
/// the invariants a lone <see cref="Account"/> cannot check by itself
/// (spec §4.2): unique-when-present <c>Code</c>, type inheritance from the
/// parent, no cycles, and the derived display path.
/// </summary>
public class ChartOfAccountsTests
{
    // ---- Empty / lookup -----------------------------------------------

    [Fact]
    public void Empty_chart_has_no_accounts()
    {
        var chart = ChartOfAccounts.Empty();

        chart.Accounts.Should().BeEmpty();
        chart.Find(Guid.NewGuid()).Should().BeNull();
        chart.FindByCode("1000").Should().BeNull();
    }

    // ---- AddRoot ------------------------------------------------------

    [Fact]
    public void AddRoot_adds_a_parentless_account_findable_by_id_and_code()
    {
        var chart = ChartOfAccounts.Empty();
        var id = Guid.NewGuid();

        var result = chart.AddRoot(id, "Checking", AccountType.Asset, code: "1000");

        result.IsSuccess.Should().BeTrue();
        chart.Find(id).Should().Be(result.Value);
        chart.FindByCode("1000").Should().Be(result.Value);
        result.Value.ParentAccountId.Should().BeNull();
    }

    [Fact]
    public void AddRoot_rejects_a_duplicate_id()
    {
        var chart = ChartOfAccounts.Empty();
        var id = Guid.NewGuid();
        chart.AddRoot(id, "Checking", AccountType.Asset);

        var result = chart.AddRoot(id, "Savings", AccountType.Asset);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.id.duplicate");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
    }

    [Fact]
    public void AddRoot_rejects_a_duplicate_code_and_leaves_the_original_owner_intact()
    {
        var chart = ChartOfAccounts.Empty();
        var first = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset, code: "1000").Value;

        var result = chart.AddRoot(Guid.NewGuid(), "Savings", AccountType.Asset, code: "1000");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.code.duplicate");
        result.Error.Category.Should().Be(ErrorCategory.Conflict);
        chart.FindByCode("1000").Should().Be(first);
    }

    [Fact]
    public void AddRoot_two_accounts_with_no_code_do_not_collide()
    {
        var chart = ChartOfAccounts.Empty();

        chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).IsSuccess.Should().BeTrue();
        var second = chart.AddRoot(Guid.NewGuid(), "Savings", AccountType.Asset);

        second.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void AddRoot_bubbles_up_account_level_validation_failures()
    {
        var chart = ChartOfAccounts.Empty();

        var result = chart.AddRoot(Guid.NewGuid(), "   ", AccountType.Asset);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.name.required");
        chart.Accounts.Should().BeEmpty();
    }

    // ---- AddChild -------------------------------------------------------

    [Fact]
    public void AddChild_sets_the_parent_when_types_match()
    {
        var chart = ChartOfAccounts.Empty();
        var parent = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;

        var child = chart.AddChild(Guid.NewGuid(), parent.Id, "Electric", AccountType.Expense);

        child.IsSuccess.Should().BeTrue();
        child.Value.ParentAccountId.Should().Be(parent.Id);
    }

    [Fact]
    public void AddChild_rejects_an_unknown_parent()
    {
        var chart = ChartOfAccounts.Empty();

        var result = chart.AddChild(Guid.NewGuid(), Guid.NewGuid(), "Electric", AccountType.Expense);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.parent.not_found");
    }

    [Fact]
    public void AddChild_rejects_a_type_that_differs_from_its_parent()
    {
        var chart = ChartOfAccounts.Empty();
        var parent = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;

        var result = chart.AddChild(Guid.NewGuid(), parent.Id, "Checking", AccountType.Asset);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.parent.type_mismatch");
        result.Error.Category.Should().Be(ErrorCategory.BusinessRule);
    }

    [Fact]
    public void Children_returns_only_direct_children_not_grandchildren()
    {
        var chart = ChartOfAccounts.Empty();
        var root = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;
        var child = chart.AddChild(Guid.NewGuid(), root.Id, "Electric", AccountType.Expense).Value;
        chart.AddChild(Guid.NewGuid(), child.Id, "Peak", AccountType.Expense);

        var children = chart.Children(root.Id);

        children.Should().ContainSingle().Which.Id.Should().Be(child.Id);
    }

    // ---- Reparent ---------------------------------------------------------

    [Fact]
    public void Reparent_moves_an_account_under_a_new_same_type_parent()
    {
        var chart = ChartOfAccounts.Empty();
        var oldParent = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;
        var newParent = chart.AddRoot(Guid.NewGuid(), "Household", AccountType.Expense).Value;
        var child = chart.AddChild(Guid.NewGuid(), oldParent.Id, "Electric", AccountType.Expense).Value;

        var result = chart.Reparent(child.Id, newParent.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParentAccountId.Should().Be(newParent.Id);
        chart.Find(child.Id)!.ParentAccountId.Should().Be(newParent.Id);
    }

    [Fact]
    public void Reparent_to_null_makes_the_account_a_root()
    {
        var chart = ChartOfAccounts.Empty();
        var parent = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;
        var child = chart.AddChild(Guid.NewGuid(), parent.Id, "Electric", AccountType.Expense).Value;

        var result = chart.Reparent(child.Id, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParentAccountId.Should().BeNull();
    }

    [Fact]
    public void Reparent_rejects_an_unknown_account()
    {
        var chart = ChartOfAccounts.Empty();
        var parent = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;

        var result = chart.Reparent(Guid.NewGuid(), parent.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.not_found");
    }

    [Fact]
    public void Reparent_rejects_an_unknown_new_parent()
    {
        var chart = ChartOfAccounts.Empty();
        var account = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;

        var result = chart.Reparent(account.Id, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.parent.not_found");
    }

    [Fact]
    public void Reparent_rejects_a_new_parent_of_a_different_type()
    {
        var chart = ChartOfAccounts.Empty();
        var expenseParent = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;
        var assetParent = chart.AddRoot(Guid.NewGuid(), "Cash", AccountType.Asset).Value;
        var child = chart.AddChild(Guid.NewGuid(), expenseParent.Id, "Electric", AccountType.Expense).Value;

        var result = chart.Reparent(child.Id, assetParent.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.parent.type_mismatch");
    }

    [Fact]
    public void Reparent_rejects_making_an_account_its_own_parent()
    {
        var chart = ChartOfAccounts.Empty();
        var account = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;

        var result = chart.Reparent(account.Id, account.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.hierarchy.cycle");
    }

    [Fact]
    public void Reparent_rejects_moving_an_account_under_its_own_descendant()
    {
        var chart = ChartOfAccounts.Empty();
        var grandparent = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;
        var parent = chart.AddChild(Guid.NewGuid(), grandparent.Id, "Electric", AccountType.Expense).Value;
        var child = chart.AddChild(Guid.NewGuid(), parent.Id, "Peak", AccountType.Expense).Value;

        var result = chart.Reparent(grandparent.Id, child.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.hierarchy.cycle");
        chart.Find(grandparent.Id)!.ParentAccountId.Should().BeNull();
    }

    // ---- Rename / SetCode via the chart -------------------------------

    [Fact]
    public void Rename_updates_the_stored_account()
    {
        var chart = ChartOfAccounts.Empty();
        var account = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;

        var result = chart.Rename(account.Id, "Primary Checking");

        result.IsSuccess.Should().BeTrue();
        chart.Find(account.Id)!.Name.Should().Be("Primary Checking");
    }

    [Fact]
    public void Rename_rejects_an_unknown_account()
    {
        var chart = ChartOfAccounts.Empty();

        var result = chart.Rename(Guid.NewGuid(), "Whatever");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.not_found");
    }

    [Fact]
    public void SetCode_updates_the_code_index_freeing_the_old_code()
    {
        var chart = ChartOfAccounts.Empty();
        var account = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset, code: "1000").Value;

        chart.SetCode(account.Id, "1001").IsSuccess.Should().BeTrue();

        chart.FindByCode("1000").Should().BeNull();
        chart.FindByCode("1001").Should().Be(chart.Find(account.Id));
    }

    [Fact]
    public void SetCode_to_its_own_current_value_is_not_treated_as_a_duplicate()
    {
        var chart = ChartOfAccounts.Empty();
        var account = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset, code: "1000").Value;

        var result = chart.SetCode(account.Id, "1000");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void SetCode_rejects_a_code_already_used_by_another_account()
    {
        var chart = ChartOfAccounts.Empty();
        chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset, code: "1000");
        var other = chart.AddRoot(Guid.NewGuid(), "Savings", AccountType.Asset, code: "1001").Value;

        var result = chart.SetCode(other.Id, "1000");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.code.duplicate");
        chart.FindByCode("1001").Should().Be(other);
    }

    [Fact]
    public void SetCode_null_clears_the_code_and_frees_it_for_reuse()
    {
        var chart = ChartOfAccounts.Empty();
        var account = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset, code: "1000").Value;

        chart.SetCode(account.Id, null).Value.Code.Should().BeNull();

        var reused = chart.AddRoot(Guid.NewGuid(), "Savings", AccountType.Asset, code: "1000");
        reused.IsSuccess.Should().BeTrue();
    }

    // ---- Activate / Deactivate / Placeholder via the chart -----------------

    [Fact]
    public void Deactivate_and_Activate_update_the_stored_account()
    {
        var chart = ChartOfAccounts.Empty();
        var account = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;

        chart.Deactivate(account.Id).Value.IsActive.Should().BeFalse();
        chart.Find(account.Id)!.IsActive.Should().BeFalse();

        chart.Activate(account.Id).Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public void MarkAsPlaceholder_and_ClearPlaceholder_update_the_stored_account()
    {
        var chart = ChartOfAccounts.Empty();
        var account = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;

        chart.MarkAsPlaceholder(account.Id).Value.IsPlaceholder.Should().BeTrue();
        chart.Find(account.Id)!.IsPlaceholder.Should().BeTrue();

        chart.ClearPlaceholder(account.Id).Value.IsPlaceholder.Should().BeFalse();
    }

    // ---- Display path -------------------------------------------------

    [Fact]
    public void DisplayPathOf_a_root_is_just_its_name()
    {
        var chart = ChartOfAccounts.Empty();
        var root = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;

        chart.DisplayPathOf(root.Id).Value.Should().Be("Utilities");
    }

    [Fact]
    public void DisplayPathOf_a_nested_account_joins_ancestors_with_a_colon()
    {
        var chart = ChartOfAccounts.Empty();
        var root = chart.AddRoot(Guid.NewGuid(), "Utilities", AccountType.Expense).Value;
        var child = chart.AddChild(Guid.NewGuid(), root.Id, "Electric", AccountType.Expense).Value;

        chart.DisplayPathOf(child.Id).Value.Should().Be("Utilities:Electric");
    }

    [Fact]
    public void DisplayPathOf_an_unknown_account_fails()
    {
        var chart = ChartOfAccounts.Empty();

        var result = chart.DisplayPathOf(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("account.not_found");
    }

    // ---- Property-based tree invariants --------------------------------

    /// <summary>
    /// Builds a single connected tree of <c>[2, 8]</c> accounts, all of the
    /// given <see cref="AccountType"/>. Node 0 is always the root; every
    /// later node's parent is chosen uniformly from strictly earlier nodes,
    /// so no cycle is possible by construction — every Add is expected to
    /// succeed.
    /// </summary>
    private static (ChartOfAccounts Chart, IReadOnlyList<Guid> Ids) BuildRandomTree(int seed, AccountType type)
    {
        var random = new Random(seed);
        var n = random.Next(2, 9);
        var chart = ChartOfAccounts.Empty();
        var ids = new List<Guid> { Guid.NewGuid() };
        chart.AddRoot(ids[0], "Node0", type).IsSuccess.Should().BeTrue();

        for (var i = 1; i < n; i++)
        {
            var parentIndex = random.Next(0, i);
            var id = Guid.NewGuid();
            chart.AddChild(id, ids[parentIndex], $"Node{i}", type).IsSuccess.Should().BeTrue();
            ids.Add(id);
        }

        return (chart, ids);
    }

    [Fact]
    public void Property_every_account_shares_its_ancestors_type()
    {
        Gen.Int[1, 1000].Sample(seed =>
        {
            var (chart, ids) = BuildRandomTree(seed, AccountType.Expense);

            foreach (var id in ids)
            {
                var current = chart.Find(id)!;
                while (current.ParentAccountId is Guid parentId)
                {
                    var parent = chart.Find(parentId)!;
                    parent.Type.Should().Be(current.Type);
                    current = parent;
                }
            }
        });
    }

    [Fact]
    public void Property_reparenting_an_account_under_its_own_descendant_always_fails_as_a_cycle()
    {
        Gen.Int[1, 1000].Sample(seed =>
        {
            var random = new Random(seed);
            var (chart, ids) = BuildRandomTree(seed, AccountType.Expense);

            var descendantIndex = random.Next(1, ids.Count);
            var descendantId = ids[descendantIndex];

            var ancestors = new List<Guid>();
            var current = chart.Find(descendantId)!;
            while (current.ParentAccountId is Guid parentId)
            {
                ancestors.Add(parentId);
                current = chart.Find(parentId)!;
            }

            var ancestorId = ancestors[random.Next(ancestors.Count)];

            var result = chart.Reparent(ancestorId, descendantId);

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("account.hierarchy.cycle");
        });
    }
}