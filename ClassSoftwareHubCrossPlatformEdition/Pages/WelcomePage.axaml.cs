using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClassSoftwareHubCrossPlatformEdition.Core;
using ClassSoftwareHubCrossPlatformEdition.Services;
using FluentAvalonia.UI.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Pages;

/// <summary>
/// 首页：大标题 ClassSoftwareHub → 站点大标题 → 版本图 + 版本号 → 快速开始卡片。
/// 全部原生，不加载网页。
/// </summary>
public sealed partial class WelcomePage : UserControl
{
    public WelcomePage()
    {
        InitializeComponent();
        Loaded += (_, _) => Populate();
    }

    private void Populate()
    {
        var ui = App.Content.Ui;

        var title = ui.AppTitle.Length > 0 ? ui.AppTitle : "电教委员常用软件下载站";
        HomeTitleText.Text = title + " • 桌面版";
        SiteVersionText.Text = ShellConfig.VersionPrefix + ShellConfig.ShellVersion;
        ToolTip.SetTip(SiteVersionText, "网站版本：" + ui.ShortVersion);
        AppVersionText.Text = "网站版本：" + ui.ShortVersion;

        LoadBanner();
        BuildQuickInfo();
        BuildQuickLinks();
    }

    /// <summary>一行快捷入口：项目仓库 / 作者主页 / 赞助作者 / 加入Q群 / 更新日志。</summary>
    private void BuildQuickLinks()
    {
        var links = QuickLinks.Build(App.Content.Ui);
        // ItemsControl 没有 ItemClick，给每张卡挂上 Tag 认回来
        QuickGrid.ItemsSource = links;
    }

    private void Quick_Tapped(object? sender, TappedEventArgs e)
    {
        // ItemsControl 没有 WinUI 的 ItemClick，卡片上挂的是链接本身当 Tag
        var url = UiKit.FindTag(e);
        if (!string.IsNullOrEmpty(url)) App.MainWindow?.OpenExternal(url);
    }

    /// <summary>「硬件信息 / 系统信息」：一行一项的列表（图标 + 标签 + 值），不用卡片。</summary>
    private void BuildQuickInfo()
    {
        var q = SystemInfo.Gather();
        FillRows(HardwareList, q.Hardware);
        FillRows(SystemList, q.System);
    }

    private static void FillRows(StackPanel panel, IReadOnlyList<SystemInfo.InfoLine> lines)
    {
        panel.Children.Clear();
        foreach (var control in UiKit.BuildInfoRows(lines, RowGlyphs))
            panel.Children.Add(control);
    }

    /// <summary>每行的图标。码位表那套（Segoe Fluent Icons）跨平台用不了，这里换成 <see cref="FASymbol"/>。</summary>
    private static readonly Dictionary<string, FASymbol> RowGlyphs = new()
    {
        ["处理器"] = FASymbol.Repair,        // CPU
        ["内存"] = FASymbol.Library,        // RAM
        ["硬盘"] = FASymbol.SaveLocal,      // HardDrive
        ["触摸"] = FASymbol.ZoomIn,         // Touchscreen
        ["显卡"] = FASymbol.Video,          // TVMonitor
        ["显存"] = FASymbol.Pictures,       // Video
        ["显示器"] = FASymbol.Remote,       // SettingsDisplaySound
        ["操作系统"] = FASymbol.Globe,      // System
        ["系统版本"] = FASymbol.Memo,       // Info
        ["安装日期"] = FASymbol.Calendar,   // Calendar
        ["虚拟内存"] = FASymbol.SaveLocal,  // HardDrive
        ["虚拟化"] = FASymbol.Copy,         // VirtualMachineGroup
        ["DirectX"] = FASymbol.Games,       // Game
    };

    private void LoadBanner()
    {
        try
        {
            var path = EmbeddedAssets.ExtractToCache("dv1.0.png", "banner-dv1.0.png");
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                Banner.Source = new Bitmap(path);
        }
        catch { /* 图片读不到就不显示 */ }
    }

    private void Card_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Control el) el.Opacity = 0.88;
    }

    private void Card_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control el) el.Opacity = 1.0;
    }

    private void Card_Tapped(object? sender, TappedEventArgs e)
    {
        var tag = (sender as Control)?.Tag as string;
        switch (tag)
        {
            case "web":
                // 「体验网页版」→ 交给系统默认浏览器打开整站
                // （跨平台版没有内嵌网页窗口）
                App.MainWindow?.OpenExternal(ShellConfig.SiteUrl);
                break;
            case "apps":
                App.MainWindow?.Shell.NavigateTo("apps");
                break;
            case "tools":
                App.MainWindow?.Shell.NavigateTo("tools");
                break;
        }
    }
}
