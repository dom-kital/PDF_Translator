using Microsoft.Extensions.Logging;
using PDFTranslator.GUI.ViewModels;
using System;

namespace PDFTranslator.GUI.Logging;

/// <summary>
/// 自定义日志记录器，将日志同时输出到控制台和 GUI
/// </summary>
public class CustomLogger : ILogger
{
    private readonly string _categoryName;
    private readonly MainWindowViewModel _viewModel;

    public CustomLogger(string categoryName, MainWindowViewModel viewModel)
    {
        _categoryName = categoryName;
        _viewModel = viewModel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        // 只输出 Information 及以上级别的日志（避免调试信息过多）
        return logLevel >= LogLevel.Information;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        // 生成日志消息
        var message = formatter(state, exception);
        
        // 格式化日志，添加时间戳和级别
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var logEntry = $"[{timestamp}] [{logLevel}] {message}";
        
        if (exception != null)
        {
            logEntry += $"\n[异常] {exception.Message}";
        }
        
        // 输出到控制台（通过 Console.WriteLine）
        Console.WriteLine(logEntry);
        
        // 输出到 GUI 日志
        _viewModel.AddLogMessage(logEntry + "\n");
    }
}