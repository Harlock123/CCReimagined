
using CCReimagined.Core.Model;

namespace CCReimagined.App.Models;

/// <summary>A table or view in the picker, with its kind shown so views are obvious.</summary>
public sealed record RelationRow(TableRef Relation)
{
    public string Display => Relation.QualifiedName;

    public string KindGlyph => Relation.Kind == RelationKind.View ? "view" : "table";

    public override string ToString() => Display;
}
