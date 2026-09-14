using CCReimagined.App.ViewModels;
using CCReimagined.Core.Model;
using CCReimagined.Core.Profiles;
using CCReimagined.Core.Providers;
using Oracle.ManagedDataAccess.Client;

namespace CCReimagined.Core.Tests;

/// <summary>
/// Oracle keeps schemas and services in different namespaces. The list beside the relation
/// picker holds schemas; the connection string names a service. Treating one as the other used
/// to corrupt the connection the moment a connection succeeded: the connected service is never
/// in the schema list, the view model fell back to the first entry, and writing that into the
/// service slot produced ORA-50201 — whose message blames the syntax of the connect string and
/// sends you hunting for a typo that is not there.
/// </summary>
[Collection("live-servers")]
public sealed class OracleSchemaSelectionTests : IDisposable
{
    private readonly string _configRoot =
        Path.Combine(Path.GetTempPath(), $"ccr-ora-{Guid.NewGuid():N}");

    private ProfileStore TempStore() => new(Path.Combine(_configRoot, "connections.json"));

    public void Dispose()
    {
        if (Directory.Exists(_configRoot))
            Directory.Delete(_configRoot, recursive: true);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var cn = new OracleConnection(connectionString);
        await cn.OpenAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public void Oracle_lists_schemas_and_never_reconnects_to_one()
    {
        var provider = ProviderRegistry.ById("oracle");

        Assert.Equal(DatabaseListKind.Schema, provider.Capabilities.DatabaseListKind);
        Assert.Equal("Schemas", provider.Capabilities.DatabaseListLabel);

        // The guarantee that matters: a schema name cannot reach the service-name slot.
        const string cs = "USER ID=ccr;PASSWORD=p;DATA SOURCE=localhost:1521/FREEPDB1";
        Assert.Equal(cs, provider.WithDatabase(cs, "CCR"));
        Assert.Contains("FREEPDB1", provider.WithDatabase(cs, "SOME_OTHER_SCHEMA"));
    }

    [Fact]
    public void The_other_engines_still_reconnect_when_a_database_is_picked()
    {
        foreach (var id in new[] { "sqlserver", "postgresql", "mysql" })
        {
            var provider = ProviderRegistry.ById(id);
            Assert.Equal(DatabaseListKind.Catalog, provider.Capabilities.DatabaseListKind);
            Assert.Equal("Databases", provider.Capabilities.DatabaseListLabel);
        }

        // And the rewrite still does its job where it belongs.
        var pg = ProviderRegistry.ById("postgresql");
        var moved = pg.WithDatabase(
            pg.BuildConnectionString(new ConnectionSettings { Host = "h", Database = "one" }), "two");

        Assert.Contains("two", moved);
        Assert.DoesNotContain("Database=one", moved);
    }

    [SkippableFact]
    public async Task Connecting_then_selecting_a_schema_leaves_the_connection_working()
    {
        var target = LiveServers.Oracle;
        var connectionString = await LiveServers.ReachableConnectionStringAsync(target);
        Skip.If(connectionString is null, LiveServers.SkipReason(target));

        // A schema with no objects in it is not listed at all, and a freshly started server
        // has none — so the test makes its own rather than assuming a seeded database.
        var table = $"ccr_sel_{Guid.NewGuid():N}"[..20];
        await ExecuteAsync(connectionString!, $"CREATE TABLE {table} (id NUMBER(9) PRIMARY KEY)");

        try
        {
            await AssertSchemaSelectionAsync(target, connectionString!, table);
        }
        finally
        {
            await ExecuteAsync(connectionString!, $"DROP TABLE {table}");
        }
    }

    private async Task AssertSchemaSelectionAsync(LiveTarget target, string connectionString, string table)
    {
        var vm = new MainViewModel(TempStore())
        {
            SelectedProvider = ProviderRegistry.ById("oracle"),
        };

        vm.Host = target.Defaults.Host;
        vm.Port = target.Defaults.Port?.ToString() ?? "1521";
        vm.DatabaseOrPath = "FREEPDB1";          // the service, as the user types it
        vm.UseIntegratedSecurity = false;
        vm.UserName = target.Defaults.UserName!;
        vm.Password = target.Defaults.Password!;

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected, vm.StatusMessage);
        Assert.Contains("FREEPDB1", vm.EffectiveConnectionString);
        Assert.Contains(vm.Relations, r =>
            string.Equals(r.Relation.Name, table, StringComparison.OrdinalIgnoreCase));

        // Nothing is auto-selected, because the connected service is not one of these entries.
        Assert.Null(vm.SelectedDatabase);
        Assert.Contains("CCR", vm.Databases);

        // Selecting a schema filters the relations and leaves the connection string alone —
        // this is the step that used to break it.
        var before = vm.EffectiveConnectionString;
        vm.SelectedDatabase = "CCR";

        Assert.Equal(before, vm.EffectiveConnectionString);
        Assert.Contains("FREEPDB1", vm.EffectiveConnectionString);
        Assert.DoesNotContain("/CCR", vm.EffectiveConnectionString);
        Assert.All(vm.Relations, r => Assert.Equal("CCR", r.Relation.Schema));
        Assert.Contains(vm.Relations, r =>
            string.Equals(r.Relation.Name, table, StringComparison.OrdinalIgnoreCase));

        // And the connection still works afterwards.
        var probe = await ProviderRegistry.ById("oracle")
            .TestConnectionAsync(vm.EffectiveConnectionString);

        Assert.True(probe.Success, probe.Error);
    }
}
