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
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace PDFTranslator.Core;

/// <summary>
/// PDF 翻译器核心类 - 支持流式处理和传统模式
/// </summary>
public class PdfTranslator
{
    private readonly OllamaService _ollama;
    private readonly ILogger<PdfTranslator> _logger;
    private readonly TranslationOptions _options;
    private IProgressReporter? _progressReporter;

    public PdfTranslator(OllamaService ollama, ILogger<PdfTranslator> logger, TranslationOptions options)
    {
        _ollama = ollama;
        _logger = logger;
        _options = options;
    }

    public void SetProgressReporter(IProgressReporter reporter)
    {
        _progressReporter = reporter;
    }

    /// <summary>
    /// 翻译 PDF 文件的主入口方法 - 根据配置选择处理模式
    /// </summary>
    public async Task TranslatePdfAsync(string inputPath, string outputPath)
    {
        _logger.LogInformation("开始处理 PDF: {InputPath}", inputPath);
        _logger.LogInformation("处理模式: {Mode}, 批处理大小: {BatchSize}, 最大文本块: {MaxBlocks}", 
            _options.UseStreamingMode ? "流式处理" : "传统模式",
            _options.TextBlockBatchSize,
            _options.MaxTextBlocksPerPage);

        long startMemory = LogMemoryUsage("初始状态");

        if (_options.UseStreamingMode)
        {
            await TranslatePdfStreamingAsync(inputPath, outputPath);
        }
        else
        {
            await TranslatePdfTraditionalAsync(inputPath, outputPath);
        }

        long endMemory = LogMemoryUsage("翻译完成");
        _logger.LogInformation("内存变化: {Start}MB -> {End}MB, 差异: {Delta}MB", 
            startMemory, endMemory, endMemory - startMemory);
    }

    #region 流式处理模式

    /// <summary>
    /// 流式处理模式 - 低内存占用
    /// </summary>
    private async Task TranslatePdfStreamingAsync(string inputPath, string outputPath)
    {
        _logger.LogInformation("使用流式处理模式");

        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf = new PdfDocument(reader, writer);

        int totalPages = pdf.GetNumberOfPages();
        _logger.LogInformation("PDF 总页数: {TotalPages}", totalPages);

        // 解析页面范围并排序（流式处理要求顺序访问）
        var pagesToProcess = ParsePageRange(_options, totalPages, _logger);
        pagesToProcess.Sort();
        
        if (pagesToProcess.Count == 0)
        {
            _logger.LogWarning("没有符合条件的页面需要翻译");
            _progressReporter?.Complete("没有页面需要翻译");
            return;
        }

        _logger.LogInformation("将处理 {Count} 页: [{Pages}]", 
            pagesToProcess.Count, string.Join(", ", pagesToProcess));

        _progressReporter?.Report(0, pagesToProcess.Count, "开始处理...");

        int processedCount = 0;

        // 流式处理：顺序访问所有页面
        for (int pageNum = 1; pageNum <= totalPages; pageNum++)
        {
            // 检查内存使用
            CheckMemoryUsage();

            bool needProcess = pagesToProcess.Contains(pageNum);
            
            if (needProcess)
            {
                processedCount++;
                
                _logger.LogInformation("正在流式处理第 {PageNum}/{TotalPages} 页 (进度: {Processed}/{TotalToProcess})", 
                    pageNum, totalPages, processedCount, pagesToProcess.Count);
                
                _progressReporter?.Report(processedCount - 1, pagesToProcess.Count, 
                    $"正在处理第 {pageNum} 页 (共需处理 {pagesToProcess.Count} 页)");

                try
                {
                    await ProcessPageStreamingAsync(pdf, pageNum);
                }
                catch (OutOfMemoryException)
                {
                    _logger.LogError("处理第 {PageNum} 页时内存不足，跳过该页", pageNum);
                    ForceFullGC();
                    
                    // 检查是否达到临界阈值
                    if (IsMemoryCritical())
                    {
                        _logger.LogError("内存使用达到临界值，停止处理");
                        _progressReporter?.Complete("内存使用过高，请重启程序后重试");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理第 {PageNum} 页时发生错误", pageNum);
                }
            }
            else
            {
                _logger.LogDebug("跳过第 {PageNum} 页", pageNum);
            }

            // 每页后根据配置决定是否GC
            if (_options.ForceGCAfterPage)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        _progressReporter?.Complete("翻译完成！");
        _logger.LogInformation("流式PDF处理完成");
    }

    /// <summary>
    /// 流式处理单个页面
    /// </summary>
    private async Task ProcessPageStreamingAsync(PdfDocument pdf, int pageNum)
    {
        var page = pdf.GetPage(pageNum);
        
        // 提取文本块（使用配置的最大数量）
        var textBlocks = await ExtractTextBlocksWithLimitAsync(page, _options.MaxTextBlocksPerPage);
        
        if (textBlocks.Count == 0)
        {
            _logger.LogWarning("第 {PageNum} 页没有可翻译的文本", pageNum);
            return;
        }

        _logger.LogInformation("第 {PageNum} 页提取到 {Count} 个文本块", pageNum, textBlocks.Count);

        // 添加译文（使用配置的批处理大小）
        await AddTranslationsBatchedAsync(pdf, page, textBlocks, _options.TextBlockBatchSize);
        
        // 清理页面引用，帮助GC
        page = null;
    }

    /// <summary>
    /// 批量添加译文 - 使用配置的批处理大小
    /// </summary>
    private async Task AddTranslationsBatchedAsync(PdfDocument pdf, PdfPage page, List<TextBlock> blocks, int batchSize)
    {
        PdfFont? font = null;
        PdfCanvas? canvas = null;
        
        try
        {
            canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdf);

            // 获取字体
            try
            {
                font = FontHelper.GetFont(_options, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "加载字体失败，使用默认字体");
                font = PdfFontFactory.CreateFont();
            }

            float defaultFontSize = 10;

            // 将文本块分成多个批次
            var batches = blocks.Select((block, index) => new { block, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.block).ToList())
                .ToList();

            _logger.LogDebug("将 {TotalBlocks} 个文本块分为 {BatchCount} 批处理", blocks.Count, batches.Count);

            int batchIndex = 0;
            foreach (var batch in batches)
            {
                batchIndex++;
                _logger.LogDebug("处理第 {BatchIndex}/{BatchCount} 批，共 {BatchSize} 个文本块", 
                    batchIndex, batches.Count, batch.Count);

                // 翻译当前批次
                var translations = await TranslateBatchAsync(batch);

                // 绘制当前批次
                if (_options.Mode == TranslationMode.Bilingual)
                {
                    canvas.SetColor(ColorConstants.BLUE, true);
                    canvas.SetExtGState(new PdfExtGState().SetFillOpacity(0.8f));

                    foreach (var (block, translated) in translations)
                    {
                        if (block.Rect == null) continue;

                        float x = block.Rect.GetX();
                        float y = block.Rect.GetY() - defaultFontSize - 2;

                        if (y < 0)
                        {
                            y = block.Rect.GetY() + block.Rect.GetHeight() + 2;
                        }

                        canvas.BeginText()
                            .SetFontAndSize(font, defaultFontSize)
                            .MoveText(x, y)
                            .ShowText(translated)
                            .EndText();
                    }
                }
                else // 仅译文模式
                {
                    canvas.SetFillColor(ColorConstants.WHITE);
                    canvas.SetStrokeColor(ColorConstants.WHITE);

                    foreach (var (block, translated) in translations)
                    {
                        if (block.Rect == null) continue;

                        float x = block.Rect.GetX();
                        float y = block.Rect.GetY();
                        float width = block.Rect.GetWidth();
                        float height = block.Rect.GetHeight();
                        float blockFontSize = height * 0.8f;

                        canvas.Rectangle(x, y, width, height).Fill();

                        canvas.SetColor(ColorConstants.BLACK, true);
                        canvas.BeginText()
                            .SetFontAndSize(font, blockFontSize)
                            .MoveText(x, y + (height - blockFontSize) / 2)
                            .ShowText(translated)
                            .EndText();

                        canvas.SetFillColor(ColorConstants.WHITE);
                    }
                }

                // 每批次后清理
                translations.Clear();
                GC.Collect();
                await Task.Delay(5); // 短暂暂停，让系统有机会回收
            }
        }
        finally
        {
            // 清理资源
            canvas = null;
            font = null;
        }
    }

    /// <summary>
    /// 批量翻译文本块
    /// </summary>
    private async Task<List<(TextBlock Block, string Translation)>> TranslateBatchAsync(List<TextBlock> batch)
    {
        var translations = new List<(TextBlock, string)>();
        
        foreach (var block in batch)
        {
            if (string.IsNullOrWhiteSpace(block.Text) || block.Rect == null)
                continue;

            // 限制单个文本长度
            string textToTranslate = block.Text.Length > _options.MaxTextBlockLength 
                ? block.Text.Substring(0, _options.MaxTextBlockLength) + "..." 
                : block.Text;

            string translated = await _ollama.TranslateAsync(
                textToTranslate, 
                _options.SourceLanguage, 
                _options.TargetLanguage);

            translations.Add((block, translated));
        }
        
        return translations;
    }

    #endregion

    #region 传统处理模式

    /// <summary>
    /// 传统处理模式 - 兼容原有逻辑
    /// </summary>
    private async Task TranslatePdfTraditionalAsync(string inputPath, string outputPath)
    {
        _logger.LogInformation("使用传统处理模式");

        using var reader = new PdfReader(inputPath);
        using var writer = new PdfWriter(outputPath);
        using var pdf = new PdfDocument(reader, writer);

        int totalPages = pdf.GetNumberOfPages();
        _logger.LogInformation("PDF 总页数: {TotalPages}", totalPages);

        var pagesToProcess = ParsePageRange(_options, totalPages, _logger);
        if (pagesToProcess.Count == 0)
        {
            _logger.LogWarning("没有符合条件的页面需要翻译");
            _progressReporter?.Complete("没有页面需要翻译");
            return;
        }

        _logger.LogInformation("将处理 {Count} 页: [{Pages}]", pagesToProcess.Count, string.Join(", ", pagesToProcess));

        _progressReporter?.Report(0, pagesToProcess.Count, "开始处理...");

        int processedCount = 0;
        foreach (int pageNum in pagesToProcess)
        {
            processedCount++;
            _logger.LogInformation("正在处理第 {PageNum} 页...", pageNum);
            _progressReporter?.Report(processedCount - 1, pagesToProcess.Count, $"正在处理第 {pageNum} 页...");

            try
            {
                await ProcessPageTraditionalAsync(pdf, pageNum);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理第 {PageNum} 页时发生错误", pageNum);
            }
        }

        _progressReporter?.Complete("翻译完成！");
        _logger.LogInformation("传统PDF处理完成");
    }

    /// <summary>
    /// 传统模式处理单个页面
    /// </summary>
    private async Task ProcessPageTraditionalAsync(PdfDocument pdf, int pageNum)
    {
        var page = pdf.GetPage(pageNum);
        var textBlocks = ExtractTextBlocks(page);

        if (textBlocks.Count == 0)
        {
            _logger.LogWarning("第 {PageNum} 页没有可翻译的文本", pageNum);
            return;
        }

        await AddTranslationsToPage(pdf, page, textBlocks);
    }

    #endregion

    #region 公共辅助方法

    /// <summary>
    /// 提取文本块（传统方式）
    /// </summary>
    private List<TextBlock> ExtractTextBlocks(PdfPage page)
    {
        var strategy = new TextBlockExtractionStrategy();
        var parser = new PdfCanvasProcessor(strategy);
        parser.ProcessPageContent(page);
        return strategy.GetTextBlocks();
    }

    /// <summary>
    /// 提取文本块并限制数量
    /// </summary>
    private async Task<List<TextBlock>> ExtractTextBlocksWithLimitAsync(PdfPage page, int maxBlocks)
    {
        return await Task.Run(() =>
        {
            var strategy = new TextBlockExtractionStrategy();
            var parser = new PdfCanvasProcessor(strategy);
            parser.ProcessPageContent(page);
            
            var allBlocks = strategy.GetTextBlocks();
            
            if (allBlocks.Count > maxBlocks)
            {
                _logger.LogWarning("文本块数量过多 ({Count})，将选择最重要的 {Max} 个处理", 
                    allBlocks.Count, maxBlocks);
                
                // 按文本长度排序，取最长的
                return allBlocks
                    .Where(b => !string.IsNullOrWhiteSpace(b.Text))
                    .OrderByDescending(b => b.Text?.Length ?? 0)
                    .Take(maxBlocks)
                    .ToList();
            }
            
            return allBlocks;
        });
    }

    /// <summary>
    /// 添加译文到页面（传统方式）
    /// </summary>
    private async Task AddTranslationsToPage(PdfDocument pdf, PdfPage page, List<TextBlock> blocks)
    {
        var canvas = new PdfCanvas(page.NewContentStreamAfter(), page.GetResources(), pdf);

        PdfFont font;
        try
        {
            font = FontHelper.GetFont(_options, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载字体失败，使用默认字体");
            font = PdfFontFactory.CreateFont();
        }

        float defaultFontSize = 10;

        if (_options.Mode == TranslationMode.Bilingual)
        {
            canvas.SetColor(ColorConstants.BLUE, true);
            canvas.SetExtGState(new PdfExtGState().SetFillOpacity(0.8f));

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text) || block.Rect == null)
                    continue;

                string translated = await _ollama.TranslateAsync(block.Text, _options.SourceLanguage, _options.TargetLanguage);

                float x = block.Rect.GetX();
                float y = block.Rect.GetY() - defaultFontSize - 2;

                if (y < 0)
                {
                    y = block.Rect.GetY() + block.Rect.GetHeight() + 2;
                }

                canvas.BeginText()
                    .SetFontAndSize(font, defaultFontSize)
                    .MoveText(x, y)
                    .ShowText(translated)
                    .EndText();
            }
        }
        else
        {
            canvas.SetFillColor(ColorConstants.WHITE);
            canvas.SetStrokeColor(ColorConstants.WHITE);

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Text) || block.Rect == null)
                    continue;

                string translated = await _ollama.TranslateAsync(block.Text, _options.SourceLanguage, _options.TargetLanguage);

                float x = block.Rect.GetX();
                float y = block.Rect.GetY();
                float width = block.Rect.GetWidth();
                float height = block.Rect.GetHeight();
                float blockFontSize = height * 0.8f;

                canvas.Rectangle(x, y, width, height).Fill();

                canvas.SetColor(ColorConstants.BLACK, true);
                canvas.BeginText()
                    .SetFontAndSize(font, blockFontSize)
                    .MoveText(x, y + (height - blockFontSize) / 2)
                    .ShowText(translated)
                    .EndText();

                canvas.SetFillColor(ColorConstants.WHITE);
            }
        }
    }

    /// <summary>
    /// 检查内存使用并记录警告
    /// </summary>
    private void CheckMemoryUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var memoryMB = process.WorkingSet64 / 1024 / 1024;

            if (memoryMB > _options.MemoryWarningThresholdMB)
            {
                _logger.LogWarning("内存使用超过警告阈值: {Current}MB > {Threshold}MB", 
                    memoryMB, _options.MemoryWarningThresholdMB);
                
                if (memoryMB > _options.MemoryCriticalThresholdMB)
                {
                    _logger.LogError("内存使用超过临界阈值: {Current}MB > {Threshold}MB", 
                        memoryMB, _options.MemoryCriticalThresholdMB);
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// 判断内存是否达到临界值
    /// </summary>
    private bool IsMemoryCritical()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var memoryMB = process.WorkingSet64 / 1024 / 1024;
            return memoryMB > _options.MemoryCriticalThresholdMB;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 强制完全垃圾回收
    /// </summary>
    private void ForceFullGC()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);
    }

    /// <summary>
    /// 记录内存使用
    /// </summary>
    private long LogMemoryUsage(string stage)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64 / 1024 / 1024;
            var managedMemory = GC.GetTotalMemory(false) / 1024 / 1024;
            var gcGen0 = GC.CollectionCount(0);
            var gcGen1 = GC.CollectionCount(1);
            var gcGen2 = GC.CollectionCount(2);
            
            _logger.LogInformation("[内存] {Stage} - 工作集: {WorkingSet}MB, 托管: {ManagedMemory}MB, GC: {G0}/{G1}/{G2}",
                stage, workingSet, managedMemory, gcGen0, gcGen1, gcGen2);
            
            return workingSet;
        }
        catch
        {
            return 0;
        }
    }

    #endregion

    #region 页面范围解析方法

    /// <summary>
    /// 解析页面范围字符串，返回要处理的页码列表
    /// </summary>
    private List<int> ParsePageRange(TranslationOptions options, int totalPages, ILogger logger)
    {
        var pages = new List<int>();

        switch (options.PageRangeMode)
        {
            case PageRangeMode.All:
                pages.AddRange(Enumerable.Range(1, totalPages));
                logger.LogDebug("选择全部页面: 1-{Total}", totalPages);
                break;

            case PageRangeMode.Single:
                if (options.SinglePage.HasValue && options.SinglePage.Value >= 1 && options.SinglePage.Value <= totalPages)
                {
                    pages.Add(options.SinglePage.Value);
                    logger.LogDebug("选择单个页面: {Page}", options.SinglePage.Value);
                }
                else
                {
                    logger.LogWarning("无效的单个页面页码: {Page}，将使用全部页面", options.SinglePage);
                    pages.AddRange(Enumerable.Range(1, totalPages));
                }
                break;

            case PageRangeMode.Range:
                if (!string.IsNullOrWhiteSpace(options.PageRange))
                {
                    pages = ParseRangeString(options.PageRange, totalPages, logger);
                    if (pages.Count == 0)
                    {
                        logger.LogWarning("页码范围解析失败，将使用全部页面");
                        pages.AddRange(Enumerable.Range(1, totalPages));
                    }
                }
                else
                {
                    logger.LogWarning("未指定页码范围，将使用全部页面");
                    pages.AddRange(Enumerable.Range(1, totalPages));
                }
                break;
        }

        return pages.Distinct().OrderBy(p => p).ToList();
    }

    /// <summary>
    /// 解析范围字符串，如 "1-5,7,9-11"
    /// </summary>
    private List<int> ParseRangeString(string rangeStr, int totalPages, ILogger logger)
    {
        var pages = new List<int>();
        var parts = rangeStr.Split(',');

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed.Contains('-'))
            {
                var rangeParts = trimmed.Split('-');
                if (rangeParts.Length == 2 &&
                    int.TryParse(rangeParts[0], out int start) &&
                    int.TryParse(rangeParts[1], out int end))
                {
                    start = Math.Max(1, Math.Min(start, totalPages));
                    end = Math.Max(1, Math.Min(end, totalPages));
                    if (start <= end)
                    {
                        pages.AddRange(Enumerable.Range(start, end - start + 1));
                    }
                    else
                    {
                        logger.LogWarning("无效的范围: {Range}", trimmed);
                    }
                }
                else
                {
                    logger.LogWarning("无法解析范围: {Range}", trimmed);
                }
            }
            else if (int.TryParse(trimmed, out int singlePage))
            {
                if (singlePage >= 1 && singlePage <= totalPages)
                {
                    pages.Add(singlePage);
                }
                else
                {
                    logger.LogWarning("页码超出范围: {Page}", singlePage);
                }
            }
            else
            {
                logger.LogWarning("无法解析: {Part}", trimmed);
            }
        }

        return pages;
    }

    #endregion

    #region 内部类

    private class TextBlock
    {
        public string? Text { get; set; }
        public Rectangle? Rect { get; set; }
    }

    private class TextBlockExtractionStrategy : ITextExtractionStrategy
    {
        private List<TextBlock> _textBlocks = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type == EventType.RENDER_TEXT)
            {
                var renderInfo = (TextRenderInfo)data;
                var text = renderInfo.GetText();
                if (string.IsNullOrWhiteSpace(text))
                    return;

                var ascentLine = renderInfo.GetAscentLine();
                var descentLine = renderInfo.GetDescentLine();

                float x1 = Math.Min(ascentLine.GetStartPoint().Get(0), descentLine.GetStartPoint().Get(0));
                float x2 = Math.Max(ascentLine.GetEndPoint().Get(0), descentLine.GetEndPoint().Get(0));
                float y1 = Math.Min(descentLine.GetStartPoint().Get(1), descentLine.GetEndPoint().Get(1));
                float y2 = Math.Max(ascentLine.GetStartPoint().Get(1), ascentLine.GetEndPoint().Get(1));
                var rect = new Rectangle(x1, y1, x2 - x1, y2 - y1);

                _textBlocks.Add(new TextBlock { Text = text, Rect = rect });
            }
        }

        public ICollection<EventType> GetSupportedEvents() => new List<EventType> { EventType.RENDER_TEXT };
        public List<TextBlock> GetTextBlocks() => _textBlocks;
        public string GetResultantText() => string.Join("", _textBlocks.Select(t => t.Text));
        public void RenderText(TextRenderInfo renderInfo) { }
    }

    #endregion
}