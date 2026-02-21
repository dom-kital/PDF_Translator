using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PDFTranslator.GUI.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;

namespace PDFTranslator.GUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 窗口加载完成后执行
        this.Loaded += (sender, args) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SetStorageProvider(StorageProvider);
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        };
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.Log))
        {
            Dispatcher.UIThread.Post(() =>
            {
                var scrollViewer = LogTextBox.GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .FirstOrDefault();
                scrollViewer?.ScrollToEnd();
            });
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
    }
}