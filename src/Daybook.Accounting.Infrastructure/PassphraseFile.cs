namespace Daybook.Accounting.Infrastructure;

/// <summary>
/// Reads the database passphrase from a fixed file path (spec §13.5) — the
/// only mechanism this builds. Never an environment variable: it leaks via
/// <c>inspect</c>, <c>/proc</c>, crash dumps, and logs.
/// </summary>
public static class PassphraseFile
{
    /// <exception cref="FileNotFoundException">No file exists at <paramref name="path"/>.</exception>
    public static string Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No passphrase file found at '{path}'. Create it with the database passphrase as its contents.",
                path);
        }

        return File.ReadAllText(path).Trim();
    }
}