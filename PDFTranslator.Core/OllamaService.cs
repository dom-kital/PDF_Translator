using System.Net.Http.Json;      // 提供 PostAsJsonAsync 扩展方法
using Microsoft.Extensions.Logging; // 提供 ILogger 接口

namespace PDFTranslator.Core;

/// <summary>
/// 调用本地 Ollama API 的翻译服务
/// </summary>
public class OllamaService
{
    private readonly HttpClient _httpClient;        // 用于发送 HTTP 请求的客户端
    private readonly ILogger<OllamaService> _logger; // 日志记录器
    private readonly string _model;                  // 当前使用的模型名称

    /// <summary>
    /// 构造函数，通过依赖注入提供所需服务
    /// </summary>
    /// <param name="httpClient">配置好的 HttpClient（BaseAddress 已设为 Ollama 地址）</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="options">翻译配置选项（从中获取模型名称）</param>
    public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger, TranslationOptions options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _model = options.Model;
    }

    /// <summary>
    /// 翻译文本
    /// </summary>
    /// <param name="text">要翻译的原文</param>
    /// <param name="sourceLang">源语言（用于提示词，可选）</param>
    /// <param name="targetLang">目标语言（用于提示词，可选）</param>
    /// <returns>翻译后的文本，失败时返回原文</returns>
    public async Task<string> TranslateAsync(string text, string sourceLang = "en", string targetLang = "zh")
    {
        // 如果原文为空或空白，直接返回原文（无需翻译）
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // 构造翻译提示词，要求保留原始格式（如换行符）
        var prompt = $"Translate the following {sourceLang} text to {targetLang}. Keep the original formatting (like line breaks) if possible.\n\n{text}";

        // 构建请求体，符合 Ollama API 的 generate 接口要求
        var request = new
        {
            model = _model,      // 指定模型
            prompt = prompt,     // 输入的提示词
            stream = false       // 禁用流式响应，一次返回完整结果
        };

        try
        {
            // 发送 POST 请求到 "/api/generate"
            var response = await _httpClient.PostAsJsonAsync("api/generate", request);
            // 确保请求成功（状态码 2xx）
            response.EnsureSuccessStatusCode();

            // 将 JSON 响应反序列化为 OllamaResponse 对象
            var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
            // 如果响应中包含了翻译文本则返回，否则返回原文
            return result?.response ?? text;
        }
        catch (Exception ex)
        {
            // 记录错误日志
            _logger.LogError(ex, "翻译失败，原文：{Text}", text);
            // 失败时返回原文，避免程序中断
            return text;
        }
    }

    /// <summary>
    /// 内部类，用于映射 Ollama API 返回的 JSON 结构
    /// </summary>
    private class OllamaResponse
    {
        /// <summary>翻译后的文本（可能为 null）</summary>
        public string? response { get; set; }
    }
}