using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace PDFTranslator.Core;

/// <summary>
/// 调用本地 Ollama API 的翻译服务。
/// </summary>
public class OllamaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;
    private readonly string _model;

    /// <summary>
    /// 构造函数，通过依赖注入获取所需服务。
    /// </summary>
    /// <param name="httpClient">配置好的 HttpClient（BaseAddress 已设为 Ollama 地址）。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="options">翻译配置选项，从中获取模型名称。</param>
    public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger, TranslationOptions options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _model = options.Model;
    }

    /// <summary>
    /// 翻译文本。
    /// </summary>
    /// <param name="text">要翻译的原文。</param>
    /// <param name="sourceLang">源语言（用于提示词，可选）。</param>
    /// <param name="targetLang">目标语言（用于提示词，可选）。</param>
    /// <returns>翻译后的文本，失败时返回原文。</returns>
    public async Task<string> TranslateAsync(string text, string sourceLang = "en", string targetLang = "zh")
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // 构造翻译提示词，要求保留原始格式（如换行符）
        var prompt = $"Translate the following {sourceLang} text to {targetLang}. Keep the original formatting (like line breaks) if possible.\n\n{text}";

        // 构建请求体，符合 Ollama API 的 generate 接口要求
        var request = new
        {
            model = _model,
            prompt = prompt,
            stream = false // 禁用流式响应，一次返回完整结果
        };

        try
        {
            // 发送 POST 请求到 "/api/generate"
            var response = await _httpClient.PostAsJsonAsync("api/generate", request);
            response.EnsureSuccessStatusCode();

            // 将 JSON 响应反序列化为 OllamaResponse 对象
            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
            return result?.response ?? text;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻译失败，原文：{Text}", text);
            return text; // 失败时返回原文，避免程序中断
        }
    }

    /// <summary>
    /// 内部类，用于映射 Ollama API 返回的 JSON 结构。
    /// </summary>
    private class OllamaResponse
    {
        public string? response { get; set; }
    }
}