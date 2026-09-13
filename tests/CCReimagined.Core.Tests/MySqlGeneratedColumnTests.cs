using System.Data.Common;
using CCReimagined.Core.Model;
using MySqlConnector;

namespace CCReimagined.Core.Tests;

/// <summary>
/// MySQL reports "DEFAULT_GENERATED" in a column's <c>extra</c> for anything carrying a default
/// expression — <c>DEFAULT CURRENT_TIMESTAMP</c> most of all — and "VIRTUAL GENERATED" or
/// "STORED GENERATED" for a column the engine actually computes. Only the latter two may be
/// kept out of INSERT and UPDATE.
///
/// Matching loosely on "generated" silently dropped every created_at column from the generated
/// writes, which is about the most common column idiom there is.
/// </summary>
[Collection("live-servers")]
public sealed class MySqlGeneratedColumnTests
{
    public static IEnumerable<object[]> MySqlFamily()
    {
        yield return [LiveServers.MySql];
        yield return [LiveServers.MariaDb];
    }

    [SkippableTheory]
    [MemberData(nameof(MySqlFamily))]
    public async Task A_default_expression_does_not_make_a_column_computed(LiveTarget target)
    {
        var connectionString = await LiveServers.ReachableConnectionStringAsync(target);
        Skip.If(connectionString is null, LiveServers.SkipReason(target));

        var provider = target.Provider;
        var table = $"ccr_gen_{Guid.NewGuid():N}"[..22];

        await ExecuteAsync(connectionString!, $"""
            CREATE TABLE {table} (
                id         INT AUTO_INCREMENT PRIMARY KEY,
                label      VARCHAR(40) NOT NULL,
                qty        INT NOT NULL DEFAULT 0,
                unit_cost  DECIMAL(10,2) NOT NULL DEFAULT 0,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                total      DECIMAL(12,2) AS (qty * unit_cost) STORED
            )
            """);

        try
        {
            var schema = await provider.GetTableSchemaAsync(connectionString!, new TableRef(null, table));
            var byName = schema.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);

            // Only the computed column is computed.
            Assert.True(byName["total"].IsComputed, "a STORED generated column should be computed");
            Assert.False(byName["created_at"].IsComputed, "DEFAULT CURRENT_TIMESTAMP is not a generated column");
            Assert.False(byName["updated_at"].IsComputed, "ON UPDATE CURRENT_TIMESTAMP is not a generated column");
            Assert.False(byName["qty"].IsComputed);

            // Which means they reach the generated writes.
            var writable = schema.WritableColumns.Select(c => c.Name).ToList();
            Assert.Contains("created_at", writable);
            Assert.Contains("updated_at", writable);
            Assert.DoesNotContain("total", writable);
            Assert.DoesNotContain("id", writable);
        }
        finally
        {
            await ExecuteAsync(connectionString!, $"DROP TABLE IF EXISTS {table}");
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using DbConnection connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
