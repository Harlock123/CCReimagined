using System.Collections.Generic;
using System.Linq;
using SyntaxColorizer.Themes;

namespace CCReimagined.App.Models;

/// <summary>
/// One entry in the editor's theme picker. SyntaxColorizer exposes its themes as static
/// properties rather than a list, so they are named and collected here for binding.
/// </summary>
public sealed record EditorThemeOption(string Name, SyntaxTheme Theme, bool IsDark)
{
    public override string ToString() => Name;

    /// <summary>The built-in themes, dark first, since the app's own chrome defaults to dark.</summary>
    public static IReadOnlyList<EditorThemeOption> All { get; } =
    [
        new("Visual Studio Dark", BuiltInThemes.VisualStudioDark, true),
        new("Visual Studio Light", BuiltInThemes.VisualStudioLight, false),
        new("GitHub Dark", BuiltInThemes.GitHubDark, true),
        new("GitHub Light", BuiltInThemes.GitHubLight, false),
        new("Monokai", BuiltInThemes.Monokai, true),
        new("Dracula", BuiltInThemes.Dracula, true),
        new("One Dark", BuiltInThemes.OneDark, true),
        new("One Light", BuiltInThemes.OneLight, false),
        new("Nord", BuiltInThemes.Nord, true),
        new("Solarized Dark", BuiltInThemes.SolarizedDark, true),
        new("Solarized Light", BuiltInThemes.SolarizedLight, false),
        new("Gruvbox Dark", BuiltInThemes.GruvboxDark, true),
        new("Gruvbox Light", BuiltInThemes.GruvboxLight, false),
        new("Quiet Light", BuiltInThemes.QuietLight, false),
    ];

    public static EditorThemeOption Default => All[0];

    public static EditorThemeOption ByName(string? name) =>
        All.FirstOrDefault(t => t.Name == name) ?? Default;
}
