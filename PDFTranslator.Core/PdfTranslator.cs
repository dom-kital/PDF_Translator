using iText.Kernel.Colors;                     // 颜色常量
using iText.Kernel.Font;                        // 字体操作
using iText.Kernel.Geom;                        // 几何图形（如矩形）
using iText.Kernel.Pdf;                          // 核心 PDF 文档类
using iText.Kernel.Pdf.Canvas;                   // PDF 画布，用于绘制内容
using iText.Kernel.Pdf.Canvas.Parser;            // PDF 内容解析器
using iText.Kernel.Pdf.Canvas.Parser.Data;       // 解析过程中的事件数据
using iText.Kernel.Pdf.Canvas.Parser.Listener;   // 解析监听器接口
using iText.Kernel.Pdf.Extgstate;                 // 扩展图形状态（用于透明度等）
using Microsoft.Extensions.Logging;               // 日志
using System;                                      // 基础类型（如 Math）
using System.Collections.Generic;                  // 集合类型
using System.Linq;                                 // LINQ 扩展（用于 Select 等）
using System.Threading.Tasks;                      // 异步任务

namespace PDFTranslator.Core;

/// <summary>
/// PDF 翻译器核心类：负责提取文本、调用翻译、生成新 PDF，尽量保持原排版。
/// 支持两种模式：双语对照（保留原文，添加译文）和仅译文（用译文覆盖原文）。
/// </summary>
public class PdfTranslator
{
    private readonly OllamaService _ollama;         // 翻译服务
    private readonly ILogger<PdfTranslator> _logger; // 日志记录器
    private readonly TranslationOptions _options;    // 翻译配置选项（模式、模型等）

    /// <summary>
    /// 构造函数，通过依赖注入获取所需服务
    /// </summary>
    public PdfTranslator(OllamaService ollama, ILogger<PdfTranslator> logger, TranslationOptions options)
    {
        _ollama = ollama;
        _logger = logger;
        _options = options;
    }

    /// <summary>
    /// 翻译 PDF 文件的主入口
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
        // 逐页处理
        for (int pageNum = 1; pageNum <= pageCount; pageNum++)
        {
            _logger.LogInformation("正在处理第 {PageNum}/{TotalPages} 页...", pageNum, pageCount);

            var page = pdf.GetPage(pageNum);
            // 提取当前页的所有文本块及其位置
            var textBlocks = ExtractTextBlocks(page);

            // 如果当前页没有文本，跳过（但保留原页面内容）
            if (textBlocks.Count == 0)
            {
                _logger.LogWarning("第 {PageNum} 页没有可翻译的文本", pageNum);
                continue;
            }

            // 在当前页上添加译文（根据模式）
            await AddTranslationsToPage(pdf, page, textBlocks);
        }

        _logger.LogInformation("PDF 翻译完成，已保存至 {OutputPath}", outputPath);
    }

    /// <summary>
    /// 提取页面中的所有文本块及其位置矩形
    /// </summary>
    /// <param name="page">PDF 页面对象</param>
    /// <returns>文本块列表</returns>
    private List<TextBlock> ExtractTextBlocks(PdfPage page)
    {
        // 使用自定义的文本提取策略
        var strategy = new TextBlockExtractionStrategy();
        var parser = new PdfCanvasProcessor(strategy);
        // 处理页面内容，触发策略中的事件
        parser.ProcessPageContent(page);
        return strategy.GetTextBlocks();
    }

    /// <summary>
    /// 根据配置的模式在页面上添加译文
    /// </summary>
    /// <param name="pdf">PDF 文档对象</param>
    /// <param name="page">当前页面</param>
    /// <param name="blocks">当前页的文本块列表</param>
    private async Task AddTranslationsToPage(PdfDocument pdf, PdfPage page, List<TextBlock> blocks)
    {
        // 创建一个新的内容流，追加到原页面内容之后，保证原内容完全保留
        var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdf);

        // 创建默认字体（注意：默认字体可能不支持中文，实际使用时建议替换为中文字体）
        PdfFont font = PdfFontFactory.CreateFont();
        float fontSize = 10; // 默认字号，后续可根据需要动态调整

        if (_options.Mode == TranslationMode.Bilingual)
        {
            // ---------- 双语模式：保留原文，在原文下方添加蓝色半透明译文 ----------
            canvas.SetColor(ColorConstants.BLUE, true);          // 设置字体颜色为蓝色
            canvas.SetExtGState(new PdfExtGState().SetFillOpacity(0.8f)); // 设置透明度 80%

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text) || block.Rect == null)
                    continue;

                string translated = await _ollama.TranslateAsync(block.Text);

                float x = block.Rect.GetX();                     // 原文 X 坐标
                float y = block.Rect.GetY() - fontSize - 2;      // 译文放在原文下方，间距 2 点

                // 如果译文位置超出页面底部，则放在原文上方
                if (y < 0)
                {
                    y = block.Rect.GetY() + block.Rect.GetHeight() + 2;
                }

                // 开始文本对象，设置字体和位置，绘制译文，然后结束文本对象
                canvas.BeginText()
                    .SetFontAndSize(font, fontSize)
                    .MoveText(x, y)
                    .ShowText(translated)
                    .EndText();
            }
        }
        else // TranslationMode.Translate
        {
            // ---------- 仅译文模式：用白色矩形覆盖原文，然后在同一位置绘制黑色译文 ----------
            // 注意：此方法仅在背景为白色时效果较好，否则会留下白色块。
            // 后续优化可考虑提取背景色或使用半透明覆盖。
            canvas.SetFillColor(ColorConstants.WHITE);   // 设置填充色为白色
            canvas.SetStrokeColor(ColorConstants.WHITE); // 设置描边色也为白色

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
    /// 表示一个文本块及其位置矩形
    /// </summary>
    private class TextBlock
    {
        /// <summary>文本内容（可能为 null）</summary>
        public string? Text { get; set; }
        /// <summary>文本所占的矩形区域（可能为 null）</summary>
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
        /// 当解析器遇到事件时调用（如文本渲染、图像等）
        /// </summary>
        /// <param name="data">事件数据</param>
        /// <param name="type">事件类型</param>
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

                // 计算矩形左下角坐标和宽高
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
        /// 返回此策略关心的事件类型，以优化解析性能
        /// </summary>
        public ICollection<EventType> GetSupportedEvents()
        {
            return new List<EventType> { EventType.RENDER_TEXT };
        }

        /// <summary>
        /// 获取收集到的所有文本块
        /// </summary>
        public List<TextBlock> GetTextBlocks() => _textBlocks;

        /// <summary>
        /// 获取提取到的全部文本（用于兼容旧版接口）
        /// </summary>
        public string GetResultantText() => string.Join("", _textBlocks.Select(t => t.Text));

        /// <summary>
        /// 旧版接口遗留方法，无需实现，留空即可
        /// </summary>
        public void RenderText(TextRenderInfo renderInfo) { }
    }
}