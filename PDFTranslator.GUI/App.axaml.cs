using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDFTranslator.Core;
using PDFTranslator.GUI.Logging;
using PDFTranslator.GUI.ViewModels;
using PDFTranslator.GUI.Views;
using System;

namespace PDFTranslator.GUI;

/// <summary>
/// 应用程序主类，负责依赖注入配置、主窗口创建以及全局异常处理。
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _serviceProvider;
    private MainWindowViewModel? _mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        Console.WriteLine("✓ App.axaml 资源加载完成");
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if DEBUG
        Console.WriteLine("🚀 应用程序正在启动...");
#endif

        try
        {
            // ---------- 1. 先创建 ViewModel（用于接收日志回调）----------
            _mainViewModel = new MainWindowViewModel();

            // ---------- 2. 配置依赖注入容器 ----------
            var services = new ServiceCollection();

            // ---------- 3. 配置日志服务 ----------
            ConfigureLogging(services);

            // ---------- 4. 配置核心翻译服务 ----------
            ConfigureCoreServices(services);

            // ---------- 5. 注册 ViewModel 为单例 ----------
            services.AddSingleton(_mainViewModel);

            // ---------- 6. 构建服务提供程序 ----------
            _serviceProvider = services.BuildServiceProvider();

            // ---------- 7. 初始化 ViewModel 的依赖注入服务 ----------
            InitializeViewModelServices();

            // ---------- 8. 根据应用程序生命周期类型设置主窗口 ----------
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
            // 添加控制台日志
            builder.AddConsole();

#if DEBUG
            // 调试模式下输出详细信息
            builder.SetMinimumLevel(LogLevel.Debug);
#else
            builder.SetMinimumLevel(LogLevel.Information);
#endif

            // 过滤掉无关的日志
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
        });

        // 注意：自定义日志提供程序需要在服务构建后通过服务提供程序添加
        // 这里不能直接添加，因为 _mainViewModel 还没有通过依赖注入获取

#if DEBUG
        Console.WriteLine("  ✓ 基础日志服务已配置");
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
    /// 初始化 ViewModel 的依赖注入服务
    /// </summary>
    private void InitializeViewModelServices()
    {
        if (_serviceProvider == null || _mainViewModel == null)
            return;

        // 获取所需的依赖注入服务
        var translator = _serviceProvider.GetRequiredService<PdfTranslator>();
        var options = _serviceProvider.GetRequiredService<TranslationOptions>();
        var logger = _serviceProvider.GetRequiredService<ILogger<MainWindowViewModel>>();

        // 调用 ViewModel 的初始化方法
        _mainViewModel.InitializeServices(translator, options, logger);
        
        // 现在可以添加自定义日志提供程序，因为 ViewModel 已经初始化
        AddCustomLoggerProvider();
    }

    /// <summary>
    /// 添加自定义日志提供程序，将日志输出到 GUI
    /// </summary>
    private void AddCustomLoggerProvider()
    {
        if (_serviceProvider == null || _mainViewModel == null)
            return;

        // 获取日志工厂
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        
        // 添加自定义提供程序
        loggerFactory.AddProvider(new CustomLoggerProvider(_mainViewModel));
        
#if DEBUG
        Console.WriteLine("  ✓ 自定义日志提供程序已添加");
#endif
    }

    /// <summary>
    /// 设置桌面应用程序，创建主窗口并设置 DataContext。
    /// </summary>
    private void SetupDesktopApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_serviceProvider == null || _mainViewModel == null)
            throw new InvalidOperationException("服务提供程序或 ViewModel 未初始化");

        var mainWindow = new MainWindow();
        mainWindow.DataContext = _mainViewModel;
        desktop.MainWindow = mainWindow;

        // 订阅应用程序退出事件
        desktop.Exit += OnApplicationExit;

#if DEBUG
        Console.WriteLine("  ✓ 主窗口已创建并设置 DataContext");
#endif
    }

    /// <summary>
    /// 处理致命错误，记录并退出。
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
    /// 应用程序退出事件处理，清理资源。
    /// </summary>
    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
#if DEBUG
        Console.WriteLine("📦 应用程序正在退出...");
#endif
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
#if DEBUG
            Console.WriteLine("  ✓ 服务提供程序已释放");
#endif
        }
    }
}