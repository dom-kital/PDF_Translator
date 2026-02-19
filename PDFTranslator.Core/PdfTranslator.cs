using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Extgstate;
using Microsoft.Extensions.Logging;

namespace PDFTranslator.Core;

/// <summary>
/// PDF 翻译器主类，负责提取原文、调用翻译、生成新 PDF，并尽量保持原排版。
/// </summary>
public class PdfTranslator
{
    private readonly OllamaService _ollama;
    private readonly ILogger<PdfTranslator> _logger;
    private readonly TranslationOptions _options;

    public PdfTranslator(OllamaService ollama, ILogger<PdfTranslator> logger, TranslationOptions options)
    {
        _ollama = ollama;
        _logger = logger;
        _options = options;
    }

    /// <summary>
    /// 翻译 PDF 文件的入口方法。
    /// </summary>
    /// <param name="inputPath">输入 PDF 文件路径。</param>
    /// <param name="outputPath">输出 PDF 文件路径。</param>
    public async Task TranslatePdfAsync(string inputPath, string outputPath)
    {
        _logger.LogInformation("开始处理 PDF: {InputPath}", inputPath);

        // 使用 using 确保资源释放
        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf = new PdfDocument(reader, writer);

        int pageCount = pdf.GetNumberOfPages();
        for (int pageNum = 1; pageNum <= pageCount; pageNum++)
        {
            _logger.LogInformation("正在处理第 {PageNum}/{TotalPages} 页...", pageNum, pageCount);

            var page = pdf.GetPage(pageNum);
            // 提取当前页的所有文本块及其位置
            var textBlocks = ExtractTextBlocks(page);
            if (textBlocks.Count == 0)
            {
                _logger.LogWarning("第 {PageNum} 页没有可翻译的文本", pageNum);
                continue;
            }

            // 在页面上添加译文（根据模式不同）
            await AddTranslationsToPage(pdf, page, textBlocks);
        }

        _logger.LogInformation("PDF 翻译完成，已保存至 {OutputPath}", outputPath);
    }

    /// <summary>
    /// 使用自定义策略提取页面中的所有文本块及其位置矩形。
    /// </summary>
    private List<TextBlock> ExtractTextBlocks(PdfPage page)
    {
        var strategy = new TextBlockExtractionStrategy();
        var parser = new PdfCanvasProcessor(strategy);
        parser.ProcessPageContent(page);
        return strategy.GetTextBlocks();
    }

    /// <summary>
    /// 根据配置的模式在页面上添加译文。
    /// </summary>
    private async Task AddTranslationsToPage(PdfDocument pdf, PdfPage page, List<TextBlock> blocks)
    {
        // 创建一个新的内容流，追加到原页面内容之后，保证原内容完全保留
        var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdf);

        // 获取字体（根据用户配置自动选择）
        PdfFont font;
        try
        {
            font = FontHelper.GetFont(_options, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载字体失败，将使用默认字体（中文可能显示为方框）");
            font = PdfFontFactory.CreateFont(); // 回退到默认字体
        }

        float fontSize = 10; // 默认字号，可根据需要调整

        if (_options.Mode == TranslationMode.Bilingual)
        {
            // 双语模式：保留原文，在原文下方添加蓝色半透明译文
            canvas.SetColor(ColorConstants.BLUE, true);               // 设置字体颜色为蓝色
            canvas.SetExtGState(new PdfExtGState().SetFillOpacity(0.8f)); // 设置透明度 80%

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text) || block.Rect == null)
                    continue;

                string translated = await _ollama.TranslateAsync(block.Text);

                float x = block.Rect.GetX();                      // 原文左下角 X 坐标
                float y = block.Rect.GetY() - fontSize - 2;       // 译文放在原文下方，间距 2 点

                // 如果超出页面底部，则放在原文上方
                if (y < 0)
                {
                    y = block.Rect.GetY() + block.Rect.GetHeight() + 2;
                }

                canvas.BeginText()
                    .SetFontAndSize(font, fontSize)
                    .MoveText(x, y)
                    .ShowText(translated)
                    .EndText();
            }
        }
        else // TranslationMode.Translate
        {
            // 仅译文模式：用白色矩形覆盖原文，然后在相同位置绘制黑色译文
            canvas.SetFillColor(ColorConstants.WHITE);   // 设置填充色为白色
            canvas.SetStrokeColor(ColorConstants.WHITE); // 设置描边色为白色

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text) || block.Rect == null)
                    continue;

                string translated = await _ollama.TranslateAsync(block.Text);

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
    /// </summary>
    private class TextBlock
    {
        public string? Text { get; set; }
        public Rectangle? Rect { get; set; }
    }

    /// <summary>
    /// 自定义文本提取策略，实现 ITextExtractionStrategy 接口，
    /// 用于收集页面中每个文本块及其精确位置。
    /// </summary>
    private class TextBlockExtractionStrategy : ITextExtractionStrategy
    {
        private List<TextBlock> _textBlocks = new();

        /// <summary>
        /// 当解析器处理页面内容时触发的事件。
        /// </summary>
        /// <param name="data">事件数据，如文本渲染信息。</param>
        /// <param name="type">事件类型。</param>
        public void EventOccurred(IEventData data, EventType type)
        {
            // 只关心文本渲染事件
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