using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;   // 依赖注入容器
using Microsoft.Extensions.Logging;                // 日志接口
using PDFTranslator.Core;                           // 核心翻译服务
using PDFTranslator.GUI.ViewModels;                 // 视图模型
using PDFTranslator.GUI.Views;                       // 视图
using System;                                       // IServiceProvider 所需

namespace PDFTranslator.GUI;

/// <summary>
/// Avalonia 应用程序类，负责初始化依赖注入和主窗口
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _serviceProvider;  // 服务提供程序（用于获取视图模型等）

    /// <summary>
    /// 初始化 XAML 资源
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 框架初始化完成后调用，用于设置主窗口和依赖注入
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        // ---------- 配置依赖注入 ----------
        var services = new ServiceCollection();

        // 添加日志，输出到控制台（调试时方便查看）
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 添加核心翻译服务，使用默认 Ollama 地址和模型
        services.AddPDFTranslatorCore(ollamaBaseUrl: "http://localhost:11434", model: "llama3.2");

        // 添加主窗口视图模型
        services.AddTransient<MainWindowViewModel>();

        // 构建服务提供程序
        _serviceProvider = services.BuildServiceProvider();

        // ---------- 设置主窗口 ----------
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 创建主窗口，并从 DI 容器获取视图模型作为 DataContext
            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>()
            };
        }

        // 调用基类方法完成初始化
        base.OnFrameworkInitializationCompleted();
    }
}