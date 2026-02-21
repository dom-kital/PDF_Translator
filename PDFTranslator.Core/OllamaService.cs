using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace PDFTranslator.Core;

public class OllamaService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaService> _logger;
    private readonly TranslationOptions _options;  // 保存完整配置
    
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    private const int DEFAULT_TIMEOUT_SECONDS = 60;
    private const int MAX_RETRIES = 2;
    private const int RETRY_DELAY_MS = 1000;
    private const int MAX_TEXT_LENGTH = 1500;
    
    private static readonly LRUCache<string, string> _translationCache = new LRUCache<string, string>(100);

    public OllamaService(HttpClient httpClient, ILogger<OllamaService> logger, TranslationOptions options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options;  // 保存完整配置
        _httpClient.Timeout = TimeSpan.FromSeconds(DEFAULT_TIMEOUT_SECONDS);
    }

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        string cacheKey = $"{sourceLang}:{targetLang}:{text.GetHashCode()}";
        
        if (_translationCache.TryGetValue(cacheKey, out string? cachedResult) && cachedResult != null)
        {
            _logger.LogDebug("使用缓存的翻译结果");
            return cachedResult;
        }

        string processedText = text;
        if (text.Length > MAX_TEXT_LENGTH)
        {
            _logger.LogWarning("文本过长 ({Length} 字符)，将被截断为 {MaxLength} 字符", 
                text.Length, MAX_TEXT_LENGTH);
            processedText = text.Substring(0, MAX_TEXT_LENGTH) + "...";
        }

        await _semaphore.WaitAsync();
        
        try
        {
            _logger.LogDebug("开始翻译请求，使用模型: {Model}", _options.Model);
            string result = await TranslateWithRetryAsync(processedText, sourceLang, targetLang);
            
            if (processedText.Length < 500 && result != text)
            {
                _translationCache.Put(cacheKey, result);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻译过程中发生致命错误");
            return text;
        }
        finally
        {
            _semaphore.Release();
            _logger.LogDebug("翻译请求完成，信号量释放");
        }
    }

    private async Task<string> TranslateWithRetryAsync(string text, string sourceLang, string targetLang)
    {
        int retryCount = 0;
        Exception? lastException = null;

        while (retryCount < MAX_RETRIES)
        {
            try
            {
                return await ExecuteTranslateAsync(text, sourceLang, targetLang);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("404"))
            {
                _logger.LogError("API 404错误，请检查模型名称是否正确: {Model}", _options.Model);
                // 404错误不重试，直接返回原文
                return text;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("timeout") || ex.Message.Contains("Timeout"))
            {
                lastException = ex;
                retryCount++;
                _logger.LogWarning("翻译超时 (尝试 {RetryCount}/{MaxRetries})", retryCount, MAX_RETRIES);
                
                if (retryCount < MAX_RETRIES)
                {
                    await Task.Delay(RETRY_DELAY_MS * retryCount);
                }
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("503") || ex.Message.Contains("Service Unavailable"))
            {
                lastException = ex;
                retryCount++;
                _logger.LogWarning("Ollama 服务不可用 (尝试 {RetryCount}/{MaxRetries})", retryCount, MAX_RETRIES);
                
                if (retryCount < MAX_RETRIES)
                {
                    await Task.Delay(RETRY_DELAY_MS * 2);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON 解析错误，无需重试");
                return text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻译请求失败");
                throw;
            }
        }

        _logger.LogError(lastException, "翻译失败，已达到最大重试次数 {MaxRetries}", MAX_RETRIES);
        return text;
    }

    private async Task<string> ExecuteTranslateAsync(string text, string sourceLang, string targetLang)
    {
        var prompt = $"Translate the following {sourceLang} text to {targetLang}. Keep the original formatting (like line breaks) if possible.\n\n{text}";

        try
        {
            _logger.LogDebug("发送翻译请求: 模型={Model}, 源语言={Source}, 目标语言={Target}, 原文长度={Length}", 
                _options.Model, sourceLang, targetLang, text.Length);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DEFAULT_TIMEOUT_SECONDS));
            
            // 首先尝试 chat API
            var chatRequest = new
            {
                model = _options.Model,  // 使用 _options.Model
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                stream = false
            };
            
            var response = await _httpClient.PostAsJsonAsync("api/chat", chatRequest, cts.Token);
            
            if (response.IsSuccessStatusCode)
            {
                var chatResult = await response.Content.ReadFromJsonAsync<OllamaChatResponse>();
                if (chatResult?.message?.content != null)
                {
                    _logger.LogDebug("chat API 翻译成功");
                    return chatResult.message.content;
                }
            }
            
            // 如果 chat API 失败，尝试 generate API
            _logger.LogWarning("chat API 失败，尝试 generate API");
            
            var generateRequest = new
            {
                model = _options.Model,  // 使用 _options.Model
                prompt = prompt,
                stream = false
            };
            
            response = await _httpClient.PostAsJsonAsync("api/generate", generateRequest, cts.Token);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OllamaResponse>();
                if (result?.response != null)
                {
                    _logger.LogDebug("generate API 翻译成功");
                    return result.response;
                }
            }
            
            // 如果都失败，记录错误
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("所有 API 调用失败，状态码: {StatusCode}, 错误: {Error}", 
                response.StatusCode, errorContent);
            
            return text;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("翻译请求超时");
            throw new HttpRequestException("Request timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Ollama 服务请求失败: {Message}", ex.Message);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "解析 Ollama 响应失败");
            return text;
        }
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _httpClient.GetAsync("api/tags", cts.Token);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            
            var models = new List<string>();
            if (root.TryGetProperty("models", out var modelsArray))
            {
                foreach (var model in modelsArray.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameElement))
                    {
                        models.Add(nameElement.GetString() ?? "unknown");
                    }
                }
            }
            
            return models;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取模型列表失败");
            return new List<string>();
        }
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _httpClient.GetAsync("api/tags", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
    
    public static void ClearCache()
    {
        _translationCache.Clear();
    }
}

// 以下类定义保持不变...
public class LRUCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _cache;
    private readonly LinkedList<CacheItem> _lruList;
    private readonly object _lock = new object();

    private class CacheItem
    {
        public TKey Key { get; }
        public TValue Value { get; set; }

        public CacheItem(TKey key, TValue value)
        {
            Key = key;
            Value = value;
        }
    }

    public LRUCache(int capacity)
    {
        _capacity = capacity;
        _cache = new Dictionary<TKey, LinkedListNode<CacheItem>>(capacity);
        _lruList = new LinkedList<CacheItem>();
    }

    public bool TryGetValue(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            
            value = default;
            return false;
        }
    }

    public void Put(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var node))
            {
                node.Value.Value = value;
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }
            else
            {
                if (_cache.Count >= _capacity)
                {
                    var last = _lruList.Last;
                    if (last != null)
                    {
                        _cache.Remove(last.Value.Key);
                        _lruList.RemoveLast();
                    }
                }
                
                var newItem = new CacheItem(key, value);
                var newNode = new LinkedListNode<CacheItem>(newItem);
                _lruList.AddFirst(newNode);
                _cache.Add(key, newNode);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _lruList.Clear();
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _cache.Count;
            }
        }
    }
}

internal class OllamaResponse
{
    [JsonPropertyName("response")]
    public string? response { get; set; }
}

internal class OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaMessage? message { get; set; }
}

internal class OllamaMessage
{
    [JsonPropertyName("content")]
    public string? content { get; set; }
}