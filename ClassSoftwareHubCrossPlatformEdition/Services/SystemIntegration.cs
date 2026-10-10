using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace ClassSoftwareHubCrossPlatformEdition.Services;

/// <summary>
/// 跨平台「跟系统打交道」的那一堆：开链接 / 开文件 / 在文件管理器里定位 / 复制到剪贴板 /
/// 开机自启 / 窗口置顶 / 单实例。
///
/// 原来这些是 MainWindow 里一堆 Windows 专属 API（P/Invoke ShellExecute、注册表 Run 键、
/// SetForegroundWindow、CreateMutex…），这里统一收到一处：开东西一律走
/// <see cref="Process"/> + <c>UseShellExecute = true</c>（那正是「双击文件 / 双击链接」在系统上的语义），
/// 其余按平台各写各的（跨平台不该幻想有一套统一 API）。
/// </summary>
public static class SystemIntegration
{
    // ══════════════════════════════════════════════════════════════
    // 打开链接 / 文件
    // ══════════════════════════════════════════════════════════════

    /// <summary>用系统默认程序打开一个网址 / 本地文件 / 目录（等价于双击）。</summary>
    public static bool Open(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        try
        {
            var psi = new ProcessStartInfo(target.Trim()) { UseShellExecute = true };
            using var p = Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Log("打开失败：" + target + " → " + ex.Message);
            return false;
        }
    }

    /// <summary>「在文件夹里显示」：macOS 用 <c>open -R</c>，Windows 用 explorer /select，Linux 退化到开目录。</summary>
    public static bool Reveal(string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return false;
        try
        {
            var full = Path.GetFullPath(file);

            if (OperatingSystem.IsMacOS())
            {
                using var p = Process.Start(new ProcessStartInfo("open", "-R \"" + full + "\"")
                {
                    UseShellExecute = false,
                });
                return true;
            }

            if (OperatingSystem.IsWindows())
            {
                using var explorer = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"")
                {
                    UseShellExecute = true,
                });
                return true;
            }

            // Linux： nautilus 认 --select，其它管理器大多只认开目录 → 先试前者再退回后者
            if (File.Exists(full))
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo("xdg-open", $"--select \"{full}\"")
                    {
                        UseShellExecute = false,
                    });
                    return true;
                }
                catch { /* 这个文件管理器不认，退化成开目录 */ }
            }

            var dir = Directory.Exists(full) ? full : Path.GetDirectoryName(full) ?? "";
            if (string.IsNullOrWhiteSpace(dir)) return false;
            using var d = Process.Start(new ProcessStartInfo("xdg-open", "\"" + dir + "\"")
            {
                UseShellExecute = false,
            });
            return true;
        }
        catch (Exception ex)
        {
            Log("定位文件失败：" + file + " → " + ex.Message);
            return false;
        }
    }

    /// <summary>复制一段文字到剪贴板（<paramref name="top"/> 一般传 <see cref="App.TopLevel"/>）。</summary>
    public static bool CopyText(TopLevel? top, string? text)
    {
        if (top is null || string.IsNullOrEmpty(text)) return false;
        try
        {
            IClipboard? clipboard = top.Clipboard;
            if (clipboard is null) return false;
            clipboard.SetTextAsync(text!);
            return true;
        }
        catch (Exception ex)
        {
            Log("复制到剪贴板失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>用主窗口当剪贴板宿主的便捷版。</summary>
    public static bool CopyText(string? text) => CopyText(App.TopLevel, text);

    // ══════════════════════════════════════════════════════════════
    // 开机自启
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// 开机自启。Windows 走注册表 Run 键，macOS 走 LaunchAgents plist，Linux 走 XDG 自启目录的 .desktop。
    /// 三个系统各有各的规矩，这里各写各的。
    /// </summary>
    public static bool SetAutoStart(bool enabled)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsAutoStart(enabled);
            if (OperatingSystem.IsMacOS()) return MacAutoStart(enabled);
            if (OperatingSystem.IsLinux()) return LinuxAutoStart(enabled);

            Log("这个系统暂时不支持开机自启（跨平台版只做了 Win / macOS / Linux）。");
            return false;
        }
        catch (Exception ex)
        {
            Log("设置开机自启失败：" + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 自启时要不要顺手带上 <c>--minimized</c>（设置页「开机时最小化启动」）。
    ///
    /// ⚠️ 三个系统的注册格式完全不同，但「启动参数怎么拼」这件事是一样的，所以在这里统一算一次：
    /// 每个平台方法里都用 <c>AutoStartArgs()</c> 拼，而不是各写一遍 —— 免得改一个漏两个。
    /// </summary>
    private static string AutoStartArgs()
    {
        var flag = App.Settings.Current.MinimizeOnStart ? " --minimized" : "";
        return flag;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool WindowsAutoStart(bool enabled)
    {
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        using var reg = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key, writable: true);
        if (reg is null) return false;

        if (!enabled)
        {
            reg.DeleteValue(Core.ShellConfig.AppName, throwOnMissingValue: false);
            return true;
        }

        var exe = Environment.ProcessPath ?? "";
        if (exe.Length == 0) return false;
        reg.SetValue(Core.ShellConfig.AppName, "\"" + exe + "\"" + AutoStartArgs());
        return true;
    }

    private static bool MacAutoStart(bool enabled)
    {
        // ~/Library/LaunchAgents/<name>.plist
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(home, "Library", "LaunchAgents");
        var plist = Path.Combine(dir, "com." + Core.ShellConfig.AppName + ".plist");

        if (!enabled)
        {
            if (File.Exists(plist)) File.Delete(plist);
            return true;
        }

        var exe = Environment.ProcessPath ?? "";
        if (exe.Length == 0) return false;
        Directory.CreateDirectory(dir);

        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n<dict>\n" +
            "  <key>Label</key><string>com." + Core.ShellConfig.AppName + "</string>\n" +
            "  <key>ProgramArguments</key>\n  <array>\n    <string>" + EscapeXml(exe) + "</string>\n" +
            (AutoStartArgs().Length > 0 ? "    <string>--minimized</string>\n" : "") +
            "  </array>\n" +
            "  <key>RunAtLoad</key><true/>\n" +
            "</dict>\n</plist>\n";
        File.WriteAllText(plist, xml);
        return true;
    }

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static bool LinuxAutoStart(bool enabled)
    {
        // ~/.config/autostart/<name>.desktop
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = Path.Combine(
            string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".config") : xdg!,
            "autostart");
        var file = Path.Combine(dir, Core.ShellConfig.AppName.ToLowerInvariant() + ".desktop");

        if (!enabled)
        {
            if (File.Exists(file)) File.Delete(file);
            return true;
        }

        var exe = Environment.ProcessPath ?? "";
        if (exe.Length == 0) return false;
        Directory.CreateDirectory(dir);

        var text =
            "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=" + Core.ShellConfig.AppName + "\n" +
            "Comment=" + Core.ShellConfig.WindowTitle + "\n" +
            "Exec=\"" + exe + "\"" + AutoStartArgs() + "\n" +
            "Terminal=false\n" +
            "X-GNOME-Autostart-enabled=true\n";
        File.WriteAllText(file, text);
        return true;
    }

    // ══════════════════════════════════════════════════════════════
    // 窗口
    // ══════════════════════════════════════════════════════════════

    /// <summary>窗口置顶。Avalonia 12 直接有这个属性，不用再像 WinUI 那样 SetWindowLong。</summary>
    public static void SetAlwaysOnTop(Window? window, bool onTop)
    {
        try { if (window is not null) window.Topmost = onTop; }
        catch { }
    }

    /// <summary>把窗口拉到前台并聚焦（托盘点「打开主界面」用）。</summary>
    public static void Show(Window? window)
    {
        if (window is null) return;
        try
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Show();
            window.Activate();
        }
        catch { }
    }

    // ══════════════════════════════════════════════════════════════
    // 单实例
    // ══════════════════════════════════════════════════════════════

    private static Mutex? _instanceMutex;

    /// <summary>
    /// 抢占单实例。返回 true = 抢到了（自己是第一个）；false = 已经有别的实例在跑。
    /// 三个系统上 .NET 的命名互斥量都是本机 IPC，语义一致，不用分平台。
    /// </summary>
    public static bool TryClaimSingleInstance()
    {
        try
        {
            _instanceMutex = new Mutex(initiallyOwned: true, Core.ShellConfig.MutexName, out var created);
            return created;
        }
        catch (AbandonedMutexException)
        {
            // 上一个进程崩了，锁被系统回收 —— 等于抢到了
            return true;
        }
        catch
        {
            return true;   // 建不出来就当没这个限制，别把应用卡死
        }
    }

    /// <summary>释放单实例锁。</summary>
    public static void ReleaseSingleInstance()
    {
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        try { _instanceMutex?.Dispose(); } catch { }
        _instanceMutex = null;
    }

    // ══════════════════════════════════════════════════════════════

    private static void Log(string message)
    {
        try
        {
            var dir = Core.AppPaths.EnsureDir();
            if (dir.Length == 0) return;
            File.AppendAllText(Path.Combine(dir, "system.log"),
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }
}
