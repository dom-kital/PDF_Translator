namespace PDFTranslator.Core;

/// <summary>
/// 翻译配置选项，用于控制翻译行为和字体选择。
/// </summary>
public class TranslationOptions
{
    /// <summary>
    /// Ollama 使用的模型名称，例如 "llama3.2" 或 "qwen2.5"。
    /// </summary>
    public string Model { get; set; } = "llama3.2";

    /// <summary>
    /// 是否翻译图片中的文字（预留功能，暂未实现）。
    /// </summary>
    public bool TranslateImages { get; set; } = false;

    /// <summary>
    /// 翻译模式：仅译文 或 双语对照。
    /// </summary>
    public TranslationMode Mode { get; set; } = TranslationMode.Translate;

    /// <summary>
    /// 用户指定的字体名称（例如 "SimSun", "Microsoft YaHei"）。
    /// 如果为空，将自动检测系统字体。
    /// </summary>
    public string? FontName { get; set; }

    /// <summary>
    /// 用户指定的字体文件路径（例如 "C:\Fonts\NotoSansSC-Regular.ttf"）。
    /// 如果提供，将优先使用该字体文件。
    /// </summary>
    public string? FontPath { get; set; }
}

/// <summary>
/// 翻译模式枚举。
/// </summary>
public enum TranslationMode
{
    /// <summary>仅译文模式：用译文替换原文（用白色矩形覆盖原文后绘制译文）。</summary>
    Translate,
    /// <summary>双语对照模式：保留原文，在原文下方添加蓝色半透明译文。</summary>
    Bilingual
}