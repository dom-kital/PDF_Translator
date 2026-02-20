using System.Net.Http.Json;           // 提供 PostAsJsonAsync 等扩展方法，用于发送 JSON 请求
using Microsoft.Extensions.Logging;   // 提供 ILogger 接口，用于记录日志

namespace PDFTranslator.Core;

/// <summary>
/// Ollama API 客户端服务，负责与本地运行的 Ollama 服务进行通信。
/// 提供文本翻译功能，通过构造合适的提示词调用 Ollama 的生成接口。
/// </summary>
public class OllamaService
{
    private readonly HttpClient _httpClient;      // 用于发送 HTTP 请求的客户端，通过依赖注入配置
    private readonly ILogger<OllamaService> _logger; // 日志记录器，用于记录翻译过程中的信息或错误
    private readonly string _model;                // 当前使用的模型名称，从 TranslationOptions 中获取

    /// <summary>
    /// 构造函数，通过依赖注入获取所需的服务和配置。
    /// </summary>
    /// <param name="httpClient">配置好的 HttpClient 实例，其 BaseAddress 已设为 Ollama 服务的地址（如 http://localhost:11434）。</param>
    /// <param name="logger">日志记录器，用于输出调试信息和错误。</param>
    /// <param name="options">翻译配置选项，从中获取模型名称等设置。</param>
    public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger, TranslationOptions options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _model = options.Model;       // 保存模型名称，供后续翻译调用使用
    }

    /// <summary>
    /// 将指定的文本从源语言翻译为目标语言。
    /// 内部通过调用 Ollama 的 generate 接口，构造包含语言指示的提示词来实现翻译。
    /// </summary>
    /// <param name="text">要翻译的原文文本。</param>
    /// <param name="sourceLang">源语言代码（例如 "en" 表示英语，"zh" 表示中文）。此代码将嵌入提示词中，引导模型理解输入语言。</param>
    /// <param name="targetLang">目标语言代码（例如 "zh" 表示中文，"en" 表示英语）。告诉模型应该输出哪种语言的译文。</param>
    /// <returns>翻译后的文本。如果翻译失败或模型返回空结果，则返回原文。</returns>
    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang)
    {
        // 如果原文为空或仅包含空白字符，直接返回原文（无需翻译）
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // 构造翻译提示词，明确指定源语言和目标语言，并要求保留原始格式（如换行符）
        // 这是指导模型正确翻译的关键步骤。
        var prompt = $"Translate the following {sourceLang} text to {targetLang}. Keep the original formatting (like line breaks) if possible.\n\n{text}";

        // 构建请求体，符合 Ollama API 的 generate 接口要求
        // 参考：https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-completion
        var request = new
        {
            model = _model,      // 指定使用的模型
            prompt = prompt,     // 输入的提示词，包含翻译指令和原文
            stream = false       // 禁用流式响应，一次返回完整结果（简化处理）
        };

        try
        {
            // 发送 POST 请求到 "/api/generate"
            // PostAsJsonAsync 会自动将对象序列化为 JSON 并设置 Content-Type 头
            var response = await _httpClient.PostAsJsonAsync("api/generate", request);

            // 确保请求成功（状态码 2xx），否则抛出异常
            response.EnsureSuccessStatusCode();

            // 将响应内容反序列化为 OllamaResponse 对象（只关心其中的 response 字段）
            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();

            // 如果响应中包含了翻译文本，则返回；否则返回原文作为后备
            return result?.response ?? text;
        }
        catch (Exception ex)
        {
            // 记录翻译过程中的异常，包括原文内容（注意：记录原文可能涉及隐私，但这里是本地服务，相对安全）
            _logger.LogError(ex, "翻译失败，原文：{Text}", text);
            // 失败时返回原文，确保程序不会因此中断，用户可以看到原始内容
            return text;
        }
    }

    /// <summary>
    /// 内部类，用于映射 Ollama API 返回的 JSON 结构。
    /// 我们只关心 generate 接口返回的 "response" 字段。
    /// </summary>
    private class OllamaResponse
    {
        /// <summary>模型生成的文本（可能是 null）</summary>
        public string? response { get; set; }
    }
}