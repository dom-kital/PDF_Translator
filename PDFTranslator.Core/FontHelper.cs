using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Layout.Font;
using Microsoft.Extensions.Logging;

namespace PDFTranslator.Core;

/// <summary>
/// 字体辅助类，提供多级字体加载机制：
/// 1. 用户指定字体文件路径（最高优先级）
/// 2. 用户指定字体名称
/// 3. 自动检测系统常用中文字体
/// 4. 回退到 iText 默认字体（不支持中文）
/// </summary>
public static class FontHelper
{
    /// <summary>
    /// 根据用户配置获取合适的字体。
    /// </summary>
    /// <param name="options">用户配置选项，包含字体名称或字体路径。</param>
    /// <param name="logger">日志记录器，用于记录加载过程和错误。</param>
    /// <returns>iText 的 PdfFont 对象。</returns>
    public static PdfFont GetFont(TranslationOptions options, ILogger logger)
    {
        // 1. 如果用户指定了字体文件路径，优先尝试加载该文件
        if (!string.IsNullOrEmpty(options.FontPath))
        {
            try
            {
                if (File.Exists(options.FontPath))
                {
                    // 使用 IDENTITY_H 编码以支持 Unicode 字符
                    var font = PdfFontFactory.CreateFont(options.FontPath, PdfEncodings.IDENTITY_H);
                    logger.LogInformation("成功加载用户指定的字体文件: {Path}", options.FontPath);
                    return font;
                }
                else
                {
                    logger.LogWarning("用户指定的字体文件不存在: {Path}", options.FontPath);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "加载字体文件失败: {Path}", options.FontPath);
            }
        }

        // 2. 如果用户指定了字体名称，尝试加载已注册的系统字体
        if (!string.IsNullOrEmpty(options.FontName))
        {
            try
            {
                // CreateRegisteredFont 根据字体名称（如 "SimSun"）从系统注册的字体中查找
                var font = PdfFontFactory.CreateRegisteredFont(options.FontName, PdfEncodings.IDENTITY_H);
                logger.LogInformation("成功加载用户指定的字体名称: {FontName}", options.FontName);
                return font;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "加载指定名称的字体失败: {FontName}", options.FontName);
            }
        }

        // 3. 自动检测操作系统常见的中文字体名称
        var detectedFontName = DetectSystemChineseFont();
        if (!string.IsNullOrEmpty(detectedFontName))
        {
            try
            {
                var font = PdfFontFactory.CreateRegisteredFont(detectedFontName, PdfEncodings.IDENTITY_H);
                logger.LogInformation("自动检测并使用系统字体: {FontName}", detectedFontName);
                return font;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "自动检测的字体加载失败: {FontName}", detectedFontName);
            }
        }

        // 4. 最终回退到 iText 默认字体（不支持中文）
        logger.LogWarning("未能加载任何中文字体，将使用默认字体（中文可能显示为方框）");
        return PdfFontFactory.CreateFont();
    }

    /// <summary>
    /// 根据当前操作系统检测常用的支持中文的系统字体名称。
    /// </summary>
    /// <returns>字体名称，如果无法确定则返回 null。</returns>
    private static string? DetectSystemChineseFont()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows 系统普遍安装的宋体
            return "SimSun";
        }
        else if (OperatingSystem.IsMacOS())
        {
            // macOS 系统的苹方字体
            return "PingFang SC";
        }
        else if (OperatingSystem.IsLinux())
        {
            // Linux 系统常见的 Noto Sans CJK 字体
            return "Noto Sans CJK SC";
        }
        return null;
    }
}