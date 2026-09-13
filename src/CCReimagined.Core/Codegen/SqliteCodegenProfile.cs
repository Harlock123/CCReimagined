using CCReimagined.Core.Model;

namespace CCReimagined.Core.Codegen;

public sealed class SqliteCodegenProfile : ICodegenProfile
{
    public string Id => "sqlite";
    public string DisplayName => "SQLite (Microsoft.Data.Sqlite)";
    public string NuGetPackage => "Microsoft.Data.Sqlite";
    public IReadOnlyList<string> Usings { get; } = ["Microsoft.Data.Sqlite"];
    public string ConnectionTypeName => "SqliteConnection";
    public string CommandTypeName => "SqliteCommand";
    public string ReaderTypeName => "SqliteDataReader";
    public string ParameterPrefix => "@";
    public string DbTypeEnumName => "Microsoft.Data.Sqlite.SqliteType";
    public IdentityStrategy IdentityStrategy => IdentityStrategy.AppendedSelect;
    public bool LimitIsPrefix => false;

    public string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    // SQLite has no schemas beyond attached-database names; discovery leaves Schema null.
    public string QualifyRelation(TableRef table) => string.IsNullOrEmpty(table.Schema)
        ? QuoteIdentifier(table.Name)
        : $"{QuoteIdentifier(table.Schema)}.{QuoteIdentifier(table.Name)}";

    // SQLite's storage classes collapse everything into four buckets.
    public string DbTypeMember(ColumnInfo column) => column.ClrType switch
    {
        ClrTypeKind.Boolean or ClrTypeKind.Byte or ClrTypeKind.Int16
            or ClrTypeKind.Int32 or ClrTypeKind.Int64 => "Integer",
        ClrTypeKind.Single or ClrTypeKind.Double => "Real",
        // Microsoft.Data.Sqlite round-trips decimal through TEXT so precision survives.
        ClrTypeKind.Decimal => "Text",
        ClrTypeKind.ByteArray => "Blob",
        _ => "Text",
    };

    public string IdentityRetrievalSql(TableRef table, ColumnInfo keyColumn) =>
        "SELECT last_insert_rowid();";

    public string LimitClause(int rows) => $"LIMIT {rows}";
}
