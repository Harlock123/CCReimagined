using CCReimagined.Core.Model;

namespace CCReimagined.Core.Codegen;

/// <summary>Knobs the UI exposes for the generated class.</summary>
public sealed record GenerationOptions
{
    /// <summary>
    /// Emit blocking wrappers alongside the async methods, for callers that cannot await.
    /// The async methods are always generated; these delegate to them.
    /// </summary>
    public bool GenerateSyncWrappers { get; init; } = true;

    public bool ImplementINotifyPropertyChanged { get; init; }

    /// <summary>Nullable columns become <c>int?</c>/<c>string?</c> rather than sentinel values.</summary>
    public bool NullableReferenceTypes { get; init; } = true;

    /// <summary>
    /// Length-bounded string setters silently truncate overlong values, as the old
    /// generated classes did, instead of letting the database reject the write.
    /// </summary>
    public bool TruncateOverlongStrings { get; init; } = true;

    /// <summary>
    /// Bind an empty string as NULL for nullable text columns. This was the old tool's
    /// unconditional behavior; it is off by default now because nullable property types
    /// let a caller say NULL explicitly.
    /// </summary>
    public bool TreatEmptyStringAsNull { get; init; }

    public bool GenerateRecExists { get; init; } = true;

    /// <summary>The modern stand-in for the old ReadAsDataSet method.</summary>
    public bool GenerateReadAsDataTable { get; init; } = true;

    public bool GenerateGetAll { get; init; } = true;

    /// <summary>Row cap applied to the list getters. Zero means unbounded.</summary>
    public int ListMaxRows { get; init; }

    public bool FileScopedNamespace { get; init; } = true;

    /// <summary>Emit the class as <c>sealed</c>; off leaves it open for inheritance.</summary>
    public bool SealedClass { get; init; } = true;

    /// <summary>Include a header comment recording what generated the file and from where.</summary>
    public bool IncludeProvenanceHeader { get; init; } = true;

    /// <summary>
    /// Whether a view gets Add, Update and Delete.
    ///
    /// <c>null</c>, the default, follows the database: a view the engine reports as read-only
    /// generates without them, and one it reports as updatable — or will not vouch for either
    /// way — gets them. Set it explicitly to overrule that, for a view the engine misjudges or
    /// one you would rather keep read-only regardless.
    /// </summary>
    public bool? MutatingMethodsForViews { get; init; }
}

/// <summary>
/// One generation job: the schema that was discovered, the provider profile the emitted
/// code should target, and the choices the user made about naming and list getters.
/// </summary>
public sealed record GenerationRequest
{
    public required TableSchema Schema { get; init; }

    /// <summary>
    /// Which engine the generated code talks to. Normally the profile of the provider
    /// that was browsed, but it can be pointed elsewhere to port a class across engines.
    /// </summary>
    public required ICodegenProfile Profile { get; init; }

    public string Namespace { get; init; } = "Generated.Data";

    /// <summary>Class name; defaults to a PascalCase form of the relation name.</summary>
    public string? ClassName { get; init; }

    /// <summary>
    /// Overrides the key column that Read/Update/Delete key on. When null the resolved
    /// key from <see cref="TableSchema.KeyColumn"/> is used.
    /// </summary>
    public string? KeyColumnName { get; init; }

    /// <summary>
    /// The columns the user picked to become list-getter parameters — the modern form of
    /// the old tool's "what field to use for code generation of the lists getters" prompt.
    /// One <c>GetListBy…</c> method is emitted per column named here.
    /// </summary>
    public IReadOnlyList<string> ListParameterColumns { get; init; } = [];

    public GenerationOptions Options { get; init; } = new();

    /// <summary>Recorded in the provenance header so a regenerated file says where it came from.</summary>
    public string? SourceDescription { get; init; }

    public string EffectiveClassName =>
        string.IsNullOrWhiteSpace(ClassName) ? Naming.ToClassName(Schema.Table.QualifiedName) : ClassName!;

    public ColumnInfo? EffectiveKeyColumn => KeyColumnName is null
        ? Schema.KeyColumn
        : Schema.Columns.FirstOrDefault(c => c.Name == KeyColumnName) ?? Schema.KeyColumn;
}
