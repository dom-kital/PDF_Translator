namespace PDFTranslator.Core;

/// <summary>
/// 进度报告接口，用于在 CLI 和 GUI 中统一进度显示
/// 实现了关注点分离，让 PDF 翻译核心不依赖具体 UI
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// 报告当前进度
    /// </summary>
    /// <param name="current">当前已完成的任务数（已处理的页数）</param>
    /// <param name="total">总任务数（总页数）</param>
    /// <param name="message">当前状态信息（可选）</param>
    void Report(int current, int total, string? message = null);

    /// <summary>
    /// 报告完成状态
    /// </summary>
    /// <param name="message">完成信息（可选）</param>
    void Complete(string? message = null);
}