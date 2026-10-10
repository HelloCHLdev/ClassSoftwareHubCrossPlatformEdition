using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ClassSoftwareHubCrossPlatformEdition.Services;

/// <summary>读取内嵌的注入脚本 / 图标资源（单文件发布下也能用）。</summary>
public static class EmbeddedAssets
{
    public static string ReadText(string fileNameEndingWith)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileNameEndingWith, StringComparison.OrdinalIgnoreCase));
        if (name is null) return string.Empty;

        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>把内嵌图片释放到本地缓存目录，返回文件路径（界面加载位图用）。</summary>
    public static string? ExtractToCache(string fileNameEndingWith, string outFileName)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(fileNameEndingWith, StringComparison.OrdinalIgnoreCase));
            if (name is null) return null;

            var dir = Core.AppPaths.EnsureDir();
            if (dir.Length == 0) return null;
            var outPath = Path.Combine(dir, outFileName);

            // 缓存里的跟内嵌的对不上（换了图）就重写一份 —— 不然换了 banner 还显示旧图
            using var stream = asm.GetManifestResourceStream(name)!;
            var stale = true;
            try { stale = !File.Exists(outPath) || new FileInfo(outPath).Length != stream.Length; }
            catch { stale = true; }

            if (stale)
            {
                using var file = File.Create(outPath);
                stream.CopyTo(file);
            }
            return outPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>程序图标解到本地（托盘 / 窗口图标用；Avalonia 吃文件路径，不吃内嵌资源流）。</summary>
    public static string? IconPath => ExtractToCache("AppIcon.ico", "app.ico");

    /// <summary>工具小图标解到本地。</summary>
    public static string? ToolIconPath => ExtractToCache("ToolIcon.ico", "tool.ico");
}
