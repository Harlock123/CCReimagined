using System.Threading.Tasks;

namespace CCReimagined.App.Services;

/// <summary>
/// What the view model needs from the window it lives in: the clipboard, a save dialog, and
/// the ability to shut the application down. Kept behind an interface so the view model stays
/// testable and free of any reference to Avalonia's visual tree.
/// </summary>
public interface IShellServices
{
    Task CopyToClipboardAsync(string text);

    /// <summary>
    /// Prompts for a location and writes <paramref name="content"/> there.
    /// Returns the chosen path, or null when the user cancelled.
    /// </summary>
    Task<string?> SaveTextFileAsync(string suggestedFileName, string content);

    /// <summary>
    /// Ends the application. Hyprland and most tiling window managers can close any focused
    /// window, but a desktop that cannot leaves the user stuck, so the app offers its own way out.
    /// </summary>
    void Shutdown();
}
