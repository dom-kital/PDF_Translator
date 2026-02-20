using System;
using System.Collections.ObjectModel;
using System.Linq; // 添加此行以解决 Any() 方法未找到的问题
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
/// Ollama 模型信息类
/// 用于存储从 API 获取的模型信息
/// </summary>
public class OllamaModelInfo
{
    /// <summary>
    /// 模型名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 模型大小（字节）
    /// </summary>
    public long Size { get; set; }
    
    /// <summary>
    /// 模型修改时间
    /// </summary>
    public DateTime ModifiedAt { get; set; }
    
    /// <summary>
    /// 用于下拉菜单显示的文本
    /// </summary>
    public string DisplayName => $"{Name} ({(Size / 1024 / 1024):F1} MB)";
}

/// <summary>
/// GUI 进度报告器
/// 实现 IProgressReporter 接口，通过回调函数将进度更新转发到 UI 线程
/// </summary>
public class GuiProgressReporter : IProgressReporter
{
    private readonly Action<int, int, string?> _onProgress;
    private readonly Action<string?> _onComplete;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="onProgress">进度更新回调，参数为：当前值、最大值、状态消息</param>
    /// <param name="onComplete">完成回调，参数为完成消息</param>
    public GuiProgressReporter(Action<int, int, string?> onProgress, Action<string?> onComplete)
    {
        _onProgress = onProgress;
        _onComplete = onComplete;
    }

    /// <summary>
    /// 报告当前进度（由 PdfTranslator 调用）
    /// </summary>
    public void Report(int current, int total, string? message = null)
    {
        _onProgress(current, total, message);
    }

    /// <summary>
    /// 报告完成状态（由 PdfTranslator 调用）
    /// </summary>
    public void Complete(string? message = null)
    {
        _onComplete(message);
    }
}

/// <summary>
/// 主窗口视图模型
/// 负责处理用户界面交互、命令绑定、进度显示等所有业务逻辑
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private readonly PdfTranslator _translator;
    private readonly TranslationOptions _options;
    private readonly ILogger<MainWindowViewModel> _logger;
    private IStorageProvider? _storageProvider;

    // ========== Ollama 配置属性 ==========

    private string _ollamaUrl = "http://localhost:11434";
    /// <summary>
    /// Ollama API 地址
    /// </summary>
    public string OllamaUrl
    {
        get => _ollamaUrl;
        set => this.RaiseAndSetIfChanged(ref _ollamaUrl, value);
    }

    private string _ollamaModel = "llama3.2";
    /// <summary>
    /// Ollama 模型名称
    /// </summary>
    public string OllamaModel
    {
        get => _ollamaModel;
        set
        {
            this.RaiseAndSetIfChanged(ref _ollamaModel, value);
            // 同步更新 Core 配置
            _options.Model = value;
        }
    }

    /// <summary>
    /// 当前选择的模型对象（用于下拉菜单双向绑定）
    /// </summary>
    private OllamaModelInfo? _selectedModel;
    public OllamaModelInfo? SelectedModel
    {
        get => _selectedModel;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedModel, value);
            if (value != null)
            {
                OllamaModel = value.Name;
            }
        }
    }

    private int _timeoutSeconds = 60;
    /// <summary>
    /// Ollama 请求超时时间（秒）
    /// </summary>
    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => this.RaiseAndSetIfChanged(ref _timeoutSeconds, value);
    }

    private bool _isOllamaConnected;
    /// <summary>
    /// Ollama 连接状态
    /// </summary>
    public bool IsOllamaConnected
    {
        get => _isOllamaConnected;
        private set => this.RaiseAndSetIfChanged(ref _isOllamaConnected, value);
    }

    private string _ollamaStatusMessage = "未连接";
    /// <summary>
    /// Ollama 状态信息
    /// </summary>
    public string OllamaStatusMessage
    {
        get => _ollamaStatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _ollamaStatusMessage, value);
    }

    // ========== 模型列表属性 ==========
    
    private ObservableCollection<OllamaModelInfo> _availableModels = new();
    /// <summary>
    /// 可用模型列表（用于下拉菜单）
    /// </summary>
    public ObservableCollection<OllamaModelInfo> AvailableModels
    {
        get => _availableModels;
        private set => this.RaiseAndSetIfChanged(ref _availableModels, value);
    }

    private bool _isRefreshingModels;
    /// <summary>
    /// 是否正在刷新模型列表
    /// </summary>
    public bool IsRefreshingModels
    {
        get => _isRefreshingModels;
        private set => this.RaiseAndSetIfChanged(ref _isRefreshingModels, value);
    }

    private string _modelListStatus = string.Empty;
    /// <summary>
    /// 模型列表状态信息
    /// </summary>
    public string ModelListStatus
    {
        get => _modelListStatus;
        private set => this.RaiseAndSetIfChanged(ref _modelListStatus, value);
    }

    // ========== 文件路径属性 ==========

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

    // ========== 翻译模式属性 ==========

    private bool _isBilingual;
    /// <summary>
    /// 是否为双语对照模式
    /// </summary>
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
    /// <summary>
    /// 是否翻译图片中的文字（预留功能）
    /// </summary>
    public bool TranslateImages
    {
        get => _translateImages;
        set
        {
            this.RaiseAndSetIfChanged(ref _translateImages, value);
            _options.TranslateImages = value;
        }
    }

    // ========== 字体配置属性 ==========

    private string _fontName = string.Empty;
    /// <summary>
    /// 用户指定的字体名称
    /// </summary>
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
    /// <summary>
    /// 用户指定的字体文件路径
    /// </summary>
    public string FontPath
    {
        get => _fontPath;
        set
        {
            this.RaiseAndSetIfChanged(ref _fontPath, value);
            _options.FontPath = string.IsNullOrEmpty(value) ? null : value;
        }
    }

    // ========== 日志和状态属性 ==========

    private string _log = string.Empty;
    /// <summary>
    /// 日志文本
    /// </summary>
    public string Log
    {
        get => _log;
        private set => this.RaiseAndSetIfChanged(ref _log, value);
    }

    private bool _isBusy;
    /// <summary>
    /// 是否正在处理中
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    // ========== 进度条属性 ==========

    private int _progressValue;
    /// <summary>
    /// 当前进度值
    /// </summary>
    public int ProgressValue
    {
        get => _progressValue;
        private set => this.RaiseAndSetIfChanged(ref _progressValue, value);
    }

    private int _progressMax = 100;
    /// <summary>
    /// 进度最大值
    /// </summary>
    public int ProgressMax
    {
        get => _progressMax;
        private set => this.RaiseAndSetIfChanged(ref _progressMax, value);
    }

    private bool _showProgress;
    /// <summary>
    /// 是否显示进度条
    /// </summary>
    public bool ShowProgress
    {
        get => _showProgress;
        private set => this.RaiseAndSetIfChanged(ref _showProgress, value);
    }

    private string _progressMessage = string.Empty;
    /// <summary>
    /// 进度状态信息
    /// </summary>
    public string ProgressMessage
    {
        get => _progressMessage;
        private set => this.RaiseAndSetIfChanged(ref _progressMessage, value);
    }

    // ========== 命令 ==========

    public ReactiveCommand<Unit, Unit> SelectInputCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectOutputCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectFontCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<Unit, Unit> TestOllamaConnectionCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshModelsCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveOllamaConfigCommand { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public MainWindowViewModel(
        PdfTranslator translator, 
        TranslationOptions options, 
        ILogger<MainWindowViewModel> logger)
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

        TestOllamaConnectionCommand = ReactiveCommand.CreateFromTask(TestOllamaConnectionAsync);
        RefreshModelsCommand = ReactiveCommand.CreateFromTask(RefreshModelsAsync);
        SaveOllamaConfigCommand = ReactiveCommand.Create(SaveOllamaConfig);

        // 从环境变量加载配置
        LoadOllamaConfigFromEnvironment();

        // 从 Core 配置中初始化属性值
        IsBilingual = _options.Mode == TranslationMode.Bilingual;
        TranslateImages = _options.TranslateImages;
        FontName = _options.FontName ?? string.Empty;
        FontPath = _options.FontPath ?? string.Empty;
        
        // 初始化进度条状态
        ProgressMax = 100;
        ShowProgress = false;

        // 自动测试 Ollama 连接
        Dispatcher.UIThread.Post(async () => await TestOllamaConnectionAsync());
    }

    /// <summary>
    /// 从环境变量加载 Ollama 配置
    /// </summary>
    private void LoadOllamaConfigFromEnvironment()
    {
        string? ollamaHost = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (!string.IsNullOrEmpty(ollamaHost))
        {
            if (!ollamaHost.StartsWith("http://") && !ollamaHost.StartsWith("https://"))
            {
                OllamaUrl = $"http://{ollamaHost}";
            }
            else
            {
                OllamaUrl = ollamaHost;
            }
        }
        
        string? ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
        if (!string.IsNullOrEmpty(ollamaModel))
        {
            OllamaModel = ollamaModel;
        }
    }

    /// <summary>
    /// 设置存储提供程序
    /// </summary>
    public void SetStorageProvider(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider;
        Log += "文件系统已就绪。\n";
    }

    /// <summary>
    /// 检查存储提供程序是否可用
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
    /// 测试 Ollama 连接并自动获取模型列表
    /// </summary>
    private async Task TestOllamaConnectionAsync()
    {
        IsOllamaConnected = false;
        OllamaStatusMessage = "正在连接...";
        
        try
        {
            using var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(OllamaUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);
            
            var response = await httpClient.GetAsync("api/tags");
            
            if (response.IsSuccessStatusCode)
            {
                IsOllamaConnected = true;
                OllamaStatusMessage = "已连接";
                Log += $"✓ Ollama 连接成功 ({OllamaUrl})\n";
                
                // 自动刷新模型列表
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

    /// <summary>
    /// 刷新可用模型列表（从 Ollama API 获取）
    /// </summary>
    private async Task RefreshModelsAsync()
    {
        // 如果没有连接，则不执行
        if (!IsOllamaConnected)
        {
            ModelListStatus = "请先连接 Ollama";
            return;
        }

        IsRefreshingModels = true;
        ModelListStatus = "正在获取模型列表...";
        
        try
        {
            using var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(OllamaUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            var response = await httpClient.GetAsync("api/tags");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                
                // 解析 JSON 响应
                using JsonDocument doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("models", out var modelsArray))
                {
                    var models = new ObservableCollection<OllamaModelInfo>();
                    
                    foreach (var model in modelsArray.EnumerateArray())
                    {
                        var name = model.GetProperty("name").GetString() ?? "未知";
                        
                        // 尝试获取大小信息（可能不存在）
                        long size = 0;
                        if (model.TryGetProperty("size", out var sizeElement))
                        {
                            size = sizeElement.GetInt64();
                        }
                        
                        // 尝试获取修改时间
                        DateTime modifiedAt = DateTime.Now;
                        if (model.TryGetProperty("modified_at", out var modifiedElement))
                        {
                            DateTime.TryParse(modifiedElement.GetString(), out modifiedAt);
                        }
                        
                        models.Add(new OllamaModelInfo
                        {
                            Name = name,
                            Size = size,
                            ModifiedAt = modifiedAt
                        });
                    }
                    
                    AvailableModels = models;
                    
                    if (models.Count > 0)
                    {
                        ModelListStatus = $"找到 {models.Count} 个模型";
                        Log += $"✓ 已获取模型列表: {models.Count} 个模型可用\n";
                        
                        // 如果当前选择的模型不在列表中，且列表不为空，自动选择第一个
                        if (!models.Any(m => m.Name == OllamaModel) && models.Count > 0)
                        {
                            SelectedModel = models[0];
                            OllamaModel = models[0].Name;
                            Log += $"自动选择模型: {OllamaModel}\n";
                        }
                        else
                        {
                            // 如果当前模型在列表中，选中对应的模型对象
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
                    Log += "✗ API 返回格式异常，缺少 models 字段\n";
                }
            }
            else
            {
                ModelListStatus = $"获取失败 (HTTP {response.StatusCode})";
                Log += $"✗ 获取模型列表失败: HTTP {response.StatusCode}\n";
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

    /// <summary>
    /// 保存 Ollama 配置
    /// </summary>
    private void SaveOllamaConfig()
    {
        Log += $"Ollama 配置已更新:\n";
        Log += $"  URL: {OllamaUrl}\n";
        Log += $"  模型: {OllamaModel}\n";
        Log += $"  超时: {TimeoutSeconds}秒\n";
        Log += "注意: 部分配置更改可能需要重启应用才能生效\n";
    }

    // ========== 文件选择方法 ==========

    private async Task SelectInputAsync()
    {
        if (!CheckStorageProvider()) return;

        try
        {
            var files = await _storageProvider!.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择输入 PDF 文件",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("PDF 文件") 
                    { Patterns = new[] { "*.pdf" } } }
            });

            if (files.Count == 1)
            {
                InputPath = files[0].Path.LocalPath;
                Log += $"已选择输入文件: {InputPath}\n";
            }
        }
        catch (Exception ex)
        {
            Log += $"选择文件时出错: {ex.Message}\n";
            _logger.LogError(ex, "选择输入文件时发生错误");
        }
    }

    private async Task SelectOutputAsync()
    {
        if (!CheckStorageProvider()) return;

        try
        {
            var file = await _storageProvider!.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "选择输出 PDF 文件",
                DefaultExtension = "pdf",
                FileTypeChoices = new[] { new FilePickerFileType("PDF 文件") 
                    { Patterns = new[] { "*.pdf" } } }
            });

            if (file != null)
            {
                OutputPath = file.Path.LocalPath;
                Log += $"已选择输出文件: {OutputPath}\n";
            }
        }
        catch (Exception ex)
        {
            Log += $"选择文件时出错: {ex.Message}\n";
            _logger.LogError(ex, "选择输出文件时发生错误");
        }
    }

    private async Task SelectFontAsync()
    {
        if (!CheckStorageProvider()) return;

        try
        {
            var files = await _storageProvider!.OpenFilePickerAsync(new FilePickerOpenOptions
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
        catch (Exception ex)
        {
            Log += $"选择字体文件时出错: {ex.Message}\n";
            _logger.LogError(ex, "选择字体文件时发生错误");
        }
    }

    // ========== 翻译方法 ==========

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
        Log += $"Ollama 模型: {OllamaModel}\n";
        Log += $"模式: {(IsBilingual ? "双语对照" : "仅译文")}\n";
        if (!string.IsNullOrEmpty(FontName))
            Log += $"字体名称: {FontName}\n";
        if (!string.IsNullOrEmpty(FontPath))
            Log += $"字体路径: {FontPath}\n";

        // 更新 Core 配置
        _options.Model = OllamaModel;

        // 创建进度报告器
        var progressReporter = new GuiProgressReporter(
            onProgress: (current, total, message) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ProgressMax = total;
                    ProgressValue = current;
                    ProgressMessage = message ?? $"正在处理第 {current} 页...";
                });
            },
            onComplete: (message) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ShowProgress = false;
                    if (!string.IsNullOrEmpty(message))
                        Log += message + "\n";
                });
            });

        _translator.SetProgressReporter(progressReporter);

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