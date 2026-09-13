namespace CCReimagined.Core.Model;

/// <summary>
/// Turns a path a person typed into one the filesystem understands.
///
/// A shell expands <c>~</c> and <c>$HOME</c> before a program ever sees them; a text box in a
/// GUI does not. Without this, pasting a perfectly ordinary <c>~/data/app.db</c> reaches SQLite
/// as a relative path called "~", and the only feedback is "unable to open database file".
/// </summary>
public static class FilePath
{
    /// <summary>
    /// Expands a leading <c>~</c>, expands environment variables, and makes the result absolute.
    /// Returns the input unchanged when it is null, blank, or already absolute and plain.
    /// </summary>
    public static string Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path ?? "";

        var value = path.Trim();

        // %VAR% on Windows, and a no-op elsewhere.
        value = Environment.ExpandEnvironmentVariables(value);

        // $HOME and ${HOME}, which ExpandEnvironmentVariables does not touch.
        value = ExpandUnixVariables(value);

        value = ExpandTilde(value);

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Not a path we can normalise. Hand it back and let the provider report the real error.
            return value;
        }
    }

    private static string ExpandTilde(string value)
    {
        if (value.Length == 0 || value[0] != '~')
            return value;

        // "~" alone, or "~/rest". A "~user/rest" form is left alone: resolving another
        // account's home needs more than string work, and guessing would be worse.
        if (value.Length == 1)
            return Home();

        if (value[1] == '/' || value[1] == Path.DirectorySeparatorChar)
            return Path.Combine(Home(), value[2..].TrimStart('/', Path.DirectorySeparatorChar));

        return value;
    }

    private static string ExpandUnixVariables(string value)
    {
        if (!value.Contains('$'))
            return value;

        return System.Text.RegularExpressions.Regex.Replace(
            value,
            @"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}|\$(?<name>[A-Za-z_][A-Za-z0-9_]*)",
            m => Environment.GetEnvironmentVariable(m.Groups["name"].Value) ?? m.Value);
    }

    private static string Home()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return string.IsNullOrEmpty(home)
            ? Environment.GetEnvironmentVariable("HOME") ?? ""
            : home;
    }
}
