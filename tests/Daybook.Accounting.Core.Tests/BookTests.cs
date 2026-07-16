namespace Daybook.Accounting.Core.Tests;

/// <summary>
/// Milestone 6 — the <see cref="Book"/> entity (spec §4.1). An entity like
/// <see cref="Account"/> and <see cref="JournalEntry"/> — identity equality,
/// not structural. <see cref="Basis"/> is explicitly "fixed per book" in
/// the spec, and <see cref="Currency"/> gets the same treatment here
/// (retargeting a book's currency after entries exist is the same class of
/// problem as <c>Account.Type</c> becoming immutable once referenced) — so
/// neither has a mutator at all.
/// </summary>
public class BookTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly MonthDay JanuaryFirst = MonthDay.Create(1, 1).Value;

    // ---- Construction ----------------------------------------------------

    [Fact]
    public void Create_with_valid_fields_succeeds()
    {
        var result = Book.Create(Id, "Household", Basis.Cash, Currency.Usd, JanuaryFirst);

        result.IsSuccess.Should().BeTrue();
        var book = result.Value;
        book.Id.Should().Be(Id);
        book.Name.Should().Be("Household");
        book.Basis.Should().Be(Basis.Cash);
        book.BaseCurrency.Should().Be(Currency.Usd);
        book.FiscalYearStart.Should().Be(JanuaryFirst);
        book.Status.Should().Be(BookStatus.Open);
    }

    [Fact]
    public void Create_trims_the_name()
    {
        var book = Book.Create(Id, "  Household  ", Basis.Cash, Currency.Usd, JanuaryFirst).Value;

        book.Name.Should().Be("Household");
    }

    [Fact]
    public void Create_accepts_an_explicit_status()
    {
        var book = Book.Create(Id, "Household", Basis.Cash, Currency.Usd, JanuaryFirst, BookStatus.Archived).Value;

        book.Status.Should().Be(BookStatus.Archived);
    }

    [Fact]
    public void Create_rejects_empty_id()
    {
        var act = () => Book.Create(Guid.Empty, "Household", Basis.Cash, Currency.Usd, JanuaryFirst);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_rejects_null_name()
    {
        var act = () => Book.Create(Id, null!, Basis.Cash, Currency.Usd, JanuaryFirst);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_rejects_null_currency()
    {
        var act = () => Book.Create(Id, "Household", Basis.Cash, null!, JanuaryFirst);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_rejects_null_fiscal_year_start()
    {
        var act = () => Book.Create(Id, "Household", Basis.Cash, Currency.Usd, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string blank)
    {
        var result = Book.Create(Id, blank, Basis.Cash, Currency.Usd, JanuaryFirst);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("book.name.required");
        result.Error.Category.Should().Be(ErrorCategory.Validation);
    }

    [Fact]
    public void Create_rejects_an_undefined_basis()
    {
        var result = Book.Create(Id, "Household", (Basis)(-1), Currency.Usd, JanuaryFirst);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("book.basis.invalid");
    }

    [Fact]
    public void Create_rejects_an_undefined_status()
    {
        var result = Book.Create(Id, "Household", Basis.Cash, Currency.Usd, JanuaryFirst, (BookStatus)(-1));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("book.status.invalid");
    }

    [Fact]
    public void Create_rejects_a_non_usd_currency()
    {
        var result = Book.Create(Id, "Household", Basis.Cash, Currency.Of("EUR"), JanuaryFirst);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("book.currency.unsupported");
    }

    // ---- Rename ------------------------------------------------------------

    [Fact]
    public void Rename_replaces_the_name_leaving_everything_else()
    {
        var book = Book.Create(Id, "Household", Basis.Cash, Currency.Usd, JanuaryFirst).Value;

        var renamed = book.Rename("  Family Household  ").Value;

        renamed.Name.Should().Be("Family Household");
        renamed.Basis.Should().Be(Basis.Cash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_rejects_blank_name(string blank)
    {
        var book = Book.Create(Id, "Household", Basis.Cash, Currency.Usd, JanuaryFirst).Value;

        var result = book.Rename(blank);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("book.name.required");
    }

    // ---- Archive / Reopen ---------------------------------------------------

    [Fact]
    public void Archive_and_Reopen_toggle_status_only()
    {
        var book = Book.Create(Id, "Household", Basis.Cash, Currency.Usd, JanuaryFirst).Value;

        var archived = book.Archive();
        archived.Status.Should().Be(BookStatus.Archived);
        archived.Name.Should().Be(book.Name);

        archived.Reopen().Status.Should().Be(BookStatus.Open);
    }

    // ---- SetFiscalYearStart -------------------------------------------------

    [Fact]
    public void SetFiscalYearStart_replaces_it()
    {
        var book = Book.Create(Id, "Household", Basis.Cash, Currency.Usd, JanuaryFirst).Value;
        var aprilSixth = MonthDay.Create(4, 6).Value;

        book.SetFiscalYearStart(aprilSixth).FiscalYearStart.Should().Be(aprilSixth);
    }

    [Fact]
    public void SetFiscalYearStart_rejects_null()
    {
        var book = Book.Create(Id, "Household", Basis.Cash, Currency.Usd, JanuaryFirst).Value;

        var act = () => book.SetFiscalYearStart(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---- Identity equality (entity, not value object) ----------------------

    [Fact]
    public void Books_with_the_same_id_are_equal_even_if_other_fields_differ()
    {
        var original = Book.Create(Id, "Household", Basis.Cash, Currency.Usd, JanuaryFirst).Value;
        var renamed = original.Rename("Family Household").Value;

        renamed.Should().Be(original);
    }

    [Fact]
    public void Books_with_different_ids_are_not_equal_even_with_identical_fields()
    {
        var a = Book.Create(Id, "Household", Basis.Cash, Currency.Usd, JanuaryFirst).Value;
        var b = Book.Create(Guid.NewGuid(), "Household", Basis.Cash, Currency.Usd, JanuaryFirst).Value;

        a.Should().NotBe(b);
    }
}