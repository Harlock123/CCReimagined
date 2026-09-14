using CCReimagined.Core.Codegen;
using CCReimagined.Core.Model;
using MySqlConnector;

namespace CCReimagined.Core.Providers;

public sealed class MySqlProvider : IDatabaseProvider
{
    public string Id => "mysql";
    public string DisplayName => "MySQL / MariaDB";
    public ICodegenProfile CodegenProfile { get; } = new MySqlCodegenProfile();

    public ProviderCapabilities Capabilities { get; } = new()
    {
        NeedsHost = true,
        SupportsIntegratedAuth = false,
        SupportsDatabaseEnumeration = true,
        // MySQL's "schema" and "database" are the same thing, so relations carry no schema.
        SupportsSchemas = false,
        DefaultPort = 3306,
    };

    public string BuildConnectionString(ConnectionSettings s)
    {
        if (s.UsesRaw)
            return s.RawConnectionString!;

        var b = new MySqlConnectionStringBuilder
        {
            Server = s.Host,
            Port = (uint)(s.Port is > 0 ? s.Port.Value : 3306),
            UserID = s.UserName ?? "",
            Password = s.Password ?? "",
            ConnectionTimeout = (uint)s.ConnectTimeoutSeconds,
            ApplicationName = "CCReimagined",
        };

        if (!string.IsNullOrWhiteSpace(s.Database))
            b.Database = s.Database;

        if (s.TrustServerCertificate)
            b.SslMode = MySqlSslMode.Preferred;

        return b.ConnectionString;
    }

    public string WithDatabase(string connectionString, string database) =>
        new MySqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    public async Task<ProbeResult> TestConnectionAsync(string cs, CancellationToken ct = default)
    {
        try
        {
            await using var cn = new MySqlConnection(cs);
            await cn.OpenAsync(ct);
            return ProbeResult.Ok($"MySQL {cn.ServerVersion}");
        }
        catch (Exception ex)
        {
            return ProbeResult.Fail(ex.Message);
        }
    }

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(string cs, CancellationToken ct = default)
    {
        const string sql = """
            SELECT schema_name
            FROM information_schema.schemata
            WHERE schema_name NOT IN ('information_schema', 'performance_schema', 'mysql', 'sys')
            ORDER BY schema_name
            """;

        await using var cn = new MySqlConnection(cs);
        await cn.OpenAsync(ct);
        return await AdoHelpers.ReadStringsAsync(cn, sql, ct);
    }

    public async Task<IReadOnlyList<TableRef>> ListRelationsAsync(string cs, CancellationToken ct = default)
    {
        // Scoped to the connected database, since that is what MySQL calls a schema.
        const string sql = """
            SELECT table_name, table_type
            FROM information_schema.tables
            WHERE table_schema = DATABASE()
              AND table_type IN ('BASE TABLE', 'VIEW')
            ORDER BY table_name
            """;

        await using var cn = new MySqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var result = new List<TableRef>();
        while (await r.ReadAsync(ct))
        {
            var kind = r.GetString(1) == "VIEW" ? RelationKind.View : RelationKind.Table;
            result.Add(new TableRef(null, r.GetString(0), kind));
        }

        return result;
    }

    public async Task<TableSchema> GetTableSchemaAsync(string cs, TableRef table, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                column_name,
                data_type,
                character_maximum_length,
                numeric_precision,
                numeric_scale,
                is_nullable,
                COALESCE(extra, '')        AS extra,
                COALESCE(column_key, '')   AS column_key,
                ordinal_position,
                COALESCE(column_default, '') AS column_default,
                column_type
            FROM information_schema.columns
            WHERE table_schema = COALESCE(@schema, DATABASE())
              AND table_name = @table
            ORDER BY ordinal_position
            """;

        await using var cn = new MySqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql,
            ("@schema", string.IsNullOrEmpty(table.Schema) ? null : table.Schema),
            ("@table", table.Name));
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var columns = new List<ColumnInfo>();
        while (await r.ReadAsync(ct))
        {
            var dataType = r.GetString(1);
            var extra = r.GetString(6).ToLowerInvariant();
            // column_type carries the display width, which is how tinyint(1) — MySQL's
            // stand-in for a boolean — can be told from a genuine one-byte integer.
            var columnType = r.GetString(10).ToLowerInvariant();
            var columnDefault = r.GetString(9);

            columns.Add(new ColumnInfo
            {
                Name = r.GetString(0),
                NativeTypeName = dataType,
                MaxLength = AdoHelpers.NullableInt(r, 2),
                Precision = AdoHelpers.NullableInt(r, 3),
                Scale = AdoHelpers.NullableInt(r, 4),
                IsNullable = AdoHelpers.TruthyFlag(r, 5),
                IsAutoGenerated = extra.Contains("auto_increment"),
                // MySQL spells a real generated column "VIRTUAL GENERATED" or "STORED
                // GENERATED". It also puts "DEFAULT_GENERATED" on any column with a default
                // expression — DEFAULT CURRENT_TIMESTAMP above all — which is writable like any
                // other. Matching on "generated" alone silently drops every created_at column
                // from INSERT and UPDATE.
                IsComputed = extra.Contains("virtual generated") || extra.Contains("stored generated"),
                IsPrimaryKey = r.GetString(7) == "PRI",
                Ordinal = AdoHelpers.NullableInt(r, 8) ?? 0,
                DefaultExpression = columnDefault is { Length: > 0 } ? columnDefault : null,
                ClrType = MapType(dataType, columnType),
            });
        }

        return new TableSchema
        {
            Table = table,
            Columns = columns,
            KeyColumn = TableSchema.ResolveKeyColumn(columns, table.Name),
            Mutability = table.Kind == RelationKind.View
                ? await ViewMutabilityAsync(cs, table, ct)
                : ViewMutability.NotApplicable,
        };
    }

    /// <summary>MySQL and MariaDB both report this accurately.</summary>
    private static async Task<ViewMutability> ViewMutabilityAsync(
        string cs, TableRef table, CancellationToken ct)
    {
        const string sql = """
            SELECT is_updatable
            FROM information_schema.views
            WHERE table_schema = COALESCE(@schema, DATABASE()) AND table_name = @view
            """;

        // A second connection: the column reader still owns the first.
        await using var cn = new MySqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql,
            ("@schema", string.IsNullOrEmpty(table.Schema) ? null : table.Schema),
            ("@view", table.Name));
        await using var r = await cmd.ExecuteReaderAsync(ct);

        if (!await r.ReadAsync(ct))
            return ViewMutability.Unknown;

        return AdoHelpers.TruthyFlag(r, 0) ? ViewMutability.Updatable : ViewMutability.ReadOnly;
    }


    internal static ClrTypeKind MapType(string dataType, string columnType)
    {
        var unsigned = columnType.Contains("unsigned", StringComparison.OrdinalIgnoreCase);

        return dataType.ToLowerInvariant() switch
        {
            "bit" => columnType.StartsWith("bit(1)", StringComparison.OrdinalIgnoreCase)
                ? ClrTypeKind.Boolean
                : ClrTypeKind.ByteArray,
            "bool" or "boolean" => ClrTypeKind.Boolean,
            "tinyint" => columnType.StartsWith("tinyint(1)", StringComparison.OrdinalIgnoreCase)
                ? ClrTypeKind.Boolean
                : unsigned ? ClrTypeKind.Byte : ClrTypeKind.Int16,
            "smallint" => unsigned ? ClrTypeKind.Int32 : ClrTypeKind.Int16,
            "mediumint" => ClrTypeKind.Int32,
            "int" or "integer" => unsigned ? ClrTypeKind.Int64 : ClrTypeKind.Int32,
            "bigint" => ClrTypeKind.Int64,
            "float" => ClrTypeKind.Single,
            "double" => ClrTypeKind.Double,
            "decimal" or "numeric" => ClrTypeKind.Decimal,
            "char" or "varchar" or "tinytext" or "text" or "mediumtext"
                or "longtext" or "json" or "enum" or "set" => ClrTypeKind.String,
            "date" => ClrTypeKind.DateOnly,
            "time" => ClrTypeKind.TimeOnly,
            "datetime" or "timestamp" => ClrTypeKind.DateTime,
            "year" => ClrTypeKind.Int16,
            "binary" or "varbinary" or "tinyblob" or "blob"
                or "mediumblob" or "longblob" => ClrTypeKind.ByteArray,
            _ => ClrTypeKind.Unknown,
        };
    }
}
