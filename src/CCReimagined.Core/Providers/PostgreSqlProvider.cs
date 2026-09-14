using CCReimagined.Core.Codegen;
using CCReimagined.Core.Model;
using Npgsql;

namespace CCReimagined.Core.Providers;

public sealed class PostgreSqlProvider : IDatabaseProvider
{
    public string Id => "postgresql";
    public string DisplayName => "PostgreSQL";
    public ICodegenProfile CodegenProfile { get; } = new PostgreSqlCodegenProfile();

    public ProviderCapabilities Capabilities { get; } = new()
    {
        NeedsHost = true,
        // Peer/ident auth over a Unix socket is the Postgres analogue of integrated security.
        SupportsIntegratedAuth = true,
        SupportsDatabaseEnumeration = true,
        SupportsSchemas = true,
        DefaultPort = 5432,
    };

    public string BuildConnectionString(ConnectionSettings s)
    {
        if (s.UsesRaw)
            return s.RawConnectionString!;

        var b = new NpgsqlConnectionStringBuilder
        {
            Host = s.Host,
            Port = s.Port is > 0 ? s.Port.Value : 5432,
            Database = string.IsNullOrWhiteSpace(s.Database) ? "postgres" : s.Database,
            Timeout = s.ConnectTimeoutSeconds,
            ApplicationName = "CCReimagined",
        };

        if (s.AuthMode == AuthMode.UserPassword)
        {
            b.Username = s.UserName ?? "";
            b.Password = s.Password ?? "";
        }
        else if (!string.IsNullOrWhiteSpace(s.UserName))
        {
            // Peer auth still needs a role name to connect as.
            b.Username = s.UserName;
        }

        if (s.TrustServerCertificate)
            b.SslMode = SslMode.Prefer;

        return b.ConnectionString;
    }

    public string WithDatabase(string connectionString, string database) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    public async Task<ProbeResult> TestConnectionAsync(string cs, CancellationToken ct = default)
    {
        try
        {
            await using var cn = new NpgsqlConnection(cs);
            await cn.OpenAsync(ct);
            return ProbeResult.Ok($"PostgreSQL {cn.PostgreSqlVersion}");
        }
        catch (Exception ex)
        {
            return ProbeResult.Fail(ex.Message);
        }
    }

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(string cs, CancellationToken ct = default)
    {
        const string sql = """
            SELECT datname
            FROM pg_database
            WHERE datallowconn AND NOT datistemplate
            ORDER BY datname
            """;

        await using var cn = new NpgsqlConnection(cs);
        await cn.OpenAsync(ct);
        return await AdoHelpers.ReadStringsAsync(cn, sql, ct);
    }

    public async Task<IReadOnlyList<TableRef>> ListRelationsAsync(string cs, CancellationToken ct = default)
    {
        const string sql = """
            SELECT table_schema, table_name, table_type
            FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
              AND table_type IN ('BASE TABLE', 'VIEW')
            ORDER BY table_schema, table_name
            """;

        await using var cn = new NpgsqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var result = new List<TableRef>();
        while (await r.ReadAsync(ct))
        {
            var kind = r.GetString(2) == "VIEW" ? RelationKind.View : RelationKind.Table;
            result.Add(new TableRef(r.GetString(0), r.GetString(1), kind));
        }

        return result;
    }

    public async Task<TableSchema> GetTableSchemaAsync(string cs, TableRef table, CancellationToken ct = default)
    {
        // identity_generation covers GENERATED ... AS IDENTITY; the nextval() default test
        // catches the older serial/bigserial spelling, which is still everywhere.
        const string sql = """
            SELECT
                c.column_name,
                c.data_type,
                c.character_maximum_length,
                c.numeric_precision,
                c.numeric_scale,
                c.is_nullable,
                COALESCE(c.is_identity, 'NO')                                  AS is_identity,
                COALESCE(c.identity_generation, '')                            AS identity_generation,
                COALESCE(c.column_default, '')                                 AS column_default,
                COALESCE(c.is_generated, 'NEVER')                              AS is_generated,
                c.ordinal_position,
                CASE WHEN pk.column_name IS NULL THEN 0 ELSE 1 END             AS is_primary_key,
                COALESCE(c.udt_name, c.data_type)                              AS udt_name
            FROM information_schema.columns c
            LEFT JOIN (
                SELECT kcu.column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                    ON kcu.constraint_name = tc.constraint_name
                   AND kcu.table_schema = tc.table_schema
                WHERE tc.constraint_type = 'PRIMARY KEY'
                  AND tc.table_schema = @schema
                  AND tc.table_name = @table
            ) pk ON pk.column_name = c.column_name
            WHERE c.table_schema = @schema AND c.table_name = @table
            ORDER BY c.ordinal_position
            """;

        var schema = string.IsNullOrEmpty(table.Schema) ? "public" : table.Schema;

        await using var cn = new NpgsqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql, ("@schema", schema), ("@table", table.Name));
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var columns = new List<ColumnInfo>();
        while (await r.ReadAsync(ct))
        {
            var dataType = r.GetString(1);
            var udt = r.GetString(12);
            var columnDefault = r.GetString(8);
            var isIdentity = AdoHelpers.TruthyFlag(r, 6);
            var isSerial = columnDefault.Contains("nextval(", StringComparison.OrdinalIgnoreCase);

            columns.Add(new ColumnInfo
            {
                Name = r.GetString(0),
                // data_type says "character varying"; udt_name says "varchar". The udt name is
                // the one that matches the NpgsqlDbType members, so prefer it for arrays and enums.
                NativeTypeName = dataType == "ARRAY" || dataType == "USER-DEFINED" ? udt : dataType,
                MaxLength = AdoHelpers.NullableInt(r, 2),
                Precision = AdoHelpers.NullableInt(r, 3),
                Scale = AdoHelpers.NullableInt(r, 4),
                IsNullable = AdoHelpers.TruthyFlag(r, 5),
                IsAutoGenerated = isIdentity || isSerial,
                IsComputed = r.GetString(9) != "NEVER",
                Ordinal = AdoHelpers.NullableInt(r, 10) ?? 0,
                IsPrimaryKey = r.GetInt32(11) == 1,
                DefaultExpression = columnDefault is { Length: > 0 } ? columnDefault : null,
                ClrType = MapType(dataType, udt),
            });
        }

        return new TableSchema
        {
            Table = table,
            Columns = columns,
            KeyColumn = TableSchema.ResolveKeyColumn(columns, table.Name),
            Mutability = table.Kind == RelationKind.View
                ? await ViewMutabilityAsync(cs, schema, table.Name, ct)
                : ViewMutability.NotApplicable,
        };
    }

    /// <summary>
    /// PostgreSQL answers this properly: a simple view is auto-updatable, and one with an
    /// INSTEAD OF trigger is updatable through the trigger.
    /// </summary>
    private static async Task<ViewMutability> ViewMutabilityAsync(
        string cs, string schema, string view, CancellationToken ct)
    {
        const string sql = """
            SELECT is_updatable, is_trigger_updatable
            FROM information_schema.views
            WHERE table_schema = @schema AND table_name = @view
            """;

        // A second connection, because the column reader still owns the first one and these
        // clients allow a single command in flight at a time.
        await using var cn = new NpgsqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql, ("@schema", schema), ("@view", view));
        await using var r = await cmd.ExecuteReaderAsync(ct);

        if (!await r.ReadAsync(ct))
            return ViewMutability.Unknown;

        return AdoHelpers.TruthyFlag(r, 0) || AdoHelpers.TruthyFlag(r, 1)
            ? ViewMutability.Updatable
            : ViewMutability.ReadOnly;
    }


    internal static ClrTypeKind MapType(string dataType, string udtName)
    {
        var mapped = dataType.ToLowerInvariant() switch
        {
            "boolean" => ClrTypeKind.Boolean,
            "smallint" => ClrTypeKind.Int16,
            "integer" => ClrTypeKind.Int32,
            "bigint" => ClrTypeKind.Int64,
            "real" => ClrTypeKind.Single,
            "double precision" => ClrTypeKind.Double,
            "numeric" or "decimal" or "money" => ClrTypeKind.Decimal,
            "character varying" or "character" or "text" or "citext"
                or "name" or "json" or "jsonb" or "xml" => ClrTypeKind.String,
            "uuid" => ClrTypeKind.Guid,
            "date" => ClrTypeKind.DateOnly,
            "time" or "time without time zone" => ClrTypeKind.TimeOnly,
            "timestamp" or "timestamp without time zone" => ClrTypeKind.DateTime,
            "timestamp with time zone" or "time with time zone" => ClrTypeKind.DateTimeOffset,
            "interval" => ClrTypeKind.TimeSpan,
            "bytea" => ClrTypeKind.ByteArray,
            // Enums arrive as USER-DEFINED; Npgsql reads them as strings unless mapped.
            "user-defined" => ClrTypeKind.String,
            _ => ClrTypeKind.Unknown,
        };

        if (mapped != ClrTypeKind.Unknown)
            return mapped;

        // Fall back to the underlying type name, which is what arrays and domains report.
        return udtName.ToLowerInvariant() switch
        {
            "varchar" or "bpchar" or "text" or "citext" or "name" => ClrTypeKind.String,
            "bool" => ClrTypeKind.Boolean,
            "int2" => ClrTypeKind.Int16,
            "int4" => ClrTypeKind.Int32,
            "int8" => ClrTypeKind.Int64,
            "float4" => ClrTypeKind.Single,
            "float8" => ClrTypeKind.Double,
            "numeric" => ClrTypeKind.Decimal,
            "uuid" => ClrTypeKind.Guid,
            "bytea" => ClrTypeKind.ByteArray,
            _ => ClrTypeKind.Unknown,
        };
    }
}
