using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDFTranslator.Core;

namespace PDFTranslator.CLI;

/// <summary>
/// 命令行进度报告器
/// 实现 IProgressReporter 接口，在控制台显示动态进度条
/// </summary>
public class ConsoleProgressReporter : IProgressReporter
{
    private int _lastPercent = -1;
    private readonly object _lock = new object();

    public void Report(int current, int total, string? message = null)
    {
        int percent = (int)((double)current / total * 100);
        
        lock (_lock)
        {
            if (percent != _lastPercent)
            {
                _lastPercent = percent;
                int completedBars = percent / 2;
                string bar = new string('#', completedBars) + 
                             new string('-', 50 - completedBars);
                
                Console.Write($"\r进度: [{bar}] {percent}%");
                
                if (!string.IsNullOrEmpty(message))
                {
                    Console.Write($" - {message}");
                }
            }
        }
    }

    public void Complete(string? message = null)
    {
        Console.WriteLine();
        if (!string.IsNullOrEmpty(message))
        {
            Console.WriteLine(message);
        }
    }
}

/// <summary>
/// Ollama 配置信息类
/// </summary>
public class OllamaConfig
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.2";
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// 从环境变量加载配置（可选）
    /// </summary>
    public static OllamaConfig LoadFromEnvironment()
    {
        var config = new OllamaConfig();
        
        // 从环境变量读取 OLLAMA_HOST（如果存在）
        string? ollamaHost = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (!string.IsNullOrEmpty(ollamaHost))
        {
            // OLLAMA_HOST 可能包含端口，例如 "localhost:11434" 或 "http://192.168.1.100:11434"
            if (!ollamaHost.StartsWith("http://") && !ollamaHost.StartsWith("https://"))
            {
                config.BaseUrl = $"http://{ollamaHost}";
            }
            else
            {
                config.BaseUrl = ollamaHost;
            }
        }
        
        // 从环境变量读取 OLLAMA_MODEL（如果存在）
        string? ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
        if (!string.IsNullOrEmpty(ollamaModel))
        {
            config.Model = ollamaModel;
        }
        
        return config;
    }

    /// <summary>
    /// 验证配置是否有效
    /// </summary>
    public bool IsValid(out string errorMessage)
    {
        errorMessage = "";
        
        if (!Uri.IsWellFormedUriString(BaseUrl, UriKind.Absolute))
        {
            errorMessage = $"无效的 URL 格式: {BaseUrl}";
            return false;
        }
        
        if (string.IsNullOrWhiteSpace(Model))
        {
            errorMessage = "模型名称不能为空";
            return false;
        }
        
        if (TimeoutSeconds < 1 || TimeoutSeconds > 300)
        {
            errorMessage = "超时时间必须在 1-300 秒之间";
            return false;
        }
        
        return true;
    }
}

class Program
{
    static async Task Main(string[] args)
    {
        // 加载默认配置（可从环境变量读取）
        var defaultConfig = OllamaConfig.LoadFromEnvironment();
        
        // 当前配置（可从命令行覆盖）
        string ollamaUrl = defaultConfig.BaseUrl;
        string model = defaultConfig.Model;
        int timeoutSeconds = defaultConfig.TimeoutSeconds;
        
        bool showProgress = true;
        bool showConfig = false; // 是否只显示配置而不翻译

        // ---------- 参数检查 ----------
        if (args.Length == 0 || (args.Length == 1 && (args[0] == "--help" || args[0] == "-h")))
        {
            PrintUsage(defaultConfig);
            return;
        }

        // 特殊命令：显示当前配置
        if (args.Length == 1 && args[0] == "--config")
        {
            ShowCurrentConfig(defaultConfig);
            return;
        }

        // 解析必需参数
        if (args.Length < 2)
        {
            Console.WriteLine("错误：缺少必需参数。");
            PrintUsage(defaultConfig);
            return;
        }

        string inputPath = args[0];
        string outputPath = args[1];

        // 验证输入文件是否存在
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"错误：输入文件不存在 - {inputPath}");
            Environment.ExitCode = 1;
            return;
        }

        // 默认值
        var mode = TranslationMode.Translate;
        bool translateImages = false;
        string? fontName = null;
        string? fontPath = null;

        // 解析可选参数（从第三个参数开始）
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                // 翻译模式
                case "--mode":
                case "-m":
                    if (i + 1 < args.Length)
                    {
                        var modeArg = args[++i].ToLower();
                        mode = modeArg switch
                        {
                            "bilingual" => TranslationMode.Bilingual,
                            "translate" => TranslationMode.Translate,
                            _ => throw new ArgumentException($"无效的模式: {modeArg}，有效值为 translate 或 bilingual")
                        };
                    }
                    break;

                // Ollama URL
                case "--url":
                    if (i + 1 < args.Length)
                        ollamaUrl = args[++i];
                    break;

                // Ollama 模型
                case "--model":
                    if (i + 1 < args.Length)
                        model = args[++i];
                    break;

                // 超时时间（秒）
                case "--timeout":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int timeout))
                        timeoutSeconds = timeout;
                    break;

                // 字体名称
                case "--font-name":
                    if (i + 1 < args.Length)
                        fontName = args[++i];
                    break;

                // 字体文件路径
                case "--font-path":
                    if (i + 1 < args.Length)
                        fontPath = args[++i];
                    break;

                // 禁用进度条
                case "--no-progress":
                    showProgress = false;
                    break;

                // 显示配置
                case "--show-config":
                    showConfig = true;
                    break;

                // 帮助
                case "--help":
                case "-h":
                    PrintUsage(defaultConfig);
                    return;

                default:
                    Console.WriteLine($"未知参数: {args[i]}");
                    PrintUsage(defaultConfig);
                    return;
            }
        }

        // 验证 Ollama 配置
        var currentConfig = new OllamaConfig
        {
            BaseUrl = ollamaUrl,
            Model = model,
            TimeoutSeconds = timeoutSeconds
        };

        if (!currentConfig.IsValid(out string configError))
        {
            Console.WriteLine($"配置错误: {configError}");
            Environment.ExitCode = 1;
            return;
        }

        // 如果只是显示配置，则输出后退出
        if (showConfig)
        {
            ShowCurrentConfig(currentConfig);
            return;
        }

        // ---------- 验证字体文件路径（如果提供了） ----------
        if (!string.IsNullOrEmpty(fontPath) && !File.Exists(fontPath))
        {
            Console.WriteLine($"警告：指定的字体文件不存在 - {fontPath}");
            Console.WriteLine("将继续使用其他字体加载方式...");
        }

        // ---------- 显示当前配置 ----------
        Console.WriteLine("=== Ollama 配置 ===");
        Console.WriteLine($"API 地址: {ollamaUrl}");
        Console.WriteLine($"模型名称: {model}");
        Console.WriteLine($"超时时间: {timeoutSeconds} 秒");
        Console.WriteLine();

        // ---------- 配置依赖注入 ----------
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning); // 减少日志输出，避免干扰进度条
        });

        // 添加核心翻译服务，使用自定义 Ollama 地址和模型
        services.AddPDFTranslatorCore(ollamaBaseUrl: ollamaUrl, model: model);

        var serviceProvider = services.BuildServiceProvider();

        var translator = serviceProvider.GetRequiredService<PdfTranslator>();
        var options = serviceProvider.GetRequiredService<TranslationOptions>();

        options.Mode = mode;
        options.TranslateImages = translateImages;
        options.FontName = fontName;
        options.FontPath = fontPath;

        // ---------- 设置进度报告器 ----------
        if (showProgress)
        {
            translator.SetProgressReporter(new ConsoleProgressReporter());
        }

        // ---------- 测试 Ollama 连接 ----------
        Console.WriteLine("正在测试 Ollama 连接...");
        try
        {
            // 尝试调用一个简单的翻译来验证连接
            var testService = serviceProvider.GetRequiredService<OllamaService>();
            await testService.TranslateAsync("test", "en", "zh");
            Console.WriteLine("✓ Ollama 连接成功\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Ollama 连接失败: {ex.Message}");
            Console.WriteLine("请确保:");
            Console.WriteLine("1. Ollama 服务已启动 (运行 'ollama serve')");
            Console.WriteLine($"2. 服务地址正确: {ollamaUrl}");
            Console.WriteLine("3. 防火墙未阻止连接");
            Console.WriteLine();
            Console.WriteLine("是否继续尝试翻译？(y/n)");
            
            var key = Console.ReadKey();
            if (key.KeyChar != 'y' && key.KeyChar != 'Y')
            {
                Console.WriteLine("\n已取消翻译");
                return;
            }
            Console.WriteLine();
        }

        // ---------- 执行翻译 ----------
        try
        {
            Console.WriteLine($"开始翻译 PDF: {inputPath}");
            Console.WriteLine($"输出路径: {outputPath}");
            Console.WriteLine($"模式: {(mode == TranslationMode.Translate ? "仅译文" : "双语对照")}");
            
            if (!string.IsNullOrEmpty(fontName))
                Console.WriteLine($"字体名称: {fontName}");
            if (!string.IsNullOrEmpty(fontPath))
                Console.WriteLine($"字体路径: {fontPath}");
            
            Console.WriteLine();

            await translator.TranslatePdfAsync(inputPath, outputPath);

            if (!showProgress)
            {
                Console.WriteLine("翻译完成！");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    /// <summary>
    /// 显示当前 Ollama 配置
    /// </summary>
    static void ShowCurrentConfig(OllamaConfig config)
    {
        Console.WriteLine("=== PDFTranslator 当前配置 ===");
        Console.WriteLine();
        Console.WriteLine("Ollama 配置:");
        Console.WriteLine($"  API 地址: {config.BaseUrl}");
        Console.WriteLine($"  默认模型: {config.Model}");
        Console.WriteLine($"  超时时间: {config.TimeoutSeconds} 秒");
        Console.WriteLine();
        Console.WriteLine("环境变量支持:");
        Console.WriteLine("  OLLAMA_HOST  - 设置 Ollama 服务地址 (如: localhost:11434)");
        Console.WriteLine("  OLLAMA_MODEL - 设置默认模型名称 (如: llama3.2)");
        Console.WriteLine();
        Console.WriteLine("可用模型列表:");
        ShowAvailableModels(config.BaseUrl).Wait();
    }

    /// <summary>
    /// 显示可用的 Ollama 模型列表
    /// </summary>
    static async Task ShowAvailableModels(string baseUrl)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(baseUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            var response = await httpClient.GetAsync("api/tags");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                // 简单解析，实际项目中可以使用 JSON 库
                Console.WriteLine("  通过 'ollama list' 命令查看完整列表");
                Console.WriteLine($"  或访问: {baseUrl}/api/tags");
            }
            else
            {
                Console.WriteLine("  无法获取模型列表，请确保 Ollama 服务正常运行");
            }
        }
        catch
        {
            Console.WriteLine("  无法连接到 Ollama 服务，请检查服务是否启动");
        }
    }

    static void PrintUsage(OllamaConfig defaultConfig)
    {
        Console.WriteLine("PDFTranslator.CLI - 基于 Ollama 的 PDF 翻译器命令行版");
        Console.WriteLine("==================================================");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  PDFTranslator.CLI <输入PDF> <输出PDF> [选项]");
        Console.WriteLine("  PDFTranslator.CLI --config                    # 显示当前配置");
        Console.WriteLine();
        Console.WriteLine("必需参数:");
        Console.WriteLine("  <输入PDF>               要翻译的 PDF 文件路径");
        Console.WriteLine("  <输出PDF>               翻译后保存的 PDF 文件路径");
        Console.WriteLine();
        Console.WriteLine("Ollama 配置选项:");
        Console.WriteLine($"  --url <地址>             Ollama API 地址 (默认: {defaultConfig.BaseUrl})");
        Console.WriteLine($"  --model <模型名>         使用的模型 (默认: {defaultConfig.Model})");
        Console.WriteLine($"  --timeout <秒数>         请求超时时间 (默认: {defaultConfig.TimeoutSeconds})");
        Console.WriteLine();
        Console.WriteLine("翻译选项:");
        Console.WriteLine("  --mode, -m <模式>       翻译模式：translate 或 bilingual (默认: translate)");
        Console.WriteLine("  --font-name <名称>       指定字体名称 (如 SimSun)");
        Console.WriteLine("  --font-path <路径>       指定字体文件路径");
        Console.WriteLine("  --no-progress             禁用进度条显示");
        Console.WriteLine("  --show-config             显示当前配置后退出");
        Console.WriteLine("  --help, -h               显示此帮助信息");
        Console.WriteLine();
        Console.WriteLine("环境变量支持:");
        Console.WriteLine("  OLLAMA_HOST  - 设置默认 Ollama 地址 (如: localhost:11434)");
        Console.WriteLine("  OLLAMA_MODEL - 设置默认模型 (如: llama3.2)");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  PDFTranslator.CLI doc.pdf output.pdf");
        Console.WriteLine("  PDFTranslator.CLI doc.pdf output.pdf --url http://192.168.1.100:11434 --model qwen2.5");
        Console.WriteLine("  PDFTranslator.CLI doc.pdf output.pdf --mode bilingual --font-name SimSun");
        Console.WriteLine("  PDFTranslator.CLI --config  # 查看当前配置");
    }
}