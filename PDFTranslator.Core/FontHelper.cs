using iText.Kernel.Font;
using iText.Kernel.Pdf;  // 必须添加此行以解决 PdfEncodings 未找到的错误
using Microsoft.Extensions.Logging;
using System.Reflection;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Layout.Font;


namespace PDFTranslator.Core;

/// <summary>
/// 字体信息类，用于存储字体详细信息
/// </summary>
public class FontInfo
{
    /// <summary>
    /// 字体名称
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 字体文件路径（如果是文件字体）
    /// </summary>
    public string? FilePath { get; set; }
    
    /// <summary>
    /// 字体类型：系统字体、用户指定、嵌入字体
    /// </summary>
    public FontSourceType SourceType { get; set; }
    
    /// <summary>
    /// 字体是否支持中文
    /// </summary>
    public bool SupportsChinese { get; set; }
    
    /// <summary>
    /// 字体显示名称
    /// </summary>
    public string DisplayName => $"{Name} ({GetSourceTypeName()})";
    
    private string GetSourceTypeName()
    {
        return SourceType switch
        {
            FontSourceType.System => "系统字体",
            FontSourceType.UserFile => "用户文件",
            FontSourceType.Embedded => "内置字体",
            _ => "未知"
        };
    }
}

/// <summary>
/// 字体来源类型枚举
/// </summary>
public enum FontSourceType
{
    /// <summary>系统已安装字体</summary>
    System,
    /// <summary>用户指定的字体文件</summary>
    UserFile,
    /// <summary>程序内嵌的字体</summary>
    Embedded
}

/// <summary>
/// 字体辅助类，提供多级字体加载机制：
/// 1. 用户指定字体文件路径（最高优先级）
/// 2. 用户指定字体名称
/// 3. 自动检测系统常用中文字体
/// 4. 回退到嵌入字体（内置 Noto Sans SC）
/// 5. 最终回退到 iText 默认字体（不支持中文）
/// </summary>
public static class FontHelper
{
    // 字体缓存，避免重复加载
    private static PdfFont? _cachedFont;
    private static FontInfo? _cachedFontInfo;
    private static readonly object _lock = new object();

    // 嵌入字体资源名称列表（按优先级排序）
    private static readonly string[] _embeddedFontResources = new[]
    {
        "PDFTranslator.Core.Fonts.NotoSansSC-Regular.ttf",
        "PDFTranslator.Core.Fonts.NotoSansSC-Regular.otf",
        "PDFTranslator.Core.Fonts.SimSun.ttf",
        "PDFTranslator.Core.Fonts.MicrosoftYaHei.ttf"
    };

    // 常用系统字体名称（按平台和优先级排序）
    private static readonly Dictionary<PlatformID, string[]> _systemFonts = new()
    {
        [PlatformID.Win32NT] = new[] 
        { 
            "SimSun",           // 宋体（最常用）
            "Microsoft YaHei",  // 微软雅黑
            "KaiTi",            // 楷体
            "FangSong"          // 仿宋
        },
        [PlatformID.Unix] = new[]
        {
            "Noto Sans CJK SC", // Linux 常用
            "Noto Sans CJK JP",
            "WenQuanYi Zen Hei" // 文泉驿
        },
        [PlatformID.MacOSX] = new[]
        {
            "PingFang SC",      // 苹方
            "STHeiti",          // 黑体
            "Apple LiGothic"    // 苹果丽黑
        }
    };

    /// <summary>
    /// 获取当前操作系统平台
    /// </summary>
    private static PlatformID CurrentPlatform
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return PlatformID.Win32NT;
            if (OperatingSystem.IsMacOS())
                return PlatformID.MacOSX;
            if (OperatingSystem.IsLinux())
                return PlatformID.Unix;
            return PlatformID.Other;
        }
    }

    /// <summary>
    /// 获取合适的字体，按优先级尝试
    /// </summary>
    /// <param name="options">用户配置选项</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>iText 的 PdfFont 对象</returns>
    public static PdfFont GetFont(TranslationOptions options, ILogger logger)
    {
        lock (_lock)
        {
            // 如果缓存有效且配置未变，直接返回缓存
            if (_cachedFont != null && _cachedFontInfo != null)
            {
                // 检查配置是否匹配
                bool configMatch = true;
                if (!string.IsNullOrEmpty(options.FontPath))
                    configMatch = _cachedFontInfo.FilePath == options.FontPath;
                else if (!string.IsNullOrEmpty(options.FontName))
                    configMatch = _cachedFontInfo.Name == options.FontName;
                
                if (configMatch)
                {
                    logger.LogDebug("使用缓存的字体: {FontName}", _cachedFontInfo.DisplayName);
                    return _cachedFont;
                }
            }

            // 清除旧缓存
            _cachedFont = null;
            _cachedFontInfo = null;

            // 1. 尝试用户指定的字体文件路径
            if (!string.IsNullOrEmpty(options.FontPath))
            {
                var font = TryLoadFromFile(options.FontPath, logger);
                if (font != null)
                {
                    _cachedFont = font;
                    _cachedFontInfo = new FontInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(options.FontPath),
                        FilePath = options.FontPath,
                        SourceType = FontSourceType.UserFile,
                        SupportsChinese = true // 假设用户指定的字体支持中文
                    };
                    logger.LogInformation("✓ 成功加载用户指定的字体文件: {Path}", options.FontPath);
                    return font;
                }
            }

            // 2. 尝试用户指定的字体名称
            if (!string.IsNullOrEmpty(options.FontName))
            {
                var font = TryLoadSystemFontByName(options.FontName, logger);
                if (font != null)
                {
                    _cachedFont = font;
                    _cachedFontInfo = new FontInfo
                    {
                        Name = options.FontName,
                        SourceType = FontSourceType.System,
                        SupportsChinese = true
                    };
                    logger.LogInformation("✓ 成功加载用户指定的字体名称: {FontName}", options.FontName);
                    return font;
                }
            }

            // 3. 自动检测系统常用中文字体
            var systemFontResult = DetectAndLoadSystemFont(logger);
            if (systemFontResult.HasValue)
            {
                var (font, info) = systemFontResult.Value;  // 使用元组解构
                _cachedFont = font;
                _cachedFontInfo = info;
                logger.LogInformation("✓ 自动检测并使用系统字体: {FontName}", info.DisplayName);
                return font;
            }

            // 4. 尝试加载嵌入字体
            var embeddedFontResult = LoadEmbeddedFont(logger);
            if (embeddedFontResult.HasValue)
            {
                var (font, info) = embeddedFontResult.Value;  // 使用元组解构
                _cachedFont = font;
                _cachedFontInfo = info;
                logger.LogInformation("✓ 使用内置字体: {FontName}", info.DisplayName);
                return font;
            }

            // 5. 最终回退到 iText 默认字体（不支持中文）
            logger.LogWarning("⚠️ 未能加载任何中文字体，将使用默认字体（中文可能显示为方框）");
            var defaultFont = PdfFontFactory.CreateFont();
            _cachedFont = defaultFont;
            _cachedFontInfo = new FontInfo
            {
                Name = "Default",
                SourceType = FontSourceType.System,
                SupportsChinese = false
            };
            return defaultFont;
        }
    }

    /// <summary>
    /// 获取当前使用的字体信息
    /// </summary>
    public static FontInfo? GetCurrentFontInfo() => _cachedFontInfo;

    /// <summary>
    /// 清除字体缓存
    /// </summary>
    public static void ClearCache()
    {
        lock (_lock)
        {
            _cachedFont = null;
            _cachedFontInfo = null;
        }
    }

    /// <summary>
    /// 从文件加载字体
    /// </summary>
    private static PdfFont? TryLoadFromFile(string fontPath, ILogger logger)
    {
        try
        {
            if (!File.Exists(fontPath))
            {
                logger.LogWarning("字体文件不存在: {Path}", fontPath);
                return null;
            }

            // 检查文件扩展名
            var ext = Path.GetExtension(fontPath).ToLower();
            if (ext != ".ttf" && ext != ".ttc" && ext != ".otf")
            {
                logger.LogWarning("不支持的字体格式: {Ext}，仅支持 .ttf、.ttc、.otf", ext);
                return null;
            }

            var font = PdfFontFactory.CreateFont(fontPath, PdfEncodings.IDENTITY_H);
            return font;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载字体文件失败: {Path}", fontPath);
            return null;
        }
    }

    /// <summary>
    /// 按名称加载系统字体
    /// </summary>
    private static PdfFont? TryLoadSystemFontByName(string fontName, ILogger logger)
    {
        try
        {
            var font = PdfFontFactory.CreateRegisteredFont(fontName, PdfEncodings.IDENTITY_H);
            return font;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "系统字体加载失败: {FontName}", fontName);
            return null;
        }
    }

    /// <summary>
    /// 检测并加载系统常用中文字体
    /// </summary>
    private static (PdfFont font, FontInfo info)? DetectAndLoadSystemFont(ILogger logger)
    {
        var platform = CurrentPlatform;
        
        if (_systemFonts.TryGetValue(platform, out var fontNames))
        {
            foreach (var fontName in fontNames)
            {
                try
                {
                    var font = PdfFontFactory.CreateRegisteredFont(fontName, PdfEncodings.IDENTITY_H);
                    if (font != null)
                    {
                        return (font, new FontInfo
                        {
                            Name = fontName,
                            SourceType = FontSourceType.System,
                            SupportsChinese = true
                        });
                    }
                }
                catch
                {
                    // 忽略单个字体加载失败
                    continue;
                }
            }
        }

        // 跨平台通用尝试
        string[] commonFonts = { "Arial Unicode MS", "FreeSerif", "DejaVu Sans" };
        foreach (var fontName in commonFonts)
        {
            try
            {
                var font = PdfFontFactory.CreateRegisteredFont(fontName, PdfEncodings.IDENTITY_H);
                if (font != null)
                {
                    return (font, new FontInfo
                    {
                        Name = fontName,
                        SourceType = FontSourceType.System,
                        SupportsChinese = false // 这些字体可能不支持完整中文
                    });
                }
            }
            catch
            {
                continue;
            }
        }

        return null;
    }

    /// <summary>
    /// 从嵌入资源加载字体
    /// </summary>
    private static (PdfFont font, FontInfo info)? LoadEmbeddedFont(ILogger logger)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            
            // 遍历所有可能的嵌入字体资源
            foreach (var resourceName in _embeddedFontResources)
            {
                try
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        // 保存到临时文件（iText 需要从文件或流加载）
                        var tempPath = Path.Combine(Path.GetTempPath(), $"PDFTranslator_{Guid.NewGuid():N}.ttf");
                        
                        try
                        {
                            // 确保临时文件可写
                            if (File.Exists(tempPath))
                                File.Delete(tempPath);
                            
                            using var fileStream = File.Create(tempPath);
                            stream.CopyTo(fileStream);
                            fileStream.Flush();
                            
                            // 从临时文件加载字体
                            var font = PdfFontFactory.CreateFont(tempPath, PdfEncodings.IDENTITY_H);
                            
                            // 注册程序退出时删除临时文件
                            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
                            {
                                try
                                {
                                    if (File.Exists(tempPath))
                                        File.Delete(tempPath);
                                }
                                catch { /* 忽略清理错误 */ }
                            };
                            
                            var fontName = Path.GetFileNameWithoutExtension(resourceName);
                            return (font, new FontInfo
                            {
                                Name = fontName,
                                SourceType = FontSourceType.Embedded,
                                SupportsChinese = true
                            });
                        }
                        catch
                        {
                            // 如果加载失败，清理临时文件
                            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "加载嵌入字体失败: {Resource}", resourceName);
                    continue;
                }
            }

            logger.LogWarning("未找到任何可用的嵌入字体资源");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载嵌入字体时发生错误");
            return null;
        }
    }

    /// <summary>
    /// 获取系统所有已安装的中文字体（用于下拉菜单）
    /// </summary>
    public static List<FontInfo> GetSystemChineseFonts()
    {
        var fonts = new List<FontInfo>();
        var platform = CurrentPlatform;

        if (_systemFonts.TryGetValue(platform, out var fontNames))
        {
            foreach (var name in fontNames)
            {
                fonts.Add(new FontInfo
                {
                    Name = name,
                    SourceType = FontSourceType.System,
                    SupportsChinese = true
                });
            }
        }

        return fonts;
    }

    /// <summary>
    /// 测试字体是否支持指定文本
    /// </summary>
    public static bool TestFontSupport(string fontName, string testText = "测试中文")
    {
        try
        {
            var font = PdfFontFactory.CreateRegisteredFont(fontName, PdfEncodings.IDENTITY_H);
            // 简单的测试：尝试获取字符宽度
            font.GetWidth(testText);
            return true;
        }
        catch
        {
            return false;
        }
    }
}