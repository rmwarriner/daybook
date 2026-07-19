namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 14 — the <see cref="Reconciliation"/> entity's own local
/// invariants (spec §4.5): a required <c>AccountId</c>, a non-null
/// <c>StatementEndingBalance</c>, and at least one cleared line. Unlike
/// <see cref="JournalEntry"/>, there's no draft/incremental-build lifecycle
/// here - a session is created already-complete - so <c>ClearedLines</c> is
/// validated eagerly, not deferred to a later step. Cross-line invariants
/// (does each line actually resolve to a posted line, does it belong to
/// this account, is it already reconciled elsewhere) need <see cref="Journal"/>
/// context a lone <see cref="Reconciliation"/> doesn't have, so those live
/// in <c>ReconciliationLedgerTests</c> instead.
/// </summary>
public class ReconciliationTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateOnly StatementDate = new(2026, 7, 31);
    private static readonly DateTimeOffset ReconciledAtUtc = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ReconciledByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static IReadOnlySet<LineLocation> ALineSet() => new HashSet<LineLocation> { new(Guid.NewGuid(), 0) };

    [Fact]
    public void Create_with_valid_fields_succeeds()
    {
        var clearedLines = ALineSet();

        var result = Reconciliation.Create(
            Id, AccountId, StatementDate, Money.Of(500m, Currency.Usd), ReconciledAtUtc, ReconciledByUserId, clearedLines);

        result.IsSuccess.Should().BeTrue();
        var reconciliation = result.Value;
        reconciliation.Id.Should().Be(Id);
        reconciliation.AccountId.Should().Be(AccountId);
        reconciliation.StatementDate.Should().Be(StatementDate);
        reconciliation.StatementEndingBalance.Should().Be(Money.Of(500m, Currency.Usd));
        reconciliation.ReconciledAtUtc.Should().Be(ReconciledAtUtc);
        reconciliation.ReconciledByUserId.Should().Be(ReconciledByUserId);
        reconciliation.ClearedLines.Should().BeEquivalentTo(clearedLines);
    }

    [Fact]
    public void Create_rejects_empty_id()
    {
        var act = () => Reconciliation.Create(
            Guid.Empty, AccountId, StatementDate, Money.Of(500m, Currency.Usd), ReconciledAtUtc, ReconciledByUserId, ALineSet());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_empty_account_id()
    {
        var act = () => Reconciliation.Create(
            Id, Guid.Empty, StatementDate, Money.Of(500m, Currency.Usd), ReconciledAtUtc, ReconciledByUserId, ALineSet());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_null_statement_ending_balance()
    {
        var act = () => Reconciliation.Create(
            Id, AccountId, StatementDate, null!, ReconciledAtUtc, ReconciledByUserId, ALineSet());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_rejects_null_cleared_lines()
    {
        var act = () => Reconciliation.Create(
            Id, AccountId, StatementDate, Money.Of(500m, Currency.Usd), ReconciledAtUtc, ReconciledByUserId, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_rejects_empty_cleared_lines()
    {
        var result = Reconciliation.Create(
            Id, AccountId, StatementDate, Money.Of(500m, Currency.Usd), ReconciledAtUtc, ReconciledByUserId,
            new HashSet<LineLocation>());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.cleared_lines.required");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
    }

    [Fact]
    public void Reconciliations_with_the_same_id_are_equal_even_if_other_fields_differ()
    {
        var a = Reconciliation.Create(
            Id, AccountId, StatementDate, Money.Of(500m, Currency.Usd), ReconciledAtUtc, ReconciledByUserId, ALineSet()).Value;
        var b = Reconciliation.Create(
            Id, Guid.NewGuid(), StatementDate, Money.Of(999m, Currency.Usd), ReconciledAtUtc, ReconciledByUserId, ALineSet()).Value;

        a.Should().Be(b);
    }
}