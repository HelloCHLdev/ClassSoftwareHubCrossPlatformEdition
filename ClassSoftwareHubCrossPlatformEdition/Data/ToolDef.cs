using System;
using System.Collections.Generic;
using FluentAvalonia.UI.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Data;

/// <summary>内置工具卡片定义（工具索引页用）。</summary>
public sealed class ToolDef
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Desc { get; init; } = "";

    /// <summary>
    /// 图标。跨平台版直接用 <see cref="FASymbol"/>（FluentAvalonia 自带的 Segoe Fluent Icons 字体），
    /// XAML 里 &lt;FASymbolIcon Symbol="{x:Bind Symbol}"/&gt; 直接用，不做码位查表。
    /// </summary>
    public FASymbol Symbol { get; init; } = FASymbol.Star;

    /// <summary>
    /// 点开要导航到的页面。
    /// ⚠️ 跨平台版这一版只做工具索引页（Pages/Tools 下的工具子页面还没搬），所以永远是 null，
    /// 点击卡片走「用对话框说明」的兜底分支。保留这个属性是为了以后补页面时不用改结构。
    /// </summary>
    public Type? Page { get; init; }

    /// <summary>无障碍 / 调试用：列表项名字就是工具名。</summary>
    public override string ToString() => Name;
}

/// <summary>
/// 内置工具清单 —— 工具索引页的卡片共用这一份。
///
/// 与 Windows 版 <c>Data/ToolDef.cs</c> 的 <c>ToolCatalog</c> 同源，但**只收跨平台版要展示的工具**：
///   · ⚠️「系统镜像下载（mirror-download）」2026-10-09 整体下线，已删；
///   · ⛔「自动关机（autoshutdown）」是 Windows 专有关机能力，跨平台版不收；
///   · ⛔ 其余 Windows 专有工具（音量 / 亮度 / 放大镜 / 截屏 / 任务视图…）同样不收。
///
/// <see cref="ToolDef.Page"/> 一律留 null（工具子页面还没搬），点卡片走对话框兜底分支，
/// 见 <c>Pages/ToolsPage</c>。
/// </summary>
public static class ToolCatalog
{
    public static readonly List<ToolDef> All = new()
    {
        new ToolDef
        {
            Id = "image-color", Name = "图片取色",
            Desc = "丢一张图片进来，自动提取一组柔和的配色（种子色 + 7 档明暗变体），点色块即可复制。",
            Symbol = FASymbol.ColorFill,
        },
        new ToolDef
        {
            Id = "pick-number", Name = "随机抽号",
            Desc = "输入号码范围（比如学号 1~50）就能抽号，支持一次抽多个、抽过不重复。",
            Symbol = FASymbol.Shuffle,
        },
        new ToolDef
        {
            Id = "timer", Name = "课堂计时器",
            Desc = "倒计时 / 秒表，大字号方便投影，到点响铃。",
            Symbol = FASymbol.Clock,
        },
        new ToolDef
        {
            Id = "clock", Name = "全屏时钟",
            Desc = "把屏幕变成一面大钟：可放背景图、调蒙版，全屏显示看时间。",
            Symbol = FASymbol.FullScreen,
        },
        new ToolDef
        {
            Id = "encoding", Name = "编码 / 哈希工具",
            Desc = "Base64、URL 编解码，以及 MD5 / SHA-1 / SHA-256 / SHA-512 哈希。",
            Symbol = FASymbol.Code,
        },
    };
}
