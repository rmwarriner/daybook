namespace Daybook.Accounting.Core;

/// <summary>
/// The daybook itself (spec §4.3/§4.7) — the one guarded write door
/// (golden rule 5) through which every draft is created, edited, and
/// posted. Owns the full §5 posting checklist and gapless per-book
/// <see cref="JournalEntry.SequenceNumber"/> assignment.
/// </summary>
/// <remarks>
/// <paramref name="postedAtUtc"/>/<paramref name="postedByUserId"/> on
/// <see cref="Post"/> are caller-supplied rather than read from an ambient
/// clock or current-user service — the same determinism rule already
/// applied to every generated id in this layer (no wall-clock, no ambient
/// state in domain code). The book's base currency is hardcoded to
/// <see cref="Currency.Usd"/> for now; there is no <c>Book</c> entity yet
/// to carry it.
/// </remarks>
public sealed class Journal
{
    private readonly Dictionary<Guid, JournalEntry> _byId = [];
    private readonly Dictionary<Guid, Guid> _reversalOf = [];
    private int _nextSequenceNumber = 1;

    private Journal()
    {
    }

    public static Journal Empty() => new();

    public JournalEntry? Find(Guid id) => _byId.GetValueOrDefault(id);

    public IReadOnlyCollection<JournalEntry> Drafts =>
        _byId.Values.Where(e => e.Status == JournalEntryStatus.Draft).ToList();

    public IReadOnlyCollection<JournalEntry> PostedEntries =>
        _byId.Values.Where(e => e.Status == JournalEntryStatus.Posted)
            .OrderBy(e => e.SequenceNumber)
            .ToList();

    /// <summary>
    /// The id of the entry that reverses <paramref name="originalEntryId"/>,
    /// or null if it hasn't been reversed. Kept separately from
    /// <see cref="JournalEntry"/> rather than as a field on it — an
    /// at-most-one-row link, not a mutation of an already-posted entry.
    /// </summary>
    public Guid? ReversalOf(Guid originalEntryId) =>
        _reversalOf.TryGetValue(originalEntryId, out var reversalId) ? reversalId : null;

    public bool IsReversed(Guid entryId) => _reversalOf.ContainsKey(entryId);

    /// <summary>Creates a new draft. A draft may be incomplete — the §5 checklist only applies at post time.</summary>
    public Result<JournalEntry> CreateDraft(
        Guid id,
        DateOnly entryDate,
        string description,
        IReadOnlyList<JournalLine> lines)
    {
        if (_byId.ContainsKey(id))
        {
            return DuplicateId(id);
        }

        var result = JournalEntry.CreateDraft(id, entryDate, description, lines);
        if (result.IsFailure)
        {
            return result.Error;
        }

        _byId[id] = result.Value;
        return result.Value;
    }

    /// <summary>Replaces a draft's date, description, and lines. Fails once the entry is posted.</summary>
    public Result<JournalEntry> UpdateDraft(
        Guid id,
        DateOnly entryDate,
        string description,
        IReadOnlyList<JournalLine> lines)
    {
        var entry = Find(id);
        if (entry is null)
        {
            return EntryNotFound(id);
        }

        var result = entry.UpdateDraft(entryDate, description, lines);
        if (result.IsFailure)
        {
            return result.Error;
        }

        _byId[id] = result.Value;
        return result.Value;
    }

    /// <summary>Removes a draft. Fails once the entry is posted — posted entries are corrected by reversal, not deletion.</summary>
    public Result<JournalEntry> DeleteDraft(Guid id)
    {
        var entry = Find(id);
        if (entry is null)
        {
            return EntryNotFound(id);
        }

        if (entry.Status != JournalEntryStatus.Draft)
        {
            return JournalEntry.PostedImmutable();
        }

        _byId.Remove(id);
        return entry;
    }

    /// <summary>Validates the full spec §5 checklist and, on success, posts the entry with a gapless sequence number.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="chart"/> is null.</exception>
    public Result<JournalEntry> Post(Guid id, ChartOfAccounts chart, DateTimeOffset postedAtUtc, Guid postedByUserId)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var entry = Find(id);
        if (entry is null)
        {
            return EntryNotFound(id);
        }

        if (entry.Status != JournalEntryStatus.Draft)
        {
            return AlreadyPosted(entry);
        }

        var validated = ValidateAndBalance(entry, chart);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var posted = entry.MarkPosted(_nextSequenceNumber, postedAtUtc, postedByUserId);
        _byId[id] = posted;
        _nextSequenceNumber++;
        return posted;
    }

    /// <summary>
    /// Posts a new entry that reverses <paramref name="originalEntryId"/>:
    /// the same lines with every side flipped, linked via
    /// <see cref="JournalEntry.ReversesEntryId"/> and the reversal index
    /// (spec §4.3/§4.7/§5). Goes straight to Posted — there is no draft
    /// stage for a reversal. Re-runs the full §5 checklist against
    /// <paramref name="chart"/>, since an account referenced by the
    /// original could have been deactivated or made a placeholder since it
    /// was posted.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="chart"/> is null.</exception>
    public Result<JournalEntry> Reverse(
        Guid originalEntryId,
        Guid reversalEntryId,
        ChartOfAccounts chart,
        DateOnly entryDate,
        string description,
        DateTimeOffset postedAtUtc,
        Guid postedByUserId)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var original = Find(originalEntryId);
        if (original is null)
        {
            return EntryNotFound(originalEntryId);
        }

        if (original.Status != JournalEntryStatus.Posted)
        {
            return NotPosted(original);
        }

        if (IsReversed(originalEntryId))
        {
            return AlreadyReversed(original);
        }

        if (_byId.ContainsKey(reversalEntryId))
        {
            return DuplicateId(reversalEntryId);
        }

        var flippedLines = original.Lines
            .Select(l => JournalLine.Create(l.AccountId, Flip(l.Side), l.Amount, l.Memo).Value)
            .ToList();

        var draftResult = JournalEntry.CreateDraft(reversalEntryId, entryDate, description, flippedLines);
        if (draftResult.IsFailure)
        {
            return draftResult.Error;
        }

        var validated = ValidateAndBalance(draftResult.Value, chart);
        if (validated.IsFailure)
        {
            return validated.Error;
        }

        var postedReversal = draftResult.Value.MarkPosted(
            _nextSequenceNumber, postedAtUtc, postedByUserId, reversesEntryId: originalEntryId);

        _byId[reversalEntryId] = postedReversal;
        _reversalOf[originalEntryId] = reversalEntryId;
        _nextSequenceNumber++;
        return postedReversal;
    }

    private static Side Flip(Side side) => side == Side.Debit ? Side.Credit : Side.Debit;

    /// <summary>
    /// The shared spec §5 line/balance checklist: at least two lines,
    /// every account exists/active/non-placeholder, every line in the
    /// book's base currency, and total debits equal total credits. Shared
    /// between <see cref="Post"/> and <see cref="Reverse"/> so the two
    /// can't drift apart.
    /// </summary>
    private Result<JournalEntry> ValidateAndBalance(JournalEntry candidate, ChartOfAccounts chart)
    {
        if (candidate.Lines.Count < 2)
        {
            return InsufficientLines(candidate);
        }

        foreach (var line in candidate.Lines)
        {
            var account = chart.Find(line.AccountId);
            if (account is null)
            {
                return AccountNotFound(line);
            }

            if (!account.IsActive)
            {
                return AccountInactive(account);
            }

            if (account.IsPlaceholder)
            {
                return AccountPlaceholder(account);
            }

            if (line.Amount.Currency != Currency.Usd)
            {
                return CurrencyMismatch(line);
            }
        }

        var debitTotal = Money.Zero(Currency.Usd);
        var creditTotal = Money.Zero(Currency.Usd);
        foreach (var line in candidate.Lines)
        {
            if (line.Side == Side.Debit)
            {
                debitTotal += line.Amount;
            }
            else
            {
                creditTotal += line.Amount;
            }
        }

        return debitTotal == creditTotal ? candidate : Unbalanced(candidate, debitTotal, creditTotal);
    }

    private static Error EntryNotFound(Guid id) => new(
        "entry.not_found",
        ErrorCategory.Validation,
        $"No journal entry with id '{id}' exists.",
        ["Check the entry id, or create the draft first."]);

    private static Error DuplicateId(Guid id) => new(
        "entry.id.duplicate",
        ErrorCategory.Conflict,
        $"A journal entry with id '{id}' already exists.",
        ["Generate a new EntryId; ids must be unique."]);

    private static Error AlreadyPosted(JournalEntry entry) => new(
        "entry.already_posted",
        ErrorCategory.BusinessRule,
        $"Entry '{entry.Id}' is already posted (sequence {entry.SequenceNumber}).",
        ["This entry cannot be posted again; look it up by its SequenceNumber instead."]);

    private static Error NotPosted(JournalEntry entry) => new(
        "entry.reversal.not_posted",
        ErrorCategory.BusinessRule,
        $"Entry '{entry.Id}' is not posted and cannot be reversed.",
        ["Post the entry first."]);

    private static Error AlreadyReversed(JournalEntry entry) => new(
        "entry.reversal.already_reversed",
        ErrorCategory.BusinessRule,
        $"Entry '{entry.Id}' has already been reversed.",
        ["Entries can only be reversed once; look up the existing reversal."]);

    private static Error InsufficientLines(JournalEntry entry) => new(
        "entry.lines.insufficient",
        ErrorCategory.Validation,
        $"Entry '{entry.Id}' has {entry.Lines.Count} line(s); at least 2 are required to post.",
        ["Add lines until the entry has at least two."]);

    private static Error AccountNotFound(JournalLine line) => new(
        "entry.line.account_not_found",
        ErrorCategory.Validation,
        $"Line references account '{line.AccountId}', which does not exist in this chart.",
        ["Check the AccountId, or create the account first."]);

    private static Error AccountInactive(Account account) => new(
        "entry.line.account_inactive",
        ErrorCategory.BusinessRule,
        $"Account '{account.Name}' is inactive and cannot accept new postings.",
        ["Reactivate the account, or choose another."]);

    private static Error AccountPlaceholder(Account account) => new(
        "entry.line.account_placeholder",
        ErrorCategory.BusinessRule,
        $"Account '{account.Name}' is a placeholder (roll-up-only) and rejects direct postings.",
        ["Post to one of its leaf sub-accounts instead."]);

    private static Error CurrencyMismatch(JournalLine line) => new(
        "entry.line.currency_mismatch",
        ErrorCategory.Validation,
        $"Line amount is in '{line.Amount.Currency}', but the book's base currency is '{Currency.Usd}'.",
        ["Use an amount in the book's base currency."]);

    private static Error Unbalanced(JournalEntry entry, Money debitTotal, Money creditTotal) => new(
        "entry.unbalanced",
        ErrorCategory.Validation,
        $"Entry '{entry.Id}' does not balance: total debits {debitTotal} vs total credits {creditTotal} " +
        $"(difference {debitTotal - creditTotal}).",
        ["Adjust line amounts so total debits equal total credits."]);
}