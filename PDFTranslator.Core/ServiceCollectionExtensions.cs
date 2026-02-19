using Microsoft.Extensions.DependencyInjection;

namespace PDFTranslator.Core;

/// <summary>
/// 依赖注入扩展方法，用于将核心服务注册到容器。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 PDFTranslator 核心服务（OllamaService、PdfTranslator 等）。
    /// </summary>
    /// <param name="services">IServiceCollection 实例。</param>
    /// <param name="ollamaBaseUrl">Ollama 服务地址，默认为 http://localhost:11434。</param>
    /// <param name="model">使用的模型名称，默认为 llama3.2。</param>
    /// <returns>返回 IServiceCollection 以支持链式调用。</returns>
    public static IServiceCollection AddPDFTranslatorCore(this IServiceCollection services,
        string ollamaBaseUrl = "http://localhost:11434",
        string model = "llama3.2")
    {
        // 注册配置选项为单例，整个应用共享同一个配置对象
        var options = new TranslationOptions { Model = model };
        services.AddSingleton(options);

        // 注册 HttpClient 供 OllamaService 使用，并设置 BaseAddress
        services.AddHttpClient<OllamaService>(client =>
        {
            client.BaseAddress = new Uri(ollamaBaseUrl);
        });

        // 注册 PdfTranslator 为瞬态，每次请求都创建新实例
        services.AddTransient<PdfTranslator>();

        return services;
    }
}