using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace CCReimagined.App.Services;

/// <summary>Implements the shell services against a real window's clipboard and storage provider.</summary>
public sealed class AvaloniaShellServices : IShellServices
{
    private readonly TopLevel _topLevel;

    public AvaloniaShellServices(TopLevel topLevel) => _topLevel = topLevel;

    public async Task CopyToClipboardAsync(string text)
    {
        if (_topLevel.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public async Task<string?> SaveTextFileAsync(string suggestedFileName, string content)
    {
        var file = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save generated class",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "cs",
            FileTypeChoices =
            [
                new FilePickerFileType("C# source") { Patterns = ["*.cs"] },
                FilePickerFileTypes.All,
            ],
        });

        if (file is null)
            return null;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);

        return file.TryGetLocalPath() ?? file.Name;
    }

    public void Shutdown()
    {
        // Shutting the lifetime down closes every window and runs the normal exit path. Closing
        // just this window would do the same today, since it is the only one, but saying it
        // outright keeps that true if a second window is ever added.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        (_topLevel as Window)?.Close();
    }
}
