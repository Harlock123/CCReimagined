using System.Globalization;
using System.Text;

namespace CCReimagined.Core.Codegen;

/// <summary>
/// Turns database identifiers into legal, non-colliding C# names.
/// The old tool did a Replace(" ", "_") and a substring check against a reserved-word
/// list — which also renamed anything merely *containing* a keyword, so a column named
/// "INTERVIEWER" got mangled because it contains "int". This is the careful version.
/// </summary>
public static class Naming
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };

    /// <summary>Members the generated class defines itself, which a column must not shadow.</summary>
    private static readonly HashSet<string> ReservedMembers = new(StringComparer.Ordinal)
    {
        "ConnectionString", "TableName", "Initialize", "CopyFields", "RecExists", "RecExistsAsync",
        "Read", "ReadAsync", "ReadAsDataTable", "ReadAsDataTableAsync", "Add", "AddAsync",
        "Update", "UpdateAsync", "Delete", "DeleteAsync", "Equals", "GetHashCode", "GetType",
        "ToString", "MemberwiseClone", "PropertyChanged", "RaisePropertyChanged",
    };

    /// <summary>
    /// A legal C# identifier for a column. Spaces and punctuation become underscores;
    /// a leading digit gets a prefix; exact keyword matches are escaped with @.
    /// </summary>
    public static string ToIdentifier(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "_column";

        var sb = new StringBuilder(raw.Length + 1);

        foreach (var ch in raw)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            var legal = char.IsLetterOrDigit(ch)
                        || ch == '_'
                        || category is UnicodeCategory.ConnectorPunctuation
                            or UnicodeCategory.NonSpacingMark
                            or UnicodeCategory.SpacingCombiningMark;

            sb.Append(legal ? ch : '_');
        }

        var name = sb.ToString();

        if (char.IsDigit(name[0]))
            name = "_" + name;

        return name;
    }

    /// <summary>
    /// The property name for a column: a legal identifier, escaped if it is exactly a
    /// keyword, and suffixed if it would collide with a member the class already defines.
    /// </summary>
    public static string ToPropertyName(string raw)
    {
        var name = ToIdentifier(raw);

        if (Keywords.Contains(name))
            return "@" + name;

        if (ReservedMembers.Contains(name))
            return name + "Column";

        return name;
    }

    /// <summary>The backing field for a property, with the @ escape stripped.</summary>
    public static string ToFieldName(string propertyName) =>
        "_" + (propertyName.StartsWith('@') ? propertyName[1..] : propertyName);

    /// <summary>
    /// A class name seeded from a relation name: "dbo.tbl_member_main" becomes
    /// "TblMemberMain". The user can always override it in the UI.
    /// </summary>
    public static string ToClassName(string qualifiedRelationName)
    {
        var bare = qualifiedRelationName.Contains('.')
            ? qualifiedRelationName[(qualifiedRelationName.LastIndexOf('.') + 1)..]
            : qualifiedRelationName;

        var parts = ToIdentifier(bare).Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "GeneratedEntity";

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            // Leave already-mixed-case words alone; only fix all-lower and all-upper runs.
            var word = part.All(char.IsUpper) && part.Length > 1 ? part.ToLowerInvariant() : part;
            sb.Append(char.ToUpperInvariant(word[0]));
            if (word.Length > 1)
                sb.Append(word[1..]);
        }

        var name = sb.ToString();

        if (char.IsDigit(name[0]))
            name = "Entity" + name;

        return Keywords.Contains(name) ? name + "Entity" : name;
    }

    /// <summary>The parameter name (without sigil) used for a column in generated SQL.</summary>
    public static string ToParameterName(string columnName) =>
        "p_" + ToIdentifier(columnName).TrimStart('_');

    /// <summary>Escapes a string for emission as a regular C# string literal.</summary>
    public static string ToCSharpLiteral(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');

        foreach (var ch in value)
        {
            sb.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ => ch.ToString(),
            });
        }

        sb.Append('"');
        return sb.ToString();
    }
}
