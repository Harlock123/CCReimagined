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

    public static IEnumerable<object[]> All()
    {
        yield return [PostgreSql];
        yield return [MySql];
        yield return [MariaDb];
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
            var result = probe.Success ? connectionString : null;

            Probed[target.Label] = result;
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static string SkipReason(LiveTarget target) =>
        $"{target.Label} is not reachable. Start it with " +
        $"`docker compose up -d` in dev/sample-databases, or set {target.EnvironmentVariable}.";
}
