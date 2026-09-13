using CCReimagined.Core.Model;
using CCReimagined.Core.Providers;

namespace CCReimagined.Core.Tests;

/// <summary>
/// One engine the live tests can run against, with the DDL for a throwaway table shaped to
/// exercise the generated CRUD: a database-generated key, a NOT NULL and a nullable string,
/// a decimal, a boolean, a date and a blob.
/// </summary>
public sealed record LiveTarget(
    string Label,
    string ProviderId,
    string EnvironmentVariable,
    Func<string, string> CreateTableSql,
    ConnectionSettings Defaults)
{
    /// <summary>
    /// Optional setup run once the server answers, returning the connection string the tests
    /// should actually use. SQL Server needs it: the engine has no init-script convention, so
    /// the test database may not exist yet.
    /// </summary>
    public Func<string, Task<string>>? Prepare { get; init; }

    public IDatabaseProvider Provider => ProviderRegistry.ById(ProviderId);

    public override string ToString() => Label;
}

/// <summary>
/// Discovery of the sample servers from dev/sample-databases. Each engine is probed once per
/// process; when it is not reachable the tests skip rather than fail, so a bare checkout with
/// no containers running still goes green.
///
/// Override any of them with a full connection string, e.g. for CI:
///     CCR_TEST_POSTGRES="Host=db;Username=x;Password=y;Database=z"
/// </summary>
public static class LiveServers
{
    private static readonly Dictionary<string, string?> Probed = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static LiveTarget PostgreSql { get; } = new(
        "PostgreSQL",
        "postgresql",
        "CCR_TEST_POSTGRES",
        table => $"""
            CREATE TABLE {table} (
                id        integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                name      varchar(40) NOT NULL,
                nickname  varchar(40),
                balance   numeric(12,2) NOT NULL,
                is_active boolean NOT NULL,
                joined    date,
                payload   bytea
            )
            """,
        new ConnectionSettings
        {
            Host = "localhost",
            Port = 5432,
            Database = "ccrsample",
            AuthMode = AuthMode.UserPassword,
            UserName = "ccr",
            Password = "ccr_dev_password",
            ConnectTimeoutSeconds = 3,
        });

    public static LiveTarget MySql { get; } = new(
        "MySQL",
        "mysql",
        "CCR_TEST_MYSQL",
        table => $"""
            CREATE TABLE {table} (
                id        INT AUTO_INCREMENT PRIMARY KEY,
                name      VARCHAR(40) NOT NULL,
                nickname  VARCHAR(40),
                balance   DECIMAL(12,2) NOT NULL,
                is_active TINYINT(1) NOT NULL,
                joined    DATE,
                payload   BLOB
            )
            """,
        new ConnectionSettings
        {
            Host = "localhost",
            Port = 3306,
            Database = "ccrsample",
            AuthMode = AuthMode.UserPassword,
            UserName = "ccr",
            Password = "ccr_dev_password",
            ConnectTimeoutSeconds = 3,
        });

    public static LiveTarget MariaDb { get; } = MySql with
    {
        Label = "MariaDB",
        EnvironmentVariable = "CCR_TEST_MARIADB",
        Defaults = MySql.Defaults with { Port = 3307 },
    };

    public static LiveTarget SqlServer { get; } = new(
        "SQL Server",
        "sqlserver",
        "CCR_TEST_SQLSERVER",
        table => $"""
            CREATE TABLE {table} (
                id        int IDENTITY(1,1) PRIMARY KEY,
                name      nvarchar(40) NOT NULL,
                nickname  nvarchar(40) NULL,
                balance   decimal(12,2) NOT NULL,
                is_active bit NOT NULL,
                joined    date NULL,
                payload   varbinary(max) NULL
            )
            """,
        new ConnectionSettings
        {
            Host = "localhost",
            Port = 1433,
            // Connect to master first; Prepare creates the test database and repoints here.
            Database = "master",
            AuthMode = AuthMode.UserPassword,
            UserName = "sa",
            Password = "ccr_Dev_Password1",
            TrustServerCertificate = true,
            ConnectTimeoutSeconds = 5,
        })
    {
        Prepare = EnsureSqlServerDatabaseAsync,
    };

    private const string SqlServerTestDatabase = "ccrsample";

    /// <summary>
    /// Creates the test database when it is missing, so the live tests stand on their own
    /// rather than depending on the sample seed having been applied.
    /// </summary>
    private static async Task<string> EnsureSqlServerDatabaseAsync(string masterConnectionString)
    {
        var provider = ProviderRegistry.ById("sqlserver");

        await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(masterConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID('{SqlServerTestDatabase}') IS NULL CREATE DATABASE [{SqlServerTestDatabase}];";
            await command.ExecuteNonQueryAsync();
        }

        return provider.WithDatabase(masterConnectionString, SqlServerTestDatabase);
    }

    public static IEnumerable<object[]> All()
    {
        yield return [PostgreSql];
        yield return [MySql];
        yield return [MariaDb];
        yield return [SqlServer];
    }

    /// <summary>
    /// The connection string for a reachable server, or null when it is not up. Probed once
    /// per engine per process, so a missing server costs one short timeout rather than one
    /// per test.
    /// </summary>
    public static async Task<string?> ReachableConnectionStringAsync(LiveTarget target)
    {
        await Gate.WaitAsync();

        try
        {
            if (Probed.TryGetValue(target.Label, out var cached))
                return cached;

            var overridden = Environment.GetEnvironmentVariable(target.EnvironmentVariable);

            var connectionString = string.IsNullOrWhiteSpace(overridden)
                ? target.Provider.BuildConnectionString(target.Defaults)
                : overridden;

            var probe = await target.Provider.TestConnectionAsync(connectionString);
            string? result = null;

            if (probe.Success)
            {
                result = target.Prepare is null
                    ? connectionString
                    : await target.Prepare(connectionString);
            }

            Probed[target.Label] = result;
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static string SkipReason(LiveTarget target) =>
        $"{target.Label} is not reachable. Start it from dev/sample-databases " +
        $"(`docker compose up -d`, plus a --profile for SQL Server), or set {target.EnvironmentVariable}.";
}
