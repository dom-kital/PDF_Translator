using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;                // 提供 IStorageProvider 用于文件对话框
using Microsoft.Extensions.Logging;              // 日志
using PDFTranslator.Core;                         // 核心翻译服务
using ReactiveUI;                                 // ReactiveUI MVVM 框架

namespace PDFTranslator.GUI.ViewModels;

/// <summary>
/// 主窗口的视图模型，负责处理用户交互和业务逻辑
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    // ---------- 只读字段（在构造函数中初始化，不可再修改） ----------
    private readonly PdfTranslator _translator;          // PDF 翻译器
    private readonly TranslationOptions _options;        // 翻译配置选项
    private readonly ILogger<MainWindowViewModel> _logger; // 日志记录器

    // ---------- 可变字段（可在方法中修改） ----------
    private IStorageProvider? _storageProvider;   // 文件存储提供程序（由视图设置，用于打开/保存文件对话框）

    // ---------- 可绑定属性 ----------
    private string _inputPath = string.Empty;
    /// <summary>
    /// 输入 PDF 文件路径
    /// </summary>
    public string InputPath
    {
        get => _inputPath;
        set => this.RaiseAndSetIfChanged(ref _inputPath, value);
    }

    private string _outputPath = string.Empty;
    /// <summary>
    /// 输出 PDF 文件路径
    /// </summary>
    public string OutputPath
    {
        get => _outputPath;
        set => this.RaiseAndSetIfChanged(ref _outputPath, value);
    }

    private bool _isBilingual;
    /// <summary>
    /// 是否为双语对照模式（true=双语，false=仅译文）
    /// </summary>
    public bool IsBilingual
    {
        get => _isBilingual;
        set
        {
            this.RaiseAndSetIfChanged(ref _isBilingual, value);
            _options.Mode = value ? TranslationMode.Bilingual : TranslationMode.Translate; // 同步到配置
        }
    }

    private bool _translateImages;
    /// <summary>
    /// 是否翻译图片中的文字（预留功能，暂未实现）
    /// </summary>
    public bool TranslateImages
    {
        get => _translateImages;
        set
        {
            this.RaiseAndSetIfChanged(ref _translateImages, value);
            _options.TranslateImages = value; // 同步到配置
        }
    }

    private string _log = string.Empty;
    /// <summary>
    /// 日志文本，用于显示在界面上
    /// </summary>
    public string Log
    {
        get => _log;
        private set => this.RaiseAndSetIfChanged(ref _log, value);
    }

    private bool _isBusy;
    /// <summary>
    /// 是否正在处理中（用于禁用按钮和显示忙状态）
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    // ---------- 命令 ----------
    /// <summary>
    /// 选择输入文件的命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> SelectInputCommand { get; }

    /// <summary>
    /// 选择输出文件的命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> SelectOutputCommand { get; }

    /// <summary>
    /// 开始翻译的命令
    /// </summary>
    public ReactiveCommand<Unit, Unit> StartCommand { get; }

    /// <summary>
    /// 构造函数，通过依赖注入获取所需服务
    /// </summary>
    public MainWindowViewModel(PdfTranslator translator, TranslationOptions options, ILogger<MainWindowViewModel> logger)
    {
        _translator = translator;
        _options = options;
        _logger = logger;

        // 初始化命令
        SelectInputCommand = ReactiveCommand.CreateFromTask(SelectInputAsync);
        SelectOutputCommand = ReactiveCommand.CreateFromTask(SelectOutputAsync);
        // 开始翻译命令：当不处于忙碌状态时可用
        StartCommand = ReactiveCommand.CreateFromTask(StartTranslationAsync,
            this.WhenAnyValue(x => x.IsBusy, x => !x));

        // 设置默认值
        IsBilingual = false;          // 默认仅译文模式
        TranslateImages = false;       // 默认不翻译图片
    }

    /// <summary>
    /// 设置存储提供程序（由视图层在窗口加载后调用）
    /// </summary>
    /// <param name="storageProvider">Avalonia 的 IStorageProvider 实例</param>
    public void SetStorageProvider(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider;
    }

    /// <summary>
    /// 选择输入文件
    /// </summary>
    private async Task SelectInputAsync()
    {
        if (_storageProvider == null)
        {
            Log += "错误：无法访问文件系统，请重启应用。\n";
            return;
        }

        // 打开文件选择对话框，只允许选择 PDF 文件
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

    /// <summary>
    /// 选择输出文件
    /// </summary>
    private async Task SelectOutputAsync()
    {
        if (_storageProvider == null)
        {
            Log += "错误：无法访问文件系统，请重启应用。\n";
            return;
        }

        // 打开保存文件对话框，默认扩展名为 .pdf
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

    /// <summary>
    /// 开始翻译
    /// </summary>
    private async Task StartTranslationAsync()
    {
        // 检查输入输出路径是否已选择
        if (string.IsNullOrEmpty(InputPath) || string.IsNullOrEmpty(OutputPath))
        {
            Log += "请先选择输入和输出文件。\n";
            return;
        }

        IsBusy = true;
        Log += "开始翻译...\n";
        Log += $"模式: {(IsBilingual ? "双语对照" : "仅译文")}\n";
        Log += $"翻译图片: {(TranslateImages ? "是" : "否")}\n";

        try
        {
            // 调用核心翻译服务
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