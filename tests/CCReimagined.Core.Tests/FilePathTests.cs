using CCReimagined.Core.Model;
using CCReimagined.Core.Providers;

namespace CCReimagined.Core.Tests;

/// <summary>
/// A shell expands ~ and $HOME before a program sees them; a text box does not. Typing an
/// ordinary "~/data/app.db" into the app reached SQLite as a relative path called "~", and the
/// only feedback was "unable to open database file".
/// </summary>
public sealed class FilePathTests
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void A_leading_tilde_becomes_the_home_directory()
    {
        Assert.Equal(
            Path.Combine(Home, "Work", "app.db"),
            FilePath.Resolve("~/Work/app.db"));

        Assert.Equal(Home, FilePath.Resolve("~"));
    }

    [Fact]
    public void A_tilde_that_is_not_a_home_reference_is_left_alone()
    {
        // "~user/..." needs more than string work to resolve, and guessing would be worse, so
        // the segment has to survive rather than be swapped for this account's home.
        var resolved = FilePath.Resolve("~someone/app.db");
        Assert.Contains("~someone", resolved);
    }

    [Fact]
    public void Home_variables_are_expanded()
    {
        Assert.Equal(Path.Combine(Home, "app.db"), FilePath.Resolve("$HOME/app.db"));
        Assert.Equal(Path.Combine(Home, "app.db"), FilePath.Resolve("${HOME}/app.db"));
    }

    [Fact]
    public void An_unknown_variable_is_left_as_written()
    {
        // Better to fail with the path the user typed than with a silently truncated one.
        Assert.Contains("$NOPE_NOT_SET", FilePath.Resolve("/tmp/$NOPE_NOT_SET/app.db"));
    }

    [Fact]
    public void A_relative_path_becomes_absolute()
    {
        var resolved = FilePath.Resolve("data/app.db");
        Assert.True(Path.IsPathRooted(resolved));
        Assert.EndsWith(Path.Combine("data", "app.db"), resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_input_survives_untouched(string? input) =>
        Assert.Equal(input ?? "", FilePath.Resolve(input));

    [Fact]
    public void The_sqlite_provider_resolves_a_tilde_path_in_its_connection_string()
    {
        var provider = new SqliteProvider();

        var cs = provider.BuildConnectionString(new ConnectionSettings { Database = "~/Work/app.db" });

        Assert.Contains(Path.Combine(Home, "Work", "app.db"), cs);
        Assert.DoesNotContain("~", cs);
    }

    [Fact]
    public async Task A_missing_file_is_reported_with_the_resolved_path()
    {
        var provider = new SqliteProvider();
        var missing = Path.Combine(Path.GetTempPath(), $"ccr-absent-{Guid.NewGuid():N}.db");

        var result = await provider.TestConnectionAsync(provider.BuildConnectionString(
            new ConnectionSettings { Database = missing }));

        Assert.False(result.Success);
        // The point of the check: the message names the file rather than saying only
        // "unable to open database file".
        Assert.Contains(missing, result.Error);
    }

    [Fact]
    public async Task A_directory_is_reported_as_a_directory()
    {
        var provider = new SqliteProvider();

        var result = await provider.TestConnectionAsync(provider.BuildConnectionString(
            new ConnectionSettings { Database = Path.GetTempPath() }));

        Assert.False(result.Success);
        Assert.Contains("directory", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
