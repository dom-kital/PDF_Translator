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
/// 应用程序主类
/// 负责应用程序的初始化、依赖注入配置和主窗口创建
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 服务提供程序，用于在整个应用程序中获取依赖注入的服务实例
    /// 声明为可空类型，因为在初始化完成前可能为 null
    /// </summary>
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// 初始化应用程序的 XAML 资源
    /// 此方法在应用程序启动时自动调用
    /// </summary>
    public override void Initialize()
    {
        // 加载 App.axaml 文件中定义的样式和资源
        AvaloniaXamlLoader.Load(this);
        
        // 可以在这里添加应用程序级别的资源
        // 例如：添加全局样式、数据转换器等
        #if DEBUG
        Console.WriteLine("✓ App.axaml 资源加载完成");
        #endif
    }

    /// <summary>
    /// 框架初始化完成后调用
    /// 这是设置主窗口和依赖注入的主要位置
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        // 记录启动信息（调试用）
        #if DEBUG
        Console.WriteLine("🚀 应用程序正在启动...");
        Console.WriteLine($"  当前线程: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
        #endif

        try
        {
            // ---------- 步骤1：配置依赖注入容器 ----------
            var services = new ServiceCollection();

            // ---------- 步骤2：配置日志服务 ----------
            ConfigureLogging(services);

            // ---------- 步骤3：配置核心翻译服务 ----------
            ConfigureCoreServices(services);

            // ---------- 步骤4：配置视图模型 ----------
            ConfigureViewModels(services);

            // ---------- 步骤5：构建服务提供程序 ----------
            _serviceProvider = services.BuildServiceProvider();

            // 验证关键服务是否可解析（调试用）
            #if DEBUG
            ValidateServices();
            #endif

            // ---------- 步骤6：根据应用程序生命周期类型设置主窗口 ----------
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // 桌面应用程序（Windows、Linux、macOS）
                SetupDesktopApplication(desktop);
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            {
                // 移动端或浏览器应用（当前项目不使用）
                // 如果需要支持移动端，可以在这里设置 MainView
                throw new NotSupportedException("当前版本仅支持桌面应用程序");
            }

            // ---------- 步骤7：记录启动完成 ----------
            #if DEBUG
            Console.WriteLine("✅ 应用程序启动完成");
            #endif

            // 调用基类方法完成初始化
            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            // 捕获初始化过程中的致命错误
            HandleFatalError(ex);
        }
    }

    /// <summary>
    /// 配置日志服务
    /// 设置日志级别和输出目标
    /// </summary>
    /// <param name="services">服务集合</param>
    private void ConfigureLogging(IServiceCollection services)
    {
        // 添加日志服务
        services.AddLogging(builder =>
        {
            // 添加控制台日志输出（对调试很有用）
            builder.AddConsole();
            
            // 可以添加文件日志（如果需要）
            // builder.AddFile("app.log", append: true);
            
            // 设置最小日志级别
            // 开发环境可以使用 Debug 或 Information
            // 生产环境建议使用 Warning 或 Error
            #if DEBUG
            builder.SetMinimumLevel(LogLevel.Debug);
            #else
            builder.SetMinimumLevel(LogLevel.Information);
            #endif

            // 配置日志过滤器
            builder.AddFilter("Microsoft", LogLevel.Warning);      // 过滤微软的日志
            builder.AddFilter("System", LogLevel.Warning);         // 过滤系统日志
            builder.AddFilter("PDFTranslator", LogLevel.Debug);    // 保留我们自己的详细日志
        });

        #if DEBUG
        Console.WriteLine("  ✓ 日志服务已配置");
        #endif
    }

    /// <summary>
    /// 配置核心翻译服务
    /// 使用默认配置，实际运行时会从 ViewModel 读取用户配置
    /// </summary>
    /// <param name="services">服务集合</param>
    private void ConfigureCoreServices(IServiceCollection services)
    {
        // 添加 PDFTranslator 核心服务
        // 使用默认的 Ollama 地址和模型
        // 注意：这些默认值会被 ViewModel 中的用户配置覆盖
        services.AddPDFTranslatorCore(
            ollamaBaseUrl: "http://localhost:11434",  // 默认 Ollama 地址
            model: "llama3.2"                          // 默认模型
        );

        #if DEBUG
        Console.WriteLine("  ✓ 核心服务已配置 (默认: http://localhost:11434, llama3.2)");
        #endif
    }

    /// <summary>
    /// 配置视图模型
    /// 将视图模型注册到依赖注入容器中
    /// </summary>
    /// <param name="services">服务集合</param>
    private void ConfigureViewModels(IServiceCollection services)
    {
        // 注册主窗口视图模型为瞬态服务
        // 每次请求都创建新实例，避免状态污染
        services.AddTransient<MainWindowViewModel>();

        // 如果将来有其他视图模型，可以在这里继续注册
        // services.AddTransient<SettingsViewModel>();
        // services.AddTransient<AboutViewModel>();

        #if DEBUG
        Console.WriteLine("  ✓ 视图模型已注册");
        #endif
    }

    /// <summary>
    /// 验证关键服务是否可解析（仅调试模式）
    /// 在开发阶段帮助发现依赖注入配置错误
    /// </summary>
    private void ValidateServices()
    {
        try
        {
            // 尝试解析核心服务
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
    /// 设置桌面应用程序
    /// 创建主窗口并设置 DataContext
    /// </summary>
    /// <param name="desktop">桌面应用程序生命周期实例</param>
    private void SetupDesktopApplication(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // 确保服务提供程序已初始化
        if (_serviceProvider == null)
        {
            throw new InvalidOperationException("服务提供程序未初始化");
        }

        // 创建主窗口实例
        var mainWindow = new MainWindow();

        // 从依赖注入容器获取主窗口视图模型
        // GetRequiredService 会在服务不存在时抛出异常
        var viewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();

        // 将视图模型设置为窗口的数据上下文
        // 这样窗口的 XAML 绑定就能访问 ViewModel 的属性
        mainWindow.DataContext = viewModel;

        // 设置主窗口
        desktop.MainWindow = mainWindow;

        #if DEBUG
        Console.WriteLine("  ✓ 主窗口已创建并设置 DataContext");
        Console.WriteLine($"    窗口类型: {mainWindow.GetType().Name}");
        Console.WriteLine($"    视图模型类型: {viewModel.GetType().Name}");
        #endif

        // 可以订阅应用程序退出事件
        desktop.Exit += OnApplicationExit;
    }

    /// <summary>
    /// 处理致命错误
    /// 在应用程序初始化失败时显示错误信息并退出
    /// </summary>
    /// <param name="ex">捕获的异常</param>
    private void HandleFatalError(Exception ex)
    {
        // 在控制台输出错误信息
        Console.WriteLine("❌ 应用程序初始化失败:");
        Console.WriteLine($"   错误类型: {ex.GetType().Name}");
        Console.WriteLine($"   错误消息: {ex.Message}");
        Console.WriteLine($"   堆栈跟踪: {ex.StackTrace}");

        // 如果有关联的内部异常，也输出
        if (ex.InnerException != null)
        {
            Console.WriteLine($"   内部错误: {ex.InnerException.Message}");
        }

        // 在调试模式下，可以显示一个错误对话框
        #if DEBUG
        var message = $"应用程序初始化失败:\n\n{ex.Message}\n\n请查看控制台输出获取详细信息。";
        // 注意：在初始化阶段可能无法显示 Avalonia 对话框
        // 这里使用控制台输出代替
        Console.WriteLine(message);
        #endif

        // 设置退出码并退出
        Environment.ExitCode = 1;
        
        // 在桌面应用程序中，可以尝试关闭应用
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown(1);
        }
    }

    /// <summary>
    /// 应用程序退出事件处理
    /// 用于清理资源、保存配置等
    /// </summary>
    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        #if DEBUG
        Console.WriteLine("📦 应用程序正在退出...");
        Console.WriteLine($"   退出码: {e.ApplicationExitCode}");
        #endif

        // 可以在这里执行清理操作
        // 例如：保存用户配置、关闭数据库连接等
        
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