using System.Data.Common;
using CCReimagined.Core.Model;

namespace CCReimagined.Core.Profiles;

/// <summary>
/// A saved connection, minus the password. Everything here is written to disk in plain JSON,
/// so nothing secret is allowed in — see <see cref="ScrubSecrets"/>.
/// </summary>
public sealed record ConnectionProfile
{
    public required string Name { get; init; }

    /// <summary>The <see cref="Providers.IDatabaseProvider.Id"/> this profile connects with.</summary>
    public required string ProviderId { get; init; }

    public string Host { get; init; } = "";

    public int? Port { get; init; }

    /// <summary>Initial catalog, or the file path for a file-backed engine.</summary>
    public string? Database { get; init; }

    public bool UseIntegratedSecurity { get; init; } = true;

    public string? UserName { get; init; }

    public bool TrustServerCertificate { get; init; } = true;

    public bool UseRawConnectionString { get; init; }

    /// <summary>Raw connection string with any password-bearing keyword removed.</summary>
    public string? RawConnectionString { get; init; }

    /// <summary>Generation defaults worth carrying between sessions.</summary>
    public string? TargetNamespace { get; init; }

    public string? CodegenProfileId { get; init; }

    public DateTimeOffset LastUsedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Connection-string keywords that carry a secret. A raw connection string is the one place
    /// a password can reach a profile, so it is stripped before the profile is ever persisted.
    /// </summary>
    private static readonly string[] SecretKeywords =
    [
        "password", "pwd", "accesstoken", "access token", "secret",
        "clientsecret", "client secret", "sharedaccesssignature", "apikey", "api key",
    ];

    /// <summary>
    /// Returns a copy safe to write to disk: the raw connection string keeps its shape but loses
    /// every password-bearing keyword, so a pasted string cannot leak a secret into the file.
    /// </summary>
    public ConnectionProfile ScrubSecrets()
    {
        if (string.IsNullOrWhiteSpace(RawConnectionString))
            return this;

        return this with { RawConnectionString = ScrubConnectionString(RawConnectionString) };
    }

    public static string ScrubConnectionString(string connectionString)
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };

            // Keys are matched loosely because providers spell them differently
            // ("Password", "PWD", "Client Secret") and DbConnectionStringBuilder lower-cases them.
            var doomed = builder.Keys
                .Cast<string>()
                .Where(IsSecretKeyword)
                .ToList();

            foreach (var key in doomed)
                builder.Remove(key);

            return builder.ConnectionString;
        }
        catch (ArgumentException)
        {
            // Not parseable as a connection string. Rather than persist something that might
            // hold a password, drop it and let the user retype it.
            return "";
        }
    }

    private static bool IsSecretKeyword(string key)
    {
        var normalised = key.Replace(" ", "").Replace("_", "").ToLowerInvariant();

        return SecretKeywords.Any(secret =>
            normalised == secret.Replace(" ", "") ||
            normalised.EndsWith("password", StringComparison.Ordinal) ||
            normalised.EndsWith("secret", StringComparison.Ordinal));
    }

    /// <summary>Builds the settings for a connection attempt, with the password supplied separately.</summary>
    public ConnectionSettings ToSettings(string? password) => new()
    {
        Host = Host,
        Port = Port,
        Database = Database,
        AuthMode = UseIntegratedSecurity ? AuthMode.Integrated : AuthMode.UserPassword,
        UserName = UserName,
        Password = password,
        TrustServerCertificate = TrustServerCertificate,
        RawConnectionString = UseRawConnectionString ? RawConnectionString : null,
    };
}

/// <summary>The on-disk shape: the saved profiles plus which one was last connected.</summary>
public sealed record ProfileBook
{
    public List<ConnectionProfile> Profiles { get; init; } = [];

    public string? LastUsedProfileName { get; init; }

    /// <summary>Name of the editor theme the user last chose, so the output tab looks the same next launch.</summary>
    public string? EditorThemeName { get; init; }
}
