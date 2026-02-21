using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using PDFTranslator.Core;
using ReactiveUI;

namespace PDFTranslator.GUI.ViewModels;

/// <summary>
/// Ollama 模型信息类，用于在 UI 中显示模型列表。
/// </summary>
public class OllamaModelInfo
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string DisplayName => $"{Name} ({(Size / 1024 / 1024):F1} MB)";
}

/// <summary>
/// GUI 进度报告器，实现 IProgressReporter 接口，将进度更新转发到 UI 线程。
/// </summary>
public class GuiProgressReporter : IProgressReporter
{
    private readonly Action<int, int, string?> _onProgress;
    private readonly Action<string?> _onComplete;

    public GuiProgressReporter(Action<int, int, string?> onProgress, Action<string?> onComplete)
    {
        _onProgress = onProgress;
        _onComplete = onComplete;
    }

    public void Report(int current, int total, string? message = null) =>
        _onProgress(current, total, message);

    public void Complete(string? message = null) =>
        _onComplete(message);
}

/// <summary>
/// 主窗口视图模型，负责所有用户交互逻辑、数据绑定和命令处理。
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private PdfTranslator? _translator;
    private TranslationOptions? _options;
    private ILogger<MainWindowViewModel>? _logger;
    private IStorageProvider? _storageProvider;

    // ==================== 自动输出文件名相关 ====================
    private bool _isOutputPathManuallySet;

    // ==================== Ollama 配置属性 ====================

    private string _ollamaUrl = "http://localhost:11434";
    public string OllamaUrl
    {
        get => _ollamaUrl;
        set => this.RaiseAndSetIfChanged(ref _ollamaUrl, value);
    }

    private string _ollamaModel = "llama3.2";
    public string OllamaModel
    {
        get => _ollamaModel;
        set
        {
            this.RaiseAndSetIfChanged(ref _ollamaModel, value);
            if (_options != null) _options.Model = value;
        }
    }

    private OllamaModelInfo? _selectedModel;
    public OllamaModelInfo? SelectedModel
    {
        get => _selectedModel;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedModel, value);
            if (value != null) OllamaModel = value.Name;
        }
    }

    private int _timeoutSeconds = 60;
    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => this.RaiseAndSetIfChanged(ref _timeoutSeconds, value);
    }

    private bool _isOllamaConnected;
    public bool IsOllamaConnected
    {
        get => _isOllamaConnected;
        private set => this.RaiseAndSetIfChanged(ref _isOllamaConnected, value);
    }

    private string _ollamaStatusMessage = "未连接";
    public string OllamaStatusMessage
    {
        get => _ollamaStatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _ollamaStatusMessage, value);
    }

    private ObservableCollection<OllamaModelInfo> _availableModels = new();
    public ObservableCollection<OllamaModelInfo> AvailableModels
    {
        get => _availableModels;
        private set => this.RaiseAndSetIfChanged(ref _availableModels, value);
    }

    private bool _isRefreshingModels;
    public bool IsRefreshingModels
    {
        get => _isRefreshingModels;
        private set => this.RaiseAndSetIfChanged(ref _isRefreshingModels, value);
    }

    private string _modelListStatus = string.Empty;
    public string ModelListStatus
    {
        get => _modelListStatus;
        private set => this.RaiseAndSetIfChanged(ref _modelListStatus, value);
    }

    // ==================== 语言选择属性 ====================

    public ObservableCollection<string> SourceLanguages { get; } = new()
        { "en", "zh", "ja", "ko", "fr", "de", "es", "ru", "ar" };

    public ObservableCollection<string> TargetLanguages { get; } = new()
        { "zh", "en", "ja", "ko", "fr", "de", "es", "ru", "ar" };

    private string _sourceLanguage = "en";
    public string SourceLanguage
    {
        get => _sourceLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _sourceLanguage, value);
            if (_options != null) _options.SourceLanguage = value;
        }
    }

    private string _targetLanguage = "zh";
    public string TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _targetLanguage, value);
            if (_options != null) _options.TargetLanguage = value;
        }
    }

    // ==================== 文件路径属性 ====================

    private string _inputPath = string.Empty;
    public string InputPath
    {
        get => _inputPath;
        set => this.RaiseAndSetIfChanged(ref _inputPath, value);
    }

    private string _outputPath = string.Empty;
    public string OutputPath
    {
        get => _outputPath;
        set => this.RaiseAndSetIfChanged(ref _outputPath, value);
    }

    // ==================== 翻译模式 ====================

    private bool _isBilingual;
    public bool IsBilingual
    {
        get => _isBilingual;
        set
        {
            this.RaiseAndSetIfChanged(ref _isBilingual, value);
            if (_options != null) _options.Mode = value ? TranslationMode.Bilingual : TranslationMode.Translate;
        }
    }

    private bool _translateImages;
    public bool TranslateImages
    {
        get => _translateImages;
        set
        {
            this.RaiseAndSetIfChanged(ref _translateImages, value);
            if (_options != null) _options.TranslateImages = value;
        }
    }

    // ==================== 字体配置 ====================

    private string _fontName = string.Empty;
    public string FontName
    {
        get => _fontName;
        set
        {
            this.RaiseAndSetIfChanged(ref _fontName, value);
            if (_options != null) _options.FontName = string.IsNullOrEmpty(value) ? null : value;
        }
    }

    private string _fontPath = string.Empty;
    public string FontPath
    {
        get => _fontPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _fontPath, value);
            if (_options != null) _options.FontPath = string.IsNullOrEmpty(value) ? null : value;
        }
    }

    // ==================== 页面范围属性 ====================

    private PageRangeMode _pageRangeMode = PageRangeMode.All;
    public PageRangeMode PageRangeMode
    {
        get => _pageRangeMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _pageRangeMode, value);
            if (_options != null) _options.PageRangeMode = value;
            
            // 当模式改变时，更新相关的布尔属性
            this.RaisePropertyChanged(nameof(IsAllPages));
            this.RaisePropertyChanged(nameof(IsRangePages));
            this.RaisePropertyChanged(nameof(IsSinglePage));
            this.RaisePropertyChanged(nameof(ShowRangeInput));
            this.RaisePropertyChanged(nameof(ShowSingleInput));
        }
    }

    private string _pageRange = string.Empty;
    public string PageRange
    {
        get => _pageRange;
        set
        {
            this.RaiseAndSetIfChanged(ref _pageRange, value);
            if (_options != null) _options.PageRange = value;
        }
    }

    private int _singlePage = 1;
    public int SinglePage
    {
        get => _singlePage;
        set
        {
            this.RaiseAndSetIfChanged(ref _singlePage, value);
            if (_options != null) _options.SinglePage = value;
        }
    }

    private int _totalPages;
    public int TotalPages
    {
        get => _totalPages;
        private set => this.RaiseAndSetIfChanged(ref _totalPages, value);
    }

    // ==================== 页面范围辅助属性（用于XAML绑定）====================
    public bool IsAllPages
    {
        get => PageRangeMode == PageRangeMode.All;
        set
        {
            if (value) PageRangeMode = PageRangeMode.All;
            this.RaisePropertyChanged();
        }
    }

    public bool IsRangePages
    {
        get => PageRangeMode == PageRangeMode.Range;
        set
        {
            if (value) PageRangeMode = PageRangeMode.Range;
            this.RaisePropertyChanged();
        }
    }

    public bool IsSinglePage
    {
        get => PageRangeMode == PageRangeMode.Single;
        set
        {
            if (value) PageRangeMode = PageRangeMode.Single;
            this.RaisePropertyChanged();
        }
    }

    public bool ShowRangeInput => PageRangeMode == PageRangeMode.Range;
    public bool ShowSingleInput => PageRangeMode == PageRangeMode.Single;

    // ==================== 日志和进度 ====================

    private string _log = string.Empty;
    public string Log
    {
        get => _log;
        private set => this.RaiseAndSetIfChanged(ref _log, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    private int _progressValue;
    public int ProgressValue
    {
        get => _progressValue;
        private set => this.RaiseAndSetIfChanged(ref _progressValue, value);
    }

    private int _progressMax = 100;
    public int ProgressMax
    {
        get => _progressMax;
        private set => this.RaiseAndSetIfChanged(ref _progressMax, value);
    }

    private bool _showProgress;
    public bool ShowProgress
    {
        get => _showProgress;
        private set => this.RaiseAndSetIfChanged(ref _showProgress, value);
    }

    private string _progressMessage = string.Empty;
    public string ProgressMessage
    {
        get => _progressMessage;
        private set => this.RaiseAndSetIfChanged(ref _progressMessage, value);
    }

    // ==================== 内存监控属性 ====================

    private string _memoryUsage = string.Empty;
    public string MemoryUsage
    {
        get => _memoryUsage;
        private set => this.RaiseAndSetIfChanged(ref _memoryUsage, value);
    }

    private bool _showMemoryWarning;
    public bool ShowMemoryWarning
    {
        get => _showMemoryWarning;
        private set => this.RaiseAndSetIfChanged(ref _showMemoryWarning, value);
    }

    private string _memoryWarningMessage = string.Empty;
    public string MemoryWarningMessage
    {
        get => _memoryWarningMessage;
        private set => this.RaiseAndSetIfChanged(ref _memoryWarningMessage, value);
    }

    // ==================== 命令 ====================

    public ReactiveCommand<Unit, Unit> SelectInputCommand { get; private set; } = ReactiveCommand.CreateFromTask(() => Task.CompletedTask);
    public ReactiveCommand<Unit, Unit> SelectOutputCommand { get; private set; } = ReactiveCommand.CreateFromTask(() => Task.CompletedTask);
    public ReactiveCommand<Unit, Unit> SelectFontCommand { get; private set; } = ReactiveCommand.CreateFromTask(() => Task.CompletedTask);
    public ReactiveCommand<Unit, Unit> StartCommand { get; private set; } = ReactiveCommand.CreateFromTask(() => Task.CompletedTask);
    public ReactiveCommand<Unit, Unit> TestOllamaConnectionCommand { get; private set; } = ReactiveCommand.CreateFromTask(() => Task.CompletedTask);
    public ReactiveCommand<Unit, Unit> RefreshModelsCommand { get; private set; } = ReactiveCommand.CreateFromTask(() => Task.CompletedTask);
    public ReactiveCommand<Unit, Unit> SaveOllamaConfigCommand { get; private set; } = ReactiveCommand.Create(() => { });

    // ==================== 定时器 ====================
    private IDisposable? _memoryTimer;

    // ==================== 构造函数 ====================

    /// <summary>
    /// 无参构造函数，用于 App.xaml.cs 中的早期创建
    /// </summary>
    public MainWindowViewModel()
    {
        InitializeCommands();
        
        // 从环境变量加载配置
        LoadOllamaConfigFromEnvironment();

        // 初始化进度条
        ProgressMax = 100;
        ShowProgress = false;

        // 初始化内存监控
        ShowMemoryWarning = false;
        StartMemoryMonitoring();

        // 监听输入文件变化和模式变化，自动建议输出路径
        this.WhenAnyValue(x => x.InputPath)
            .Where(path => !string.IsNullOrEmpty(path) && !_isOutputPathManuallySet)
            .Subscribe(_ => GenerateSuggestedOutputPath());

        this.WhenAnyValue(x => x.IsBilingual)
            .Where(_ => !_isOutputPathManuallySet && !string.IsNullOrEmpty(InputPath))
            .Subscribe(_ => GenerateSuggestedOutputPath());

        // 监听 OutputPath 的手动修改
        this.WhenAnyValue(x => x.OutputPath)
            .Skip(1)
            .Subscribe(_ => _isOutputPathManuallySet = true);

        // 自动测试 Ollama 连接
        Dispatcher.UIThread.Post(async () => await TestOllamaConnectionAsync());
    }

    /// <summary>
    /// 带参数的构造函数，用于依赖注入
    /// </summary>
    public MainWindowViewModel(
        PdfTranslator translator,
        TranslationOptions options,
        ILogger<MainWindowViewModel> logger) : this()
    {
        InitializeServices(translator, options, logger);
    }

    /// <summary>
    /// 初始化命令
    /// </summary>
    private void InitializeCommands()
    {
        SelectInputCommand = ReactiveCommand.CreateFromTask(SelectInputAsync);
        SelectOutputCommand = ReactiveCommand.CreateFromTask(SelectOutputAsync);
        SelectFontCommand = ReactiveCommand.CreateFromTask(SelectFontAsync);
        StartCommand = ReactiveCommand.CreateFromTask(StartTranslationAsync,
            this.WhenAnyValue(x => x.IsBusy, x => !x));

        TestOllamaConnectionCommand = ReactiveCommand.CreateFromTask(TestOllamaConnectionAsync);
        RefreshModelsCommand = ReactiveCommand.CreateFromTask(RefreshModelsAsync);
        SaveOllamaConfigCommand = ReactiveCommand.Create(SaveOllamaConfig);
    }

    /// <summary>
    /// 初始化服务（由 App.xaml.cs 在构建服务后调用）
    /// </summary>
    public void InitializeServices(PdfTranslator translator, TranslationOptions options, ILogger<MainWindowViewModel> logger)
    {
        _translator = translator;
        _options = options;
        _logger = logger;

        // 从 Core 配置同步到视图模型
        IsBilingual = _options.Mode == TranslationMode.Bilingual;
        TranslateImages = _options.TranslateImages;
        FontName = _options.FontName ?? string.Empty;
        FontPath = _options.FontPath ?? string.Empty;
        SourceLanguage = _options.SourceLanguage;
        TargetLanguage = _options.TargetLanguage;
        
        // 同步页面范围设置
        PageRangeMode = _options.PageRangeMode;
        PageRange = _options.PageRange ?? string.Empty;
        if (_options.SinglePage.HasValue)
            SinglePage = _options.SinglePage.Value;
    }

    // ==================== 日志方法 ====================

    /// <summary>
    /// 添加日志消息（供外部调用，自动在 UI 线程执行）
    /// </summary>
    /// <param name="message">要添加的日志消息</param>
    public void AddLogMessage(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Log += message;
        });
    }

    // ==================== 内存监控方法 ====================

    private void StartMemoryMonitoring()
    {
        _memoryTimer = Observable.Interval(TimeSpan.FromSeconds(2))
            .Subscribe(_ => UpdateMemoryUsage());
    }

    private void UpdateMemoryUsage()
    {
        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64 / 1024 / 1024;
            var privateMemory = process.PrivateMemorySize64 / 1024 / 1024;
            var managedMemory = GC.GetTotalMemory(false) / 1024 / 1024;

            var memoryText = $"内存: 工作集 {workingSet}MB | 私有 {privateMemory}MB | 托管 {managedMemory}MB";
            
            Dispatcher.UIThread.Post(() =>
            {
                MemoryUsage = memoryText;
                CheckMemoryWarning(workingSet, privateMemory, managedMemory);
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "更新内存使用信息失败");
        }
    }

    private void CheckMemoryWarning(long workingSet, long privateMemory, long managedMemory)
    {
        const long WARNING_THRESHOLD_MB = 1024;
        const long CRITICAL_THRESHOLD_MB = 2048;

        if (workingSet > CRITICAL_THRESHOLD_MB || privateMemory > CRITICAL_THRESHOLD_MB)
        {
            ShowMemoryWarning = true;
            MemoryWarningMessage = $"⚠️ 内存使用过高！当前 {workingSet}MB，建议关闭其他程序或分批处理文档。";
        }
        else if (workingSet > WARNING_THRESHOLD_MB || privateMemory > WARNING_THRESHOLD_MB)
        {
            ShowMemoryWarning = true;
            MemoryWarningMessage = $"⚠️ 内存使用较高 ({workingSet}MB)，如果出现卡顿，建议分批处理文档。";
        }
        else
        {
            ShowMemoryWarning = false;
        }
    }

    public void Cleanup()
    {
        _memoryTimer?.Dispose();
    }

    // ==================== 辅助方法 ====================

    private void LoadOllamaConfigFromEnvironment()
    {
        string? host = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (!string.IsNullOrEmpty(host))
        {
            if (!host.StartsWith("http://") && !host.StartsWith("https://"))
                OllamaUrl = $"http://{host}";
            else
                OllamaUrl = host;
        }

        string? model = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
        if (!string.IsNullOrEmpty(model))
            OllamaModel = model;
    }

    public void SetStorageProvider(IStorageProvider storageProvider) =>
        _storageProvider = storageProvider;

    private bool CheckStorageProvider()
    {
        if (_storageProvider == null)
        {
            Log += "错误：无法访问文件系统。请确保窗口已完全加载，或重启应用。\n";
            return false;
        }
        return true;
    }

    private async Task<int> GetPdfPageCount(string filePath)
    {
        try
        {
            using var pdf = new iText.Kernel.Pdf.PdfDocument(new iText.Kernel.Pdf.PdfReader(filePath));
            return pdf.GetNumberOfPages();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "获取 PDF 页数失败");
            return 0;
        }
    }

    private void GenerateSuggestedOutputPath()
    {
        if (string.IsNullOrEmpty(InputPath))
            return;

        try
        {
            string dir = Path.GetDirectoryName(InputPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(InputPath);
            string ext = Path.GetExtension(InputPath);
            string suffix = IsBilingual ? "_bilingual" : "_translated";
            string suggestedPath = Path.Combine(dir, $"{fileNameWithoutExt}{suffix}{ext}");
            
            if (OutputPath != suggestedPath)
            {
                OutputPath = suggestedPath;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "生成建议输出路径时出错");
        }
    }

    // ==================== Ollama 相关方法 ====================

    private async Task TestOllamaConnectionAsync()
    {
        IsOllamaConnected = false;
        OllamaStatusMessage = "正在连接...";
        try
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri(OllamaUrl);
            client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
            var response = await client.GetAsync("api/tags");
            if (response.IsSuccessStatusCode)
            {
                IsOllamaConnected = true;
                OllamaStatusMessage = "已连接";
                Log += $"✓ Ollama 连接成功 ({OllamaUrl})\n";
                await RefreshModelsAsync();
            }
            else
            {
                OllamaStatusMessage = $"连接失败 (HTTP {(int)response.StatusCode})";
                Log += $"✗ Ollama 连接失败: HTTP {response.StatusCode}\n";
            }
        }
        catch (Exception ex)
        {
            OllamaStatusMessage = $"连接错误: {ex.Message}";
            Log += $"✗ Ollama 连接错误: {ex.Message}\n";
        }
    }

    private async Task RefreshModelsAsync()
    {
        if (!IsOllamaConnected)
        {
            ModelListStatus = "请先连接 Ollama";
            return;
        }

        IsRefreshingModels = true;
        ModelListStatus = "正在获取模型列表...";
        try
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri(OllamaUrl);
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync("api/tags");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("models", out var modelsArray))
                {
                    var models = new ObservableCollection<OllamaModelInfo>();
                    foreach (var model in modelsArray.EnumerateArray())
                    {
                        var name = model.GetProperty("name").GetString() ?? "未知";
                        long size = 0;
                        if (model.TryGetProperty("size", out var sizeEl))
                            size = sizeEl.GetInt64();
                        models.Add(new OllamaModelInfo { Name = name, Size = size });
                    }
                    AvailableModels = models;

                    if (models.Count > 0)
                    {
                        ModelListStatus = $"找到 {models.Count} 个模型";
                        Log += $"✓ 已获取模型列表: {models.Count} 个模型可用\n";

                        if (!models.Any(m => m.Name == OllamaModel))
                        {
                            SelectedModel = models[0];
                            Log += $"自动选择模型: {OllamaModel}\n";
                        }
                        else
                        {
                            SelectedModel = models.FirstOrDefault(m => m.Name == OllamaModel);
                        }
                    }
                    else
                    {
                        ModelListStatus = "没有找到任何模型";
                        Log += "⚠️ 没有找到任何模型，请先通过 ollama pull 下载模型\n";
                    }
                }
                else
                {
                    ModelListStatus = "API 返回格式异常";
                }
            }
            else
            {
                ModelListStatus = $"获取失败 (HTTP {response.StatusCode})";
            }
        }
        catch (Exception ex)
        {
            ModelListStatus = $"错误: {ex.Message}";
            Log += $"✗ 获取模型列表错误: {ex.Message}\n";
        }
        finally
        {
            IsRefreshingModels = false;
        }
    }

    private void SaveOllamaConfig()
    {
        Log += $"Ollama 配置已更新:\n  URL: {OllamaUrl}\n  模型: {OllamaModel}\n  超时: {TimeoutSeconds}秒\n";
    }

    // ==================== 文件选择方法 ====================

    private async Task SelectInputAsync()
    {
        if (!CheckStorageProvider()) return;

        var files = await _storageProvider!.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择输入 PDF 文件",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("PDF 文件") { Patterns = new[] { "*.pdf" } } }
        });

        if (files.Count == 1)
        {
            InputPath = files[0].Path.LocalPath;
            TotalPages = await GetPdfPageCount(InputPath);
            Log += $"已选择输入文件: {InputPath} (共 {TotalPages} 页)\n";
            _isOutputPathManuallySet = false;
            GenerateSuggestedOutputPath();
        }
    }

    private async Task SelectOutputAsync()
    {
        if (!CheckStorageProvider()) return;

        var file = await _storageProvider!.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "选择输出 PDF 文件",
            DefaultExtension = "pdf",
            FileTypeChoices = new[] { new FilePickerFileType("PDF 文件") { Patterns = new[] { "*.pdf" } } },
            SuggestedFileName = !string.IsNullOrEmpty(OutputPath) 
                ? Path.GetFileName(OutputPath) 
                : "output.pdf"
        });

        if (file != null)
        {
            OutputPath = file.Path.LocalPath;
            _isOutputPathManuallySet = true;
            Log += $"已选择输出文件: {OutputPath}\n";
        }
    }

    private async Task SelectFontAsync()
    {
        if (!CheckStorageProvider()) return;

        var files = await _storageProvider!.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择字体文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("字体文件") { Patterns = new[] { "*.ttf", "*.ttc", "*.otf" } }
            }
        });

        if (files.Count == 1)
        {
            FontPath = files[0].Path.LocalPath;
            Log += $"已选择字体文件: {FontPath}\n";
        }
    }

    // ==================== 翻译核心方法 ====================

    private async Task StartTranslationAsync()
    {
        if (!CheckStorageProvider()) return;

        if (string.IsNullOrEmpty(InputPath) || string.IsNullOrEmpty(OutputPath))
        {
            Log += "请先选择输入和输出文件。\n";
            return;
        }

        if (_translator == null)
        {
            Log += "错误：翻译器未初始化。\n";
            return;
        }

        if (!IsOllamaConnected)
        {
            Log += "警告: Ollama 未连接，尝试重新连接...\n";
            await TestOllamaConnectionAsync();
            if (!IsOllamaConnected)
            {
                Log += "Ollama 连接失败，无法继续翻译\n";
                return;
            }
        }

        // 翻译前检查内存使用
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var memoryBefore = process.WorkingSet64 / 1024 / 1024;
        Log += $"翻译前内存使用: {memoryBefore}MB\n";

        IsBusy = true;
        ShowProgress = true;
        ProgressValue = 0;
        ProgressMessage = "准备中...";

        Log += "开始翻译...\n";
        Log += $"Ollama URL: {OllamaUrl}\n";
        Log += $"模型: {OllamaModel}\n";
        Log += $"语言: {SourceLanguage} → {TargetLanguage}\n";
        Log += $"模式: {(IsBilingual ? "双语对照" : "仅译文")}\n";
        
        // 显示页面范围信息
        if (PageRangeMode == PageRangeMode.Range && !string.IsNullOrEmpty(PageRange))
            Log += $"页码范围: {PageRange}\n";
        else if (PageRangeMode == PageRangeMode.Single)
            Log += $"单页: {SinglePage}\n";
        else
            Log += $"页码范围: 全部 (共 {TotalPages} 页)\n";
        
        if (!string.IsNullOrEmpty(FontName)) Log += $"字体名称: {FontName}\n";
        if (!string.IsNullOrEmpty(FontPath)) Log += $"字体路径: {FontPath}\n";

        // 同步所有配置到 Core
        if (_options != null)
        {
            _options.Model = OllamaModel;
            _options.SourceLanguage = SourceLanguage;
            _options.TargetLanguage = TargetLanguage;
            _options.PageRangeMode = PageRangeMode;
            _options.PageRange = PageRange;
            _options.SinglePage = SinglePage;
        }

        var reporter = new GuiProgressReporter(
            onProgress: (current, total, msg) => Dispatcher.UIThread.Post(() =>
            {
                ProgressMax = total;
                ProgressValue = current;
                ProgressMessage = msg ?? $"正在处理第 {current} 页...";
            }),
            onComplete: msg => Dispatcher.UIThread.Post(() =>
            {
                ShowProgress = false;
                if (!string.IsNullOrEmpty(msg)) Log += msg + "\n";
                
                // 翻译后记录内存使用
                var memoryAfter = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;
                Log += $"翻译后内存使用: {memoryAfter}MB\n";
            }));

        _translator.SetProgressReporter(reporter);

        try
        {
            await _translator.TranslatePdfAsync(InputPath, OutputPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "翻译失败");
            Log += $"错误: {ex.Message}\n";
            ShowProgress = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 析构函数，清理资源
    /// </summary>
    ~MainWindowViewModel()
    {
        Cleanup();
    }
}