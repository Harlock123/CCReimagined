namespace CCReimagined.Core.Model;

/// <summary>
/// The provider-neutral CLR shape of a column. Every provider maps its own native
/// type names onto this once, during schema discovery, so that the code generator
/// never has to reason about engine-specific type names.
/// </summary>
public enum ClrTypeKind
{
    Unknown = 0,
    Boolean,
    Byte,
    Int16,
    Int32,
    Int64,
    Single,
    Double,
    Decimal,
    String,
    DateTime,
    DateOnly,
    TimeOnly,
    DateTimeOffset,
    TimeSpan,
    Guid,
    ByteArray,
    Object,
}

public static class ClrTypeKindExtensions
{
    /// <summary>The C# type keyword/name to emit for this kind.</summary>
    public static string CSharpName(this ClrTypeKind kind) => kind switch
    {
        ClrTypeKind.Boolean => "bool",
        ClrTypeKind.Byte => "byte",
        ClrTypeKind.Int16 => "short",
        ClrTypeKind.Int32 => "int",
        ClrTypeKind.Int64 => "long",
        ClrTypeKind.Single => "float",
        ClrTypeKind.Double => "double",
        ClrTypeKind.Decimal => "decimal",
        ClrTypeKind.String => "string",
        ClrTypeKind.DateTime => "DateTime",
        ClrTypeKind.DateOnly => "DateOnly",
        ClrTypeKind.TimeOnly => "TimeOnly",
        ClrTypeKind.DateTimeOffset => "DateTimeOffset",
        ClrTypeKind.TimeSpan => "TimeSpan",
        ClrTypeKind.Guid => "Guid",
        ClrTypeKind.ByteArray => "byte[]",
        _ => "object",
    };

    /// <summary>True when the C# type is a reference type (so nullability is annotation-only).</summary>
    public static bool IsReferenceType(this ClrTypeKind kind) =>
        kind is ClrTypeKind.String or ClrTypeKind.ByteArray or ClrTypeKind.Object or ClrTypeKind.Unknown;

    /// <summary>The literal used to seed a non-nullable backing field in Initialize().</summary>
    public static string DefaultLiteral(this ClrTypeKind kind) => kind switch
    {
        ClrTypeKind.Boolean => "false",
        ClrTypeKind.Byte or ClrTypeKind.Int16 or ClrTypeKind.Int32 or ClrTypeKind.Int64 => "0",
        ClrTypeKind.Single => "0f",
        ClrTypeKind.Double => "0d",
        ClrTypeKind.Decimal => "0m",
        ClrTypeKind.String => "\"\"",
        ClrTypeKind.DateTime => "default",
        ClrTypeKind.DateOnly => "default",
        ClrTypeKind.TimeOnly => "default",
        ClrTypeKind.DateTimeOffset => "default",
        ClrTypeKind.TimeSpan => "default",
        ClrTypeKind.Guid => "Guid.Empty",
        ClrTypeKind.ByteArray => "[]",
        _ => "null!",
    };

    /// <summary>The DbDataReader.GetXxx method for this kind, when one exists.</summary>
    public static string? ReaderGetMethod(this ClrTypeKind kind) => kind switch
    {
        ClrTypeKind.Boolean => "GetBoolean",
        ClrTypeKind.Byte => "GetByte",
        ClrTypeKind.Int16 => "GetInt16",
        ClrTypeKind.Int32 => "GetInt32",
        ClrTypeKind.Int64 => "GetInt64",
        ClrTypeKind.Single => "GetFloat",
        ClrTypeKind.Double => "GetDouble",
        ClrTypeKind.Decimal => "GetDecimal",
        ClrTypeKind.String => "GetString",
        ClrTypeKind.DateTime => "GetDateTime",
        ClrTypeKind.Guid => "GetGuid",
        _ => null,
    };

    /// <summary>True when an integral kind can serve as a generated surrogate key.</summary>
    public static bool IsIntegral(this ClrTypeKind kind) =>
        kind is ClrTypeKind.Byte or ClrTypeKind.Int16 or ClrTypeKind.Int32 or ClrTypeKind.Int64;
}
