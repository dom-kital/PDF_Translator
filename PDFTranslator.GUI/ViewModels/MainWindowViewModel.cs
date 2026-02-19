using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using PDFTranslator.Core;
using ReactiveUI;

namespace PDFTranslator.GUI.ViewModels;

/// <summary>
/// 主窗口视图模型，处理用户交互和业务逻辑。
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private readonly PdfTranslator _translator;
    private readonly TranslationOptions _options;
    private readonly ILogger<MainWindowViewModel> _logger;
    private IStorageProvider? _storageProvider;

    // ---------- 可绑定属性 ----------
    private string _inputPath = string.Empty;
    /// <summary>输入 PDF 文件路径</summary>
    public string InputPath
    {
        get => _inputPath;
        set => this.RaiseAndSetIfChanged(ref _inputPath, value);
    }

    private string _outputPath = string.Empty;
    /// <summary>输出 PDF 文件路径</summary>
    public string OutputPath
    {
        get => _outputPath;
        set => this.RaiseAndSetIfChanged(ref _outputPath, value);
    }

    private bool _isBilingual;
    /// <summary>是否为双语对照模式</summary>
    public bool IsBilingual
    {
        get => _isBilingual;
        set
        {
            this.RaiseAndSetIfChanged(ref _isBilingual, value);
            _options.Mode = value ? TranslationMode.Bilingual : TranslationMode.Translate;
        }
    }

    private bool _translateImages;
    /// <summary>是否翻译图片中的文字（预留功能）</summary>
    public bool TranslateImages
    {
        get => _translateImages;
        set
        {
            this.RaiseAndSetIfChanged(ref _translateImages, value);
            _options.TranslateImages = value;
        }
    }

    private string _fontName = string.Empty;
    /// <summary>用户指定的字体名称（如 SimSun）</summary>
    public string FontName
    {
        get => _fontName;
        set
        {
            this.RaiseAndSetIfChanged(ref _fontName, value);
            _options.FontName = string.IsNullOrEmpty(value) ? null : value;
        }
    }

    private string _fontPath = string.Empty;
    /// <summary>用户指定的字体文件路径</summary>
    public string FontPath
    {
        get => _fontPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _fontPath, value);
            _options.FontPath = string.IsNullOrEmpty(value) ? null : value;
        }
    }

    private string _log = string.Empty;
    /// <summary>日志文本，用于显示在界面上</summary>
    public string Log
    {
        get => _log;
        private set => this.RaiseAndSetIfChanged(ref _log, value);
    }

    private bool _isBusy;
    /// <summary>是否正在处理中（用于禁用按钮）</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    // ---------- 命令 ----------
    public ReactiveCommand<Unit, Unit> SelectInputCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectOutputCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectFontCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCommand { get; }

    public MainWindowViewModel(PdfTranslator translator, TranslationOptions options, ILogger<MainWindowViewModel> logger)
    {
        _translator = translator;
        _options = options;
        _logger = logger;

        // 初始化命令
        SelectInputCommand = ReactiveCommand.CreateFromTask(SelectInputAsync);
        SelectOutputCommand = ReactiveCommand.CreateFromTask(SelectOutputAsync);
        SelectFontCommand = ReactiveCommand.CreateFromTask(SelectFontAsync);
        StartCommand = ReactiveCommand.CreateFromTask(StartTranslationAsync,
            this.WhenAnyValue(x => x.IsBusy, x => !x));

        // 从配置中初始化属性值
        IsBilingual = _options.Mode == TranslationMode.Bilingual;
        TranslateImages = _options.TranslateImages;
        FontName = _options.FontName ?? string.Empty;
        FontPath = _options.FontPath ?? string.Empty;
    }

    /// <summary>
    /// 设置存储提供程序（由视图层在窗口加载后调用）。
    /// </summary>
    public void SetStorageProvider(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider;
    }

    private async Task SelectInputAsync()
    {
        if (_storageProvider == null)
        {
            Log += "错误：无法访问文件系统，请重启应用。\n";
            return;
        }

        var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择输入 PDF 文件",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("PDF 文件") { Patterns = new[] { "*.pdf" } } }
        });

        if (files.Count == 1)
        {
            InputPath = files[0].Path.LocalPath;
            Log += $"已选择输入文件: {InputPath}\n";
        }
    }

    private async Task SelectOutputAsync()
    {
        if (_storageProvider == null)
        {
            Log += "错误：无法访问文件系统，请重启应用。\n";
            return;
        }

        var file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "选择输出 PDF 文件",
            DefaultExtension = "pdf",
            FileTypeChoices = new[] { new FilePickerFileType("PDF 文件") { Patterns = new[] { "*.pdf" } } }
        });

        if (file != null)
        {
            OutputPath = file.Path.LocalPath;
            Log += $"已选择输出文件: {OutputPath}\n";
        }
    }

    private async Task SelectFontAsync()
    {
        if (_storageProvider == null)
        {
            Log += "错误：无法访问文件系统，请重启应用。\n";
            return;
        }

        var files = await _storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择字体文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("字体文件")
                {
                    Patterns = new[] { "*.ttf", "*.ttc", "*.otf" }
                }
            }
        });

        if (files.Count == 1)
        {
            FontPath = files[0].Path.LocalPath;
            Log += $"已选择字体文件: {FontPath}\n";
        }
    }

    private async Task StartTranslationAsync()
    {
        if (string.IsNullOrEmpty(InputPath) || string.IsNullOrEmpty(OutputPath))
        {
            Log += "请先选择输入和输出文件。\n";
            return;
        }

        IsBusy = true;
        Log += "开始翻译...\n";
        Log += $"模式: {(IsBilingual ? "双语对照" : "仅译文")}\n";
        Log += $"翻译图片: {(TranslateImages ? "是" : "否")}\n";
        if (!string.IsNullOrEmpty(FontName))
            Log += $"字体名称: {FontName}\n";
        if (!string.IsNullOrEmpty(FontPath))
            Log += $"字体路径: {FontPath}\n";

        try
        {
            await _translator.TranslatePdfAsync(InputPath, OutputPath);
            Log += "翻译完成！\n";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻译失败");
            Log += $"错误: {ex.Message}\n";
        }
        finally
        {
            IsBusy = false;
        }
    }
}