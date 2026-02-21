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
                string bar = new string('#', completedBars) + new string('-', 50 - completedBars);
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

    public static OllamaConfig LoadFromEnvironment()
    {
        var config = new OllamaConfig();
        string? ollamaHost = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (!string.IsNullOrEmpty(ollamaHost))
        {
            if (!ollamaHost.StartsWith("http://") && !ollamaHost.StartsWith("https://"))
            {
                config.BaseUrl = $"http://{ollamaHost}";
            }
            else
            {
                config.BaseUrl = ollamaHost;
            }
        }
        string? ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL");
        if (!string.IsNullOrEmpty(ollamaModel))
        {
            config.Model = ollamaModel;
        }
        return config;
    }

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
        // 加载默认配置
        var defaultConfig = OllamaConfig.LoadFromEnvironment();

        // 初始化变量
        string? inputPath = null;
        string? outputPath = null;
        bool autoOutput = false;
        var mode = TranslationMode.Translate;
        bool translateImages = false;
        string model = defaultConfig.Model;
        string ollamaUrl = defaultConfig.BaseUrl;
        int timeoutSeconds = defaultConfig.TimeoutSeconds;
        string? fontName = null;
        string? fontPath = null;
        string sourceLang = "en";
        string targetLang = "zh";
        bool showProgress = true;
        bool showConfig = false;

        // 页面范围相关变量
        PageRangeMode pageRangeMode = PageRangeMode.All;
        string? pageRange = null;
        int? singlePage = null;

        // 先简单解析参数，识别 --auto-output 和 --help 等
        List<string> remainingArgs = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--auto-output" || args[i] == "-a")
            {
                autoOutput = true;
            }
            else if (args[i] == "--help" || args[i] == "-h")
            {
                PrintUsage(defaultConfig);
                return;
            }
            else if (args[i] == "--config")
            {
                showConfig = true;
                remainingArgs.Add(args[i]);
            }
            else
            {
                remainingArgs.Add(args[i]);
            }
        }

        // 现在处理剩余参数（不含已识别的特殊命令）
        if (showConfig)
        {
            if (remainingArgs.Count == 1) // 只有 --config
            {
                ShowCurrentConfig(defaultConfig);
                return;
            }
        }

        // 确定位置参数数量
        int positionalCount = autoOutput ? 1 : 2;
        if (remainingArgs.Count < positionalCount)
        {
            Console.WriteLine($"错误：参数不足。需要 {(autoOutput ? "1个输入文件" : "2个参数（输入文件和输出文件）")}。");
            PrintUsage(defaultConfig);
            return;
        }

        // 解析位置参数
        inputPath = remainingArgs[0];
        if (!autoOutput)
        {
            outputPath = remainingArgs[1];
            remainingArgs = remainingArgs.Skip(2).ToList();
        }
        else
        {
            remainingArgs = remainingArgs.Skip(1).ToList();
        }

        // 验证输入文件是否存在
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"错误：输入文件不存在 - {inputPath}");
            Environment.ExitCode = 1;
            return;
        }

        // 解析剩余的可选参数
        for (int i = 0; i < remainingArgs.Count; i++)
        {
            switch (remainingArgs[i])
            {
                case "--mode":
                case "-m":
                    if (i + 1 < remainingArgs.Count)
                    {
                        var modeArg = remainingArgs[++i].ToLower();
                        mode = modeArg switch
                        {
                            "bilingual" => TranslationMode.Bilingual,
                            "translate" => TranslationMode.Translate,
                            _ => throw new ArgumentException($"无效的模式: {modeArg}，有效值为 translate 或 bilingual")
                        };
                    }
                    break;

                case "--model":
                    if (i + 1 < remainingArgs.Count)
                        model = remainingArgs[++i];
                    break;

                case "--url":
                    if (i + 1 < remainingArgs.Count)
                        ollamaUrl = remainingArgs[++i];
                    break;

                case "--timeout":
                    if (i + 1 < remainingArgs.Count && int.TryParse(remainingArgs[++i], out int timeout))
                        timeoutSeconds = timeout;
                    break;

                case "--font-name":
                    if (i + 1 < remainingArgs.Count)
                        fontName = remainingArgs[++i];
                    break;

                case "--font-path":
                    if (i + 1 < remainingArgs.Count)
                        fontPath = remainingArgs[++i];
                    break;

                case "--source":
                case "-s":
                    if (i + 1 < remainingArgs.Count)
                        sourceLang = remainingArgs[++i];
                    break;

                case "--target":
                case "-t":
                    if (i + 1 < remainingArgs.Count)
                        targetLang = remainingArgs[++i];
                    break;

                case "--pages":
                case "-p":
                    if (i + 1 < remainingArgs.Count)
                    {
                        pageRange = remainingArgs[++i];
                        pageRangeMode = PageRangeMode.Range;
                    }
                    break;

                case "--page":
                    if (i + 1 < remainingArgs.Count && int.TryParse(remainingArgs[++i], out int page))
                    {
                        singlePage = page;
                        pageRangeMode = PageRangeMode.Single;
                    }
                    break;

                case "--translate-images":
                case "-ti":
                    if (i + 1 < remainingArgs.Count && bool.TryParse(remainingArgs[++i], out bool ti))
                        translateImages = ti;
                    break;

                case "--no-progress":
                    showProgress = false;
                    break;

                default:
                    Console.WriteLine($"未知参数: {remainingArgs[i]}");
                    PrintUsage(defaultConfig);
                    return;
            }
        }

        // 如果启用 autoOutput，生成输出路径
        if (autoOutput)
        {
            string dir = Path.GetDirectoryName(inputPath) ?? Directory.GetCurrentDirectory();
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
            string ext = Path.GetExtension(inputPath);
            string suffix = mode == TranslationMode.Bilingual ? "_bilingual" : "_translated";
            outputPath = Path.Combine(dir, $"{fileNameWithoutExt}{suffix}{ext}");
            Console.WriteLine($"自动生成输出文件: {outputPath}");
        }

        // 验证输出路径是否有效
        try
        {
            string? outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"警告：无法创建输出目录 - {ex.Message}");
        }

        // 验证字体文件路径（如果提供）
        if (!string.IsNullOrEmpty(fontPath) && !File.Exists(fontPath))
        {
            Console.WriteLine($"警告：指定的字体文件不存在 - {fontPath}");
            Console.WriteLine("将继续使用其他字体加载方式...");
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

        // 显示配置摘要
        Console.WriteLine("=== PDFTranslator 配置 ===");
        Console.WriteLine($"输入文件: {inputPath}");
        Console.WriteLine($"输出文件: {outputPath}");
        Console.WriteLine($"翻译模式: {(mode == TranslationMode.Translate ? "仅译文" : "双语对照")}");
        
        // 显示页面范围信息
        if (pageRangeMode == PageRangeMode.Range && !string.IsNullOrEmpty(pageRange))
            Console.WriteLine($"页码范围: {pageRange}");
        else if (pageRangeMode == PageRangeMode.Single && singlePage.HasValue)
            Console.WriteLine($"单页: {singlePage}");
        else
            Console.WriteLine($"页码范围: 全部");
        
        Console.WriteLine($"源语言: {sourceLang} -> 目标语言: {targetLang}");
        Console.WriteLine($"Ollama 地址: {ollamaUrl}");
        Console.WriteLine($"模型: {model}");
        Console.WriteLine($"超时: {timeoutSeconds} 秒");
        if (!string.IsNullOrEmpty(fontName)) Console.WriteLine($"字体名称: {fontName}");
        if (!string.IsNullOrEmpty(fontPath)) Console.WriteLine($"字体路径: {fontPath}");
        Console.WriteLine();

        // 配置依赖注入
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
#if DEBUG
            builder.SetMinimumLevel(LogLevel.Debug);
#else
            builder.SetMinimumLevel(LogLevel.Warning);
#endif
        });
        services.AddPDFTranslatorCore(ollamaBaseUrl: ollamaUrl, model: model);

        var serviceProvider = services.BuildServiceProvider();
        var translator = serviceProvider.GetRequiredService<PdfTranslator>();
        var options = serviceProvider.GetRequiredService<TranslationOptions>();

        // 设置所有选项
        options.Mode = mode;
        options.TranslateImages = translateImages;
        options.FontName = fontName;
        options.FontPath = fontPath;
        options.SourceLanguage = sourceLang;
        options.TargetLanguage = targetLang;
        options.PageRangeMode = pageRangeMode;
        options.PageRange = pageRange;
        options.SinglePage = singlePage;

        // 设置进度报告器
        if (showProgress)
        {
            translator.SetProgressReporter(new ConsoleProgressReporter());
        }

        // 测试 Ollama 连接
        Console.WriteLine("正在测试 Ollama 连接...");
        try
        {
            using var httpClient = new HttpClient();
            httpClient.BaseAddress = new Uri(ollamaUrl);
            httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var response = await httpClient.GetAsync("api/tags");
            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✓ Ollama 连接成功");
            }
            else
            {
                Console.WriteLine($"✗ Ollama 连接失败 (HTTP {response.StatusCode})");
                Console.WriteLine("是否继续尝试翻译？(y/n)");
                var key = Console.ReadKey();
                if (key.KeyChar != 'y' && key.KeyChar != 'Y')
                {
                    Console.WriteLine("\n已取消翻译");
                    return;
                }
                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Ollama 连接错误: {ex.Message}");
            Console.WriteLine("是否继续尝试翻译？(y/n)");
            var key = Console.ReadKey();
            if (key.KeyChar != 'y' && key.KeyChar != 'Y')
            {
                Console.WriteLine("\n已取消翻译");
                return;
            }
            Console.WriteLine();
        }

        // 执行翻译前检查输出路径是否为空
        if (string.IsNullOrEmpty(outputPath))
        {
            Console.WriteLine("错误：输出文件路径为空，无法进行翻译。");
            Environment.ExitCode = 1;
            return;
        }

        // 执行翻译
        try
        {
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
    }

    static void PrintUsage(OllamaConfig defaultConfig)
    {
        Console.WriteLine("PDFTranslator.CLI - 基于 Ollama 的 PDF 翻译器命令行版");
        Console.WriteLine("==================================================");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  PDFTranslator.CLI <输入PDF> [输出PDF] [选项]");
        Console.WriteLine("  PDFTranslator.CLI <输入PDF> --auto-output [选项]  # 自动生成输出文件名");
        Console.WriteLine("  PDFTranslator.CLI --config                           # 显示当前配置");
        Console.WriteLine();
        Console.WriteLine("必需参数:");
        Console.WriteLine("  <输入PDF>               要翻译的 PDF 文件路径");
        Console.WriteLine("  [输出PDF]                可选，若省略则需使用 --auto-output 自动生成");
        Console.WriteLine();
        Console.WriteLine("自动输出选项:");
        Console.WriteLine("  --auto-output, -a       根据输入文件名和翻译模式自动生成输出文件名");
        Console.WriteLine("                           仅译文模式添加 _translated 后缀，双语模式添加 _bilingual 后缀");
        Console.WriteLine();
        Console.WriteLine("翻译选项:");
        Console.WriteLine("  --mode, -m <模式>       翻译模式：translate 或 bilingual，默认 translate");
        Console.WriteLine("  --source, -s <代码>     源语言代码 (如 en, zh, ja, fr)，默认 en");
        Console.WriteLine("  --target, -t <代码>     目标语言代码 (如 zh, en, ja, de)，默认 zh");
        Console.WriteLine();
        Console.WriteLine("页面范围选项:");
        Console.WriteLine("  --pages, -p <范围>       指定页码范围，如 1-5,7,9-11");
        Console.WriteLine("  --page <页码>             指定单个页面，如 3");
        Console.WriteLine("                            不指定则翻译全部页面");
        Console.WriteLine();
        Console.WriteLine("Ollama 配置:");
        Console.WriteLine($"  --model <模型名>         使用的模型名称，默认 {defaultConfig.Model}");
        Console.WriteLine($"  --url <地址>             Ollama API 地址，默认 {defaultConfig.BaseUrl}");
        Console.WriteLine($"  --timeout <秒数>         请求超时时间，默认 {defaultConfig.TimeoutSeconds} 秒");
        Console.WriteLine();
        Console.WriteLine("字体配置:");
        Console.WriteLine("  --font-name <名称>       指定字体名称（如 SimSun）");
        Console.WriteLine("  --font-path <路径>       指定字体文件路径（支持 .ttf/.ttc/.otf）");
        Console.WriteLine();
        Console.WriteLine("其他选项:");
        Console.WriteLine("  --translate-images, -ti <true/false>  是否翻译图片中的文字（预留），默认 false");
        Console.WriteLine("  --no-progress             禁用进度条显示");
        Console.WriteLine("  --help, -h               显示此帮助信息");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  PDFTranslator.CLI doc.pdf --auto-output");
        Console.WriteLine("  PDFTranslator.CLI doc.pdf --auto-output --pages 1-5,7");
        Console.WriteLine("  PDFTranslator.CLI doc.pdf output.pdf --page 3 --mode bilingual");
        Console.WriteLine("  PDFTranslator.CLI doc.pdf output.pdf --model qwen2.5 --font-name SimSun");
        Console.WriteLine("  PDFTranslator.CLI doc.pdf output.pdf --source en --target zh");
    }
}