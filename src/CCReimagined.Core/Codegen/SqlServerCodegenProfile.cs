using CCReimagined.Core.Model;

namespace CCReimagined.Core.Codegen;

public sealed class SqlServerCodegenProfile : ICodegenProfile
{
    public string Id => "sqlserver";
    public string DisplayName => "Microsoft SQL Server (Microsoft.Data.SqlClient)";
    public string NuGetPackage => "Microsoft.Data.SqlClient";
    public IReadOnlyList<string> Usings { get; } = ["Microsoft.Data.SqlClient"];
    public string ConnectionTypeName => "SqlConnection";
    public string CommandTypeName => "SqlCommand";
    public string ReaderTypeName => "SqlDataReader";
    public string ParameterPrefix => "@";
    public string DbTypeEnumName => "System.Data.SqlDbType";
    public IdentityStrategy IdentityStrategy => IdentityStrategy.AppendedSelect;
    public bool LimitIsPrefix => true;

    public string QuoteIdentifier(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    public string QualifyRelation(TableRef table) => string.IsNullOrEmpty(table.Schema)
        ? QuoteIdentifier(table.Name)
        : $"{QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)}";

    public string DbTypeMember(ColumnInfo column)
    {
        // The native type name is authoritative here: SQL Server draws finer distinctions
        // (nvarchar vs varchar, money vs decimal) than the neutral ClrTypeKind carries.
        var native = column.NativeTypeName.ToLowerInvariant();
        return native switch
        {
            "bit" => "Bit",
            "tinyint" => "TinyInt",
            "smallint" => "SmallInt",
            "int" => "Int",
            "bigint" => "BigInt",
            "real" => "Real",
            "float" => "Float",
            "money" => "Money",
            "smallmoney" => "SmallMoney",
            "decimal" or "numeric" => "Decimal",
            "char" => "Char",
            "nchar" => "NChar",
            "varchar" => "VarChar",
            "nvarchar" or "sysname" => "NVarChar",
            "text" => "Text",
            "ntext" => "NText",
            "xml" => "Xml",
            "date" => "Date",
            "time" => "Time",
            "datetime" => "DateTime",
            "datetime2" => "DateTime2",
            "smalldatetime" => "SmallDateTime",
            "datetimeoffset" => "DateTimeOffset",
            "uniqueidentifier" => "UniqueIdentifier",
            "binary" => "Binary",
            "varbinary" => "VarBinary",
            "image" => "Image",
            "timestamp" or "rowversion" => "Timestamp",
            _ => FromClrType(column.ClrType),
        };
    }

    private static string FromClrType(ClrTypeKind kind) => kind switch
    {
        ClrTypeKind.Boolean => "Bit",
        ClrTypeKind.Byte => "TinyInt",
        ClrTypeKind.Int16 => "SmallInt",
        ClrTypeKind.Int32 => "Int",
        ClrTypeKind.Int64 => "BigInt",
        ClrTypeKind.Single => "Real",
        ClrTypeKind.Double => "Float",
        ClrTypeKind.Decimal => "Decimal",
        ClrTypeKind.DateTime => "DateTime2",
        ClrTypeKind.DateOnly => "Date",
        ClrTypeKind.TimeOnly or ClrTypeKind.TimeSpan => "Time",
        ClrTypeKind.DateTimeOffset => "DateTimeOffset",
        ClrTypeKind.Guid => "UniqueIdentifier",
        ClrTypeKind.ByteArray => "VarBinary",
        _ => "NVarChar",
    };

    public string IdentityRetrievalSql(TableRef table, ColumnInfo keyColumn) =>
        "SELECT CAST(SCOPE_IDENTITY() AS bigint);";

    public string LimitClause(int rows) => $"TOP ({rows})";
}
