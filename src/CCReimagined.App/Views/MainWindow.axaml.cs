using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using CCReimagined.App.ViewModels;

namespace CCReimagined.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        Attach(DataContext as MainViewModel);
    }

    private MainViewModel? _viewModel;

    private void Attach(MainViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
            return;

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = viewModel;

        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Puts a freshly generated class at its first line.
    ///
    /// The editor keeps the scroll position it had, so generating a second class left the
    /// viewport wherever the previous one was scrolled to — usually past the end of a shorter
    /// file, which shows as an empty editor and reads as a failed generation.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.GeneratedCode))
            return;

        // Generating also brings the output tab forward, and a tab's content is not built
        // until it is shown. Posting at Background priority lets that happen first —
        // scrolling now would reach a control whose editor does not exist yet, and be
        // silently dropped.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (GeneratedCodeEditor is not { } editor || string.IsNullOrEmpty(editor.Text))
                    return;

                editor.CaretIndex = 0;
                editor.ScrollToLine(1);
            },
            DispatcherPriority.Background);
    }
}
