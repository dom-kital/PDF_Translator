using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.IO.Font;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PDFTranslator.Core;

/// <summary>
/// 字体信息类，用于存储字体详细信息
/// </summary>
public class FontInfo
{
    public string Name { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public FontSourceType SourceType { get; set; }
    public bool SupportsChinese { get; set; }
    public long MemoryUsage { get; set; }
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
    System,
    UserFile,
    Embedded
}

/// <summary>
/// 字体辅助类 - 内存优化版本
/// </summary>
public static class FontHelper
{
    // 字体缓存，使用弱引用避免内存泄漏
    private static WeakReference<PdfFont>? _cachedFontWeak;
    private static FontInfo? _cachedFontInfo;
    private static readonly object _lock = new object();
    
    // 字体使用计数
    private static int _fontUsageCount = 0;
    
    // 最大缓存次数，超过后强制重新加载（避免内存泄漏）
    private const int MAX_CACHE_USAGE = 100;

    // 嵌入字体资源名称列表（按优先级排序）
    private static readonly string[] _embeddedFontResources = new[]
    {
        "PDFTranslator.Core.Fonts.NotoSansSC-Regular.ttf",
        "PDFTranslator.Core.Fonts.NotoSansSC-Regular.otf"
    };

    // 常用系统字体名称（按平台和优先级排序）
    private static readonly Dictionary<PlatformID, string[]> _systemFonts = new()
    {
        [PlatformID.Win32NT] = new[] { "SimSun" },
        [PlatformID.Unix] = new[] { "Noto Sans CJK SC" },
        [PlatformID.MacOSX] = new[] { "PingFang SC" }
    };

    /// <summary>
    /// 获取当前操作系统平台
    /// </summary>
    private static PlatformID CurrentPlatform
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return PlatformID.Win32NT;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return PlatformID.MacOSX;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return PlatformID.Unix;
            return PlatformID.Other;
        }
    }

    /// <summary>
    /// 获取合适的字体 - 内存优化版本
    /// </summary>
    public static PdfFont GetFont(TranslationOptions options, ILogger logger)
    {
        lock (_lock)
        {
            // 检查弱引用缓存
            PdfFont? cachedFont = null;
            if (_cachedFontWeak != null && _cachedFontWeak.TryGetTarget(out cachedFont))
            {
                // 检查配置是否匹配
                bool configMatch = true;
                if (!string.IsNullOrEmpty(options.FontPath))
                    configMatch = _cachedFontInfo?.FilePath == options.FontPath;
                else if (!string.IsNullOrEmpty(options.FontName))
                    configMatch = _cachedFontInfo?.Name == options.FontName;
                
                if (configMatch && _fontUsageCount < MAX_CACHE_USAGE)
                {
                    _fontUsageCount++;
                    logger.LogDebug("使用缓存的字体: {FontName} (使用次数: {Count})", 
                        _cachedFontInfo?.DisplayName ?? "未知", _fontUsageCount);
                    return cachedFont;
                }
                
                logger.LogDebug("字体缓存使用次数过多，重新加载");
                _cachedFontWeak = null;
                _cachedFontInfo = null;
                _fontUsageCount = 0;
            }

            // 清除旧缓存
            _cachedFontWeak = null;
            _cachedFontInfo = null;
            _fontUsageCount = 0;

            // 1. 尝试用户指定的字体文件路径
            if (!string.IsNullOrEmpty(options.FontPath))
            {
                var result = TryLoadFromFile(options.FontPath, logger);
                if (result.font != null && result.info != null)
                {
                    CacheFont(result.font, result.info);
                    logger.LogInformation("✓ 成功加载用户指定的字体文件: {Path}", options.FontPath);
                    return result.font;
                }
            }

            // 2. 尝试用户指定的字体名称
            if (!string.IsNullOrEmpty(options.FontName))
            {
                var result = TryLoadSystemFontByName(options.FontName, logger);
                if (result.font != null && result.info != null)
                {
                    CacheFont(result.font, result.info);
                    logger.LogInformation("✓ 成功加载用户指定的字体名称: {FontName}", options.FontName);
                    return result.font;
                }
            }

            // 3. 自动检测系统常用中文字体
            var systemFontResult = DetectAndLoadSystemFont(logger);
            if (systemFontResult.font != null && systemFontResult.info != null)
            {
                CacheFont(systemFontResult.font, systemFontResult.info);
                logger.LogInformation("✓ 自动检测并使用系统字体: {FontName}", systemFontResult.info.DisplayName);
                return systemFontResult.font;
            }

            // 4. 尝试加载嵌入字体
            var embeddedFontResult = LoadEmbeddedFont(logger);
            if (embeddedFontResult.font != null && embeddedFontResult.info != null)
            {
                CacheFont(embeddedFontResult.font, embeddedFontResult.info);
                logger.LogInformation("✓ 使用内置字体: {FontName}", embeddedFontResult.info.DisplayName);
                return embeddedFontResult.font;
            }

            // 5. 最终回退到 iText 默认字体
            logger.LogWarning("⚠️ 未能加载任何中文字体，将使用默认字体");
            var defaultFont = PdfFontFactory.CreateFont();
            var defaultInfo = new FontInfo
            {
                Name = "Default",
                SourceType = FontSourceType.System,
                SupportsChinese = false,
                MemoryUsage = EstimateFontMemory(defaultFont)
            };
            CacheFont(defaultFont, defaultInfo);
            return defaultFont;
        }
    }

    /// <summary>
    /// 缓存字体（使用弱引用）
    /// </summary>
    private static void CacheFont(PdfFont font, FontInfo info)
    {
        _cachedFontWeak = new WeakReference<PdfFont>(font);
        _cachedFontInfo = info;
        _fontUsageCount = 1;
    }

    /// <summary>
    /// 获取当前使用的字体信息
    /// </summary>
    public static FontInfo? GetCurrentFontInfo()
    {
        lock (_lock)
        {
            return _cachedFontInfo;
        }
    }

    /// <summary>
    /// 清除字体缓存
    /// </summary>
    public static void ClearCache()
    {
        lock (_lock)
        {
            _cachedFontWeak = null;
            _cachedFontInfo = null;
            _fontUsageCount = 0;
            GC.Collect();
        }
    }

    /// <summary>
    /// 从文件加载字体 - 返回字体和信息
    /// </summary>
    private static (PdfFont? font, FontInfo? info) TryLoadFromFile(string fontPath, ILogger logger)
    {
        try
        {
            if (!File.Exists(fontPath))
            {
                logger.LogWarning("字体文件不存在: {Path}", fontPath);
                return (null, null);
            }

            var ext = Path.GetExtension(fontPath).ToLower();
            if (ext != ".ttf" && ext != ".ttc" && ext != ".otf")
            {
                logger.LogWarning("不支持的字体格式: {Ext}，仅支持 .ttf、.ttc、.otf", ext);
                return (null, null);
            }

            // 修复：移除第三个参数，使用正确的重载
            var font = PdfFontFactory.CreateFont(fontPath, PdfEncodings.IDENTITY_H);
            
            var fontInfo = new FontInfo
            {
                Name = Path.GetFileNameWithoutExtension(fontPath),
                FilePath = fontPath,
                SourceType = FontSourceType.UserFile,
                SupportsChinese = true,
                MemoryUsage = EstimateFontMemory(font)
            };
            
            return (font, fontInfo);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载字体文件失败: {Path}", fontPath);
            return (null, null);
        }
    }

    /// <summary>
    /// 按名称加载系统字体
    /// </summary>
    private static (PdfFont? font, FontInfo? info) TryLoadSystemFontByName(string fontName, ILogger logger)
    {
        try
        {
            // 修复：移除第三个参数
            var font = PdfFontFactory.CreateRegisteredFont(fontName, PdfEncodings.IDENTITY_H);
            
            var fontInfo = new FontInfo
            {
                Name = fontName,
                SourceType = FontSourceType.System,
                SupportsChinese = true,
                MemoryUsage = EstimateFontMemory(font)
            };
            
            return (font, fontInfo);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "系统字体加载失败: {FontName}", fontName);
            return (null, null);
        }
    }

    /// <summary>
    /// 检测并加载系统常用中文字体
    /// </summary>
    private static (PdfFont? font, FontInfo? info) DetectAndLoadSystemFont(ILogger logger)
    {
        var platform = CurrentPlatform;
        
        if (_systemFonts.TryGetValue(platform, out var fontNames))
        {
            foreach (var fontName in fontNames)
            {
                try
                {
                    // 修复：移除第三个参数
                    var font = PdfFontFactory.CreateRegisteredFont(fontName, PdfEncodings.IDENTITY_H);
                    if (font != null)
                    {
                        var fontInfo = new FontInfo
                        {
                            Name = fontName,
                            SourceType = FontSourceType.System,
                            SupportsChinese = true,
                            MemoryUsage = EstimateFontMemory(font)
                        };
                        return (font, fontInfo);
                    }
                }
                catch
                {
                    continue;
                }
            }
        }

        return (null, null);
    }

    /// <summary>
    /// 从嵌入资源加载字体 - 优化内存
    /// </summary>
    private static (PdfFont? font, FontInfo? info) LoadEmbeddedFont(ILogger logger)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            
            foreach (var resourceName in _embeddedFontResources)
            {
                try
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        var tempPath = Path.Combine(Path.GetTempPath(), $"PDFTranslator_{Guid.NewGuid():N}.ttf");
                        
                        try
                        {
                            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096))
                            {
                                stream.CopyTo(fileStream);
                                fileStream.Flush();
                            }
                            
                            // 修复：移除第三个参数
                            var font = PdfFontFactory.CreateFont(tempPath, PdfEncodings.IDENTITY_H);
                            
                            try { File.Delete(tempPath); } catch { }
                            
                            var fontName = Path.GetFileNameWithoutExtension(resourceName);
                            var fontInfo = new FontInfo
                            {
                                Name = fontName,
                                SourceType = FontSourceType.Embedded,
                                SupportsChinese = true,
                                MemoryUsage = EstimateFontMemory(font)
                            };
                            
                            logger.LogDebug("嵌入字体加载成功，估计内存: {Memory}KB", fontInfo.MemoryUsage / 1024);
                            return (font, fontInfo);
                        }
                        catch
                        {
                            try { File.Delete(tempPath); } catch { }
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
            return (null, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载嵌入字体时发生错误");
            return (null, null);
        }
    }

    /// <summary>
    /// 估计字体内存使用
    /// </summary>
    private static long EstimateFontMemory(PdfFont font)
    {
        try
        {
            return 1024 * 1024; // 默认 1MB
        }
        catch
        {
            return 512 * 1024; // 默认 512KB
        }
    }

    /// <summary>
    /// 获取系统字体列表（延迟加载）
    /// </summary>
    public static IEnumerable<FontInfo> GetSystemChineseFonts()
    {
        var platform = CurrentPlatform;

        if (_systemFonts.TryGetValue(platform, out var fontNames))
        {
            foreach (var name in fontNames)
            {
                yield return new FontInfo
                {
                    Name = name,
                    SourceType = FontSourceType.System,
                    SupportsChinese = true,
                    MemoryUsage = 0
                };
            }
        }
    }

    /// <summary>
    /// 测试字体是否支持指定文本 - 修复 using 错误
    /// </summary>
    public static bool TestFontSupport(string fontName, string testText = "测试中文")
    {
        try
        {
            var font = PdfFontFactory.CreateRegisteredFont(fontName, PdfEncodings.IDENTITY_H);
            font.GetWidth(testText);
            return true;
        }
        catch
        {
            return false;
        }
    }
}