using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PDFTranslator.Core;

/// <summary>
/// 依赖注入扩展方法，用于将核心服务注册到容器中。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 PDFTranslator 核心服务到依赖注入容器。
    /// </summary>
    public static IServiceCollection AddPDFTranslatorCore(
        this IServiceCollection services,
        string ollamaBaseUrl = "http://localhost:11434",
        string model = "llama3.2")
    {
        // 1. 注册配置选项为单例
        var options = new TranslationOptions { Model = model };
        services.AddSingleton(options);

        // 2. 注册 HttpClient 供 OllamaService 使用
        services.AddHttpClient<OllamaService>(client =>
        {
            client.BaseAddress = new Uri(ollamaBaseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "PDFTranslator/1.0");
        });
        // 移除过时的 ConfigureHttpMessageHandlerBuilder 调用

        // 3. 注册 PdfTranslator 为瞬态
        services.AddTransient<PdfTranslator>();

        return services;
    }

    /// <summary>
    /// 添加 PDFTranslator 核心服务，允许从配置对象读取设置。
    /// </summary>
    public static IServiceCollection AddPDFTranslatorCore(
        this IServiceCollection services,
        Action<TranslationOptions> configureOptions)
    {
        var options = new TranslationOptions();
        configureOptions(options);
        services.AddSingleton(options);

        services.AddHttpClient<OllamaService>(client =>
        {
            client.BaseAddress = new Uri("http://localhost:11434");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "PDFTranslator/1.0");
        });

        services.AddTransient<PdfTranslator>();

        return services;
    }

    /// <summary>
    /// 添加 PDFTranslator 核心服务，使用完整的自定义配置。
    /// </summary>
    public static IServiceCollection AddPDFTranslatorCore(
        this IServiceCollection services,
        string ollamaBaseUrl,
        string model,
        Action<TranslationOptions>? configureOptions = null)
    {
        var options = new TranslationOptions { Model = model };
        configureOptions?.Invoke(options);
        services.AddSingleton(options);

        services.AddHttpClient<OllamaService>(client =>
        {
            client.BaseAddress = new Uri(ollamaBaseUrl);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("User-Agent", "PDFTranslator/1.0");
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        services.AddTransient<PdfTranslator>();

        return services;
    }
}