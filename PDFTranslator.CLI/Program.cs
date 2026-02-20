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
    private int _lastPercent = -1;      // 上一次报告的百分比，用于避免重复刷新
    private readonly object _lock = new object(); // 线程锁，防止多线程同时写入控制台

    /// <summary>
    /// 报告当前进度
    /// </summary>
    /// <param name="current">当前已完成的任务数（已处理的页数）</param>
    /// <param name="total">总任务数（总页数）</param>
    /// <param name="message">当前状态信息（可选）</param>
    public void Report(int current, int total, string? message = null)
    {
        int percent = (int)((double)current / total * 100);
        
        lock (_lock)
        {
            if (percent != _lastPercent)
            {
                _lastPercent = percent;
                int completedBars = percent / 2; // 每个百分比占0.5个字符，50格满
                string bar = new string('#', completedBars) + new string('-', 50 - completedBars);
                
                Console.Write($"\r进度: [{bar}] {percent}%");
                
                if (!string.IsNullOrEmpty(message))
                {
                    Console.Write($" - {message}");
                }
            }
        }
    }

    /// <summary>
    /// 报告完成状态
    /// </summary>
    /// <param name="message">完成信息（可选）</param>
    public void Complete(string? message = null)
    {
        Console.WriteLine(); // 换行
        if (!string.IsNullOrEmpty(message))
        {
            Console.WriteLine(message);
        }
    }
}

/// <summary>
/// 命令行程序主类
/// </summary>
class Program
{
    /// <summary>
    /// 程序入口点
    /// </summary>
    /// <param name="args">命令行参数数组</param>
    static async Task Main(string[] args)
    {
        // ---------- 参数检查 ----------
        if (args.Length == 0 || (args.Length == 1 && (args[0] == "--help" || args[0] == "-h")))
        {
            PrintUsage();
            return;
        }

        // 特殊命令：--config 显示当前配置（需要定义，此处暂略）
        if (args.Length == 1 && args[0] == "--config")
        {
            // 可以显示默认配置，但为了简化，暂不实现
            Console.WriteLine("当前配置功能尚未实现，请直接使用参数。");
            return;
        }

        if (args.Length < 2)
        {
            Console.WriteLine("错误：缺少必需参数。");
            PrintUsage();
            return;
        }

        string inputPath = args[0];   // 输入 PDF 文件路径
        string outputPath = args[1];  // 输出 PDF 文件路径

        // 验证输入文件是否存在
        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"错误：输入文件不存在 - {inputPath}");
            Environment.ExitCode = 1;
            return;
        }

        // ---------- 设置默认值 ----------
        var mode = TranslationMode.Translate;      // 默认翻译模式：仅译文
        bool translateImages = false;               // 默认不翻译图片（预留功能）
        string model = "llama3.2";                   // 默认 Ollama 模型
        string? fontName = null;                     // 默认无指定字体名称
        string? fontPath = null;                     // 默认无指定字体文件
        string sourceLang = "en";                     // 默认源语言：英语
        string targetLang = "zh";                     // 默认目标语言：中文
        string ollamaUrl = "http://localhost:11434";  // 默认 Ollama 地址
        int timeoutSeconds = 60;                       // 默认超时时间
        bool showProgress = true;                      // 默认显示进度条

        // ---------- 解析可选参数 ----------
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

                // 模型名称
                case "--model":
                    if (i + 1 < args.Length)
                        model = args[++i];
                    break;

                // Ollama API 地址
                case "--url":
                    if (i + 1 < args.Length)
                        ollamaUrl = args[++i];
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

                // 源语言
                case "--source":
                case "-s":
                    if (i + 1 < args.Length)
                        sourceLang = args[++i];
                    break;

                // 目标语言
                case "--target":
                case "-t":
                    if (i + 1 < args.Length)
                        targetLang = args[++i];
                    break;

                // 图片翻译（预留）
                case "--translate-images":
                case "-ti":
                    if (i + 1 < args.Length && bool.TryParse(args[++i], out bool ti))
                        translateImages = ti;
                    break;

                // 禁用进度条
                case "--no-progress":
                    showProgress = false;
                    break;

                // 显示帮助
                case "--help":
                case "-h":
                    PrintUsage();
                    return;

                // 未知参数
                default:
                    Console.WriteLine($"未知参数: {args[i]}");
                    PrintUsage();
                    return;
            }
        }

        // ---------- 验证字体文件路径（如果提供了） ----------
        if (!string.IsNullOrEmpty(fontPath) && !File.Exists(fontPath))
        {
            Console.WriteLine($"警告：指定的字体文件不存在 - {fontPath}");
            Console.WriteLine("将继续使用其他字体加载方式...");
        }

        // ---------- 显示当前配置摘要 ----------
        Console.WriteLine("=== PDFTranslator 配置 ===");
        Console.WriteLine($"输入文件: {inputPath}");
        Console.WriteLine($"输出文件: {outputPath}");
        Console.WriteLine($"翻译模式: {(mode == TranslationMode.Translate ? "仅译文" : "双语对照")}");
        Console.WriteLine($"源语言: {sourceLang} -> 目标语言: {targetLang}");
        Console.WriteLine($"Ollama 地址: {ollamaUrl}");
        Console.WriteLine($"模型: {model}");
        Console.WriteLine($"超时: {timeoutSeconds} 秒");
        if (!string.IsNullOrEmpty(fontName)) Console.WriteLine($"字体名称: {fontName}");
        if (!string.IsNullOrEmpty(fontPath)) Console.WriteLine($"字体路径: {fontPath}");
        Console.WriteLine();

        // ---------- 配置依赖注入 ----------
        var services = new ServiceCollection();

        // 添加日志服务，输出到控制台
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning); // 减少干扰，进度条由我们自行控制
        });

        // 添加核心翻译服务，使用自定义 Ollama 地址和模型
        services.AddPDFTranslatorCore(ollamaBaseUrl: ollamaUrl, model: model);

        // 构建服务提供程序
        var serviceProvider = services.BuildServiceProvider();

        // 获取所需实例
        var translator = serviceProvider.GetRequiredService<PdfTranslator>();
        var options = serviceProvider.GetRequiredService<TranslationOptions>();

        // 将命令行参数的值设置到配置选项中
        options.Mode = mode;
        options.TranslateImages = translateImages;
        options.FontName = fontName;
        options.FontPath = fontPath;
        options.SourceLanguage = sourceLang;
        options.TargetLanguage = targetLang;

        // ---------- 设置进度报告器（如果需要） ----------
        if (showProgress)
        {
            translator.SetProgressReporter(new ConsoleProgressReporter());
        }

        // ---------- 测试 Ollama 连接（可选，但推荐） ----------
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

        // ---------- 执行翻译 ----------
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
    /// 打印程序使用说明
    /// </summary>
    static void PrintUsage()
    {
        Console.WriteLine("PDFTranslator.CLI - 基于 Ollama 的 PDF 翻译器命令行版");
        Console.WriteLine("==================================================");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  PDFTranslator.CLI <输入PDF> <输出PDF> [选项]");
        Console.WriteLine();
        Console.WriteLine("必需参数:");
        Console.WriteLine("  <输入PDF>               要翻译的 PDF 文件路径");
        Console.WriteLine("  <输出PDF>               翻译后保存的 PDF 文件路径");
        Console.WriteLine();
        Console.WriteLine("翻译选项:");
        Console.WriteLine("  --mode, -m <模式>       翻译模式：translate 或 bilingual，默认 translate");
        Console.WriteLine("  --source, -s <代码>     源语言代码 (如 en, zh, ja, fr)，默认 en");
        Console.WriteLine("  --target, -t <代码>     目标语言代码 (如 zh, en, ja, de)，默认 zh");
        Console.WriteLine();
        Console.WriteLine("Ollama 配置:");
        Console.WriteLine("  --model <模型名>         使用的模型名称，默认 llama3.2");
        Console.WriteLine("  --url <地址>             Ollama API 地址，默认 http://localhost:11434");
        Console.WriteLine("  --timeout <秒数>         请求超时时间，默认 60 秒");
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
        Console.WriteLine("  PDFTranslator.CLI doc.pdf output.pdf");
        Console.WriteLine("  PDFTranslator.CLI doc.pdf output.pdf --mode bilingual --source en --target zh");
        Console.WriteLine("  PDFTranslator.CLI doc.pdf output.pdf --model qwen2.5 --font-name SimSun");
        Console.WriteLine("  PDFTranslator.CLI doc.pdf output.pdf --url http://192.168.1.100:11434 --timeout 120");
    }
}