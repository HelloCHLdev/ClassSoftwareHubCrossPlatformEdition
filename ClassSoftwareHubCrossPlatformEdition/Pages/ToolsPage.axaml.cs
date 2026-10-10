using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassSoftwareHubCrossPlatformEdition.Data;
using FluentAvalonia.UI.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Pages;

/// <summary>
/// 内置工具索引页。
///
/// ⚠️ 跟 Windows 版的差别：那边每个 <see cref="ToolDef.Page"/> 都指向 <c>Pages/Tools/</c> 下的一个
/// 真页面（抽号 / 计时 / 时钟 / 取色 / 编码 / 镜像）。这次跨平台移植**不含工具子页**，
/// 所以 <c>Page</c> 一律是 null —— 点卡片不跳页，而是老实说清楚「这个工具还没进跨平台版」，
/// 顺便把它的说明给出来。侧边栏的模块勾选在「侧边布局」页里照旧能配、能存。
/// </summary>
public sealed partial class ToolsPage : UserControl
{
    private static readonly List<ToolDef> All =
    [
        new()
        {
            Id = "image-color", Name = "图片取色",
            Desc = "丢一张图片进来，自动提取一组柔和的配色（种子色 + 7 档明暗变体），点色块即可复制。",
            Symbol = FASymbol.ColorFill,
        },
        new()
        {
            Id = "pick-number", Name = "随机抽号",
            Desc = "输入号码范围（比如学号 1~50）就能抽号，支持一次抽多个、抽过不重复。",
            Symbol = FASymbol.Shuffle,
        },
        new()
        {
            Id = "timer", Name = "课堂计时器",
            Desc = "倒计时 / 秒表，大字号方便投影，到点响铃。",
            Symbol = FASymbol.Clock,
        },
        new()
        {
            Id = "clock", Name = "全屏时钟",
            Desc = "把屏幕变成一面大钟：可放背景图、调蒙版，全屏显示看时间。",
            Symbol = FASymbol.FullScreen,
        },
        new()
        {
            Id = "encoding", Name = "编码 / 哈希工具",
            Desc = "Base64、URL 编解码，以及 MD5 / SHA-1 / SHA-256 / SHA-512 哈希。",
            Symbol = FASymbol.Code,
        },
        new()
        {
            Id = "mirror-download", Name = "系统镜像下载",
            Desc = "Windows 等系统镜像的官方 / 可信第三方入口，点一行直接跳转（本站不存镜像）。",
            Symbol = FASymbol.Download,
        },
    ];

    private bool _loading;

    public ToolsPage()
    {
        InitializeComponent();
        ToolGrid.ItemsSource = All;

        // 这里的开关（「点关闭时收进托盘」和设置页里是同一个值）
        _loading = true;
        TraySwitch.IsChecked = App.Settings.Current.CloseToTray;
        _loading = false;
    }

    private void OpenPalette_Click(object? sender, RoutedEventArgs e) => _ = ShowPaletteNoteAsync();

    private static async Task ShowPaletteNoteAsync() =>
        await UiKit.ShowInfoAsync("常用工具窗口",
            "跨平台版暂时没有独立的置顶工具浮窗。\n\n" +
            "想用这些工具，可以先在「侧边布局」页里挑好要放哪几个模块（选择会存下来），" +
            "「点关闭时收进托盘」的开关也在这页下方。");

    private void TraySwitch_Toggled(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.MainWindow?.SetCloseToTray(TraySwitch.IsChecked == true);
    }

    private async void ToolGrid_Tapped(object? sender, TappedEventArgs e)
    {
        var id = UiKit.FindTag(e);
        if (string.IsNullOrEmpty(id)) return;
        var def = All.Find(t => t.Id == id);
        if (def is null) return;

        // 工具子页不在这次的移植范围里：说清楚 + 给出说明，别让人以为程序坏了
        await UiKit.ShowInfoAsync(def.Name,
            def.Desc + "\n\n这个工具还没有跨平台版本（Windows 版里有独立的浮窗和工具页）。");
    }

    // 卡片自己的悬停反馈（容器 chrome 已由 App.axaml 里的 ItemsControl.cshcard 规则关掉）
    private void Card_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border b) b.Opacity = 0.88;
    }

    private void Card_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border b) b.Opacity = 1.0;
    }
}
