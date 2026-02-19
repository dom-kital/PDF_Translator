using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDFTranslator.Core;

namespace PDFTranslator.CLI;

class Program
{
    static async Task Main(string[] args)
    {
        // 参数不足时显示帮助
        if (args.Length < 2)
        {
            PrintUsage();
            return;
        }

        string inputPath = args[0];
        string outputPath = args[1];

        // 默认值
        var mode = TranslationMode.Translate;
        bool translateImages = false;
        string model = "llama3.2";
        string? fontName = null; // 可选字体名称
        string? fontPath = null; // 可选字体文件路径

        // 解析可选参数
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
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

                case "--model":
                    if (i + 1 < args.Length)
                        model = args[++i];
                    break;

                case "--font-name":
                    if (i + 1 < args.Length)
                        fontName = args[++i];
                    break;

                case "--font-path":
                    if (i + 1 < args.Length)
                        fontPath = args[++i];
                    break;

                case "--help":
                case "-h":
                    PrintUsage();
                    return;

                default:
                    Console.WriteLine($"未知参数: {args[i]}");
                    PrintUsage();
                    return;
            }
        }

        // 配置依赖注入
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        services.AddPDFTranslatorCore(ollamaBaseUrl: "http://localhost:11434", model: model);

        var serviceProvider = services.BuildServiceProvider();
        var translator = serviceProvider.GetRequiredService<PdfTranslator>();
        var options = serviceProvider.GetRequiredService<TranslationOptions>();
        options.Mode = mode;
        options.TranslateImages = translateImages; // 预留，暂未实现
        options.FontName = fontName;
        options.FontPath = fontPath;

        // 执行翻译
        try
        {
            Console.WriteLine($"开始翻译 PDF: {inputPath}");
            Console.WriteLine($"输出路径: {outputPath}");
            Console.WriteLine($"模式: {(mode == TranslationMode.Translate ? "仅译文" : "双语对照")}");
            if (!string.IsNullOrEmpty(fontName))
                Console.WriteLine($"字体名称: {fontName}");
            if (!string.IsNullOrEmpty(fontPath))
                Console.WriteLine($"字体路径: {fontPath}");

            await translator.TranslatePdfAsync(inputPath, outputPath);
            Console.WriteLine("翻译完成！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"翻译过程中发生错误: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("PDFTranslator.CLI - 基于 Ollama 的 PDF 翻译器命令行版");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  PDFTranslator.CLI <输入PDF> <输出PDF> [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --mode, -m <模式>        翻译模式：translate（仅译文）或 bilingual（双语对照），默认为 translate");
        Console.WriteLine("  --model <模型名>          使用的 Ollama 模型名称，默认为 llama3.2");
        Console.WriteLine("  --font-name <名称>        指定字体名称（如 SimSun），系统需安装该字体");
        Console.WriteLine("  --font-path <路径>        指定字体文件路径（如 C:\\Windows\\Fonts\\simsun.ttc）");
        Console.WriteLine("  --help, -h                显示此帮助信息");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  PDFTranslator.CLI input.pdf output.pdf --mode bilingual --font-name SimSun");
        Console.WriteLine("  PDFTranslator.CLI input.pdf output.pdf --font-path C:\\Windows\\Fonts\\msyh.ttc");
    }
}