using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PDFTranslator.GUI.ViewModels;
using System.ComponentModel;
using System.Linq;

namespace PDFTranslator.GUI.Views;

/// <summary>
/// 主窗口视图类，负责处理视图特有的事件和与 ViewModel 的交互。
/// 主要职责：
/// 1. 在窗口加载后将 IStorageProvider 传递给 ViewModel，使 ViewModel 能够调用文件对话框。
/// 2. 监听 ViewModel 的 Log 属性变化，当日志更新时自动将滚动条滚动到底部。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 构造函数，初始化组件并订阅窗口加载事件。
    /// </summary>
    public MainWindow()
    {
        // 初始化 XAML 中定义的组件（必须调用）
        InitializeComponent();

        // 订阅窗口加载完成事件。此时窗口已经完全初始化，DataContext 已设置，StorageProvider 可用。
        this.Loaded += (sender, args) =>
        {
            // 获取当前窗口的 DataContext 并尝试转换为 MainWindowViewModel 类型
            if (DataContext is MainWindowViewModel viewModel)
            {
                // 将 StorageProvider 传递给 ViewModel，以便其可以打开文件选择对话框
                viewModel.SetStorageProvider(StorageProvider);

                // 订阅 ViewModel 的属性变更事件，用于实现日志自动滚动
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        };
    }

    /// <summary>
    /// ViewModel 属性变更事件处理方法。
    /// 当 ViewModel 中任何属性变化时调用，我们只关心 Log 属性的变化。
    /// </summary>
    /// <param name="sender">事件源，即 ViewModel</param>
    /// <param name="e">事件参数，包含变更的属性名</param>
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 只处理 Log 属性的变更
        if (e.PropertyName == nameof(MainWindowViewModel.Log))
        {
            // 由于属性变更事件可能从后台线程触发（例如在翻译过程中），而 UI 操作必须在 UI 线程执行，
            // 因此使用 Dispatcher.UIThread.Post 将操作封送到 UI 线程。
            Dispatcher.UIThread.Post(() =>
            {
                // 查找日志文本框内部的 ScrollViewer 控件。
                // LogTextBox 是在 XAML 中定义的 TextBox，x:Name="LogTextBox"。
                // GetVisualDescendants() 是 Avalonia 提供的扩展方法，用于获取所有可视后代元素。
                // 使用 OfType<ScrollViewer>() 筛选出其中的 ScrollViewer，并取第一个（通常只有一个）。
                var scrollViewer = LogTextBox.GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .FirstOrDefault();

                // 如果找到了 ScrollViewer，则将其滚动到最底部，以便用户看到最新的日志。
                scrollViewer?.ScrollToEnd();
            });
        }
    }
}