using System.Text.Json;
using System.Text.Json.Serialization;

namespace CCReimagined.Core.Profiles;

/// <summary>
/// Reads and writes the saved connections as JSON under the platform's per-user config
/// directory. Deliberately small and forgiving: a corrupt or unreadable file costs the user
/// their saved profiles, never their ability to start the app.
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    public ProfileStore(string? path = null) => _path = path ?? DefaultPath();

    public string Path => _path;

    /// <summary>
    /// ~/.config/CCReimagined on Linux, ~/Library/Application Support/CCReimagined on macOS,
    /// %APPDATA%\CCReimagined on Windows.
    /// </summary>
    public static string DefaultPath()
    {
        var root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

        // GetFolderPath can still come back empty in a stripped-down environment.
        if (string.IsNullOrEmpty(root))
            root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return System.IO.Path.Combine(root, "CCReimagined", "connections.json");
    }

    public ProfileBook Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new ProfileBook();

            var json = File.ReadAllText(_path);

            if (string.IsNullOrWhiteSpace(json))
                return new ProfileBook();

            return JsonSerializer.Deserialize<ProfileBook>(json, Json) ?? new ProfileBook();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A profile file we cannot read is not worth failing startup over.
            return new ProfileBook();
        }
    }

    /// <summary>
    /// Writes the book, scrubbing every profile on the way out so a password can never reach
    /// the file even if one slipped into a raw connection string in memory.
    /// </summary>
    public void Save(ProfileBook book)
    {
        var safe = book with
        {
            Profiles = book.Profiles.Select(p => p.ScrubSecrets()).ToList(),
        };

        var directory = System.IO.Path.GetDirectoryName(_path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Write to a sibling file and move it into place, so an interrupted write cannot
        // leave a half-written profile list behind.
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(safe, Json));
        File.Move(temporary, _path, overwrite: true);

        RestrictPermissions(_path);
    }

    /// <summary>
    /// Narrows the file to the owner. It holds no passwords, but it does map out which servers
    /// and accounts the user reaches, which is not something to leave world-readable.
    /// </summary>
    private static void RestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort: a filesystem that cannot express the mode is not a reason to fail the save.
        }
    }
}
