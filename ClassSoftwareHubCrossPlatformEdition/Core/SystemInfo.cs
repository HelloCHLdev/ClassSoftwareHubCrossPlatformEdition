using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Core;

/// <summary>
/// 首页「硬件信息 / 系统信息」用：抓一份**本机**快照。
/// 全是本机只读查询，不联网、不落盘、不轮询。
///
/// ⚠️ 跨平台版与 Windows 版的差别：原来靠注册表 + SMBIOS + Win32，现在只依赖
/// <see cref="System.Runtime.InteropServices.RuntimeInformation"/> / <see cref="System.GC.GetGCMemoryInfo"/>
/// / <see cref="System.IO.DriveInfo"/> 这三个 .NET 自带的跨平台 API。
/// 拿不到的项（Windows 上的触摸 / 显存 / 虚拟化那种 WMI 才有的）显示 <c>—</c>，跟原来「查不到就空」的表现一致。
/// </summary>
public static class SystemInfo
{
    /// <summary>一行信息：左标签 + 右值。</summary>
    public sealed record InfoLine(string Label, string Value);

    /// <summary>硬件信息 + 系统信息两个板块。</summary>
    public sealed record QuickInfo(IReadOnlyList<InfoLine> Hardware, IReadOnlyList<InfoLine> System);

    private const string Dash = "—";

    public static QuickInfo Gather()
    {
        var hw = new List<InfoLine>
        {
            new("处理器", CpuText()),
            new("内存", MemoryText()),
            new("硬盘", DiskText()),
            new("触摸", TouchText()),
            new("显卡", GpuText()),
            new("显存", VramText()),
            new("显示器", MonitorText()),
        };

        var sys = new List<InfoLine>
        {
            new("操作系统", OsNameText()),
            new("系统版本", OsVersionText()),
            new("安装日期", InstallDateText()),
            new("虚拟内存", PageFileText()),
            new("虚拟化", VirtualizationText()),
            new("DirectX", DirectXText()),
        };

        return new QuickInfo(Clean(hw), Clean(sys));
    }

    private static IReadOnlyList<InfoLine> Clean(List<InfoLine> list) =>
        list.Select(x => new InfoLine(x.Label, Blank(x.Value) ? Dash : x.Value.Trim())).ToList();

    private static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);

    // ══════════════════════════ 硬件信息 ══════════════════════════

    /// <summary>处理器：架构 + 核心 / 线程数（.NET 拿不到型号名，就不硬编）。</summary>
    private static string CpuText()
    {
        try
        {
            var arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "ARM64",
                Architecture.Arm => "ARM",
                _ => RuntimeInformation.OSArchitecture.ToString(),
            };

            var parts = new List<string> { RuntimeInformation.ProcessArchitecture.ToString() + " · " + arch };
            if (Environment.ProcessorCount > 0) parts.Add($"{Environment.ProcessorCount} 线程");
            return string.Join(" · ", parts);
        }
        catch { return ""; }
    }

    /// <summary>内存：总容量 + 当前 GC 堆占用。</summary>
    private static string MemoryText()
    {
        try
        {
            var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (total <= 0) total = GC.GetTotalMemory(false);
            var used = GC.GetTotalMemory(false);
            return $"{Size(total)} 总量 · 进程 {Size(used)}";
        }
        catch { return ""; }
    }

    /// <summary>硬盘：把每个就绪的固定盘列出来（最多 3 个），挑最大的那个算总容量。</summary>
    private static string DiskText()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d =>
                {
                    try { return d.IsReady && d.TotalSize > 0 && d.DriveType != DriveType.CDRom; }
                    catch { return false; }
                })
                .OrderByDescending(d => SafeTotal(d))
                .ToList();

            if (drives.Count == 0) return "";

            var total = drives.Sum(SafeTotal);
            var text = string.Join(" · ", drives.Take(3).Select(d =>
            {
                var name = d.Name.TrimEnd(Path.DirectorySeparatorChar, '/');
                return $"{name} {Size(SafeTotal(d))}";
            }));

            if (drives.Count > 3) text += $" …（共 {drives.Count} 个卷）";
            else if (drives.Count > 1) text += $"　合计 {Size(total)}";
            return text;
        }
        catch { return ""; }
    }

    private static long SafeTotal(DriveInfo d)
    {
        try { return d.IsReady ? d.TotalSize : 0; }
        catch { return 0; }
    }

    /// <summary>触摸：跨平台没有统一 API，桌面端一律当作没有（跟原 Windows 版查不到时一致）。</summary>
    private static string TouchText() => "";

    /// <summary>显卡：.NET 不提供，只能显示「系统没提供」这一档。</summary>
    private static string GpuText() => "";

    /// <summary>显存：同上。</summary>
    private static string VramText() => "";

    /// <summary>显示器：主屏尺寸（这个 .NET / Avalonia 拿得到，走主窗口的 ClientSize）。</summary>
    private static string MonitorText()
    {
        try
        {
            var size = MonitorSize();
            if (size is null) return "";
            return $"{size.Value.Width} × {size.Value.Height}";
        }
        catch { return ""; }
    }

    private static (int Width, int Height)? MonitorSize()
    {
        // 拿主窗口的 ClientSize —— 比遍历屏幕稳，也不依赖 Avalonia 的 Screens（headless 下会抛）
        try
        {
            if (App.TopLevel is { } tl && tl.ClientSize.Width > 0 && tl.ClientSize.Height > 0)
                return ((int)Math.Round(tl.ClientSize.Width), (int)Math.Round(tl.ClientSize.Height));
        }
        catch { /* 还没建窗口 / headless */ }
        return null;
    }

    // ══════════════════════════ 系统信息 ══════════════════════════

    /// <summary>操作系统：系统名 + 架构。</summary>
    private static string OsNameText()
    {
        try
        {
            var name = OperatingSystem.IsWindows() ? "Windows"
                : OperatingSystem.IsMacOS() ? "macOS"
                : OperatingSystem.IsLinux() ? "Linux"
                : OperatingSystem.IsFreeBSD() ? "FreeBSD"
                : RuntimeInformation.RuntimeIdentifier;

            return name + "（" + RuntimeInformation.OSArchitecture + "）";
        }
        catch { return ""; }
    }

    /// <summary>系统版本：<see cref="Environment.OSVersion"/> 那一串（Windows 上有真实版本号，别处是内核版本）。</summary>
    private static string OsVersionText()
    {
        try
        {
            var v = Environment.OSVersion.Version;
            if (v.Major == 0) return RuntimeInformation.OSDescription.Trim();
            return $"{v.Major}.{v.Minor}.{v.Build}" +
                   (v.Revision > 0 ? $".{v.Revision}" : "");
        }
        catch { return ""; }
    }

    /// <summary>安装日期：跨平台没有可靠来源（Windows 读注册表 InstallDate 也没权限），显示「—」。</summary>
    private static string InstallDateText() => "";

    /// <summary>虚拟内存：.NET 拿不到 swap / 页面文件大小，显示「—」。</summary>
    private static string PageFileText() => "";

    /// <summary>虚拟化：同样拿不到。</summary>
    private static string VirtualizationText() => "";

    /// <summary>DirectX：Windows 专有，别的系统上压根不存在这玩意儿。</summary>
    private static string DirectXText() => OperatingSystem.IsWindows() ? "" : "";

    // ══════════════════════════ 小工具 ══════════════════════════

    /// <summary>「15.6 GB」这种人类可读体积。</summary>
    private static string Size(long bytes)
    {
        if (bytes <= 0) return "";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    /// <summary>
    /// 抓一次快照（后台线程调用，避免阻塞 UI）。
    /// Gather 里有几次只读查询（枚举磁盘），放在 UI 线程上偶尔会卡一下。
    /// </summary>
    public static Task<QuickInfo> GatherAsync() => Task.Run(Gather);
}
