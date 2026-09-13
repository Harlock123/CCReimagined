using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CCReimagined.App.Services;
using CCReimagined.App.ViewModels;
using CCReimagined.App.Views;

namespace CCReimagined.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            var window = new MainWindow { DataContext = viewModel };

            // Wire the shell services here rather than from the window's Opened event, so the
            // clipboard, save dialog and Exit are live before the user can reach any of them.
            viewModel.Shell = new AvaloniaShellServices(window);

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}