namespace CCReimagined.Core.Model;

public enum AuthMode
{
    /// <summary>Windows/Kerberos/peer authentication — no credentials in the connection string.</summary>
    Integrated,

    /// <summary>Explicit user name and password.</summary>
    UserPassword,
}

/// <summary>
/// The pieces the UI collects, which each provider turns into its own connection string.
/// A provider may also be handed a <see cref="RawConnectionString"/> to use verbatim,
/// which is the modern form of the old tool's "manual DSN" escape hatch.
/// </summary>
public sealed record ConnectionSettings
{
    public string Host { get; init; } = "";

    public int? Port { get; init; }

    /// <summary>Initial catalog / database. For SQLite this is the file path.</summary>
    public string? Database { get; init; }

    public AuthMode AuthMode { get; init; } = AuthMode.Integrated;

    public string? UserName { get; init; }

    public string? Password { get; init; }

    public bool TrustServerCertificate { get; init; } = true;

    public bool Encrypt { get; init; }

    public int ConnectTimeoutSeconds { get; init; } = 15;

    /// <summary>When set, providers hand this back untouched instead of composing one.</summary>
    public string? RawConnectionString { get; init; }

    public bool UsesRaw => !string.IsNullOrWhiteSpace(RawConnectionString);
}

/// <summary>
/// What the list beside the relation picker actually contains, because the engines do not
/// agree. For most, an entry is a catalog you reconnect to. For Oracle it is a schema inside
/// the database you are already connected to — selecting one narrows the relation list and
/// must not touch the connection string, whose service name is a different thing entirely.
/// </summary>
public enum DatabaseListKind
{
    /// <summary>Entries are catalogs. Selecting one reconnects to it.</summary>
    Catalog,

    /// <summary>Entries are schemas within the current connection. Selecting one filters.</summary>
    Schema,
}

/// <summary>What a provider needs from the user, so the UI can enable only the relevant fields.</summary>
public sealed record ProviderCapabilities
{
    /// <summary>SQLite connects to a file, not a host.</summary>
    public bool NeedsHost { get; init; } = true;

    public bool SupportsIntegratedAuth { get; init; }

    /// <summary>Whether the server can be asked to list its databases before one is chosen.</summary>
    public bool SupportsDatabaseEnumeration { get; init; } = true;

    /// <summary>SQLite has a single implicit database, so the database picker is skipped.</summary>
    public bool SupportsSchemas { get; init; } = true;

    public int DefaultPort { get; init; }

    /// <summary>File-extension filter for providers whose "database" is a file.</summary>
    public bool DatabaseIsFilePath { get; init; }

    /// <summary>Whether the browsable list holds catalogs or schemas.</summary>
    public DatabaseListKind DatabaseListKind { get; init; } = DatabaseListKind.Catalog;

    /// <summary>What to call that list in the UI.</summary>
    public string DatabaseListLabel => DatabaseListKind == DatabaseListKind.Schema ? "Schemas" : "Databases";
}

public sealed record ProbeResult(bool Success, string? ServerVersion, string? Error)
{
    public static ProbeResult Ok(string? version) => new(true, version, null);

    public static ProbeResult Fail(string error) => new(false, null, error);
}
