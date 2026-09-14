using System.Data.Common;
using CCReimagined.Core.Codegen;
using CCReimagined.Core.Model;
using Microsoft.Data.SqlClient;

namespace CCReimagined.Core.Providers;

public sealed class SqlServerProvider : IDatabaseProvider
{
    public string Id => "sqlserver";
    public string DisplayName => "Microsoft SQL Server";
    public ICodegenProfile CodegenProfile { get; } = new SqlServerCodegenProfile();

    public ProviderCapabilities Capabilities { get; } = new()
    {
        NeedsHost = true,
        SupportsIntegratedAuth = true,
        SupportsDatabaseEnumeration = true,
        SupportsSchemas = true,
        DefaultPort = 1433,
    };

    public string BuildConnectionString(ConnectionSettings s)
    {
        if (s.UsesRaw)
            return s.RawConnectionString!;

        var b = new SqlConnectionStringBuilder
        {
            DataSource = s.Port is > 0 ? $"{s.Host},{s.Port}" : s.Host,
            InitialCatalog = string.IsNullOrWhiteSpace(s.Database) ? "master" : s.Database,
            TrustServerCertificate = s.TrustServerCertificate,
            Encrypt = s.Encrypt,
            ConnectTimeout = s.ConnectTimeoutSeconds,
            ApplicationName = "CCReimagined",
        };

        if (s.AuthMode == AuthMode.Integrated)
        {
            b.IntegratedSecurity = true;
        }
        else
        {
            b.UserID = s.UserName ?? "";
            b.Password = s.Password ?? "";
        }

        return b.ConnectionString;
    }

    public string WithDatabase(string connectionString, string database) =>
        new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database }.ConnectionString;

    public async Task<ProbeResult> TestConnectionAsync(string cs, CancellationToken ct = default)
    {
        try
        {
            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync(ct);
            return ProbeResult.Ok($"SQL Server {cn.ServerVersion}");
        }
        catch (Exception ex)
        {
            return ProbeResult.Fail(ex.Message);
        }
    }

    public async Task<IReadOnlyList<string>> ListDatabasesAsync(string cs, CancellationToken ct = default)
    {
        const string sql = """
            SELECT name
            FROM sys.databases
            WHERE state = 0 AND HAS_DBACCESS(name) = 1
            ORDER BY name
            """;

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        return await AdoHelpers.ReadStringsAsync(cn, sql, ct);
    }

    public async Task<IReadOnlyList<TableRef>> ListRelationsAsync(string cs, CancellationToken ct = default)
    {
        // sys.objects covers tables and views in one pass, which is what the picker wants.
        const string sql = """
            SELECT s.name AS [schema], o.name AS [name], o.type AS [type]
            FROM sys.objects o
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('U', 'V') AND o.is_ms_shipped = 0
            ORDER BY s.name, o.name
            """;

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var result = new List<TableRef>();
        while (await r.ReadAsync(ct))
        {
            var kind = r.GetString(2).Trim() == "V" ? RelationKind.View : RelationKind.Table;
            result.Add(new TableRef(r.GetString(0), r.GetString(1), kind));
        }

        return result;
    }

    public async Task<TableSchema> GetTableSchemaAsync(string cs, TableRef table, CancellationToken ct = default)
    {
        // The old tool ran two queries (OBJECT_ID, then sys.columns) and ordered columns
        // alphabetically. Ordering by column_id instead keeps the generated INSERT column
        // list in the table's real shape, and the primary-key join is new.
        const string sql = """
            SELECT
                c.name                                   AS ColumnName,
                t.name                                   AS NativeType,
                c.max_length                             AS MaxLength,
                c.precision                              AS [Precision],
                c.scale                                  AS [Scale],
                c.is_nullable                            AS IsNullable,
                c.is_identity                            AS IsIdentity,
                c.is_computed                            AS IsComputed,
                c.column_id                              AS Ordinal,
                CASE WHEN ic.column_id IS NULL THEN 0 ELSE 1 END AS IsPrimaryKey,
                COALESCE(dc.definition, '')              AS DefaultExpression,
                CASE WHEN c.generated_always_type <> 0 OR COALESCE(ic2.is_identity, 0) = 1
                     THEN 1 ELSE 0 END                   AS IsGeneratedAlways
            FROM sys.columns c
            JOIN sys.types t
                ON t.user_type_id = c.user_type_id
            LEFT JOIN sys.identity_columns ic2
                ON ic2.object_id = c.object_id AND ic2.column_id = c.column_id
            LEFT JOIN sys.default_constraints dc
                ON dc.object_id = c.default_object_id
            LEFT JOIN (
                SELECT i.object_id, k.column_id
                FROM sys.indexes i
                JOIN sys.index_columns k
                    ON k.object_id = i.object_id AND k.index_id = i.index_id
                WHERE i.is_primary_key = 1
            ) ic
                ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE c.object_id = OBJECT_ID(@relation)
            ORDER BY c.column_id
            """;

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql, ("@relation", table.QualifiedName));
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var columns = new List<ColumnInfo>();
        while (await r.ReadAsync(ct))
        {
            var native = r.GetString(1);
            var maxLength = (int)r.GetInt16(2);

            // sys.columns reports max_length in bytes, and -1 for the (max) types.
            var charLength = native.ToLowerInvariant() switch
            {
                "nvarchar" or "nchar" or "sysname" when maxLength > 0 => maxLength / 2,
                "text" or "varchar" or "char" when maxLength < 0 => -1,
                "ntext" => 1_073_741_823,
                _ => maxLength,
            };

            columns.Add(new ColumnInfo
            {
                Name = r.GetString(0),
                NativeTypeName = native,
                MaxLength = charLength == 0 ? null : charLength,
                Precision = r.GetByte(3),
                Scale = r.GetByte(4),
                IsNullable = r.GetBoolean(5),
                IsAutoGenerated = r.GetBoolean(6) || r.GetInt32(11) == 1,
                IsComputed = r.GetBoolean(7),
                Ordinal = r.GetInt32(8),
                IsPrimaryKey = r.GetInt32(9) == 1,
                DefaultExpression = r.GetString(10) is { Length: > 0 } d ? d : null,
                ClrType = MapType(native),
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

    /// <summary>
    /// SQL Server has INFORMATION_SCHEMA.VIEWS.IS_UPDATABLE, and it is not worth reading: it
    /// reports NO for views that accept an UPDATE perfectly well. An INSTEAD OF trigger is
    /// proof of updatability; without one the honest answer is that we do not know, so the
    /// mutating methods are still generated and the doubt is recorded in the file.
    /// </summary>
    private static async Task<ViewMutability> ViewMutabilityAsync(
        string cs, TableRef table, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM sys.triggers
            WHERE parent_id = OBJECT_ID(@relation) AND is_instead_of_trigger = 1
            """;

        // A second connection: the column reader still owns the first.
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql, ("@relation", table.QualifiedName));
        var triggers = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);

        return triggers > 0 ? ViewMutability.Updatable : ViewMutability.Unknown;
    }


    internal static ClrTypeKind MapType(string native) => native.ToLowerInvariant() switch
    {
        "bit" => ClrTypeKind.Boolean,
        "tinyint" => ClrTypeKind.Byte,
        "smallint" => ClrTypeKind.Int16,
        "int" => ClrTypeKind.Int32,
        "bigint" => ClrTypeKind.Int64,
        "real" => ClrTypeKind.Single,
        "float" => ClrTypeKind.Double,
        "decimal" or "numeric" or "money" or "smallmoney" => ClrTypeKind.Decimal,
        "char" or "nchar" or "varchar" or "nvarchar" or "text" or "ntext"
            or "sysname" or "xml" => ClrTypeKind.String,
        "date" => ClrTypeKind.DateOnly,
        "time" => ClrTypeKind.TimeOnly,
        "datetime" or "datetime2" or "smalldatetime" => ClrTypeKind.DateTime,
        "datetimeoffset" => ClrTypeKind.DateTimeOffset,
        "uniqueidentifier" => ClrTypeKind.Guid,
        "binary" or "varbinary" or "image" or "timestamp" or "rowversion" => ClrTypeKind.ByteArray,
        _ => ClrTypeKind.Unknown,
    };
}
