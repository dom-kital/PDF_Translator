namespace PDFTranslator.Core;

/// <summary>
/// 翻译配置选项，用于控制翻译行为、语言选择、字体设置等。
/// 该类的实例通过依赖注入在应用程序中共享，可在运行时动态修改。
/// </summary>
public class TranslationOptions
{
    // ========== Ollama 模型配置 ==========

    /// <summary>
    /// Ollama 使用的模型名称，例如 "llama3.2"、"qwen2.5"、"llama3.2-vision" 等。
    /// 默认值为 "llama3.2"，用户可在 GUI 或 CLI 中修改。
    /// </summary>
    public string Model { get; set; } = "llama3.2";

    /// <summary>
    /// 是否翻译图片中的文字（预留功能，暂未完全实现）。
    /// 当前版本仅处理 PDF 中的文本内容，图片翻译计划在后续版本中支持。
    /// </summary>
    public bool TranslateImages { get; set; } = false;

    // ========== 翻译模式 ==========

    /// <summary>
    /// 翻译模式：仅译文（Translate）或双语对照（Bilingual）。
    /// 默认值为 Translate。
    /// </summary>
    public TranslationMode Mode { get; set; } = TranslationMode.Translate;

    // ========== 字体设置 ==========

    /// <summary>
    /// 用户指定的字体名称（例如 "SimSun"、"Microsoft YaHei"、"PingFang SC" 等）。
    /// 如果该值为空或 null，程序将自动检测系统字体；若检测失败，则回退到内嵌字体。
    /// 注意：字体名称需要与系统中注册的字体名称完全一致。
    /// </summary>
    public string? FontName { get; set; }

    /// <summary>
    /// 用户指定的字体文件路径（例如 "C:\Windows\Fonts\simsun.ttc" 或 "/usr/share/fonts/noto/NotoSansCJK-Regular.ttc"）。
    /// 如果提供了有效的路径，程序将优先使用该字体文件，忽略 FontName 的设置。
    /// 支持的文件格式：.ttf、.ttc、.otf。
    /// </summary>
    public string? FontPath { get; set; }

    // ========== 语言设置 ==========

    /// <summary>
    /// 源语言代码，遵循 ISO 639-1 标准（如 "en" 表示英语，"zh" 表示中文，"ja" 表示日语）。
    /// 默认值为 "en"（英语）。此代码将用于构造翻译提示词，告诉模型源语言是什么。
    /// 注意：模型对语言的支持程度取决于其训练数据，部分模型可能不支持所有语言。
    /// </summary>
    public string SourceLanguage { get; set; } = "en";

    /// <summary>
    /// 目标语言代码，同样遵循 ISO 639-1 标准（如 "zh" 表示中文，"en" 表示英语，"fr" 表示法语）。
    /// 默认值为 "zh"（中文）。程序将要求模型将源语言文本翻译为此目标语言。
    /// 与 SourceLanguage 类似，模型需支持目标语言才能获得较高质量的翻译。
    /// </summary>
    public string TargetLanguage { get; set; } = "zh";
}

/// <summary>
/// 翻译模式枚举，定义两种可选的翻译行为。
/// </summary>
public enum TranslationMode
{
    /// <summary>仅译文模式：用白色矩形覆盖原文后绘制黑色译文，适合背景为白色的文档。</summary>
    Translate,

    /// <summary>双语对照模式：保留原文，在原文下方添加蓝色半透明译文，便于对比学习。</summary>
    Bilingual
}