using CCReimagined.App.ViewModels;
using CCReimagined.Core.Profiles;
using CCReimagined.Core.Providers;
using Microsoft.Data.Sqlite;

namespace CCReimagined.Core.Tests;

/// <summary>
/// Drives the view model through the same sequence the window does — pick an engine, connect,
/// pick a relation, choose the key and list parameters, generate — against a real SQLite file.
/// The view model holds no Avalonia types, so this runs headless.
/// </summary>
public sealed class MainViewModelFlowTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ccr-vm-{Guid.NewGuid():N}.db");

    // Kept under temp so the suite never writes to the developer's real config directory.
    private readonly string _configRoot = Path.Combine(Path.GetTempPath(), $"ccr-vm-cfg-{Guid.NewGuid():N}");

    private ProfileStore TempStore() => new(Path.Combine(_configRoot, "connections.json"));

    public async Task InitializeAsync()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ConnectionString;

        await using var cn = new SqliteConnection(cs);
        await cn.OpenAsync();
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE Member (
                MemberId    INTEGER PRIMARY KEY,
                LastName    VARCHAR(40) NOT NULL,
                ProgramCode VARCHAR(8)
            );

            CREATE TABLE Claim (
                ClaimId  INTEGER PRIMARY KEY,
                MemberId INTEGER NOT NULL,
                Amount   DECIMAL(10,2) NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
            File.Delete(_dbPath);

        if (Directory.Exists(_configRoot))
            Directory.Delete(_configRoot, recursive: true);

        return Task.CompletedTask;
    }

    private MainViewModel ConnectedToSqlite()
    {
        var vm = new MainViewModel(TempStore())
        {
            SelectedProvider = ProviderRegistry.ById("sqlite"),
        };

        vm.DatabaseOrPath = _dbPath;
        return vm;
    }

    [Fact]
    public async Task Choosing_sqlite_reshapes_the_connection_form()
    {
        var vm = ConnectedToSqlite();

        // SQLite has no host, no server-side database list and no integrated auth, and the
        // window binds each of those to the provider's capabilities rather than hard-coding them.
        Assert.False(vm.NeedsHost);
        Assert.False(vm.SupportsDatabaseEnumeration);
        Assert.False(vm.SupportsIntegratedAuth);
        Assert.True(vm.DatabaseIsFilePath);
        Assert.Equal("Database file", vm.DatabaseLabel);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Connect_lists_relations_without_a_database_picker()
    {
        var vm = ConnectedToSqlite();

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.False(vm.StatusIsError);
        Assert.Equal(2, vm.Relations.Count);
        Assert.Contains(vm.Relations, r => r.Display == "Member");
        Assert.Contains(vm.Relations, r => r.Display == "Claim");
    }

    [Fact]
    public async Task Connecting_to_a_missing_file_reports_the_error_and_stays_disconnected()
    {
        var vm = new MainViewModel(TempStore())
        {
            SelectedProvider = ProviderRegistry.ById("sqlite"),
            DatabaseOrPath = Path.Combine(Path.GetTempPath(), "definitely-not-here.db"),
        };

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.False(vm.IsConnected);
        Assert.True(vm.StatusIsError);
        Assert.Empty(vm.Relations);
    }

    [Fact]
    public async Task Filtering_narrows_the_relation_list()
    {
        var vm = ConnectedToSqlite();
        await vm.ConnectCommand.ExecuteAsync(null);

        vm.RelationFilter = "clai";

        Assert.Single(vm.Relations);
        Assert.Equal("Claim", vm.Relations[0].Display);

        vm.RelationFilter = "";
        Assert.Equal(2, vm.Relations.Count);
    }

    [Fact]
    public async Task Selecting_a_relation_loads_columns_and_preselects_the_key()
    {
        var vm = ConnectedToSqlite();
        await vm.ConnectCommand.ExecuteAsync(null);

        vm.SelectedRelation = vm.Relations.First(r => r.Display == "Member");
        await vm.PendingWork;

        Assert.Equal(3, vm.Columns.Count);
        Assert.Equal("Member", vm.ClassName);

        var key = Assert.Single(vm.Columns, c => c.IsKey);
        Assert.Equal("MemberId", key.Name);
        Assert.Contains("database-generated", vm.SchemaSummary);
    }

    [Fact]
    public async Task Ticking_a_second_key_clears_the_first()
    {
        var vm = ConnectedToSqlite();
        await vm.ConnectCommand.ExecuteAsync(null);

        vm.SelectedRelation = vm.Relations.First(r => r.Display == "Member");
        await vm.PendingWork;

        vm.Columns.First(c => c.Name == "LastName").IsKey = true;

        // The grid's Key column behaves as a radio group, so exactly one stays ticked.
        var key = Assert.Single(vm.Columns, c => c.IsKey);
        Assert.Equal("LastName", key.Name);
    }

    [Fact]
    public async Task Generate_emits_a_class_keyed_and_parameterised_as_chosen()
    {
        var vm = ConnectedToSqlite();
        await vm.ConnectCommand.ExecuteAsync(null);

        vm.SelectedRelation = vm.Relations.First(r => r.Display == "Member");
        await vm.PendingWork;

        vm.TargetNamespace = "Contoso.Data";
        vm.ClassName = "Member";
        vm.Columns.First(c => c.Name == "ProgramCode").IsListParameter = true;

        vm.GenerateCommand.Execute(null);

        Assert.True(vm.HasOutput);
        Assert.Equal("Member.cs", vm.GeneratedFileName);
        Assert.Contains("namespace Contoso.Data;", vm.GeneratedCode);
        Assert.Contains("GetListByProgramCodeAsync", vm.GeneratedCode);
        Assert.Contains("SqliteConnection", vm.GeneratedCode);
        Assert.DoesNotContain("GetListByLastNameAsync", vm.GeneratedCode);

        var (assembly, errors) = GeneratedCodeCompiler.Compile(
            vm.GeneratedCode,
            "VmGeneratedMember",
            typeof(SqliteConnection));

        Assert.True(assembly is not null, string.Join("\n", errors));
    }

    [Fact]
    public async Task Retargeting_the_profile_emits_another_engines_client_from_the_same_schema()
    {
        var vm = ConnectedToSqlite();
        await vm.ConnectCommand.ExecuteAsync(null);

        vm.SelectedRelation = vm.Relations.First(r => r.Display == "Member");
        await vm.PendingWork;

        // Browsing SQLite but emitting Npgsql is the migration case the provider seam exists for.
        vm.SelectedCodegenProfile = ProviderRegistry.ById("postgresql").CodegenProfile;
        vm.GenerateCommand.Execute(null);

        Assert.Contains("NpgsqlConnection", vm.GeneratedCode);
        Assert.Contains("RETURNING", vm.GeneratedCode);
        Assert.DoesNotContain("SqliteConnection", vm.GeneratedCode);
    }

    [Fact]
    public async Task Generating_brings_the_output_tab_forward()
    {
        var vm = ConnectedToSqlite();
        await vm.ConnectCommand.ExecuteAsync(null);

        vm.SelectedRelation = vm.Relations.First(r => r.Display == "Member");
        await vm.PendingWork;

        // Picking a relation shows its columns...
        Assert.Equal(0, vm.SelectedTabIndex);

        vm.GenerateCommand.Execute(null);

        // ...and generating shows the result, rather than leaving it behind a tab.
        Assert.Equal(2, vm.SelectedTabIndex);
    }

    [Fact]
    public async Task Switching_provider_resets_the_browse_state()
    {
        var vm = ConnectedToSqlite();
        await vm.ConnectCommand.ExecuteAsync(null);

        vm.SelectedRelation = vm.Relations.First(r => r.Display == "Member");
        await vm.PendingWork;

        vm.SelectedProvider = ProviderRegistry.ById("sqlserver");

        // Relations and columns belonged to the old connection, so they must not linger.
        Assert.False(vm.IsConnected);
        Assert.Empty(vm.Relations);
        Assert.Empty(vm.Columns);
        Assert.Equal("1433", vm.Port);
        Assert.Equal("sqlserver", vm.SelectedCodegenProfile.Id);
    }
}
