using CCReimagined.Core.Codegen;
using CCReimagined.Core.Model;

namespace CCReimagined.Core.Providers;

/// <summary>
/// Everything the tool needs to explore one database engine at design time.
/// The old app hard-wired SqlConnection and sys.* catalog queries into the form;
/// each engine now supplies its own implementation behind this interface, and the
/// UI never names a concrete ADO.NET type.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>Stable id used in settings and on the command line, e.g. "sqlserver".</summary>
    string Id { get; }

    string DisplayName { get; }

    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// The profile describing the code this provider's generated classes should emit.
    /// Discovery and generation are deliberately separable: you may explore a SQL Server
    /// database and still emit Npgsql code, which is what a migration needs.
    /// </summary>
    ICodegenProfile CodegenProfile { get; }

    /// <summary>Composes an engine-specific connection string from the UI's fields.</summary>
    string BuildConnectionString(ConnectionSettings settings);

    /// <summary>
    /// Returns the same connection string pointed at a different catalog. Used when the
    /// user picks a database from the list after connecting to the server.
    /// </summary>
    string WithDatabase(string connectionString, string database);

    Task<ProbeResult> TestConnectionAsync(string connectionString, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListDatabasesAsync(string connectionString, CancellationToken ct = default);

    Task<IReadOnlyList<TableRef>> ListRelationsAsync(string connectionString, CancellationToken ct = default);

    Task<TableSchema> GetTableSchemaAsync(string connectionString, TableRef table, CancellationToken ct = default);
}
