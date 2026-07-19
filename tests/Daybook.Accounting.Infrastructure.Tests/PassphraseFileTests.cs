namespace Daybook.Accounting.Infrastructure.Tests;

/// <summary>
/// <see cref="PassphraseFile"/> — reads the database passphrase from a
/// fixed file path (spec §13.5: "never place the passphrase in an
/// environment variable" — it leaks via <c>inspect</c>, <c>/proc</c>,
/// crash dumps, and logs).
/// </summary>
public sealed class PassphraseFileTests : IDisposable
{
    private readonly string _path;

    public PassphraseFileTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"daybook-test-{Guid.NewGuid()}.passphrase");
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Read_returns_the_files_contents()
    {
        File.WriteAllText(_path, "correct horse battery staple");

        PassphraseFile.Read(_path).Should().Be("correct horse battery staple");
    }

    [Fact]
    public void Read_trims_surrounding_whitespace_and_trailing_newline()
    {
        File.WriteAllText(_path, "  correct horse battery staple  \n");

        PassphraseFile.Read(_path).Should().Be("correct horse battery staple");
    }

    [Fact]
    public void Read_of_a_missing_file_throws_a_clear_actionable_exception()
    {
        var act = () => PassphraseFile.Read(_path);

        act.Should().Throw<FileNotFoundException>().WithMessage($"*{_path}*");
    }
}