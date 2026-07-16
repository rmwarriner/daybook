namespace Daybook.Accounting.Core;

/// <summary>
/// How an <see cref="Error"/> should be presented and recovered from
/// (spec §10). Only the categories Core actually produces today are
/// present; <c>Auth</c> and <c>Infrastructure</c> are Api/Infrastructure
/// concerns and are added when those layers need them.
/// </summary>
public enum ErrorCategory
{
    /// <summary>Fixable input — the caller supplied a bad value for a field.</summary>
    Validation,

    /// <summary>A collision with existing state (e.g. a duplicate unique value).</summary>
    Conflict,

    /// <summary>The input is individually valid but violates a domain rule.</summary>
    BusinessRule,
}