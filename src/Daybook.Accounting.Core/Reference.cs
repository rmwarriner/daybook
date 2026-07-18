namespace Daybook.Accounting.Core;

/// <summary>
/// Identifying metadata riding alongside a <see cref="JournalEntry"/> — a
/// check number, an ACH/wire confirmation, and the like (spec §4.3.1).
/// Never part of the balancing math. A value object, like <see cref="JournalLine"/>.
/// </summary>
/// <remarks>
/// This type enforces only what it can check on its own: a defined
/// <see cref="Type"/> and a non-blank <see cref="Value"/>. Duplicate
/// detection needs the rest of the journal, so that's a query on
/// <see cref="Journal"/> instead.
/// </remarks>
public sealed record Reference
{
    public ReferenceType Type { get; }

    public string Value { get; }

    private Reference(ReferenceType type, string value)
    {
        Type = type;
        Value = value;
    }

    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null — a caller bug, not a business rule.</exception>
    public static Result<Reference> Create(ReferenceType type, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!Enum.IsDefined(type))
        {
            return new Error(
                "reference.type.invalid",
                ErrorCategory.Validation,
                $"'{type}' is not a recognized reference type.",
                ["Use one of: Check, ACH, Wire, Invoice, Receipt, Card, Other."]);
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return new Error(
                "reference.value.required",
                ErrorCategory.Validation,
                "Reference value must not be empty.",
                ["Provide a non-empty Value."]);
        }

        return new Reference(type, trimmed);
    }
}