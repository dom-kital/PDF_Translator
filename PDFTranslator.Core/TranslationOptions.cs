using System.Collections.Generic;

namespace PDFTranslator.Core;

/// <summary>
/// 翻译配置选项，用于控制翻译行为、语言选择、字体设置等。
/// 该类的实例通过依赖注入在应用程序中共享，可在运行时动态修改。
/// </summary>
public class TranslationOptions
{
    // ========== Ollama 模型配置 ==========

    /// <summary>
    /// Ollama 使用的模型名称，例如 "llama3.2"、"qwen2.5"、"deepseek-r1" 等。
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

    // ========== 页面范围设置 ==========

    /// <summary>
    /// 页面范围选择模式：All（全部页面）、Range（页码范围）、Single（单个页面）
    /// 默认值为 All。
    /// </summary>
    public PageRangeMode PageRangeMode { get; set; } = PageRangeMode.All;

    /// <summary>
    /// 页码范围（例如 "1-5,7,9-11"）
    /// 当 PageRangeMode 为 Range 时有效
    /// </summary>
    public string? PageRange { get; set; }

    /// <summary>
    /// 单个页面页码
    /// 当 PageRangeMode 为 Single 时有效
    /// </summary>
    public int? SinglePage { get; set; }

    /// <summary>
    /// 解析后的页面列表（由 PdfTranslator 内部使用）
    /// </summary>
    public List<int>? ParsedPages { get; set; }

    // ========== 流式处理配置 ==========

    /// <summary>
    /// 是否使用流式处理模式。
    /// 流式处理模式下，程序会逐页处理 PDF，任何时候只有一页在内存中。
    /// 这可以大幅降低内存使用，特别适合处理大型 PDF 文件。
    /// 默认值为 true。
    /// </summary>
    public bool UseStreamingMode { get; set; } = true;

    /// <summary>
    /// 每批处理的文本块数量。
    /// 将一页中的文本块分成多个批次处理，每批处理完成后会进行垃圾回收。
    /// 较小的值可以降低内存峰值，但会略微降低处理速度。
    /// 默认值为 20，取值范围建议 5-50。
    /// </summary>
    public int TextBlockBatchSize { get; set; } = 20;

    /// <summary>
    /// 每页最大处理的文本块数量。
    /// 当一页的文本块数量超过此值时，程序将只处理最重要的部分（按文本长度排序）。
    /// 这可以避免因页面包含大量小文本块而导致的内存暴增。
    /// 默认值为 100，取值范围建议 50-200。
    /// </summary>
    public int MaxTextBlocksPerPage { get; set; } = 100;

    /// <summary>
    /// 单个文本块的最大字符数。
    /// 超过此长度的文本块将被截断，避免过长的翻译请求。
    /// 默认值为 500，取值范围建议 200-1000。
    /// </summary>
    public int MaxTextBlockLength { get; set; } = 500;

    /// <summary>
    /// 是否在每页处理后强制进行垃圾回收。
    /// 启用此选项可以更积极地释放内存，但会增加 CPU 开销。
    /// 默认值为 true。
    /// </summary>
    public bool ForceGCAfterPage { get; set; } = true;

    /// <summary>
    /// 内存警告阈值（MB）。
    /// 当程序占用内存超过此值时，会记录警告日志并尝试强制垃圾回收。
    /// 默认值为 800 MB。
    /// </summary>
    public long MemoryWarningThresholdMB { get; set; } = 800;

    /// <summary>
    /// 内存临界阈值（MB）。
    /// 当程序占用内存超过此值时，会停止处理并建议用户重启。
    /// 默认值为 1500 MB。
    /// </summary>
    public long MemoryCriticalThresholdMB { get; set; } = 1500;

    // ========== Ollama API 配置 ==========

    /// <summary>
    /// Ollama API 版本: "chat" 或 "generate"
    /// chat: 使用 /api/chat 端点（新版 Ollama 推荐）
    /// generate: 使用 /api/generate 端点（兼容旧版）
    /// 默认值为 "chat"，如果遇到 404 错误可以尝试切换为 "generate"
    /// </summary>
    public string OllamaApiVersion { get; set; } = "chat";

    /// <summary>
    /// 是否自动检测 API 版本。
    /// 如果设为 true，程序会先尝试 chat API，失败后自动切换到 generate API。
    /// 默认值为 true。
    /// </summary>
    public bool AutoDetectApiVersion { get; set; } = true;
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

/// <summary>
/// 页面范围模式枚举，定义三种可选的页面选择方式。
/// </summary>
public enum PageRangeMode
{
    /// <summary>全部页面</summary>
    All,

    /// <summary>页码范围（如 "1-5,7,9-11"）</summary>
    Range,

    /// <summary>单个页面</summary>
    Single
}