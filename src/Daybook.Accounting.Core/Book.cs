namespace Daybook.Accounting.Core;

/// <summary>
/// One complete set of accounts (spec §4.1) — one household = one book,
/// but the system supports many. An entity — identity equality by
/// <see cref="Id"/>, not structural.
/// </summary>
/// <remarks>
/// <see cref="Basis"/> is explicitly "fixed per book" in the spec, and
/// <see cref="BaseCurrency"/> gets the same treatment here even though the
/// spec doesn't say so explicitly: retargeting a book's currency after
/// entries exist is the same class of problem as <c>Account.Type</c>
/// becoming immutable once referenced. Neither has a mutator.
/// <see cref="BaseCurrency"/> is further restricted to <see cref="Currency.Usd"/>
/// for now — the field is currency-typed so multi-currency is additive
/// later (spec §4.6), but v1 itself is USD-only.
/// </remarks>
public sealed class Book : IEquatable<Book>
{
    public Guid Id { get; }

    public string Name { get; }

    public Basis Basis { get; }

    public Currency BaseCurrency { get; }

    public MonthDay FiscalYearStart { get; }

    public BookStatus Status { get; }

    private Book(
        Guid id,
        string name,
        Basis basis,
        Currency baseCurrency,
        MonthDay fiscalYearStart,
        BookStatus status)
    {
        Id = id;
        Name = name;
        Basis = basis;
        BaseCurrency = baseCurrency;
        FiscalYearStart = fiscalYearStart;
        Status = status;
    }

    /// <exception cref="ArgumentException"><paramref name="id"/> is <see cref="Guid.Empty"/> — a caller bug, not a business rule.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/>, <paramref name="baseCurrency"/>, or <paramref name="fiscalYearStart"/> is null — a caller bug, not a business rule.</exception>
    public static Result<Book> Create(
        Guid id,
        string name,
        Basis basis,
        Currency baseCurrency,
        MonthDay fiscalYearStart,
        BookStatus status = BookStatus.Open)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Book id must not be Guid.Empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(baseCurrency);
        ArgumentNullException.ThrowIfNull(fiscalYearStart);

        if (!Enum.IsDefined(basis))
        {
            return new Error(
                "book.basis.invalid",
                ErrorCategory.Validation,
                $"'{basis}' is not a recognized basis.",
                ["Use Basis.Cash or Basis.Accrual."]);
        }

        if (!Enum.IsDefined(status))
        {
            return new Error(
                "book.status.invalid",
                ErrorCategory.Validation,
                $"'{status}' is not a recognized status.",
                ["Use BookStatus.Open or BookStatus.Archived."]);
        }

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
        {
            return nameResult.Error;
        }

        if (baseCurrency != Currency.Usd)
        {
            return new Error(
                "book.currency.unsupported",
                ErrorCategory.Validation,
                $"Base currency '{baseCurrency}' is not supported; v1 books are USD-only.",
                ["Use Currency.Usd as the base currency."]);
        }

        return new Book(id, nameResult.Value, basis, baseCurrency, fiscalYearStart, status);
    }

    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public Result<Book> Rename(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var nameResult = ValidateName(name);
        if (nameResult.IsFailure)
        {
            return nameResult.Error;
        }

        return new Book(Id, nameResult.Value, Basis, BaseCurrency, FiscalYearStart, Status);
    }

    public Book Archive() => new(Id, Name, Basis, BaseCurrency, FiscalYearStart, BookStatus.Archived);

    public Book Reopen() => new(Id, Name, Basis, BaseCurrency, FiscalYearStart, BookStatus.Open);

    /// <exception cref="ArgumentNullException"><paramref name="fiscalYearStart"/> is null.</exception>
    public Book SetFiscalYearStart(MonthDay fiscalYearStart)
    {
        ArgumentNullException.ThrowIfNull(fiscalYearStart);

        return new Book(Id, Name, Basis, BaseCurrency, fiscalYearStart, Status);
    }

    private static Result<string> ValidateName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return new Error(
                "book.name.required",
                ErrorCategory.Validation,
                "Book name must not be empty.",
                ["Provide a non-empty Name."]);
        }

        return trimmed;
    }

    public bool Equals(Book? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as Book);

    public override int GetHashCode() => Id.GetHashCode();
}