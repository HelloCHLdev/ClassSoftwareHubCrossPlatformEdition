using System;
using System.Collections.Generic;
using Avalonia.Media;
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

/// <summary>一个系统镜像下载入口（对应内容包 text/mirror-sites.json）。</summary>
public sealed class MirrorSite
{
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string Url { get; set; } = "";
    public string Color { get; set; } = "";
    public string Icon { get; set; } = "";

    /// <summary>显示域名（去掉 https:// 与路径）。</summary>
    public string Host
    {
        get
        {
            try { return new Uri(Url).Host; }
            catch { return ""; }
        }
    }

    /// <summary>没有图标时兜底：名字首字。</summary>
    public string Badge => Name.Length > 0 ? Name.Substring(0, 1).ToUpperInvariant() : "?";

    /// <summary>站点有没有自带图标（内容包里的 icon 字段）。</summary>
    public bool IconVisibility => Icon.Length > 0;

    public bool BadgeVisibility => Icon.Length == 0;

    /// <summary>首字方块底色（站点色的淡染）；站点没写 color 就用主题蓝。</summary>
    public IBrush BadgeBackground => Tint(0.12);

    public IBrush BadgeBorder => Tint(0.40);

    private IBrush Tint(double alpha)
    {
        var hex = Color.TrimStart('#');
        if (hex.Length != 6) return Brushes.Transparent;

        try
        {
            var r = Convert.ToByte(hex.Substring(0, 2), 16);
            var g = Convert.ToByte(hex.Substring(2, 2), 16);
            var b = Convert.ToByte(hex.Substring(4, 2), 16);
            var a = (byte)(Math.Clamp(alpha, 0, 1) * 255);
            return new SolidColorBrush(Avalonia.Media.Color.FromArgb(a, r, g, b));
        }
        catch
        {
            return Brushes.Transparent;
        }
    }
}

/// <summary>系统镜像下载页的整页文字 + 站点清单。</summary>
public sealed class MirrorInfo
{
    public string Title { get; set; } = "系统镜像下载";
    public string Subtitle { get; set; } = "";
    public string Disclaimer { get; set; } = "";
    public string Note { get; set; } = "";
    public List<MirrorSite> Sites { get; } = new();
}
