using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDFTranslator.Core;
using PDFTranslator.GUI.ViewModels;
using PDFTranslator.GUI.Views;
using System;

namespace PDFTranslator.GUI;

/// <summary>
/// 应用程序主类，负责依赖注入配置、主窗口创建以及全局异常处理。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 服务提供程序，用于在应用程序中获取依赖注入的服务实例。
    /// 声明为可空，因为在初始化完成前可能为 null。
    /// </summary>
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// 初始化应用程序的 XAML 资源。
    /// 此方法在应用程序启动时自动调用。
    /// </summary>
    public override void Initialize()
    {
        // 加载 App.axaml 文件中定义的样式和资源
        AvaloniaXamlLoader.Load(this);
        
#if DEBUG
        Console.WriteLine("✓ App.axaml 资源加载完成");
#endif
    }

    /// <summary>
    /// 框架初始化完成后调用，这是设置依赖注入和主窗口的主要位置。
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
#if DEBUG
        Console.WriteLine("🚀 应用程序正在启动...");
#endif

        try
        {
            // ---------- 1. 配置依赖注入容器 ----------
            var services = new ServiceCollection();

            // ---------- 2. 配置日志服务 ----------
            ConfigureLogging(services);

            // ---------- 3. 配置核心翻译服务 ----------
            ConfigureCoreServices(services);

            // ---------- 4. 配置视图模型 ----------
            ConfigureViewModels(services);

            // ---------- 5. 构建服务提供程序 ----------
            _serviceProvider = services.BuildServiceProvider();

            // ---------- 6. 验证关键服务是否可解析（仅调试模式） ----------
#if DEBUG
            ValidateServices();
#endif

            // ---------- 7. 根据应用程序生命周期类型设置主窗口 ----------
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                SetupDesktopApplication(desktop);
            }
            else
            {
                throw new NotSupportedException("当前版本仅支持桌面应用程序");
            }

#if DEBUG
            Console.WriteLine("✅ 应用程序启动完成");
#endif

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            // 捕获初始化过程中的致命错误
            HandleFatalError(ex);
        }
    }

    /// <summary>
    /// 配置日志服务。
    /// </summary>
    private void ConfigureLogging(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole(); // 控制台输出（对调试很有用）
#if DEBUG
            builder.SetMinimumLevel(LogLevel.Debug);
#else
            builder.SetMinimumLevel(LogLevel.Information);
#endif
            // 过滤掉无关的日志
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
        });

#if DEBUG
        Console.WriteLine("  ✓ 日志服务已配置");
#endif
    }

    /// <summary>
    /// 配置核心翻译服务。
    /// </summary>
    private void ConfigureCoreServices(IServiceCollection services)
    {
        // 使用默认 Ollama 地址和模型，实际运行时会从 ViewModel 读取用户配置
        services.AddPDFTranslatorCore(
            ollamaBaseUrl: "http://localhost:11434",
            model: "llama3.2"
        );

#if DEBUG
        Console.WriteLine("  ✓ 核心服务已配置 (默认: http://localhost:11434, llama3.2)");
#endif
    }

    /// <summary>
    /// 配置视图模型。
    /// </summary>
    private void ConfigureViewModels(IServiceCollection services)
    {
        // 主窗口视图模型注册为瞬态，每次请求都创建新实例
        services.AddTransient<MainWindowViewModel>();

        // 可在此添加其他视图模型
        // services.AddTransient<SettingsViewModel>();

#if DEBUG
        Console.WriteLine("  ✓ 视图模型已注册");
#endif
    }

    /// <summary>
    /// 验证关键服务是否可解析（仅调试模式）。
    /// </summary>
    private void ValidateServices()
    {
        try
        {
            var translator = _serviceProvider!.GetService<PdfTranslator>();
            var options = _serviceProvider!.GetService<TranslationOptions>();
            var viewModel = _serviceProvider!.GetService<MainWindowViewModel>();

            if (translator == null)
                Console.WriteLine("  ⚠️ 警告: PdfTranslator 服务解析失败");
            else
                Console.WriteLine("  ✓ PdfTranslator 服务可解析");

            if (options == null)
                Console.WriteLine("  ⚠️ 警告: TranslationOptions 服务解析失败");
            else
                Console.WriteLine("  ✓ TranslationOptions 服务可解析");

            if (viewModel == null)
                Console.WriteLine("  ⚠️ 警告: MainWindowViewModel 服务解析失败");
            else
                Console.WriteLine("  ✓ MainWindowViewModel 服务可解析");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ 服务验证失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 设置桌面应用程序，创建主窗口并设置 DataContext。
    /// </summary>
    private void SetupDesktopApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException("服务提供程序未初始化");

        var mainWindow = new MainWindow();
        var viewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
        mainWindow.DataContext = viewModel;
        desktop.MainWindow = mainWindow;

        // 订阅应用程序退出事件
        desktop.Exit += OnApplicationExit;

#if DEBUG
        Console.WriteLine("  ✓ 主窗口已创建并设置 DataContext");
#endif
    }

    /// <summary>
    /// 处理致命错误，在应用程序初始化失败时记录错误并退出。
    /// </summary>
    private void HandleFatalError(Exception ex)
    {
        Console.WriteLine("❌ 应用程序初始化失败:");
        Console.WriteLine($"   错误类型: {ex.GetType().Name}");
        Console.WriteLine($"   错误消息: {ex.Message}");
        if (ex.InnerException != null)
            Console.WriteLine($"   内部错误: {ex.InnerException.Message}");

        Environment.ExitCode = 1;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(1);
        }
    }

    /// <summary>
    /// 应用程序退出事件处理，用于清理资源。
    /// </summary>
    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
#if DEBUG
        Console.WriteLine("📦 应用程序正在退出...");
#endif
        // 释放服务提供程序（如果实现了 IDisposable）
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
#if DEBUG
            Console.WriteLine("  ✓ 服务提供程序已释放");
#endif
        }
    }
}