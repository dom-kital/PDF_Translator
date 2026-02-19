// 引入必要的命名空间
using Microsoft.Extensions.DependencyInjection;   // 依赖注入容器
using Microsoft.Extensions.Logging;                // 日志接口
using PDFTranslator.Core;                           // 核心翻译服务
using System;                                       // 基础类型

namespace PDFTranslator.CLI;

/// <summary>
/// 命令行入口类
/// </summary>
class Program
{
    /// <summary>
    /// 主入口函数，解析命令行参数并执行翻译
    /// </summary>
    /// <param name="args">命令行参数数组</param>
    static async Task Main(string[] args)
    {
        // ---------- 参数解析 ----------
        if (args.Length < 2)
        {
            PrintUsage();  // 参数不足，显示帮助
            return;
        }

        string inputPath = args[0];      // 输入 PDF 路径（第一个参数）
        string outputPath = args[1];     // 输出 PDF 路径（第二个参数）

        // 默认值
        var mode = TranslationMode.Translate;   // 默认翻译模式：仅译文
        bool translateImages = false;            // 默认不翻译图片
        string model = "llama3.2";                // 默认模型

        // 从第三个参数开始解析可选参数
        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode":
                case "-m":
                    // 模式参数：--mode translate 或 --mode bilingual
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

                case "--translate-images":
                case "-ti":
                    // 图片翻译开关：--translate-images true 或 false
                    if (i + 1 < args.Length && bool.TryParse(args[++i], out bool ti))
                    {
                        translateImages = ti;
                    }
                    break;

                case "--model":
                    // 指定 Ollama 模型：--model llama3.2
                    if (i + 1 < args.Length)
                    {
                        model = args[++i];
                    }
                    break;

                case "--help":
                case "-h":
                    // 显示帮助并退出
                    PrintUsage();
                    return;

                default:
                    Console.WriteLine($"未知参数: {args[i]}");
                    PrintUsage();
                    return;
            }
        }

        // ---------- 配置依赖注入 ----------
        var services = new ServiceCollection();

        // 添加日志，输出到控制台
        services.AddLogging(builder =>
        {
            builder.AddConsole();                // 控制台日志
            builder.SetMinimumLevel(LogLevel.Information); // 最低日志级别
        });

        // 添加核心翻译服务，指定 Ollama 地址（默认本地）和模型
        services.AddPDFTranslatorCore(ollamaBaseUrl: "http://localhost:11434", model: model);

        // 构建服务提供程序
        var serviceProvider = services.BuildServiceProvider();

        // 获取所需服务
        var translator = serviceProvider.GetRequiredService<PdfTranslator>();  // PDF 翻译器
        var options = serviceProvider.GetRequiredService<TranslationOptions>(); // 配置选项

        // 将命令行参数设置到配置对象中
        options.Mode = mode;
        options.TranslateImages = translateImages;

        // ---------- 执行翻译 ----------
        try
        {
            Console.WriteLine($"开始翻译 PDF: {inputPath}");
            Console.WriteLine($"输出路径: {outputPath}");
            Console.WriteLine($"模式: {(mode == TranslationMode.Translate ? "仅译文" : "双语对照")}");
            Console.WriteLine($"翻译图片: {(translateImages ? "是" : "否")}");
            Console.WriteLine($"模型: {model}");

            // 调用翻译方法
            await translator.TranslatePdfAsync(inputPath, outputPath);

            Console.WriteLine("翻译完成！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"翻译过程中发生错误: {ex.Message}");
            Environment.ExitCode = 1;  // 设置退出码为 1 表示错误
        }
    }

    /// <summary>
    /// 打印使用说明
    /// </summary>
    static void PrintUsage()
    {
        Console.WriteLine("PDFTranslator.CLI - 基于 Ollama 的 PDF 翻译器命令行版");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  PDFTranslator.CLI <输入PDF> <输出PDF> [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --mode, -m <模式>        翻译模式：translate（仅译文）或 bilingual（双语对照），默认为 translate");
        Console.WriteLine("  --translate-images, -ti <true/false>  是否翻译图片中的文字（预留，暂未实现），默认为 false");
        Console.WriteLine("  --model <模型名>          使用的 Ollama 模型名称，默认为 llama3.2");
        Console.WriteLine("  --help, -h                显示此帮助信息");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  PDFTranslator.CLI input.pdf output.pdf --mode bilingual --model qwen2.5");
    }
}