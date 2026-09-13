using System.Data.Common;
using System.Reflection;
using CCReimagined.Core.Codegen;
using CCReimagined.Core.Model;
using MySqlConnector;
using Npgsql;

namespace CCReimagined.Core.Tests;

/// <summary>
/// Compiles a generated class and drives it through real CRUD against a live server.
///
/// The SQLite fixture already proves the generator end to end, but every engine has its own
/// emitted shape — a different ADO.NET client, a different parameter DbType enum, and above
/// all a different way of handing back a generated key (PostgreSQL returns it from the INSERT,
/// MySQL fetches it with LAST_INSERT_ID). None of that had ever been executed. These tests run
/// it, and skip when the server is not up so a bare checkout still goes green.
/// </summary>
[Collection("live-servers")]
public sealed class LiveCrudTests
{
    private static readonly Type[] ProviderTypes = [typeof(NpgsqlConnection), typeof(MySqlConnection)];

    [SkippableTheory]
    [MemberData(nameof(LiveServers.All), MemberType = typeof(LiveServers))]
    public async Task Generated_class_round_trips_a_row(LiveTarget target)
    {
        var connectionString = await LiveServers.ReachableConnectionStringAsync(target);
        Skip.If(connectionString is null, LiveServers.SkipReason(target));

        var provider = target.Provider;
        var table = $"ccr_live_{Guid.NewGuid():N}"[..24];

        await ExecuteAsync(provider, connectionString!, target.CreateTableSql(table));

        try
        {
            var schema = await provider.GetTableSchemaAsync(connectionString!, new TableRef(null, table));

            // The key has to be recognised as database-generated, or Add cannot read it back.
            Assert.True(schema.HasGeneratedKey, $"{target.Label}: '{schema.KeyColumn?.Name}' was not detected as generated.");

            var artifact = new CSharpDataClassGenerator().Generate(new GenerationRequest
            {
                Schema = schema,
                Profile = provider.CodegenProfile,
                Namespace = "Live.Data",
                ClassName = "LiveRow",
                ListParameterColumns = ["name"],
                SourceDescription = $"{target.Label} live test",
            });

            var (assembly, errors) = GeneratedCodeCompiler.Compile(
                artifact.Content,
                $"LiveRow_{target.Label}_{Guid.NewGuid():N}",
                ProviderTypes);

            Assert.True(assembly is not null,
                $"{target.Label}: generated code did not compile:\n{string.Join("\n", errors)}\n\n{artifact.Content}");

            var type = assembly!.GetType("Live.Data.LiveRow")!;

            // ---- insert, and read the generated key back -------------------------------
            var row = Activator.CreateInstance(type, connectionString)!;
            Set(row, "name", "Watson");
            Set(row, "balance", 125.50m);
            Set(row, "is_active", true);
            Set(row, "joined", new DateOnly(2026, 1, 15));
            Set(row, "payload", new byte[] { 1, 2, 3 });
            // nickname deliberately left null.

            await Invoke<object?>(row, "AddAsync");

            var id = Convert.ToInt64(Get(row, "id"));
            Assert.True(id > 0, $"{target.Label}: Add did not bring the generated key back (got {id}).");

            // ---- read it into a fresh instance -----------------------------------------
            var reloaded = Activator.CreateInstance(type, connectionString)!;
            Assert.True(await Invoke<bool>(reloaded, "ReadAsync", Convert.ChangeType(id, KeyClrType(type))));

            Assert.Equal("Watson", Get(reloaded, "name"));
            Assert.Equal(125.50m, Get(reloaded, "balance"));
            Assert.Equal(true, Get(reloaded, "is_active"));
            Assert.Equal(new DateOnly(2026, 1, 15), Get(reloaded, "joined"));
            Assert.Equal(new byte[] { 1, 2, 3 }, Get(reloaded, "payload"));
            // A nullable column has to come back as null, not as an empty sentinel.
            Assert.Null(Get(reloaded, "nickname"));

            // ---- update ------------------------------------------------------------------
            Set(reloaded, "balance", 999.99m);
            Set(reloaded, "nickname", "Leigh");
            Assert.Equal(1, await Invoke<int>(reloaded, "UpdateAsync"));

            var afterUpdate = Activator.CreateInstance(type, connectionString)!;
            await Invoke<bool>(afterUpdate, "ReadAsync", Convert.ChangeType(id, KeyClrType(type)));
            Assert.Equal(999.99m, Get(afterUpdate, "balance"));
            Assert.Equal("Leigh", Get(afterUpdate, "nickname"));

            // ---- exists, and the list getters -------------------------------------------
            Assert.True(await Invoke<bool>(afterUpdate, "RecExistsAsync", Convert.ChangeType(id, KeyClrType(type))));

            var byName = await InvokeStaticList(type, "GetListBynameAsync", connectionString, "Watson");
            Assert.Single(byName);

            var none = await InvokeStaticList(type, "GetListBynameAsync", connectionString, "NoSuchName");
            Assert.Empty(none);

            Assert.Single(await InvokeStaticList(type, "GetAllAsync", connectionString));

            // ---- delete -------------------------------------------------------------------
            Assert.Equal(1, await Invoke<int>(afterUpdate, "DeleteAsync"));
            Assert.False(await Invoke<bool>(afterUpdate, "RecExistsAsync", Convert.ChangeType(id, KeyClrType(type))));
            Assert.False(await Invoke<bool>(afterUpdate, "ReadAsync", Convert.ChangeType(id, KeyClrType(type))));
        }
        finally
        {
            await ExecuteAsync(provider, connectionString!, $"DROP TABLE IF EXISTS {table}");
        }
    }

    [SkippableTheory]
    [MemberData(nameof(LiveServers.All), MemberType = typeof(LiveServers))]
    public async Task Generated_class_writes_and_reads_nulls(LiveTarget target)
    {
        var connectionString = await LiveServers.ReachableConnectionStringAsync(target);
        Skip.If(connectionString is null, LiveServers.SkipReason(target));

        var provider = target.Provider;
        var table = $"ccr_null_{Guid.NewGuid():N}"[..24];

        await ExecuteAsync(provider, connectionString!, target.CreateTableSql(table));

        try
        {
            var schema = await provider.GetTableSchemaAsync(connectionString!, new TableRef(null, table));

            var artifact = new CSharpDataClassGenerator().Generate(new GenerationRequest
            {
                Schema = schema,
                Profile = provider.CodegenProfile,
                Namespace = "Live.Data",
                ClassName = "NullRow",
            });

            var (assembly, errors) = GeneratedCodeCompiler.Compile(
                artifact.Content,
                $"NullRow_{target.Label}_{Guid.NewGuid():N}",
                ProviderTypes);

            Assert.True(assembly is not null, string.Join("\n", errors));

            var type = assembly!.GetType("Live.Data.NullRow")!;
            var row = Activator.CreateInstance(type, connectionString)!;

            Set(row, "name", "OnlyRequired");
            Set(row, "balance", 0m);
            Set(row, "is_active", false);
            // nickname, joined and payload all left null.

            await Invoke<object?>(row, "AddAsync");
            var id = Convert.ToInt64(Get(row, "id"));

            var reloaded = Activator.CreateInstance(type, connectionString)!;
            Assert.True(await Invoke<bool>(reloaded, "ReadAsync", Convert.ChangeType(id, KeyClrType(type))));

            Assert.Null(Get(reloaded, "nickname"));
            Assert.Null(Get(reloaded, "joined"));
            Assert.Null(Get(reloaded, "payload"));
            Assert.Equal("OnlyRequired", Get(reloaded, "name"));
            Assert.Equal(false, Get(reloaded, "is_active"));
        }
        finally
        {
            await ExecuteAsync(provider, connectionString!, $"DROP TABLE IF EXISTS {table}");
        }
    }

    // ------------------------------------------------------------------ plumbing

    private static async Task ExecuteAsync(
        CCReimagined.Core.Providers.IDatabaseProvider provider,
        string connectionString,
        string sql)
    {
        DbConnection connection = provider.Id switch
        {
            "postgresql" => new NpgsqlConnection(connectionString),
            "mysql" => new MySqlConnection(connectionString),
            _ => throw new NotSupportedException($"No live harness for provider '{provider.Id}'."),
        };

        await using (connection)
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>The CLR type of the key, so a reflected id is widened or narrowed to match.</summary>
    private static Type KeyClrType(Type generated) =>
        generated.GetMethod("ReadAsync")!.GetParameters()[0].ParameterType;

    private static void Set(object instance, string property, object? value) =>
        instance.GetType().GetProperty(property)!.SetValue(instance, value);

    private static object? Get(object instance, string property) =>
        instance.GetType().GetProperty(property)!.GetValue(instance);

    private static async Task<T> Invoke<T>(object instance, string method, params object?[] args)
    {
        var info = instance.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == method && m.GetParameters().Length == args.Length + 1);

        var task = info.Invoke(instance, args.Append((object?)CancellationToken.None).ToArray())!;
        await (Task)task;

        var result = task.GetType().GetProperty("Result");
        return result is null ? default! : (T)result.GetValue(task)!;
    }

    private static async Task<System.Collections.IList> InvokeStaticList(
        Type type,
        string method,
        params object?[] args)
    {
        var info = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == method && m.GetParameters().Length == args.Length + 1);

        var task = (Task)info.Invoke(null, args.Append((object?)CancellationToken.None).ToArray())!;
        await task;

        return (System.Collections.IList)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }
}
