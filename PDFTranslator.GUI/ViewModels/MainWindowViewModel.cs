using System;
using System.IO; // 提供 Path 类
using System.Collections.ObjectModel;
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
    /// <summary>模型名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>模型大小（字节）</summary>
    public long Size { get; set; }

    /// <summary>模型修改时间</summary>
    public DateTime ModifiedAt { get; set; }

    /// <summary>显示名称（格式：名称 (大小MB)）</summary>
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
    private readonly PdfTranslator _translator;          // PDF 翻译核心
    private readonly TranslationOptions _options;        // 翻译配置（与 Core 共享）
    private readonly ILogger<MainWindowViewModel> _logger; // 日志记录器
    private IStorageProvider? _storageProvider;          // 文件存储提供程序（由视图设置）

    // ==================== 自动输出文件名相关 ====================
    private bool _isOutputPathManuallySet; // 记录用户是否手动修改过输出路径

    // ==================== Ollama 配置属性 ====================

    private string _ollamaUrl = "http://localhost:11434";
    /// <summary>Ollama API 地址</summary>
    public string OllamaUrl
    {
        get => _ollamaUrl;
        set => this.RaiseAndSetIfChanged(ref _ollamaUrl, value);
    }

    private string _ollamaModel = "llama3.2";
    /// <summary>当前选中的 Ollama 模型名称</summary>
    public string OllamaModel
    {
        get => _ollamaModel;
        set
        {
            this.RaiseAndSetIfChanged(ref _ollamaModel, value);
            _options.Model = value; // 同步到核心配置
        }
    }

    private OllamaModelInfo? _selectedModel;
    /// <summary>下拉菜单选中的模型对象</summary>
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
    /// <summary>请求超时时间（秒）</summary>
    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => this.RaiseAndSetIfChanged(ref _timeoutSeconds, value);
    }

    private bool _isOllamaConnected;
    /// <summary>Ollama 服务连接状态</summary>
    public bool IsOllamaConnected
    {
        get => _isOllamaConnected;
        private set => this.RaiseAndSetIfChanged(ref _isOllamaConnected, value);
    }

    private string _ollamaStatusMessage = "未连接";
    /// <summary>连接状态详细文本</summary>
    public string OllamaStatusMessage
    {
        get => _ollamaStatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _ollamaStatusMessage, value);
    }

    private ObservableCollection<OllamaModelInfo> _availableModels = new();
    /// <summary>可用模型列表（用于下拉菜单）</summary>
    public ObservableCollection<OllamaModelInfo> AvailableModels
    {
        get => _availableModels;
        private set => this.RaiseAndSetIfChanged(ref _availableModels, value);
    }

    private bool _isRefreshingModels;
    /// <summary>是否正在刷新模型列表</summary>
    public bool IsRefreshingModels
    {
        get => _isRefreshingModels;
        private set => this.RaiseAndSetIfChanged(ref _isRefreshingModels, value);
    }

    private string _modelListStatus = string.Empty;
    /// <summary>模型列表状态信息（如“找到 5 个模型”）</summary>
    public string ModelListStatus
    {
        get => _modelListStatus;
        private set => this.RaiseAndSetIfChanged(ref _modelListStatus, value);
    }

    // ==================== 语言选择属性 ====================

    /// <summary>源语言可选列表</summary>
    public ObservableCollection<string> SourceLanguages { get; } = new()
        { "en", "zh", "ja", "ko", "fr", "de", "es", "ru", "ar" };

    /// <summary>目标语言可选列表</summary>
    public ObservableCollection<string> TargetLanguages { get; } = new()
        { "zh", "en", "ja", "ko", "fr", "de", "es", "ru", "ar" };

    private string _sourceLanguage = "en";
    /// <summary>源语言代码</summary>
    public string SourceLanguage
    {
        get => _sourceLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _sourceLanguage, value);
            _options.SourceLanguage = value;
        }
    }

    private string _targetLanguage = "zh";
    /// <summary>目标语言代码</summary>
    public string TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _targetLanguage, value);
            _options.TargetLanguage = value;
        }
    }

    // ==================== 文件路径属性 ====================

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

    // ==================== 翻译模式 ====================

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

    // ==================== 字体配置 ====================

    private string _fontName = string.Empty;
    /// <summary>用户指定的字体名称</summary>
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

    // ==================== 日志和进度 ====================

    private string _log = string.Empty;
    /// <summary>日志文本（显示在界面下方）</summary>
    public string Log
    {
        get => _log;
        private set => this.RaiseAndSetIfChanged(ref _log, value);
    }

    private bool _isBusy;
    /// <summary>是否正在处理中（用于禁用开始按钮）</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    private int _progressValue;
    /// <summary>当前进度值（已处理页数）</summary>
    public int ProgressValue
    {
        get => _progressValue;
        private set => this.RaiseAndSetIfChanged(ref _progressValue, value);
    }

    private int _progressMax = 100;
    /// <summary>进度最大值（总页数）</summary>
    public int ProgressMax
    {
        get => _progressMax;
        private set => this.RaiseAndSetIfChanged(ref _progressMax, value);
    }

    private bool _showProgress;
    /// <summary>是否显示进度条</summary>
    public bool ShowProgress
    {
        get => _showProgress;
        private set => this.RaiseAndSetIfChanged(ref _showProgress, value);
    }

    private string _progressMessage = string.Empty;
    /// <summary>进度状态信息（如“正在处理第 3 页”）</summary>
    public string ProgressMessage
    {
        get => _progressMessage;
        private set => this.RaiseAndSetIfChanged(ref _progressMessage, value);
    }

    // ==================== 命令 ====================

    /// <summary>选择输入文件的命令</summary>
    public ReactiveCommand<Unit, Unit> SelectInputCommand { get; }

    /// <summary>选择输出文件的命令</summary>
    public ReactiveCommand<Unit, Unit> SelectOutputCommand { get; }

    /// <summary>选择字体文件的命令</summary>
    public ReactiveCommand<Unit, Unit> SelectFontCommand { get; }

    /// <summary>开始翻译的命令</summary>
    public ReactiveCommand<Unit, Unit> StartCommand { get; }

    /// <summary>测试 Ollama 连接的命令</summary>
    public ReactiveCommand<Unit, Unit> TestOllamaConnectionCommand { get; }

    /// <summary>刷新模型列表的命令</summary>
    public ReactiveCommand<Unit, Unit> RefreshModelsCommand { get; }

    /// <summary>保存 Ollama 配置的命令（仅记录日志）</summary>
    public ReactiveCommand<Unit, Unit> SaveOllamaConfigCommand { get; }

    /// <summary>
    /// 构造函数，通过依赖注入获取所需服务。
    /// </summary>
    public MainWindowViewModel(
        PdfTranslator translator,
        TranslationOptions options,
        ILogger<MainWindowViewModel> logger)
    {
        _translator = translator;
        _options = options;
        _logger = logger;

        // ---------- 初始化命令 ----------
        SelectInputCommand = ReactiveCommand.CreateFromTask(SelectInputAsync);
        SelectOutputCommand = ReactiveCommand.CreateFromTask(SelectOutputAsync);
        SelectFontCommand = ReactiveCommand.CreateFromTask(SelectFontAsync);
        StartCommand = ReactiveCommand.CreateFromTask(StartTranslationAsync,
            this.WhenAnyValue(x => x.IsBusy, x => !x));

        TestOllamaConnectionCommand = ReactiveCommand.CreateFromTask(TestOllamaConnectionAsync);
        RefreshModelsCommand = ReactiveCommand.CreateFromTask(RefreshModelsAsync);
        SaveOllamaConfigCommand = ReactiveCommand.Create(SaveOllamaConfig);

        // ---------- 从环境变量加载初始配置（可选）----------
        LoadOllamaConfigFromEnvironment();

        // ---------- 从 Core 配置同步到视图模型 ----------
        IsBilingual = _options.Mode == TranslationMode.Bilingual;
        TranslateImages = _options.TranslateImages;
        FontName = _options.FontName ?? string.Empty;
        FontPath = _options.FontPath ?? string.Empty;
        SourceLanguage = _options.SourceLanguage;
        TargetLanguage = _options.TargetLanguage;

        // ---------- 初始化进度条 ----------
        ProgressMax = 100;
        ShowProgress = false;

        // ---------- 监听输入文件变化和模式变化，自动建议输出路径 ----------
        // 当 InputPath 变化且用户未手动设置输出路径时，生成建议路径
        this.WhenAnyValue(x => x.InputPath)
            .Where(path => !string.IsNullOrEmpty(path) && !_isOutputPathManuallySet)
            .Subscribe(_ => GenerateSuggestedOutputPath());

        // 当 IsBilingual 变化且用户未手动设置输出路径且已有输入文件时，更新建议路径
        this.WhenAnyValue(x => x.IsBilingual)
            .Where(_ => !_isOutputPathManuallySet && !string.IsNullOrEmpty(InputPath))
            .Subscribe(_ => GenerateSuggestedOutputPath());

        // 监听 OutputPath 的手动修改
        // Skip(1) 跳过初始值，避免在初始化时触发
        this.WhenAnyValue(x => x.OutputPath)
            .Skip(1)
            .Subscribe(_ => _isOutputPathManuallySet = true);

        // ---------- 自动测试 Ollama 连接 ----------
        Dispatcher.UIThread.Post(async () => await TestOllamaConnectionAsync());
    }

    /// <summary>
    /// 从环境变量 OLLAMA_HOST 和 OLLAMA_MODEL 加载默认配置。
    /// </summary>
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

    /// <summary>
    /// 由视图调用，设置文件存储提供程序（用于文件对话框）。
    /// </summary>
    public void SetStorageProvider(IStorageProvider storageProvider) =>
        _storageProvider = storageProvider;

    /// <summary>
    /// 检查存储提供程序是否可用，若不可用则在日志中记录错误。
    /// </summary>
    private bool CheckStorageProvider()
    {
        if (_storageProvider == null)
        {
            Log += "错误：无法访问文件系统。请确保窗口已完全加载，或重启应用。\n";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 根据输入文件路径和当前翻译模式生成建议的输出文件路径。
    /// </summary>
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
            
            // 只有当建议路径与当前路径不同时才更新，避免触发额外的事件
            if (OutputPath != suggestedPath)
            {
                OutputPath = suggestedPath;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "生成建议输出路径时出错");
            // 不阻塞用户体验，只是记录警告
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
            // 重置手动标记，因为选择了全新的输入文件
            _isOutputPathManuallySet = false;
            GenerateSuggestedOutputPath(); // 立即生成建议路径
            Log += $"已选择输入文件: {InputPath}\n";
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
            SuggestedFileName = Path.GetFileName(OutputPath) // 使用当前输出路径的文件名作为建议
        });

        if (file != null)
        {
            OutputPath = file.Path.LocalPath;
            // 用户通过浏览选择了输出文件，视为手动设置
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

        IsBusy = true;
        ShowProgress = true;
        ProgressValue = 0;
        ProgressMessage = "准备中...";

        Log += "开始翻译...\n";
        Log += $"Ollama URL: {OllamaUrl}\n";
        Log += $"模型: {OllamaModel}\n";
        Log += $"语言: {SourceLanguage} → {TargetLanguage}\n";
        Log += $"模式: {(IsBilingual ? "双语对照" : "仅译文")}\n";
        if (!string.IsNullOrEmpty(FontName)) Log += $"字体名称: {FontName}\n";
        if (!string.IsNullOrEmpty(FontPath)) Log += $"字体路径: {FontPath}\n";

        _options.Model = OllamaModel;
        _options.SourceLanguage = SourceLanguage;
        _options.TargetLanguage = TargetLanguage;

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
            }));

        _translator.SetProgressReporter(reporter);

        try
        {
            await _translator.TranslatePdfAsync(InputPath, OutputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻译失败");
            Log += $"错误: {ex.Message}\n";
            ShowProgress = false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}