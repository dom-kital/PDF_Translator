using Avalonia;
using Avalonia.ReactiveUI;
using System;

namespace PDFTranslator.GUI;

/// <summary>
/// 图形界面程序入口类
/// </summary>
class Program
{
    /// <summary>
    /// 应用程序主入口点
    /// </summary>
    /// <param name="args">命令行参数</param>
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // 构建并启动 Avalonia 应用程序
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // 捕获未处理的异常并记录
            Console.WriteLine($"应用程序启动失败: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.ExitCode = 1;
        }
    }

    /// <summary>
    /// 构建 Avalonia 应用程序配置
    /// </summary>
    /// <returns>Avalonia 应用程序构建器</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()           // 自动检测并适配当前平台
            .WithInterFont()               // 使用 Inter 字体
            .LogToTrace()                   // 日志输出到 Trace
            .UseReactiveUI();                // 启用 ReactiveUI 支持
}