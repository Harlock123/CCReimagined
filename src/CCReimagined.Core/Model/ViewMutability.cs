namespace CCReimagined.Core.Model;

/// <summary>
/// Whether rows can be written through a view.
///
/// Three states rather than two, because one engine will not say. PostgreSQL, MySQL and
/// MariaDB answer honestly in <c>information_schema.views.is_updatable</c>. SQLite has no
/// such column and refuses writes through a view outright — "cannot modify X because it is
/// a view" — unless an INSTEAD OF trigger stands in. SQL Server exposes
/// <c>INFORMATION_SCHEMA.VIEWS.IS_UPDATABLE</c> but it reports NO for views that accept an
/// UPDATE perfectly well, so it is not worth reading; the honest answer there is Unknown.
/// </summary>
public enum ViewMutability
{
    /// <summary>The relation is a table, so the question does not arise.</summary>
    NotApplicable = 0,

    /// <summary>The engine confirms rows can be written through it.</summary>
    Updatable,

    /// <summary>The engine confirms they cannot.</summary>
    ReadOnly,

    /// <summary>The engine cannot be asked, or gave an answer not worth trusting.</summary>
    Unknown,
}

public static class ViewMutabilityExtensions
{
    /// <summary>
    /// Whether to emit Add, Update and Delete. Unknown is generous — a view the engine will
    /// not vouch for still gets them, with the doubt recorded in the file, rather than
    /// silently losing capability the original tool had.
    /// </summary>
    public static bool AllowsMutation(this ViewMutability mutability) =>
        mutability is not ViewMutability.ReadOnly;

    public static string Describe(this ViewMutability mutability) => mutability switch
    {
        ViewMutability.Updatable => "the database reports this view as updatable",
        ViewMutability.ReadOnly => "the database reports this view as read-only",
        ViewMutability.Unknown => "the database cannot say whether this view is updatable",
        _ => "",
    };
}
