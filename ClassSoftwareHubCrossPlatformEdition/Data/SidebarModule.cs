using System;
using System.Collections.Generic;
using System.Linq;
using FluentAvalonia.UI.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Data;

/// <summary>模块怎么打开。</summary>
public static class SidebarModuleKinds
{
    /// <summary>置顶小浮窗（ToolPaletteWindow），不占主界面。</summary>
    public const string Palette = "palette";
    /// <summary>主窗口里的整页工具（拉出主窗口并跳到那一页）。</summary>
    public const string Page = "page";
    /// <summary>讲台动作：按一下就干活（不外跳、不抢焦点、不开新窗口）。</summary>
    public const string Action = "action";
    /// <summary>
    /// 挨着边条弹出的单滑块面板（音量 / 屏幕亮度）：不外跳，在边条**挨着的那一侧**弹一栏浮窗，
    /// 边条本身不收起。
    /// ⚠️ 跨平台版目前**没有**任何模块用这个类型（音量 / 亮度都是 Windows 专有，见
    ///    <see cref="SidebarModules.All"/>）；这里保留定义只为跟站点数据模型 / Windows 版对齐。
    /// </summary>
    public const string Panel = "panel";
}

/// <summary>
/// 侧边栏里的一个"模块"。侧边布局页就是拿 <see cref="SidebarModules.All"/> 这份清单让用户勾选 + 排序，
/// 侧边栏再按 AppSettings.SidebarModuleIds 动态拼出来。
/// </summary>
public sealed class SidebarModule
{
    /// <summary>模块 id（同时也是存取设置用的键；palette 类就是工具 id）。</summary>
    public string Id { get; init; } = "";

    /// <summary>完整名字（侧边布局页、鼠标悬停提示用）。</summary>
    public string Name { get; init; } = "";

    /// <summary>侧边栏按钮上的短名（那条很窄，只放得下两三个字）。</summary>
    public string ShortName { get; init; } = "";

    /// <summary>Segoe Fluent Icons 字形（跨平台版用 FluentAvalonia 的 <see cref="FASymbol"/> 枚举）。</summary>
    public FASymbol Symbol { get; init; } = FASymbol.Star;

    /// <summary>SidebarModuleKinds.Palette / .Page / .Action。</summary>
    public string Kind { get; init; } = SidebarModuleKinds.Palette;

    /// <summary>kind = page 时要跳转的页面类型（跨平台版还没搬工具子页面，这里一律留空）。</summary>
    public Type? Page { get; init; }

    /// <summary>想给某个模块写更贴切的说明就填它（不填就用 Kind 的通用说法）。</summary>
    public string? Note { get; init; }

    /// <summary>给用户看的一句话说明。</summary>
    public string Hint => Note ?? Kind switch
    {
        SidebarModuleKinds.Page => "打开主窗口中的该页面",
        SidebarModuleKinds.Action => "单击即执行，不切换窗口、不抢占焦点",
        SidebarModuleKinds.Panel => "在侧边栏旁显示面板，不切换窗口、不占用主界面",
        _ => "显示浮窗，不占用主界面"
    };
}

/// <summary>侧边栏可拼的模块清单（改这里就能加新模块）。</summary>
public static class SidebarModules
{
    public static readonly IReadOnlyList<SidebarModule> All = new List<SidebarModule>
    {
        new() { Id = "pick-number", Name = "随机抽号", ShortName = "抽号", Symbol = FASymbol.Shuffle, Kind = SidebarModuleKinds.Palette },
        new() { Id = "timer", Name = "课堂计时", ShortName = "计时", Symbol = FASymbol.Clock, Kind = SidebarModuleKinds.Palette },
        new() { Id = "stopwatch", Name = "秒表计时", ShortName = "秒表", Symbol = FASymbol.Stop, Kind = SidebarModuleKinds.Palette },
        new() { Id = "clock", Name = "全屏时钟", ShortName = "时钟", Symbol = FASymbol.FullScreen, Kind = SidebarModuleKinds.Palette },
        new() { Id = "image-color", Name = "图片取色", ShortName = "取色", Symbol = FASymbol.ColorFill, Kind = SidebarModuleKinds.Page },
        new() { Id = "encoding", Name = "编码 / 哈希转换", ShortName = "编码", Symbol = FASymbol.Code, Kind = SidebarModuleKinds.Page },

        // ⚠️ 跨平台版只保留上面这 6 个纯跨平台模块。Windows 专有的一律**不保留、也不新增**：
        //    · mirror-download（2026-10-09「系统镜像下载」整体下线）
        //    · volume / brightness（音量、屏幕亮度 —— 用到 Windows 系统 API）
        //    · mag / screenshot / taskview / showdesktop / closefg / closeall（讲台动作）
        //    · taskmgr / security / clipboard / cancelshutdown / killseewo / keyboard（系统操作）
        //    老存档里若还存着这些 id，<see cref="SidebarModules.Find"/> 会返回 null，
        //    「侧边布局」页与存档迁移都会把它跳过（不会崩，见 SettingsStore.SidebarModulesRevision）。
    };

    /// <summary>恢复默认时用的清单（就是老版本侧边栏的那四个）。</summary>
    public static readonly string[] DefaultIds = { "pick-number", "timer", "stopwatch", "clock" };

    /// <summary>按 id 找模块；找不到（比如设置里存了已经删掉的模块）返回 null，调用方直接跳过。</summary>
    public static SidebarModule? Find(string? id) =>
        string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(m => m.Id == id);
}

/// <summary>「侧边布局」页里的一行（给 ItemsControl 用）。</summary>
public sealed class SidebarModuleRow
{
    public SidebarModuleRow(SidebarModule m, bool enabled)
    {
        Id = m.Id;
        Name = m.Name;
        Hint = m.Hint;
        Symbol = m.Symbol;
        Enabled = enabled;
    }

    public string Id { get; }
    public string Name { get; }
    public string Hint { get; }
    public FASymbol Symbol { get; }

    /// <summary>这个模块当前在不在侧边栏上（开关绑它）。</summary>
    public bool Enabled { get; set; }
}
