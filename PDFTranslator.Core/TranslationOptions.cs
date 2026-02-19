namespace PDFTranslator.Core;

/// <summary>
/// 翻译配置选项，用于控制翻译行为和模式
/// </summary>
public class TranslationOptions
{
    /// <summary>
    /// Ollama 使用的模型名称（例如 "llama3.2", "qwen2.5" 等）
    /// </summary>
    public string Model { get; set; } = "llama3.2";

    /// <summary>
    /// 是否翻译图片中的文字（预留功能，暂未实现）
    /// </summary>
    public bool TranslateImages { get; set; } = false;

    /// <summary>
    /// 翻译模式：仅译文 或 双语对照
    /// </summary>
    public TranslationMode Mode { get; set; } = TranslationMode.Translate;
}

/// <summary>
/// 翻译模式枚举
/// </summary>
public enum TranslationMode
{
    /// <summary>仅译文模式（替换原文）</summary>
    Translate,

    /// <summary>双语对照模式（保留原文，添加译文）</summary>
    Bilingual
}