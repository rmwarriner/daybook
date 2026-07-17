namespace Daybook.Accounting.Application;

/// <summary>
/// The shared HTML document scaffold every report presenter wraps its
/// table markup in (spec §12) — one embedded stylesheet implementing the
/// print rules, rather than each presenter duplicating it. Internal: no
/// external caller needs the scaffold on its own, only a fully rendered
/// report.
/// </summary>
/// <remarks>
/// Print rules implemented here: black text on white, no dark headers or
/// filled backgrounds; thin hairline rules only under headers and above
/// totals; right-aligned, monospaced-figure numerals via
/// <c>font-variant-numeric: tabular-nums</c> (not a whole-document
/// monospace font); standard system fonts; bold reserved for headings and
/// totals, not applied broadly; fits Letter with 1-inch margins.
/// </remarks>
internal static class HtmlReportDocument
{
    internal static string Wrap(string title, string bodyHtml)
    {
        var escapedTitle = Escape(title);
        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>{escapedTitle}</title>
            <style>
            {Css}
            </style>
            </head>
            <body>
            <h1>{escapedTitle}</h1>
            {bodyHtml}
            </body>
            </html>
            """;
    }

    internal static string Escape(string text) => System.Net.WebUtility.HtmlEncode(text);

    private const string Css = """
        @page { size: letter; margin: 1in; }
        body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Helvetica, Arial, sans-serif; color: #000; background: #fff; max-width: 7.5in; margin: 0 auto; padding: 1em; }
        h1, h2 { font-weight: 600; }
        table { border-collapse: collapse; width: 100%; margin: 1em 0; }
        th, td { padding: 0.25em 0.5em; text-align: left; }
        thead th { border-bottom: 1px solid #333; }
        .amount { text-align: right; font-variant-numeric: tabular-nums; }
        tfoot td, .total td { border-top: 1px solid #333; font-weight: bold; }
        p.total { border-top: 1px solid #333; font-weight: bold; padding-top: 0.25em; }
        """;
}