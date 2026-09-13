using CCReimagined.Core.Model;

namespace CCReimagined.Core.Codegen;

/// <summary>
/// The per-job lookup table the emitter leans on: stable names for every column, and the
/// C# expressions that read a column from a reader or bind it to a parameter.
///
/// Keeping these in one place is what makes the emitter readable — the original tool
/// repeated the same type ladder (VARCHAR/CHAR/NVARCHAR/... then INT/SMALLINT/...) in a
/// dozen methods, so a fix in one never reached the others.
/// </summary>
internal sealed class EmitContext
{
    private readonly Dictionary<string, string> _properties = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal);

    internal EmitContext(
        GenerationRequest request,
        IReadOnlyList<ColumnInfo> columns,
        IReadOnlyList<ColumnInfo> writable,
        ColumnInfo? key,
        SqlBuilder sql,
        ICodegenProfile profile,
        GenerationOptions options,
        string className)
    {
        Request = request;
        Columns = columns;
        Writable = writable;
        Key = key;
        Sql = sql;
        Profile = profile;
        Options = options;
        ClassName = className;

        var taken = new HashSet<string>(StringComparer.Ordinal) { className, "ConnectionString", "TableName" };

        foreach (var col in columns)
        {
            var prop = Naming.ToPropertyName(col.Name);

            // Two columns can sanitise to the same identifier ("Unit Cost" and "Unit_Cost"),
            // so disambiguate rather than emit a class that will not compile.
            var candidate = prop;
            var suffix = 2;
            while (!taken.Add(candidate))
                candidate = prop + suffix++;

            _properties[col.Name] = candidate;
            _fields[col.Name] = Naming.ToFieldName(candidate);
        }

        ListFilterColumns = request.ListParameterColumns
            .Select(name => columns.FirstOrDefault(c => c.Name == name))
            .Where(c => c is not null)
            .Select(c => c!)
            .DistinctBy(c => c.Name)
            .ToList();

        // Falling back to case-sensitive labels only matters for a table with two columns
        // whose names differ by case alone, which the switch could not otherwise express.
        CaseInsensitiveLabels = columns
            .Select(c => c.Name.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .Count() == columns.Count;

        NeedsEmptyStringHelper = options.TreatEmptyStringAsNull
            && writable.Any(c => c.ClrType == ClrTypeKind.String && c.IsNullable);
    }

    internal GenerationRequest Request { get; }

    internal IReadOnlyList<ColumnInfo> Columns { get; }

    internal IReadOnlyList<ColumnInfo> Writable { get; }

    internal ColumnInfo? Key { get; }

    internal SqlBuilder Sql { get; }

    internal ICodegenProfile Profile { get; }

    internal GenerationOptions Options { get; }

    internal string ClassName { get; }

    internal IReadOnlyList<ColumnInfo> ListFilterColumns { get; }

    internal bool CaseInsensitiveLabels { get; }

    internal bool NeedsEmptyStringHelper { get; }

    internal string PropOf(ColumnInfo column) => _properties[column.Name];

    internal string FieldOf(ColumnInfo column) => _fields[column.Name];

    /// <summary>The declared C# type of the property, nullable when the column is.</summary>
    internal string TypeOf(ColumnInfo column)
    {
        var baseName = column.ClrType.CSharpName();

        if (!column.IsNullable)
            return baseName;

        // A nullable reference type is only expressible with the feature turned on.
        if (column.ClrType.IsReferenceType())
            return Options.NullableReferenceTypes ? baseName + "?" : baseName;

        return baseName + "?";
    }

    /// <summary>The type used for method parameters that must always carry a value.</summary>
    internal string NonNullableTypeOf(ColumnInfo column) => column.ClrType.CSharpName();

    internal string DefaultOf(ColumnInfo column)
    {
        if (!column.IsNullable)
            return column.ClrType.DefaultLiteral();

        // Nullable columns start out null rather than at a sentinel, which removes the old
        // generated code's ambiguity between "not set" and "legitimately empty".
        return column.ClrType.IsReferenceType() && !Options.NullableReferenceTypes
            ? column.ClrType.DefaultLiteral()
            : "null";
    }

    /// <summary>
    /// The expression that pulls one column out of a reader at ordinal <paramref name="ordinal"/>.
    /// Types the reader cannot hand over directly (DateOnly, TimeOnly) go through the
    /// portable conversion rather than a provider-specific GetFieldValue overload.
    /// </summary>
    internal string ReadExpression(ColumnInfo column, string reader, string ordinal)
    {
        var read = column.ClrType switch
        {
            ClrTypeKind.DateOnly => $"DateOnly.FromDateTime({reader}.GetDateTime({ordinal}))",
            ClrTypeKind.TimeOnly => $"TimeOnly.FromTimeSpan({reader}.GetFieldValue<TimeSpan>({ordinal}))",
            ClrTypeKind.TimeSpan => $"{reader}.GetFieldValue<TimeSpan>({ordinal})",
            ClrTypeKind.DateTimeOffset => $"{reader}.GetFieldValue<DateTimeOffset>({ordinal})",
            ClrTypeKind.ByteArray => $"(byte[]){reader}.GetValue({ordinal})",
            _ => column.ClrType.ReaderGetMethod() is { } method
                ? $"{reader}.{method}({ordinal})"
                : $"{reader}.GetValue({ordinal})",
        };

        if (column.IsNullable || column.ClrType.IsReferenceType())
            return $"{reader}.IsDBNull({ordinal}) ? {DefaultOf(column)} : {read}";

        return read;
    }

    /// <summary>
    /// The value expression handed to Bind: the backing field, converted where the ADO.NET
    /// provider expects a different CLR type than the property exposes.
    /// </summary>
    internal string BindValueExpression(ColumnInfo column, string source)
    {
        var nullable = column.IsNullable;

        var value = column.ClrType switch
        {
            // Every provider here accepts DateTime for a date column; DateOnly support is uneven.
            ClrTypeKind.DateOnly => nullable
                ? $"{source}?.ToDateTime(TimeOnly.MinValue)"
                : $"{source}.ToDateTime(TimeOnly.MinValue)",
            ClrTypeKind.TimeOnly => nullable
                ? $"{source}?.ToTimeSpan()"
                : $"{source}.ToTimeSpan()",
            _ => source,
        };

        if (Options.TreatEmptyStringAsNull && column.ClrType == ClrTypeKind.String && nullable)
            value = $"NullIfEmpty({value})";

        // Bind's parameter is object?, which every CLR value converts to implicitly,
        // so no cast is needed here.
        return value;
    }

    /// <summary>
    /// One complete Bind(...) call for a column. Bounded text and binary columns pass their
    /// declared length so the provider stops inferring a size from each value — which is
    /// what fragments the server's plan cache across calls.
    /// </summary>
    internal string BindLine(ColumnInfo column, string source)
    {
        var name = Naming.ToCSharpLiteral(Sql.ParameterName(column));
        var dbType = $"{Profile.DbTypeEnumName}.{Profile.DbTypeMember(column)}";
        var value = BindValueExpression(column, source);
        var size = DeclaredSize(column);

        return size is null
            ? $"Bind(command, {name}, {dbType}, {value});"
            : $"Bind(command, {name}, {dbType}, {value}, {size});";
    }

    /// <summary>The size to pin on the parameter, or null when the provider should infer it.</summary>
    internal int? DeclaredSize(ColumnInfo column)
    {
        if (column.ClrType is not (ClrTypeKind.String or ClrTypeKind.ByteArray))
            return null;

        // -1 is the engines' spelling of "unbounded" (varchar(max), text, bytea) and is
        // also what ADO.NET wants for a max-length parameter, so it passes through.
        return column.MaxLength switch
        {
            null or 0 => null,
            var length => length,
        };
    }

    /// <summary>Converts the scalar a generated-key INSERT returns into the key's CLR type.</summary>
    internal string ConvertScalar(ColumnInfo column, string scalar) => column.ClrType switch
    {
        ClrTypeKind.Int16 => $"Convert.ToInt16({scalar})",
        ClrTypeKind.Int32 => $"Convert.ToInt32({scalar})",
        ClrTypeKind.Int64 => $"Convert.ToInt64({scalar})",
        ClrTypeKind.Byte => $"Convert.ToByte({scalar})",
        ClrTypeKind.Decimal => $"Convert.ToDecimal({scalar})",
        ClrTypeKind.Guid => $"Guid.Parse(Convert.ToString({scalar}) ?? string.Empty)",
        ClrTypeKind.String => $"Convert.ToString({scalar}) ?? string.Empty",
        _ => $"({column.ClrType.CSharpName()})Convert.ChangeType({scalar}, typeof({column.ClrType.CSharpName()}))",
    };
}
