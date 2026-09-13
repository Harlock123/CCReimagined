using CCReimagined.Core.Model;

namespace CCReimagined.Core.Codegen;

public sealed class PostgreSqlCodegenProfile : ICodegenProfile
{
    public string Id => "postgresql";
    public string DisplayName => "PostgreSQL (Npgsql)";
    public string NuGetPackage => "Npgsql";
    public IReadOnlyList<string> Usings { get; } = ["Npgsql", "NpgsqlTypes"];
    public string ConnectionTypeName => "NpgsqlConnection";
    public string CommandTypeName => "NpgsqlCommand";
    public string ReaderTypeName => "NpgsqlDataReader";
    public string ParameterPrefix => "@";
    public string DbTypeEnumName => "NpgsqlTypes.NpgsqlDbType";
    public IdentityStrategy IdentityStrategy => IdentityStrategy.ReturningClause;
    public bool LimitIsPrefix => false;

    public string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    public string QualifyRelation(TableRef table) => string.IsNullOrEmpty(table.Schema)
        ? QuoteIdentifier(table.Name)
        : $"{QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)}";

    public string DbTypeMember(ColumnInfo column)
    {
        var native = column.NativeTypeName.ToLowerInvariant();
        return native switch
        {
            "boolean" or "bool" => "Boolean",
            "smallint" or "int2" => "Smallint",
            "integer" or "int" or "int4" => "Integer",
            "bigint" or "int8" => "Bigint",
            "real" or "float4" => "Real",
            "double precision" or "float8" => "Double",
            "numeric" or "decimal" or "money" => "Numeric",
            "character varying" or "varchar" => "Varchar",
            "character" or "char" or "bpchar" => "Char",
            "text" or "citext" or "name" => "Text",
            "json" => "Json",
            "jsonb" => "Jsonb",
            "uuid" => "Uuid",
            "date" => "Date",
            "time" or "time without time zone" => "Time",
            "time with time zone" or "timetz" => "TimeTz",
            "timestamp" or "timestamp without time zone" => "Timestamp",
            "timestamp with time zone" or "timestamptz" => "TimestampTz",
            "interval" => "Interval",
            "bytea" => "Bytea",
            "xml" => "Xml",
            "inet" => "Inet",
            _ => FromClrType(column.ClrType),
        };
    }

    private static string FromClrType(ClrTypeKind kind) => kind switch
    {
        ClrTypeKind.Boolean => "Boolean",
        ClrTypeKind.Byte or ClrTypeKind.Int16 => "Smallint",
        ClrTypeKind.Int32 => "Integer",
        ClrTypeKind.Int64 => "Bigint",
        ClrTypeKind.Single => "Real",
        ClrTypeKind.Double => "Double",
        ClrTypeKind.Decimal => "Numeric",
        ClrTypeKind.DateTime => "Timestamp",
        ClrTypeKind.DateOnly => "Date",
        ClrTypeKind.TimeOnly => "Time",
        ClrTypeKind.DateTimeOffset => "TimestampTz",
        ClrTypeKind.TimeSpan => "Interval",
        ClrTypeKind.Guid => "Uuid",
        ClrTypeKind.ByteArray => "Bytea",
        _ => "Text",
    };

    // PostgreSQL hands the key back from the INSERT itself, so no second round trip.
    public string IdentityRetrievalSql(TableRef table, ColumnInfo keyColumn) =>
        $"RETURNING {QuoteIdentifier(keyColumn.Name)}";

    public string LimitClause(int rows) => $"LIMIT {rows}";
}
