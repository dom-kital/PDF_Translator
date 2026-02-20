using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree; // 添加此行以解决 VisualTreeAttachmentEventArgs 未找到的错误
using PDFTranslator.GUI.ViewModels;
using Avalonia;             // 添加基础命名空间
using System;

namespace PDFTranslator.GUI.Views;

/// <summary>
/// 主窗口视图类
/// 负责处理视图层的事件和与 ViewModel 的交互
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 构造函数，初始化组件并设置事件处理
    /// </summary>
    public MainWindow()
    {
        // 初始化 XAML 中定义的组件
        InitializeComponent();

        // 方法1：在窗口加载完成后设置 StorageProvider（最可靠）
        // Loaded 事件在窗口完全加载并呈现后触发
        this.Loaded += (s, e) =>
        {
            // 获取当前窗口的 DataContext 并转换为 ViewModel 类型
            if (DataContext is MainWindowViewModel vm)
            {
                // 调用 ViewModel 的方法设置存储提供程序
                vm.SetStorageProvider(StorageProvider);
                
                // 调试输出，确认 StorageProvider 已设置
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("✓ StorageProvider 已通过 Loaded 事件设置");
                #endif
            }
        };

        // 方法2：在窗口附加到可视化树时设置（作为备用）
        // AttachedToVisualTree 在控件添加到可视化树时触发，比 Loaded 稍早
        this.AttachedToVisualTree += (s, e) =>
        {
            // 获取 ViewModel 实例
            if (DataContext is MainWindowViewModel vm)
            {
                // 调用 SetStorageProvider 方法设置存储提供程序
                vm.SetStorageProvider(StorageProvider);
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("✓ StorageProvider 已通过 AttachedToVisualTree 事件设置");
                #endif
            }
        };

        // 方法3：当 DataContext 发生变化时处理
        // 这可以应对 DataContext 在窗口加载后才设置的情况
        this.DataContextChanged += (s, e) =>
        {
            // 获取新的 DataContext 并尝试转换为 ViewModel 类型
            if (DataContext is MainWindowViewModel vm)
            {
                // 设置存储提供程序
                vm.SetStorageProvider(StorageProvider);
                
                #if DEBUG
                System.Diagnostics.Debug.WriteLine("✓ StorageProvider 已通过 DataContextChanged 事件设置");
                #endif
            }
        };
    }

    /// <summary>
    /// 当窗口关闭时调用
    /// 可以在这里执行清理操作
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        
        #if DEBUG
        System.Diagnostics.Debug.WriteLine("窗口正在关闭");
        #endif
        
        // 移除事件处理，避免内存泄漏
        // 注意：这里不需要手动移除，因为窗口关闭后事件会自动释放
        // 但为了代码完整性，保留注释说明
    }

    /// <summary>
    /// Loaded 事件的专用处理方法
    /// </summary>
    private void OnLoaded(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SetStorageProvider(StorageProvider);
        }
    }

    /// <summary>
    /// AttachedToVisualTree 事件的专用处理方法
    /// </summary>
    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SetStorageProvider(StorageProvider);
        }
    }

    /// <summary>
    /// DataContextChanged 事件的专用处理方法
    /// </summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SetStorageProvider(StorageProvider);
        }
    }

    /// <summary>
    /// 获取当前 ViewModel 实例的辅助方法
    /// </summary>
    private MainWindowViewModel? GetViewModel()
    {
        return DataContext as MainWindowViewModel;
    }
}