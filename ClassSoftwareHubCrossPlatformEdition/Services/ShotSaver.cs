using System;
using System.IO;

namespace ClassSoftwareHubCrossPlatformEdition.Services;

/// <summary>
/// 截图自动落盘：存到「设置 → 常用工具」里那个目录（**默认桌面**），文件名 `截图_yyyyMMdd_HHmmss.png`。
/// 撞名自动加序号，不覆盖旧图；闯了什么错都只写日志，绝不往外抛。
///
/// ⚠️ 跨平台版范围：这一版**只搬了「存哪儿」和「怎么存」**（设置页那三行目录还是活的，
/// 能打开 / 更改 / 恢复默认）。真正抓屏那一套（Win32 桌面复制 / DWM 缩略图）在 Avalonia 12
/// 没有等价 API，需要额外接平台库，属于下一步的工作 —— 所以没往这里塞一个假的。
/// </summary>
public static class ShotSaver
{
    /// <summary>设置里那个目录；没设 / 不在了 → 系统桌面。</summary>
    public static string Dir()
    {
        try
        {
            var d = App.Settings.Current.ShotSaveDir;
            if (!string.IsNullOrWhiteSpace(d) && Directory.Exists(d)) return d;
        }
        catch { /* 读设置失败就走桌面 */ }

        return Desktop();
    }

    /// <summary>系统桌面目录（拿不到就返回空串，调用方自己判）。</summary>
    public static string Desktop()
    {
        try { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
        catch { return ""; }
    }

    /// <summary>显示用：设置里的目录（不管存不存在）。</summary>
    public static string DirSetting()
    {
        try
        {
            var d = App.Settings.Current.ShotSaveDir;
            if (!string.IsNullOrWhiteSpace(d)) return d;
        }
        catch { }
        return Dir();
    }

    /// <summary>存 PNG（自动防撞名）。返回落盘路径；失败 null。</summary>
    public static string? Save(byte[] png, DateTime? at = null)
    {
        try
        {
            var dir = Dir();
            if (string.IsNullOrWhiteSpace(dir)) return null;
            Directory.CreateDirectory(dir);

            var stamp = (at ?? DateTime.Now).ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(dir, $"截图_{stamp}.png");
            for (var i = 2; i < 200 && File.Exists(path); i++)
                path = Path.Combine(dir, $"截图_{stamp}_{i}.png");

            File.WriteAllBytes(path, png);
            ThemeHost.Log("截图已自动保存：" + path);
            return path;
        }
        catch (Exception ex)
        {
            ThemeHost.Log("截图自动保存失败: " + ex.Message);
            return null;
        }
    }
}
