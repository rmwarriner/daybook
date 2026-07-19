namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 14 — <see cref="ReconciliationLedger"/>, the aggregate that
/// both stores completed <see cref="Reconciliation"/> sessions and tracks
/// each line's current <see cref="ReconciliationStatus"/> (spec §4.4/§4.5).
/// Takes a <see cref="Journal"/> to validate a <see cref="LineLocation"/>
/// resolves to a real posted line — the same "other aggregate as a
/// parameter" shape <see cref="ChartOfAccounts.AddTag"/> already uses for
/// <see cref="TagRegistry"/>.
/// </summary>
public class ReconciliationLedgerTests
{
    private static readonly DateOnly EntryDate = new(2026, 7, 15);
    private static readonly DateTimeOffset PostedAtUtc = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PostedByUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly DateOnly StatementDate = new(2026, 7, 31);
    private static readonly DateTimeOffset ReconciledAtUtc = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ReconciledByUserId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private static (ChartOfAccounts Chart, Account Checking, Account Salary) AChart()
    {
        var chart = ChartOfAccounts.Empty();
        var checking = chart.AddRoot(Guid.NewGuid(), "Checking", AccountType.Asset).Value;
        var salary = chart.AddRoot(Guid.NewGuid(), "Salary", AccountType.Income).Value;
        return (chart, checking, salary);
    }

    private static JournalLine ADebit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Debit, Money.Of(amount, Currency.Usd)).Value;

    private static JournalLine ACredit(Guid accountId, decimal amount) =>
        JournalLine.Create(accountId, Side.Credit, Money.Of(amount, Currency.Usd)).Value;

    private static (Journal Journal, Guid EntryId) APostedEntry(ChartOfAccounts chart, Account checking, Account salary)
    {
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        journal.Post(id, chart, PostedAtUtc, PostedByUserId);
        return (journal, id);
    }

    // ---- MarkCleared / MarkUnreconciled -----------------------------------

    [Fact]
    public void StatusOf_an_untracked_location_is_unreconciled()
    {
        var ledger = ReconciliationLedger.Empty();

        ledger.StatusOf(new LineLocation(Guid.NewGuid(), 0)).Should().Be(ReconciliationStatus.Unreconciled);
    }

    [Fact]
    public void MarkCleared_updates_the_status_of_a_real_posted_line()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var ledger = ReconciliationLedger.Empty();
        var location = new LineLocation(entryId, 0);

        var result = ledger.MarkCleared(location, journal);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ReconciliationStatus.Cleared);
        ledger.StatusOf(location).Should().Be(ReconciliationStatus.Cleared);
    }

    [Fact]
    public void MarkCleared_rejects_an_unknown_entry()
    {
        var journal = Journal.Empty(Currency.Usd);
        var ledger = ReconciliationLedger.Empty();

        var result = ledger.MarkCleared(new LineLocation(Guid.NewGuid(), 0), journal);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.line.entry_not_found");
    }

    [Fact]
    public void MarkCleared_rejects_a_still_draft_entry()
    {
        var (chart, checking, salary) = AChart();
        var journal = Journal.Empty(Currency.Usd);
        var id = Guid.NewGuid();
        journal.CreateDraft(id, EntryDate, "Paycheck", [ADebit(checking.Id, 100m), ACredit(salary.Id, 100m)]);
        var ledger = ReconciliationLedger.Empty();

        var result = ledger.MarkCleared(new LineLocation(id, 0), journal);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.line.not_posted");
    }

    [Fact]
    public void MarkCleared_rejects_an_out_of_range_line_index()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var ledger = ReconciliationLedger.Empty();

        var result = ledger.MarkCleared(new LineLocation(entryId, 5), journal);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.line.index_out_of_range");
    }

    [Fact]
    public void MarkCleared_rejects_a_line_already_reconciled()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var location = new LineLocation(entryId, 0);
        var ledger = ReconciliationLedger.Empty();
        ledger.CreateReconciliation(
            Guid.NewGuid(), checking.Id, StatementDate, Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { location }, journal);

        var result = ledger.MarkCleared(location, journal);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.line.already_reconciled");
    }

    [Fact]
    public void MarkUnreconciled_resets_the_status()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var ledger = ReconciliationLedger.Empty();
        var location = new LineLocation(entryId, 0);
        ledger.MarkCleared(location, journal);

        var result = ledger.MarkUnreconciled(location);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(ReconciliationStatus.Unreconciled);
        ledger.StatusOf(location).Should().Be(ReconciliationStatus.Unreconciled);
    }

    [Fact]
    public void MarkUnreconciled_rejects_a_line_already_reconciled()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var location = new LineLocation(entryId, 0);
        var ledger = ReconciliationLedger.Empty();
        ledger.CreateReconciliation(
            Guid.NewGuid(), checking.Id, StatementDate, Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { location }, journal);

        var result = ledger.MarkUnreconciled(location);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.line.already_reconciled");
    }

    // ---- CreateReconciliation ----------------------------------------------

    [Fact]
    public void CreateReconciliation_reconciles_every_cleared_line()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var ledger = ReconciliationLedger.Empty();
        var location = new LineLocation(entryId, 0);
        var reconciliationId = Guid.NewGuid();

        var result = ledger.CreateReconciliation(
            reconciliationId, checking.Id, StatementDate, Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { location }, journal);

        result.IsSuccess.Should().BeTrue();
        ledger.StatusOf(location).Should().Be(ReconciliationStatus.Reconciled);
        ledger.ReconciliationOf(location).Should().Be(reconciliationId);
        ledger.FindReconciliation(reconciliationId).Should().Be(result.Value);
    }

    [Fact]
    public void CreateReconciliation_rejects_a_duplicate_id()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var ledger = ReconciliationLedger.Empty();
        var reconciliationId = Guid.NewGuid();
        ledger.CreateReconciliation(
            reconciliationId, checking.Id, StatementDate, Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { new(entryId, 0) }, journal);

        var result = ledger.CreateReconciliation(
            reconciliationId, checking.Id, StatementDate, Money.Of(50m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { new(entryId, 1) }, journal);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.id.duplicate");
    }

    [Fact]
    public void CreateReconciliation_rejects_a_line_from_an_unknown_entry()
    {
        var journal = Journal.Empty(Currency.Usd);
        var ledger = ReconciliationLedger.Empty();

        var result = ledger.CreateReconciliation(
            Guid.NewGuid(), Guid.NewGuid(), StatementDate, Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { new(Guid.NewGuid(), 0) }, journal);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.line.entry_not_found");
    }

    [Fact]
    public void CreateReconciliation_rejects_a_line_belonging_to_a_different_account()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var ledger = ReconciliationLedger.Empty();

        // Line index 0 posts to Checking; claim the session reconciles Salary instead.
        var result = ledger.CreateReconciliation(
            Guid.NewGuid(), salary.Id, StatementDate, Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { new(entryId, 0) }, journal);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.line.account_mismatch");
    }

    [Fact]
    public void CreateReconciliation_rejects_a_line_already_reconciled_by_another_session()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var location = new LineLocation(entryId, 0);
        var ledger = ReconciliationLedger.Empty();
        ledger.CreateReconciliation(
            Guid.NewGuid(), checking.Id, StatementDate, Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { location }, journal);

        var result = ledger.CreateReconciliation(
            Guid.NewGuid(), checking.Id, StatementDate.AddMonths(1), Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { location }, journal);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.line.already_reconciled");
    }

    [Fact]
    public void CreateReconciliation_bubbles_up_reconciliation_level_validation_failures()
    {
        var journal = Journal.Empty(Currency.Usd);
        var ledger = ReconciliationLedger.Empty();

        var result = ledger.CreateReconciliation(
            Guid.NewGuid(), Guid.NewGuid(), StatementDate, Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation>(), journal);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.cleared_lines.required");
    }

    // ---- ReopenReconciliation ----------------------------------------------

    [Fact]
    public void ReopenReconciliation_resets_its_lines_and_removes_the_session()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var location = new LineLocation(entryId, 0);
        var ledger = ReconciliationLedger.Empty();
        var reconciliationId = Guid.NewGuid();
        ledger.CreateReconciliation(
            reconciliationId, checking.Id, StatementDate, Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { location }, journal);

        var result = ledger.ReopenReconciliation(reconciliationId);

        result.IsSuccess.Should().BeTrue();
        ledger.StatusOf(location).Should().Be(ReconciliationStatus.Unreconciled);
        ledger.ReconciliationOf(location).Should().BeNull();
        ledger.FindReconciliation(reconciliationId).Should().BeNull();
    }

    [Fact]
    public void ReopenReconciliation_rejects_an_unknown_id()
    {
        var ledger = ReconciliationLedger.Empty();

        var result = ledger.ReopenReconciliation(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reconciliation.not_found");
    }

    [Fact]
    public void A_reopened_lines_reconciliation_guard_lifts_allowing_a_new_session()
    {
        var (chart, checking, salary) = AChart();
        var (journal, entryId) = APostedEntry(chart, checking, salary);
        var location = new LineLocation(entryId, 0);
        var ledger = ReconciliationLedger.Empty();
        var firstId = Guid.NewGuid();
        ledger.CreateReconciliation(
            firstId, checking.Id, StatementDate, Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { location }, journal);
        ledger.ReopenReconciliation(firstId);

        var secondId = Guid.NewGuid();
        var result = ledger.CreateReconciliation(
            secondId, checking.Id, StatementDate.AddMonths(1), Money.Of(100m, Currency.Usd),
            ReconciledAtUtc, ReconciledByUserId, new HashSet<LineLocation> { location }, journal);

        result.IsSuccess.Should().BeTrue();
        ledger.ReconciliationOf(location).Should().Be(secondId);
    }
}