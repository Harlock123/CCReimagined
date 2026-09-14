using CCReimagined.Core.Model;

namespace CCReimagined.Core.Codegen;

public sealed class OracleCodegenProfile : ICodegenProfile
{
    public string Id => "oracle";
    public string DisplayName => "Oracle Database (Oracle.ManagedDataAccess)";
    public string NuGetPackage => "Oracle.ManagedDataAccess.Core";
    public IReadOnlyList<string> Usings { get; } = ["Oracle.ManagedDataAccess.Client", "Oracle.ManagedDataAccess.Types"];
    public string ConnectionTypeName => "OracleConnection";
    public string CommandTypeName => "OracleCommand";
    public string ReaderTypeName => "OracleDataReader";

    /// <summary>Oracle binds by colon, not by at-sign.</summary>
    public string ParameterPrefix => ":";

    public string DbTypeEnumName => "Oracle.ManagedDataAccess.Client.OracleDbType";

    /// <summary>
    /// Oracle has no SCOPE_IDENTITY and no bare RETURNING clause that yields a result set —
    /// the INSERT writes the key into an output bind variable instead.
    /// </summary>
    public IdentityStrategy IdentityStrategy => IdentityStrategy.ReturningIntoParameter;

    public bool LimitIsPrefix => false;

    // An unquoted identifier folds to upper case, so a name is quoted to preserve the case the
    // catalog reported. That is also what keeps a column called "Order" or "Level" working.
    public string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    public string QualifyRelation(TableRef table) => string.IsNullOrEmpty(table.Schema)
        ? QuoteIdentifier(table.Name)
        : $"{QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)}";

    public string DbTypeMember(ColumnInfo column)
    {
        var native = column.NativeTypeName.ToUpperInvariant();

        // TIMESTAMP variants carry their precision in the name, e.g. "TIMESTAMP(6) WITH TIME ZONE".
        if (native.StartsWith("TIMESTAMP", StringComparison.Ordinal))
        {
            if (native.Contains("WITH LOCAL TIME ZONE", StringComparison.Ordinal))
                return "TimeStampLTZ";

            return native.Contains("WITH TIME ZONE", StringComparison.Ordinal) ? "TimeStampTZ" : "TimeStamp";
        }

        if (native.StartsWith("INTERVAL YEAR", StringComparison.Ordinal))
            return "IntervalYM";

        if (native.StartsWith("INTERVAL DAY", StringComparison.Ordinal))
            return "IntervalDS";

        return native switch
        {
            "VARCHAR2" or "VARCHAR" => "Varchar2",
            "NVARCHAR2" => "NVarchar2",
            "CHAR" => "Char",
            "NCHAR" => "NChar",
            "CLOB" => "Clob",
            "NCLOB" => "NClob",
            "BLOB" => "Blob",
            "BFILE" => "BFile",
            "RAW" or "LONG RAW" => "Raw",
            "LONG" => "Long",
            "DATE" => "Date",
            "BINARY_FLOAT" => "BinaryFloat",
            "BINARY_DOUBLE" => "BinaryDouble",
            "FLOAT" => "Double",
            // 23ai finally has a real BOOLEAN; earlier versions model it as NUMBER(1).
            "BOOLEAN" => "Boolean",
            "JSON" => "Json",
            "ROWID" or "UROWID" => "Varchar2",
            "NUMBER" or "NUMERIC" or "DECIMAL" or "INTEGER" or "INT" or "SMALLINT" => FromClrType(column.ClrType),
            _ => FromClrType(column.ClrType),
        };
    }

    private static string FromClrType(ClrTypeKind kind) => kind switch
    {
        ClrTypeKind.Boolean or ClrTypeKind.Byte or ClrTypeKind.Int16 => "Int16",
        ClrTypeKind.Int32 => "Int32",
        ClrTypeKind.Int64 => "Int64",
        ClrTypeKind.Single => "BinaryFloat",
        ClrTypeKind.Double => "BinaryDouble",
        ClrTypeKind.Decimal => "Decimal",
        ClrTypeKind.DateTime or ClrTypeKind.DateOnly => "Date",
        ClrTypeKind.DateTimeOffset => "TimeStampTZ",
        ClrTypeKind.TimeOnly or ClrTypeKind.TimeSpan => "IntervalDS",
        ClrTypeKind.Guid or ClrTypeKind.ByteArray => "Raw",
        _ => "Varchar2",
    };

    /// <summary>The bind variable the INSERT writes the generated key into.</summary>
    public string IdentityOutputParameterName => ParameterPrefix + "p_generated_key";

    public string IdentityRetrievalSql(TableRef table, ColumnInfo keyColumn) =>
        $"RETURNING {QuoteIdentifier(keyColumn.Name)} INTO {IdentityOutputParameterName}";

    // 12c and later. Earlier releases needed ROWNUM in a subquery, which this does not emit.
    public string LimitClause(int rows) => $"FETCH FIRST {rows} ROWS ONLY";
}
