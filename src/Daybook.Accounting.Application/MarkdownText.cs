namespace Daybook.Accounting.Application;

/// <summary>
/// Escapes free text (account names/paths, entry descriptions) for safe
/// use inside a GFM/CommonMark table cell. Neither <c>Account.Name</c> nor
/// <c>JournalEntry.Description</c> restrict characters, so a literal
/// <c>|</c> would otherwise split a row, and an embedded newline would
/// break it across lines.
/// </summary>
internal static class MarkdownText
{
    internal static string EscapeTableCell(string text) =>
        text.Replace("|", "\\|").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
}