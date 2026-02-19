using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PDFTranslator.GUI.ViewModels;

namespace PDFTranslator.GUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // 当 DataContext 设置后，将 StorageProvider 传递给 ViewModel
        this.AttachedToVisualTree += (s, e) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SetStorageProvider(StorageProvider);
            }
        };
    }
}