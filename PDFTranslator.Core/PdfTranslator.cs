using Microsoft.Extensions.Logging;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Extgstate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PDFTranslator.Core;

/// <summary>
/// PDF 翻译器核心类，负责：
/// 1. 从 PDF 提取文本块及其位置信息
/// 2. 调用 Ollama 翻译文本
/// 3. 生成新的 PDF，保留原文或添加译文，并尽量保持原始排版
/// 支持两种模式：双语对照（保留原文+译文）和仅译文（替换原文）
/// 支持进度报告功能，通过 IProgressReporter 接口向 UI 报告处理进度
/// </summary>
public class PdfTranslator
{
    private readonly OllamaService _ollama;           // Ollama 翻译服务
    private readonly ILogger<PdfTranslator> _logger;   // 日志记录器
    private readonly TranslationOptions _options;      // 翻译配置选项（包含语言、字体、模式等）
    private IProgressReporter? _progressReporter;      // 可选的进度报告器，由调用方设置

    /// <summary>
    /// 构造函数，通过依赖注入获取所需服务。
    /// </summary>
    /// <param name="ollama">Ollama 翻译服务实例</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="options">翻译配置选项，包含语言、字体、模式等设置</param>
    public PdfTranslator(OllamaService ollama, ILogger<PdfTranslator> logger, TranslationOptions options)
    {
        _ollama = ollama;
        _logger = logger;
        _options = options;
    }

    /// <summary>
    /// 设置进度报告器，用于接收处理进度更新（由 CLI 或 GUI 在调用翻译前设置）。
    /// </summary>
    /// <param name="reporter">实现了 IProgressReporter 接口的进度报告器</param>
    public void SetProgressReporter(IProgressReporter reporter)
    {
        _progressReporter = reporter;
    }

    /// <summary>
    /// 翻译 PDF 文件的主入口方法。
    /// </summary>
    /// <param name="inputPath">输入 PDF 文件路径</param>
    /// <param name="outputPath">输出 PDF 文件路径</param>
    public async Task TranslatePdfAsync(string inputPath, string outputPath)
    {
        _logger.LogInformation("开始处理 PDF: {InputPath}", inputPath);

        // 使用 using 确保资源释放：读取器、写入器和 PDF 文档对象
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf = new PdfDocument(reader, writer);

        int pageCount = pdf.GetNumberOfPages();

        // 报告初始进度（0%）
        _progressReporter?.Report(0, pageCount, "开始处理...");

        // 逐页处理
        for (int pageNum = 1; pageNum <= pageCount; pageNum++)
        {
            _logger.LogInformation("正在处理第 {PageNum}/{TotalPages} 页...", pageNum, pageCount);

            // 报告当前页进度（pageNum-1 表示已处理完的页数，因为当前页尚未完成）
            _progressReporter?.Report(pageNum - 1, pageCount, $"正在处理第 {pageNum} 页...");

            var page = pdf.GetPage(pageNum);

            // 提取当前页的所有文本块及其位置
            var textBlocks = ExtractTextBlocks(page);

            // 如果当前页没有文本，跳过但保留原页面内容
            if (textBlocks.Count == 0)
            {
                _logger.LogWarning("第 {PageNum} 页没有可翻译的文本", pageNum);
                continue;
            }

            // 在当前页上添加译文（根据配置的模式）
            await AddTranslationsToPage(pdf, page, textBlocks);
        }

        // 报告完成状态
        _progressReporter?.Complete("翻译完成！");
        _logger.LogInformation("PDF 翻译完成，已保存至 {OutputPath}", outputPath);
    }

    /// <summary>
    /// 使用自定义策略提取页面中的所有文本块及其位置矩形。
    /// </summary>
    /// <param name="page">PDF 页面对象</param>
    /// <returns>文本块列表，每个块包含文本内容和位置矩形</returns>
    private List<TextBlock> ExtractTextBlocks(PdfPage page)
    {
        var strategy = new TextBlockExtractionStrategy();
        var parser = new PdfCanvasProcessor(strategy);
        parser.ProcessPageContent(page);
        return strategy.GetTextBlocks();
    }

    /// <summary>
    /// 根据配置的模式在页面上添加译文。
    /// 双语模式：保留原文，在原文下方添加蓝色半透明译文。
    /// 仅译文模式：用白色矩形覆盖原文，然后在相同位置绘制黑色译文。
    /// </summary>
    /// <param name="pdf">PDF 文档对象</param>
    /// <param name="page">当前页面</param>
    /// <param name="blocks">当前页的文本块列表</param>
    private async Task AddTranslationsToPage(PdfDocument pdf, PdfPage page, List<TextBlock> blocks)
    {
        // 创建一个新的内容流，追加到原页面内容之后，保证原内容完全保留
        var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdf);

        // 获取字体（根据用户配置自动选择，支持用户指定、系统检测或内嵌字体）
        PdfFont font;
        try
        {
            font = FontHelper.GetFont(_options, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载字体失败，使用默认字体（中文可能显示为方框）");
            font = PdfFontFactory.CreateFont(); // 回退到默认字体
        }

        float fontSize = 10; // 默认字号，可根据需要调整

        if (_options.Mode == TranslationMode.Bilingual)
        {
            // ========== 双语对照模式 ==========
            canvas.SetColor(ColorConstants.BLUE, true);               // 设置字体颜色为蓝色
            canvas.SetExtGState(new PdfExtGState().SetFillOpacity(0.8f)); // 设置透明度 80%

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text) || block.Rect == null)
                    continue;

                // 调用 Ollama 翻译原文，传递源语言和目标语言代码
                string translated = await _ollama.TranslateAsync(block.Text, _options.SourceLanguage, _options.TargetLanguage);

                float x = block.Rect.GetX();                      // 原文左下角 X 坐标
                float y = block.Rect.GetY() - fontSize - 2;       // 译文放在原文下方，间距 2 点

                // 如果超出页面底部，则放在原文上方
                if (y < 0)
                {
                    y = block.Rect.GetY() + block.Rect.GetHeight() + 2;
                }

                // 绘制译文
                canvas.BeginText()
                    .SetFontAndSize(font, fontSize)
                    .MoveText(x, y)
                    .ShowText(translated)
                    .EndText();
            }
        }
        else // TranslationMode.Translate
        {
            // ========== 仅译文模式 ==========
            canvas.SetFillColor(ColorConstants.WHITE);   // 设置填充色为白色
            canvas.SetStrokeColor(ColorConstants.WHITE); // 设置描边色为白色

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text) || block.Rect == null)
                    continue;

                // 调用 Ollama 翻译原文，传递语言代码
                string translated = await _ollama.TranslateAsync(block.Text, _options.SourceLanguage, _options.TargetLanguage);

                float x = block.Rect.GetX();
                float y = block.Rect.GetY();
                float width = block.Rect.GetWidth();
                float height = block.Rect.GetHeight();

                // 绘制白色矩形覆盖原文区域
                canvas.Rectangle(x, y, width, height).Fill();

                // 切换为黑色绘制译文
                canvas.SetColor(ColorConstants.BLACK, true);
                canvas.BeginText()
                    .SetFontAndSize(font, fontSize)
                    .MoveText(x, y + (height - fontSize) / 2) // 垂直居中
                    .ShowText(translated)
                    .EndText();

                // 恢复填充色为白色，以便下一个矩形使用
                canvas.SetFillColor(ColorConstants.WHITE);
            }
        }
    }

    /// <summary>
    /// 表示一个文本块及其在页面上的位置矩形。
    /// 用于在提取文本时同时记录坐标信息，便于后续精准放置译文。
    /// </summary>
    private class TextBlock
    {
        /// <summary>文本内容（可能为 null）</summary>
        public string? Text { get; set; }

        /// <summary>文本所占的矩形区域（可能为 null）</summary>
        public Rectangle? Rect { get; set; }
    }

    /// <summary>
    /// 自定义文本提取策略，实现 ITextExtractionStrategy 接口。
    /// 用于收集页面中每个文本块及其精确位置，而不是只提取纯文本。
    /// </summary>
    private class TextBlockExtractionStrategy : ITextExtractionStrategy
    {
        private List<TextBlock> _textBlocks = new();

        /// <summary>
        /// 当解析器遇到事件时调用（如文本渲染、图像等）。
        /// 我们只关心 RENDER_TEXT 事件，获取文本及其边界矩形。
        /// </summary>
        /// <param name="data">事件数据</param>
        /// <param name="type">事件类型</param>
        public void EventOccurred(IEventData data, EventType type)
        {
            // 只处理文本渲染事件
            if (type == EventType.RENDER_TEXT)
            {
                var renderInfo = (TextRenderInfo)data;
                var text = renderInfo.GetText();
                if (string.IsNullOrWhiteSpace(text))
                    return;

                // 获取文本的上升线和下降线，用于计算精确的边界矩形
                var ascentLine = renderInfo.GetAscentLine();
                var descentLine = renderInfo.GetDescentLine();

                // 计算矩形的左下角坐标和宽高
                float x1 = Math.Min(ascentLine.GetStartPoint().Get(0), descentLine.GetStartPoint().Get(0));
                float x2 = Math.Max(ascentLine.GetEndPoint().Get(0), descentLine.GetEndPoint().Get(0));
                float y1 = Math.Min(descentLine.GetStartPoint().Get(1), descentLine.GetEndPoint().Get(1));
                float y2 = Math.Max(ascentLine.GetStartPoint().Get(1), ascentLine.GetEndPoint().Get(1));
                var rect = new Rectangle(x1, y1, x2 - x1, y2 - y1);

                // 添加到列表
                _textBlocks.Add(new TextBlock { Text = text, Rect = rect });
            }
        }

        /// <summary>
        /// 返回此策略关心的事件类型，以优化解析性能。
        /// </summary>
        public ICollection<EventType> GetSupportedEvents()
        {
            return new List<EventType> { EventType.RENDER_TEXT };
        }

        /// <summary>
        /// 获取收集到的所有文本块。
        /// </summary>
        public List<TextBlock> GetTextBlocks() => _textBlocks;

        /// <summary>
        /// 获取提取到的全部文本（用于兼容旧版接口）。
        /// </summary>
        public string GetResultantText() => string.Join("", _textBlocks.Select(t => t.Text));

        /// <summary>
        /// 旧版接口遗留方法，无需实现。
        /// </summary>
        public void RenderText(TextRenderInfo renderInfo) { }
    }
}