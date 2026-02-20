using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PDFTranslator.GUI.ViewModels;

namespace PDFTranslator.GUI.Views;

/// <summary>
/// 主窗口视图类，负责处理视图层的事件以及与 ViewModel 的交互。
/// 主要功能是在窗口加载后将文件存储提供程序（IStorageProvider）传递给 ViewModel，
/// 以便 ViewModel 能够调用文件选择对话框。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 构造函数，初始化 XAML 组件并订阅窗口加载事件。
    /// </summary>
    public MainWindow()
    {
        // 初始化 XAML 中定义的组件（必须调用）
        InitializeComponent();

        // 窗口加载完成后触发的事件。此时窗口已经完全初始化，可以安全地访问 StorageProvider。
        this.Loaded += (sender, args) =>
        {
            // 获取当前窗口的 DataContext 并尝试转换为 MainWindowViewModel 类型
            if (DataContext is MainWindowViewModel viewModel)
            {
                // 将 StorageProvider 传递给 ViewModel，以便其可以打开文件对话框
                viewModel.SetStorageProvider(StorageProvider);
            }
        };

        // 备用方案：当 DataContext 发生变化时也尝试传递 StorageProvider。
        // 这可以应对某些情况下 DataContext 在窗口加载后才设置的情况。
        this.DataContextChanged += (sender, args) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.SetStorageProvider(StorageProvider);
            }
        };
    }
}