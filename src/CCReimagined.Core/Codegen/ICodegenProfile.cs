using CCReimagined.Core.Model;

namespace CCReimagined.Core.Codegen;

/// <summary>How a given engine retrieves the key of a row it just inserted.</summary>
public enum IdentityStrategy
{
    /// <summary>The INSERT itself returns the key (PostgreSQL's RETURNING).</summary>
    ReturningClause,

    /// <summary>A scalar appended to the INSERT batch (SCOPE_IDENTITY, LAST_INSERT_ID, last_insert_rowid).</summary>
    AppendedSelect,

    /// <summary>No generated key to fetch.</summary>
    None,
}

/// <summary>
/// Everything that differs between engines in the <em>generated</em> C# and SQL:
/// which ADO.NET types to instantiate, how parameters are named and typed, how
/// identifiers are quoted, and how a new row's key comes back.
///
/// The generator is written once against this interface, which is why the same
/// class shape can be emitted for SqlClient, Npgsql, MySqlConnector or Sqlite.
/// </summary>
public interface ICodegenProfile
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>NuGet package the generated file needs a reference to.</summary>
    string NuGetPackage { get; }

    /// <summary>Namespaces the generated file must import, beyond the common BCL set.</summary>
    IReadOnlyList<string> Usings { get; }

    /// <summary>e.g. "SqlConnection", "NpgsqlConnection".</summary>
    string ConnectionTypeName { get; }

    /// <summary>e.g. "SqlCommand". Emitted for the strongly typed CopyFields overload.</summary>
    string CommandTypeName { get; }

    /// <summary>e.g. "SqlDataReader", used in the reader-typed CopyFields signature.</summary>
    string ReaderTypeName { get; }

    /// <summary>The parameter sigil inside SQL text and in parameter names: "@" or ":".</summary>
    string ParameterPrefix { get; }

    /// <summary>Fully qualified provider DbType enum, e.g. "System.Data.SqlDbType".</summary>
    string DbTypeEnumName { get; }

    IdentityStrategy IdentityStrategy { get; }

    /// <summary>Wraps one identifier in the engine's quoting, e.g. [Name], "Name", `Name`.</summary>
    string QuoteIdentifier(string identifier);

    /// <summary>Quotes and joins a schema-qualified relation name.</summary>
    string QualifyRelation(TableRef table);

    /// <summary>The enum member of <see cref="DbTypeEnumName"/> that fits this column.</summary>
    string DbTypeMember(ColumnInfo column);

    /// <summary>
    /// SQL that yields the key of the row just inserted. For
    /// <see cref="IdentityStrategy.ReturningClause"/> this is appended to the INSERT;
    /// for <see cref="IdentityStrategy.AppendedSelect"/> it is a separate statement in the batch.
    /// </summary>
    string IdentityRetrievalSql(TableRef table, ColumnInfo keyColumn);

    /// <summary>Row-limiting clause for the list getters, given a positive row count.</summary>
    string LimitClause(int rows);

    /// <summary>True when the limit goes after the SELECT keyword (SQL Server's TOP) rather than at the end.</summary>
    bool LimitIsPrefix { get; }
}
