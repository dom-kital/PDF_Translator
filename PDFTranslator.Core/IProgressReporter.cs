namespace PDFTranslator.Core;

/// <summary>
/// 进度报告接口，用于在 CLI 和 GUI 中统一显示翻译进度。
/// 实现了关注点分离，让 PDF 翻译核心不依赖具体 UI 实现。
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// 报告当前处理进度。
    /// </summary>
    /// <param name="current">当前已完成的任务数（已处理的页数）</param>
    /// <param name="total">总任务数（总页数）</param>
    /// <param name="message">当前状态信息（可选），如"正在处理第 3 页..."</param>
    void Report(int current, int total, string? message = null);

    /// <summary>
    /// 报告处理完成。
    /// </summary>
    /// <param name="message">完成信息（可选），如"翻译完成！"</param>
    void Complete(string? message = null);

    // 注意：不要添加新的方法，否则会破坏现有实现
    // 所有进度信息都通过 Report 方法的 message 参数传递
}