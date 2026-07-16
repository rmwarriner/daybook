namespace Daybook.Accounting.Core;

/// <summary>
/// A stable, actionable description of an expected business-rule failure
/// (CLAUDE.md golden rule 6 — "no dead-end errors"). Carries a machine-
/// readable <see cref="Code"/>, a category, a plain-language message, and
/// one or more <see cref="Recovery"/> options. This is the Core-level
/// payload; the Api layer maps it onto the RFC 7807 wire format (spec §10).
/// </summary>
public sealed record Error(
    string Code,
    ErrorCategory Category,
    string Message,
    IReadOnlyList<string> Recovery)
{
    public Error(string code, ErrorCategory category, string message)
        : this(code, category, message, Array.Empty<string>())
    {
    }

    public bool Equals(Error? other) =>
        other is not null &&
        Code == other.Code &&
        Category == other.Category &&
        Message == other.Message &&
        Recovery.SequenceEqual(other.Recovery);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Code);
        hash.Add(Category);
        hash.Add(Message);
        foreach (var option in Recovery)
        {
            hash.Add(option);
        }

        return hash.ToHashCode();
    }
}