using CCReimagined.Core.Codegen;
using CCReimagined.Core.Model;
using Microsoft.Data.Sqlite;

namespace CCReimagined.Core.Providers;

public sealed class SqliteProvider : IDatabaseProvider
{
    public string Id => "sqlite";
    public string DisplayName => "SQLite";
    public ICodegenProfile CodegenProfile { get; } = new SqliteCodegenProfile();

    public ProviderCapabilities Capabilities { get; } = new()
    {
        // A SQLite "server" is a file on disk, so the host and database pickers give way
        // to a single file path.
        NeedsHost = false,
        SupportsIntegratedAuth = false,
        SupportsDatabaseEnumeration = false,
        SupportsSchemas = false,
        DatabaseIsFilePath = true,
        DefaultPort = 0,
    };

    public string BuildConnectionString(ConnectionSettings s)
    {
        if (s.UsesRaw)
            return s.RawConnectionString!;

        var b = new SqliteConnectionStringBuilder
        {
            // A typed path may carry a ~ or a $HOME that no shell has expanded, and may be
            // relative to wherever the app happens to have been launched from.
            DataSource = FilePath.Resolve(s.Database),
            // Read-only would be safer, but the tool also offers to run generated DDL.
            Mode = SqliteOpenMode.ReadWrite,
        };

        if (!string.IsNullOrEmpty(s.Password))
            b.Password = s.Password;

        return b.ConnectionString;
    }

    public string WithDatabase(string connectionString, string database) =>
        new SqliteConnectionStringBuilder(connectionString) { DataSource = FilePath.Resolve(database) }.ConnectionString;

    public async Task<ProbeResult> TestConnectionAsync(string cs, CancellationToken ct = default)
    {
        // SQLite answers a missing file with "unable to open database file" and nothing else —
        // no path, no reason. Checking first turns that into something actionable.
        var source = new SqliteConnectionStringBuilder(cs).DataSource;

        if (string.IsNullOrWhiteSpace(source))
            return ProbeResult.Fail("No database file given. Enter the path to a SQLite file.");

        if (Directory.Exists(source))
            return ProbeResult.Fail($"'{source}' is a directory, not a SQLite database file.");

        if (!File.Exists(source))
        {
            return ProbeResult.Fail(
                $"No such file: {source}\n" +
                "The path is resolved from where you typed it, so a leading ~ is expanded and a " +
                "relative path is taken from the app's working directory. This tool opens an " +
                "existing database rather than creating one.");
        }

        try
        {
            await using var cn = new SqliteConnection(cs);
            await cn.OpenAsync(ct);
            await using var cmd = AdoHelpers.Command(cn, "SELECT sqlite_version()");
            var version = await cmd.ExecuteScalarAsync(ct);
            return ProbeResult.Ok($"SQLite {version}");
        }
        catch (Exception ex)
        {
            return ProbeResult.Fail($"{ex.Message} (file: {source})");
        }
    }

    /// <summary>A SQLite connection reaches exactly one file, so this reports that file.</summary>
    public Task<IReadOnlyList<string>> ListDatabasesAsync(string cs, CancellationToken ct = default)
    {
        var source = new SqliteConnectionStringBuilder(cs).DataSource;
        IReadOnlyList<string> result = string.IsNullOrEmpty(source) ? [] : [source];
        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<TableRef>> ListRelationsAsync(string cs, CancellationToken ct = default)
    {
        const string sql = """
            SELECT name, type
            FROM sqlite_master
            WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%'
            ORDER BY name
            """;

        await using var cn = new SqliteConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = AdoHelpers.Command(cn, sql);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var result = new List<TableRef>();
        while (await r.ReadAsync(ct))
        {
            var kind = r.GetString(1) == "view" ? RelationKind.View : RelationKind.Table;
            result.Add(new TableRef(null, r.GetString(0), kind));
        }

        return result;
    }

    public async Task<TableSchema> GetTableSchemaAsync(string cs, TableRef table, CancellationToken ct = default)
    {
        await using var cn = new SqliteConnection(cs);
        await cn.OpenAsync(ct);

        // SQLite has no information_schema; PRAGMA table_xinfo is the catalog, and it
        // reports hidden/generated columns that table_info leaves out.
        var columns = new List<ColumnInfo>();
        await using (var cmd = AdoHelpers.Command(cn, $"PRAGMA table_xinfo({Quote(table.Name)})"))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var declared = r.IsDBNull(2) ? "" : r.GetString(2);
                var notNull = r.GetInt32(3) != 0;
                var defaultValue = r.IsDBNull(4) ? null : r.GetValue(4)?.ToString();
                var pkPosition = r.GetInt32(5);
                // hidden: 0 normal, 2 virtual generated, 3 stored generated.
                var hidden = r.FieldCount > 6 ? r.GetInt32(6) : 0;

                columns.Add(new ColumnInfo
                {
                    Name = r.GetString(1),
                    NativeTypeName = string.IsNullOrWhiteSpace(declared) ? "BLOB" : declared,
                    MaxLength = ParseDeclaredLength(declared),
                    IsNullable = !notNull && pkPosition == 0,
                    IsComputed = hidden is 2 or 3,
                    IsPrimaryKey = pkPosition > 0,
                    Ordinal = r.GetInt32(0) + 1,
                    DefaultExpression = defaultValue,
                    ClrType = MapType(declared),
                });
            }
        }

        // In SQLite only an INTEGER PRIMARY KEY on a non-WITHOUT-ROWID table aliases
        // the rowid and therefore auto-numbers. Detect that rather than assume it.
        var withoutRowid = await IsWithoutRowIdAsync(cn, table.Name, ct);
        var pkColumns = columns.Where(c => c.IsPrimaryKey).ToList();

        if (!withoutRowid && pkColumns.Count == 1)
        {
            var pk = pkColumns[0];
            if (pk.NativeTypeName.Trim().Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
            {
                var index = columns.IndexOf(pk);
                columns[index] = pk with { IsAutoGenerated = true, ClrType = ClrTypeKind.Int64 };
            }
        }

        return new TableSchema
        {
            Table = table,
            Columns = columns,
            KeyColumn = TableSchema.ResolveKeyColumn(columns, table.Name),
        };
    }

    private static async Task<bool> IsWithoutRowIdAsync(SqliteConnection cn, string table, CancellationToken ct)
    {
        await using var cmd = AdoHelpers.Command(
            cn,
            "SELECT COALESCE(sql, '') FROM sqlite_master WHERE type = 'table' AND name = @name",
            ("@name", table));

        var ddl = await cmd.ExecuteScalarAsync(ct) as string ?? "";
        return ddl.Contains("WITHOUT ROWID", StringComparison.OrdinalIgnoreCase);
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static int? ParseDeclaredLength(string declared)
    {
        var open = declared.IndexOf('(');
        var close = declared.IndexOf(')');
        if (open < 0 || close < open)
            return null;

        var inner = declared[(open + 1)..close];
        var first = inner.Split(',')[0].Trim();
        return int.TryParse(first, out var length) ? length : null;
    }

    /// <summary>
    /// SQLite stores a declared type verbatim and applies type affinity at runtime, so the
    /// mapping follows the affinity rules from the SQLite docs rather than a fixed type list.
    /// </summary>
    internal static ClrTypeKind MapType(string declared)
    {
        var t = declared.ToUpperInvariant();

        if (t.Length == 0)
            return ClrTypeKind.ByteArray;

        if (t.Contains("BOOL"))
            return ClrTypeKind.Boolean;

        if (t.Contains("BIGINT"))
            return ClrTypeKind.Int64;

        if (t.Contains("INT"))
            return t.Contains("TINYINT") || t.Contains("SMALLINT") ? ClrTypeKind.Int16 : ClrTypeKind.Int32;

        if (t.Contains("CHAR") || t.Contains("CLOB") || t.Contains("TEXT"))
            return ClrTypeKind.String;

        if (t.Contains("BLOB"))
            return ClrTypeKind.ByteArray;

        if (t.Contains("REAL") || t.Contains("FLOA") || t.Contains("DOUB"))
            return ClrTypeKind.Double;

        if (t.Contains("DECIMAL") || t.Contains("NUMERIC") || t.Contains("MONEY"))
            return ClrTypeKind.Decimal;

        if (t.Contains("DATETIME") || t.Contains("TIMESTAMP"))
            return ClrTypeKind.DateTime;

        if (t.Contains("DATE"))
            return ClrTypeKind.DateOnly;

        if (t.Contains("TIME"))
            return ClrTypeKind.TimeOnly;

        if (t.Contains("GUID") || t.Contains("UUID"))
            return ClrTypeKind.Guid;

        return ClrTypeKind.String;
    }
}
