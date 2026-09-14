using CCReimagined.Core.Codegen;
using CCReimagined.Core.Model;
using Oracle.ManagedDataAccess.Client;

namespace CCReimagined.Core.Providers;

public sealed class OracleProvider : IDatabaseProvider
{
    public string Id => "oracle";
    public string DisplayName => "Oracle Database";
    public ICodegenProfile CodegenProfile { get; } = new OracleCodegenProfile();

    public ProviderCapabilities Capabilities { get; } = new()
    {
        NeedsHost = true,
        // Operating-system authentication exists but needs the client configured for it, which
        // is not something the tool can arrange, so credentials are always asked for.
        SupportsIntegratedAuth = false,
        // Oracle's unit of separation is the schema, not the database. The picker lists schemas.
        SupportsDatabaseEnumeration = true,
        SupportsSchemas = true,
        DefaultPort = 1521,
    };

    public string BuildConnectionString(ConnectionSettings s)
    {
        if (s.UsesRaw)
            return s.RawConnectionString!;

        var port = s.Port is > 0 ? s.Port.Value : 1521;

        // Database here is the service name — FREEPDB1 on the Free edition, ORCLPDB1 on many
        // others — not a schema. The schema is chosen afterwards from the picker.
        var service = string.IsNullOrWhiteSpace(s.Database) ? "FREEPDB1" : s.Database;

        var b = new OracleConnectionStringBuilder
        {
            DataSource = $"{s.Host}:{port}/{service}",
            UserID = s.UserName ?? "",
            Password = s.Password ?? "",
            ConnectionTimeout = s.ConnectTimeoutSeconds,
        };

        return b.ConnectionString;
    }

    /// <summary>
    /// Repoints at a different service. Switching schema is a separate matter — the relation
    /// list is schema-qualified, so nothing needs to change on the connection for that.
    /// </summary>
    public string WithDatabase(string connectionString, string database)
    {
        var b = new OracleConnectionStringBuilder(connectionString);
        var source = b.DataSource ?? "";
        var slash = source.LastIndexOf('/');

        b.DataSource = slash >= 0 ? source[..(slash + 1)] + database : $"{source}/{database}";
        return b.ConnectionString;
    }

    public async Task<ProbeResult> TestConnectionAsync(string cs, CancellationToken ct = default)
    {
        try
        {
            await using var cn = new OracleConnection(cs);
            await cn.OpenAsync(ct);

            await using var cmd = AdoHelpers.Command(cn,
                "SELECT banner FROM v$version WHERE ROWNUM = 1");

            var banner = await cmd.ExecuteScalarAsync(ct) as string;
            return ProbeResult.Ok(banner ?? $"Oracle {cn.ServerVersion}");
        }
        catch (Exception ex)
        {
            return ProbeResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Lists schemas rather than databases. Oracle's "database" is the whole instance; what a
    /// developer picks between is a schema, so that is what the picker offers.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListDatabasesAsync(string cs, CancellationToken ct = default)
    {
        // Only schemas the connected user can actually see objects in, and not the dozens of
        // Oracle-supplied ones, which would bury the interesting entries.
        const string sql = """
            SELECT DISTINCT owner
            FROM all_objects
            WHERE object_type IN ('TABLE', 'VIEW')
              AND owner NOT IN (
                  'SYS', 'SYSTEM', 'XDB', 'MDSYS', 'CTXSYS', 'OUTLN', 'DBSNMP', 'APPQOSSYS',
                  'ORDSYS', 'ORDDATA', 'OLAPSYS', 'WMSYS', 'LBACSYS', 'DVSYS', 'AUDSYS',
                  'GSMADMIN_INTERNAL', 'OJVMSYS', 'DBSFWUSER', 'REMOTE_SCHEDULER_AGENT')
            ORDER BY owner
            """;

        await using var cn = new OracleConnection(cs);
        await cn.OpenAsync(ct);
        return await AdoHelpers.ReadStringsAsync(cn, sql, ct);
    }

    public async Task<IReadOnlyList<TableRef>> ListRelationsAsync(string cs, CancellationToken ct = default)
    {
        const string sql = """
            SELECT owner, object_name, object_type
            FROM all_objects
            WHERE object_type IN ('TABLE', 'VIEW')
              AND owner NOT IN (
                  'SYS', 'SYSTEM', 'XDB', 'MDSYS', 'CTXSYS', 'OUTLN', 'DBSNMP', 'APPQOSSYS',
                  'ORDSYS', 'ORDDATA', 'OLAPSYS', 'WMSYS', 'LBACSYS', 'DVSYS', 'AUDSYS',
                  'GSMADMIN_INTERNAL', 'OJVMSYS', 'DBSFWUSER', 'REMOTE_SCHEDULER_AGENT')
              AND object_name NOT LIKE 'BIN$%'
            ORDER BY owner, object_name
            """;

        await using var cn = new OracleConnection(cs);
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
        // all_tab_cols rather than all_tab_columns, because it reports virtual columns.
        // Identity columns arrived in 12c and live in their own view.
        const string sql = """
            SELECT
                c.column_name,
                c.data_type,
                c.data_length,
                c.data_precision,
                c.data_scale,
                c.nullable,
                c.column_id,
                c.char_length,
                NVL(i.generation_type, 'NONE')                        AS identity_kind,
                CASE WHEN pk.column_name IS NULL THEN 0 ELSE 1 END    AS is_primary_key,
                NVL(c.virtual_column, 'NO')                           AS is_virtual
            FROM all_tab_cols c
            LEFT JOIN all_tab_identity_cols i
                ON i.owner = c.owner AND i.table_name = c.table_name AND i.column_name = c.column_name
            LEFT JOIN (
                SELECT cc.column_name
                FROM all_constraints ac
                JOIN all_cons_columns cc
                    ON cc.owner = ac.owner AND cc.constraint_name = ac.constraint_name
                WHERE ac.constraint_type = 'P'
                  AND ac.owner = :owner
                  AND ac.table_name = :relation
            ) pk ON pk.column_name = c.column_name
            WHERE c.owner = :owner2
              AND c.table_name = :relation2
              AND c.hidden_column = 'NO'
            ORDER BY c.column_id
            """;

        var owner = string.IsNullOrEmpty(table.Schema) ? await CurrentSchemaAsync(cs, ct) : table.Schema!;

        // An identifier written without quotes is stored upper-cased, so a caller passing
        // "member" is asking about MEMBER. One created with quotes keeps its case. Resolving
        // the stored spelling first handles both without guessing.
        var relation = await ResolveRelationNameAsync(cs, owner, table.Name, ct);

        await using var cn = new OracleConnection(cs);
        await cn.OpenAsync(ct);

        var columns = new List<ColumnInfo>();

        await using (var cmd = AdoHelpers.Command(cn, sql,
            (":owner", owner), (":relation", relation),
            (":owner2", owner), (":relation2", relation)))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var native = Text(r, 1);
                var precision = AdoHelpers.NullableInt(r, 3);
                var scale = AdoHelpers.NullableInt(r, 4);
                var charLength = AdoHelpers.NullableInt(r, 7);
                var byteLength = AdoHelpers.NullableInt(r, 2);

                columns.Add(new ColumnInfo
                {
                    Name = Text(r, 0),
                    NativeTypeName = native,
                    // Character types report both; the character count is the useful one.
                    MaxLength = charLength is > 0 ? charLength : byteLength,
                    Precision = precision,
                    Scale = scale,
                    IsNullable = Text(r, 5) == "Y",
                    IsAutoGenerated = Text(r, 8) is not ("NONE" or ""),
                    IsComputed = Text(r, 10) == "YES",
                    Ordinal = AdoHelpers.NullableInt(r, 6) ?? 0,
                    IsPrimaryKey = AdoHelpers.NullableInt(r, 9) == 1,
                    ClrType = MapType(native, precision, scale),
                });
            }
        }

        return new TableSchema
        {
            Table = table with { Schema = owner, Name = relation },
            Columns = columns,
            KeyColumn = TableSchema.ResolveKeyColumn(columns, table.Name),
            Mutability = table.Kind == RelationKind.View
                ? await ViewMutabilityAsync(cs, owner, relation, ct)
                : ViewMutability.NotApplicable,
        };
    }

    /// <summary>
    /// Reads a string column that may be NULL. Oracle stores the empty string as NULL, which
    /// makes NVL(x, '') a no-op and leaves GetString to throw — so every text read goes
    /// through here rather than trusting the query to have defaulted it.
    /// </summary>
    private static string Text(System.Data.Common.DbDataReader r, int ordinal) =>
        r.IsDBNull(ordinal) ? "" : r.GetString(ordinal);

    /// <summary>
    /// Returns the name as the catalog spells it. An exact match wins, so a quoted
    /// mixed-case name is never mistaken for a differently-cased sibling; otherwise a
    /// case-insensitive match stands in, which is the unquoted case.
    /// </summary>
    private static async Task<string> ResolveRelationNameAsync(
        string cs, string owner, string name, CancellationToken ct)
    {
        const string sql = """
            SELECT object_name
            FROM all_objects
            WHERE owner = :owner
              AND object_type IN ('TABLE', 'VIEW')
              AND (object_name = :exact OR UPPER(object_name) = UPPER(:loose))
            ORDER BY CASE WHEN object_name = :exact2 THEN 0 ELSE 1 END
            FETCH FIRST 1 ROWS ONLY
            """;

        await using var cn = new OracleConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql,
            (":owner", owner), (":exact", name), (":loose", name), (":exact2", name));

        return await cmd.ExecuteScalarAsync(ct) as string ?? name;
    }

    private static async Task<string> CurrentSchemaAsync(string cs, CancellationToken ct)
    {
        await using var cn = new OracleConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn,
            "SELECT SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') FROM dual");

        return await cmd.ExecuteScalarAsync(ct) as string ?? "";
    }

    /// <summary>
    /// Oracle answers this properly, unlike SQL Server: all_updatable_columns reports per column
    /// whether the view accepts writes, INSTEAD OF triggers included.
    /// </summary>
    private static async Task<ViewMutability> ViewMutabilityAsync(
        string cs, string owner, string view, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM all_updatable_columns
            WHERE owner = :owner AND table_name = :relation AND updatable = 'YES'
            """;

        await using var cn = new OracleConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql, (":owner", owner), (":relation", view));

        var updatable = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
        return updatable > 0 ? ViewMutability.Updatable : ViewMutability.ReadOnly;
    }

    /// <summary>
    /// Oracle has one numeric type. NUMBER(p,0) is an integer of some width, NUMBER(1) is the
    /// conventional boolean, and anything with a scale is a decimal.
    /// </summary>
    internal static ClrTypeKind MapType(string native, int? precision, int? scale)
    {
        var t = native.ToUpperInvariant();

        if (t.StartsWith("TIMESTAMP", StringComparison.Ordinal))
            return t.Contains("TIME ZONE", StringComparison.Ordinal)
                ? ClrTypeKind.DateTimeOffset
                : ClrTypeKind.DateTime;

        if (t.StartsWith("INTERVAL DAY", StringComparison.Ordinal))
            return ClrTypeKind.TimeSpan;

        return t switch
        {
            "NUMBER" or "NUMERIC" or "DECIMAL" or "DEC" => MapNumber(precision, scale),
            "INTEGER" or "INT" or "SMALLINT" => ClrTypeKind.Int32,
            "FLOAT" or "BINARY_DOUBLE" or "DOUBLE PRECISION" or "REAL" => ClrTypeKind.Double,
            "BINARY_FLOAT" => ClrTypeKind.Single,
            "VARCHAR2" or "NVARCHAR2" or "VARCHAR" or "CHAR" or "NCHAR"
                or "CLOB" or "NCLOB" or "LONG" or "ROWID" or "UROWID" or "JSON" => ClrTypeKind.String,
            "DATE" => ClrTypeKind.DateTime,
            "BLOB" or "RAW" or "LONG RAW" or "BFILE" => ClrTypeKind.ByteArray,
            "BOOLEAN" => ClrTypeKind.Boolean,
            _ => ClrTypeKind.Unknown,
        };
    }

    private static ClrTypeKind MapNumber(int? precision, int? scale)
    {
        // No scale recorded at all means an unconstrained NUMBER, which can hold more than any
        // fixed CLR type; decimal is the safest landing place.
        if (scale is null or > 0)
            return ClrTypeKind.Decimal;

        return precision switch
        {
            null => ClrTypeKind.Decimal,
            // NUMBER(1) is how a boolean is spelled everywhere before 23ai.
            1 => ClrTypeKind.Boolean,
            <= 4 => ClrTypeKind.Int16,
            <= 9 => ClrTypeKind.Int32,
            <= 18 => ClrTypeKind.Int64,
            _ => ClrTypeKind.Decimal,
        };
    }
}
