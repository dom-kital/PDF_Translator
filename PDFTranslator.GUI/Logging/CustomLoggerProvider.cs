using Microsoft.Extensions.Logging;
using PDFTranslator.GUI.ViewModels;
using System;

namespace PDFTranslator.GUI.Logging;

/// <summary>
/// 自定义日志提供程序，将日志转发到 ViewModel
/// </summary>
public class CustomLoggerProvider : ILoggerProvider
{
    private readonly MainWindowViewModel _viewModel;

    public CustomLoggerProvider(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new CustomLogger(categoryName, _viewModel);
    }

    public void Dispose() { }
}