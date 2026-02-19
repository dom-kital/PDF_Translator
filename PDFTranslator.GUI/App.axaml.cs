using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDFTranslator.Core;
using PDFTranslator.GUI.ViewModels;
using PDFTranslator.GUI.Views;
using System;  // 添加此行以解决 IServiceProvider 未找到的错误

namespace PDFTranslator.GUI;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 配置依赖注入
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 添加核心服务，使用默认 Ollama 地址和模型
        services.AddPDFTranslatorCore(ollamaBaseUrl: "http://localhost:11434", model: "llama3.2");

        // 添加主窗口视图模型
        services.AddTransient<MainWindowViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _serviceProvider.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}