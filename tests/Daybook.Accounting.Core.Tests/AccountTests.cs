namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 2 — the <see cref="Account"/> entity (issue #3).
/// Written test-first (red -> green -> refactor). Covers spec §4.2: five root
/// types, each with an enforced normal balance. Hierarchy, optional code,
/// display path, and tags are separate slices (issues #4-#7) and are out of
/// scope here.
/// </summary>
public class AccountTests
{
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
}