using CCReimagined.Core.Model;

namespace CCReimagined.Core.Codegen;

public sealed class MySqlCodegenProfile : ICodegenProfile
{
    public string Id => "mysql";
    public string DisplayName => "MySQL / MariaDB (MySqlConnector)";
    public string NuGetPackage => "MySqlConnector";
    public IReadOnlyList<string> Usings { get; } = ["MySqlConnector"];
    public string ConnectionTypeName => "MySqlConnection";
    public string CommandTypeName => "MySqlCommand";
    public string ReaderTypeName => "MySqlDataReader";
    public string ParameterPrefix => "@";
    public string DbTypeEnumName => "MySqlConnector.MySqlDbType";
    public IdentityStrategy IdentityStrategy => IdentityStrategy.AppendedSelect;
    public bool LimitIsPrefix => false;

    public string QuoteIdentifier(string identifier) => $"`{identifier.Replace("`", "``")}`";

    // MySQL has no schema layer distinct from the database, so a qualified name is
    // only emitted when discovery reported a schema other than the connected one.
    public string QualifyRelation(TableRef table) => string.IsNullOrEmpty(table.Schema)
        ? QuoteIdentifier(table.Name)
        : $"{QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)}";

    public string DbTypeMember(ColumnInfo column)
    {
        var native = column.NativeTypeName.ToLowerInvariant();
        return native switch
        {
            "bit" => "Bit",
            "bool" or "boolean" => "Bool",
            "tinyint" => "Byte",
            "smallint" => "Int16",
            "mediumint" => "Int24",
            "int" or "integer" => "Int32",
            "bigint" => "Int64",
            "float" => "Float",
            "double" => "Double",
            "decimal" or "numeric" => "Decimal",
            "char" => "String",
            "varchar" => "VarChar",
            "tinytext" => "TinyText",
            "text" => "Text",
            "mediumtext" => "MediumText",
            "longtext" => "LongText",
            "json" => "JSON",
            "enum" or "set" => "VarChar",
            "date" => "Date",
            "time" => "Time",
            "datetime" => "DateTime",
            "timestamp" => "Timestamp",
            "year" => "Year",
            "binary" => "Binary",
            "varbinary" => "VarBinary",
            "tinyblob" => "TinyBlob",
            "blob" => "Blob",
            "mediumblob" => "MediumBlob",
            "longblob" => "LongBlob",
            "guid" => "Guid",
            _ => FromClrType(column.ClrType),
        };
    }

    private static string FromClrType(ClrTypeKind kind) => kind switch
    {
        ClrTypeKind.Boolean => "Bool",
        ClrTypeKind.Byte => "Byte",
        ClrTypeKind.Int16 => "Int16",
        ClrTypeKind.Int32 => "Int32",
        ClrTypeKind.Int64 => "Int64",
        ClrTypeKind.Single => "Float",
        ClrTypeKind.Double => "Double",
        ClrTypeKind.Decimal => "Decimal",
        ClrTypeKind.DateTime => "DateTime",
        ClrTypeKind.DateOnly => "Date",
        ClrTypeKind.TimeOnly or ClrTypeKind.TimeSpan => "Time",
        ClrTypeKind.DateTimeOffset => "DateTime",
        ClrTypeKind.Guid => "Guid",
        ClrTypeKind.ByteArray => "Blob",
        _ => "VarChar",
    };

    public string IdentityRetrievalSql(TableRef table, ColumnInfo keyColumn) =>
        "SELECT LAST_INSERT_ID();";

    public string LimitClause(int rows) => $"LIMIT {rows}";
}
