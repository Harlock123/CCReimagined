using CCReimagined.App.Services;
using CCReimagined.App.ViewModels;
using CCReimagined.Core.Profiles;
using CCReimagined.Core.Providers;

namespace CCReimagined.Core.Tests;

/// <summary>
/// Covers the commands that reach out to the host window — quitting above all. A tiling window
/// manager can close any focused window, so the gap only shows up on desktops that cannot, which
/// makes this exactly the wiring worth pinning down in a test rather than by clicking.
/// </summary>
public sealed class ShellCommandTests : IDisposable
{
    // Every view model here gets a store under a temp directory. Letting one fall back to the
    // default would have the suite overwrite the developer's own saved connections.
    private readonly string _configRoot = Path.Combine(Path.GetTempPath(), $"ccr-shell-{Guid.NewGuid():N}");

    private ProfileStore TempStore() => new(Path.Combine(_configRoot, "connections.json"));

    public void Dispose()
    {
        if (Directory.Exists(_configRoot))
            Directory.Delete(_configRoot, recursive: true);
    }

    private sealed class FakeShell : IShellServices
    {
        public int ShutdownCalls { get; private set; }

        public string? CopiedText { get; private set; }

        public string? SavedContent { get; private set; }

        public string? SuggestedName { get; private set; }

        /// <summary>Null stands for the user cancelling the save dialog.</summary>
        public string? SaveResult { get; set; } = "/tmp/Member.cs";

        public Task CopyToClipboardAsync(string text)
        {
            CopiedText = text;
            return Task.CompletedTask;
        }

        public Task<string?> SaveTextFileAsync(string suggestedFileName, string content)
        {
            SuggestedName = suggestedFileName;
            SavedContent = content;
            return Task.FromResult(SaveResult);
        }

        public void Shutdown() => ShutdownCalls++;
    }

    private MainViewModel WithShell(FakeShell shell) => new(TempStore())
    {
        Shell = shell,
        SelectedProvider = ProviderRegistry.ById("sqlite"),
    };

    [Fact]
    public void Exit_shuts_the_application_down()
    {
        var shell = new FakeShell();
        var vm = WithShell(shell);

        vm.ExitCommand.Execute(null);

        Assert.Equal(1, shell.ShutdownCalls);
    }

    [Fact]
    public void Exit_does_not_throw_when_no_shell_is_attached()
    {
        // The XAML previewer constructs the view model without a window behind it.
        var vm = new MainViewModel(TempStore());

        vm.ExitCommand.Execute(null);
    }

    [Fact]
    public async Task Copy_puts_the_generated_class_on_the_clipboard()
    {
        var shell = new FakeShell();
        var vm = WithShell(shell);

        // Nothing generated yet, so there is nothing to copy.
        await vm.CopyCommand.ExecuteAsync(null);
        Assert.Null(shell.CopiedText);

        vm.GeneratedCode = "// class";
        vm.GeneratedFileName = "Member.cs";
        vm.HasOutput = true;

        await vm.CopyCommand.ExecuteAsync(null);

        Assert.Equal("// class", shell.CopiedText);
        Assert.Contains("clipboard", vm.StatusMessage);
    }

    [Fact]
    public async Task Save_reports_the_path_it_wrote_to()
    {
        var shell = new FakeShell { SaveResult = "/home/someone/Member.cs" };
        var vm = WithShell(shell);

        vm.GeneratedCode = "// class";
        vm.GeneratedFileName = "Member.cs";
        vm.HasOutput = true;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Member.cs", shell.SuggestedName);
        Assert.Equal("// class", shell.SavedContent);
        Assert.Contains("/home/someone/Member.cs", vm.StatusMessage);
        Assert.False(vm.StatusIsError);
    }

    [Fact]
    public async Task Cancelling_the_save_dialog_is_not_reported_as_an_error()
    {
        var shell = new FakeShell { SaveResult = null };
        var vm = WithShell(shell);

        vm.GeneratedCode = "// class";
        vm.GeneratedFileName = "Member.cs";
        vm.HasOutput = true;

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.StatusIsError);
    }

    [Fact]
    public void The_window_menu_is_shown_everywhere_except_macos() =>
        Assert.Equal(!OperatingSystem.IsMacOS(), MainViewModel.ShowsWindowMenu);
}
