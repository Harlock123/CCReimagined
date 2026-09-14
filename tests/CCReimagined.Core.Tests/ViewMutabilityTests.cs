using System.Data.Common;
using CCReimagined.Core.Codegen;
using CCReimagined.Core.Model;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using CCReimagined.Core.Providers;
using Npgsql;

namespace CCReimagined.Core.Tests;

/// <summary>
/// A view that cannot be written through should not get Add, Update and Delete. The engines
/// disagree about how to be asked, and one of them answers wrongly, so each is checked against
/// what it actually does rather than what it claims:
///
///   PostgreSQL, MySQL, MariaDB  information_schema.views.is_updatable, and it is accurate
///   SQLite                      no such column, and writes through a view are refused outright
///   SQL Server                  has the column; it reports NO for views that accept an UPDATE
/// </summary>
[Collection("live-servers")]
public sealed class ViewMutabilityTests
{
    // ---------------------------------------------------------------- SQLite, no server needed

    [Fact]
    public async Task A_sqlite_view_is_read_only_and_loses_its_mutating_methods()
    {
        var (path, connectionString) = NewSqliteDatabase();

        try
        {
            await ExecuteSqliteAsync(connectionString, """
                CREATE TABLE person (id INTEGER PRIMARY KEY, name VARCHAR(40) NOT NULL, active BOOLEAN NOT NULL);
                CREATE VIEW active_person AS SELECT id, name FROM person WHERE active = 1;
                """);

            var provider = ProviderRegistry.ById("sqlite");
            var schema = await provider.GetTableSchemaAsync(
                connectionString, new TableRef(null, "active_person", RelationKind.View));

            Assert.Equal(ViewMutability.ReadOnly, schema.Mutability);
            Assert.True(schema.IsReadOnlyView);

            var artifact = Generate(schema, provider.CodegenProfile);

            // Reads stay, writes go.
            Assert.Contains("ReadAsync", artifact.Content);
            Assert.Contains("GetAllAsync", artifact.Content);
            Assert.DoesNotContain("AddAsync", artifact.Content);
            Assert.DoesNotContain("UpdateAsync", artifact.Content);
            Assert.DoesNotContain("DeleteAsync", artifact.Content);
            // The SQL for them should be gone too, not merely unreferenced.
            Assert.DoesNotContain("InsertSql", artifact.Content);
            Assert.DoesNotContain("DeleteByKeySql", artifact.Content);

            Assert.Contains(artifact.Warnings, w => w.Contains("read-only view"));
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task An_instead_of_trigger_makes_a_sqlite_view_updatable()
    {
        var (path, connectionString) = NewSqliteDatabase();

        try
        {
            await ExecuteSqliteAsync(connectionString, """
                CREATE TABLE person (id INTEGER PRIMARY KEY, name VARCHAR(40) NOT NULL, active BOOLEAN NOT NULL);
                CREATE VIEW active_person AS SELECT id, name FROM person WHERE active = 1;
                CREATE TRIGGER active_person_update INSTEAD OF UPDATE ON active_person
                BEGIN
                    UPDATE person SET name = NEW.name WHERE id = OLD.id;
                END;
                """);

            var provider = ProviderRegistry.ById("sqlite");
            var schema = await provider.GetTableSchemaAsync(
                connectionString, new TableRef(null, "active_person", RelationKind.View));

            // The trigger is what makes the write possible, so the methods come back.
            Assert.Equal(ViewMutability.Updatable, schema.Mutability);
            Assert.Contains("UpdateAsync", Generate(schema, provider.CodegenProfile).Content);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task A_table_is_never_asked_the_question()
    {
        var (path, connectionString) = NewSqliteDatabase();

        try
        {
            await ExecuteSqliteAsync(connectionString,
                "CREATE TABLE person (id INTEGER PRIMARY KEY, name VARCHAR(40) NOT NULL);");

            var provider = ProviderRegistry.ById("sqlite");
            var schema = await provider.GetTableSchemaAsync(connectionString, new TableRef(null, "person"));

            Assert.Equal(ViewMutability.NotApplicable, schema.Mutability);
            Assert.False(schema.IsReadOnlyView);
            Assert.Contains("UpdateAsync", Generate(schema, provider.CodegenProfile).Content);
        }
        finally
        {
            Cleanup(path);
        }
    }

    [Fact]
    public async Task The_database_can_be_overruled_in_both_directions()
    {
        var (path, connectionString) = NewSqliteDatabase();

        try
        {
            await ExecuteSqliteAsync(connectionString, """
                CREATE TABLE person (id INTEGER PRIMARY KEY, name VARCHAR(40) NOT NULL, active BOOLEAN NOT NULL);
                CREATE VIEW active_person AS SELECT id, name FROM person WHERE active = 1;
                """);

            var provider = ProviderRegistry.ById("sqlite");
            var schema = await provider.GetTableSchemaAsync(
                connectionString, new TableRef(null, "active_person", RelationKind.View));

            // Forced on: the methods appear, and the file says they will fail.
            var forced = Generate(schema, provider.CodegenProfile, mutating: true);
            Assert.Contains("UpdateAsync", forced.Content);
            Assert.Contains(forced.Warnings, w => w.Contains("fail at runtime"));
            Assert.Contains(forced.Warnings, w => w.Contains("overridden"));

            // Forced off on a table, for a caller that wants a read-only wrapper.
            var tableSchema = await provider.GetTableSchemaAsync(connectionString, new TableRef(null, "person"));
            var readOnly = Generate(tableSchema, provider.CodegenProfile, mutating: false);
            Assert.DoesNotContain("UpdateAsync", readOnly.Content);
            Assert.Contains("ReadAsync", readOnly.Content);
        }
        finally
        {
            Cleanup(path);
        }
    }

    // ---------------------------------------------------------------- the server engines

    public static IEnumerable<object[]> Servers()
    {
        yield return [LiveServers.PostgreSql];
        yield return [LiveServers.MySql];
        yield return [LiveServers.MariaDb];
    }

    [SkippableTheory]
    [MemberData(nameof(Servers))]
    public async Task A_simple_view_is_reported_updatable_and_keeps_its_methods(LiveTarget target)
    {
        var connectionString = await LiveServers.ReachableConnectionStringAsync(target);
        Skip.If(connectionString is null, LiveServers.SkipReason(target));

        var provider = target.Provider;
        var table = $"ccr_vt_{Guid.NewGuid():N}"[..20];
        var view = table + "_v";

        await ExecuteAsync(target, connectionString!, target.CreateTableSql(table));
        await ExecuteAsync(target, connectionString!,
            $"CREATE VIEW {view} AS SELECT id, name, balance FROM {table} WHERE is_active = {TrueLiteral(target)}");

        try
        {
            var schema = await provider.GetTableSchemaAsync(
                connectionString!, new TableRef(null, view, RelationKind.View));

            // A single-table view with no aggregation is updatable on all three.
            Assert.Equal(ViewMutability.Updatable, schema.Mutability);
            Assert.Contains("UpdateAsync", Generate(schema, provider.CodegenProfile).Content);
        }
        finally
        {
            await ExecuteAsync(target, connectionString!, $"DROP VIEW IF EXISTS {view}");
            await ExecuteAsync(target, connectionString!, $"DROP TABLE IF EXISTS {table}");
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Servers))]
    public async Task An_aggregating_view_is_reported_read_only_and_loses_them(LiveTarget target)
    {
        var connectionString = await LiveServers.ReachableConnectionStringAsync(target);
        Skip.If(connectionString is null, LiveServers.SkipReason(target));

        var provider = target.Provider;
        var table = $"ccr_va_{Guid.NewGuid():N}"[..20];
        var view = table + "_v";

        await ExecuteAsync(target, connectionString!, target.CreateTableSql(table));
        // GROUP BY puts a view beyond writing on every engine that answers the question.
        await ExecuteAsync(target, connectionString!,
            $"CREATE VIEW {view} AS SELECT name, COUNT(*) AS how_many FROM {table} GROUP BY name");

        try
        {
            var schema = await provider.GetTableSchemaAsync(
                connectionString!, new TableRef(null, view, RelationKind.View));

            Assert.Equal(ViewMutability.ReadOnly, schema.Mutability);

            var artifact = Generate(schema, provider.CodegenProfile);
            Assert.DoesNotContain("AddAsync", artifact.Content);
            Assert.DoesNotContain("UpdateAsync", artifact.Content);
            Assert.DoesNotContain("DeleteAsync", artifact.Content);
            Assert.Contains("GetAllAsync", artifact.Content);
        }
        finally
        {
            await ExecuteAsync(target, connectionString!, $"DROP VIEW IF EXISTS {view}");
            await ExecuteAsync(target, connectionString!, $"DROP TABLE IF EXISTS {table}");
        }
    }

    // ---------------------------------------------------------------- plumbing

    private static string TrueLiteral(LiveTarget target) =>
        target.ProviderId == "postgresql" ? "true" : "1";

    private static GeneratedArtifact Generate(TableSchema schema, ICodegenProfile profile, bool? mutating = null) =>
        new CSharpDataClassGenerator().Generate(new GenerationRequest
        {
            Schema = schema,
            Profile = profile,
            ClassName = "Subject",
            Namespace = "Views.Test",
            Options = new GenerationOptions { MutatingMethodsForViews = mutating },
        });

    private static (string Path, string ConnectionString) NewSqliteDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccr-view-{Guid.NewGuid():N}.db");
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ConnectionString;

        return (path, cs);
    }

    private static void Cleanup(string path)
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(path))
            File.Delete(path);
    }

    private static async Task ExecuteSqliteAsync(string connectionString, string sql)
    {
        await using var cn = new SqliteConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(LiveTarget target, string connectionString, string sql)
    {
        DbConnection cn = target.ProviderId switch
        {
            "postgresql" => new NpgsqlConnection(connectionString),
            "mysql" => new MySqlConnection(connectionString),
            _ => throw new NotSupportedException(target.ProviderId),
        };

        await using (cn)
        {
            await cn.OpenAsync();
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
