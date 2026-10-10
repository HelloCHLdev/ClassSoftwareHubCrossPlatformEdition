using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ClassSoftwareHubCrossPlatformEdition.Core;
using ClassSoftwareHubCrossPlatformEdition.Data;
using ClassSoftwareHubCrossPlatformEdition.Services;
using FluentAvalonia.UI.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Pages;

/// <summary>
/// 原生外壳：左侧导航 + 页面容器。
/// 导航：上半区「首页 / 软件下载 / 内置工具 / 侧边布局」，下半区「提交软件 / 设置」。
/// </summary>
public sealed partial class ShellPage : UserControl
{
    /// <summary>导航标签 → 页面类型。</summary>
    private static readonly (string Tag, string Title, Type Page, FASymbol Icon)[] Menu =
    [
        ("home",    "首页",     typeof(WelcomePage),        FASymbol.Home),
        ("apps",    "软件下载", typeof(SoftwarePage),        FASymbol.Download),
        ("tools",   "内置工具", typeof(ToolsPage),           FASymbol.AllApps),
        ("sidebar", "侧边布局", typeof(SidebarLayoutPage),   FASymbol.DockRight),
    ];

    private static readonly (string Tag, string Title, Type Page, FASymbol Icon)[] FooterMenu =
    [
        ("submit",   "提交软件", typeof(SubmitPage),   FASymbol.Send),
        ("settings", "设置",     typeof(SettingsPage), FASymbol.Settings),
    ];

    private string _currentTag = "home";
    private bool _suppressNavEvents;

    public ShellPage()
    {
        InitializeComponent();
        DataContext = this;

        BuildMenu();
        UpdateThemeButton();

        // 每次切页停稳之后收一次内存（页面本身不缓存，这里再把工作集还给系统）
        ContentFrame.Navigated += OnFrameNavigated;
        ActualThemeVariantChanged += (_, _) => UpdateThemeButton();
    }

    private void BuildMenu()
    {
        Nav.MenuItems.Clear();
        foreach (var (tag, title, _, icon) in Menu)
            Nav.MenuItems.Add(NewItem(tag, title, icon));

        Nav.FooterMenuItems.Clear();
        foreach (var (tag, title, _, icon) in FooterMenu)
            Nav.FooterMenuItems.Add(NewItem(tag, title, icon));
    }

    private static FANavigationViewItem NewItem(string tag, string title, FASymbol icon) => new()
    {
        Content = title,
        Tag = tag,
        IconSource = new FASymbolIconSource { Symbol = icon },
    };

    /// <summary>标题栏那个站点图标（MainWindow 把抽出来的图标塞进来）。</summary>
    public void SetIcon(Bitmap? source) => TitleIcon.Source = source;

    /// <summary>顶部那行标题文字。</summary>
    public void SetTitle(string title) => TitleText.Text = title;

    /// <summary>数据加载完之后调用一次：显示数据来源、进首页。</summary>
    public void Init()
    {
        SetTitle(ShellConfig.AppName);
        LoadIcon();
        SelectTag("home");
        NavigateTag("home");
        UpdateThemeButton();
    }

    private void LoadIcon()
    {
        try
        {
            var path = EmbeddedAssets.ExtractToCache("AppIcon-512.png", "app-icon.png");
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                SetIcon(new Bitmap(path));
        }
        catch { /* 图标读不到就不显示 */ }
    }

    // ============================================================
    // 导航
    // ============================================================

    /// <summary>外部（首页卡片等）跳转导航用：先高亮导航项，再切页面。</summary>
    public void NavigateTo(string tag)
    {
        SelectTag(tag);
        NavigateTag(tag);
    }

    /// <summary>
    /// 跳到「内置工具」里的某个工具页。
    /// 先把工具列表页铺一层，这样工具页左上角的返回按钮能正常退回列表。
    /// </summary>
    public void NavigateToTool(Type pageType)
    {
        SelectTag("tools");
        if (ContentFrame.CurrentSourcePageType != typeof(ToolsPage))
            ContentFrame.Navigate(typeof(ToolsPage));
        ContentFrame.Navigate(pageType);
    }

    /// <summary>只切页面，不动导航高亮（软件下载页内部按分类切换时用）。</summary>
    public void NavigateTag(string tag)
    {
        try
        {
            NavigateTagCore(tag);
        }
        catch (Exception ex)
        {
            // 某个页面自己加载失败（比如 XAML 里引了不存在的资源键）不该把整个应用带走 ——
            // 之前踩过：应用会记住上次停留的页，页面一坏就变成「一启动就崩」，用户连设置都进不去。
            LogNavFailure(tag, ex);
            if (tag != "home")
            {
                try { NavigateTagCore("home"); } catch { }
            }
        }
    }

    private void NavigateTagCore(string tag)
    {
        _currentTag = tag;
        switch (tag)
        {
            case "submit":
                ContentFrame.Navigate(typeof(SubmitPage));
                return;
            case "settings":
                ContentFrame.Navigate(typeof(SettingsPage));
                return;
            case "sidebar":
                ContentFrame.Navigate(typeof(SidebarLayoutPage));
                return;
            case "tools":
                ContentFrame.Navigate(typeof(ToolsPage));
                return;
            case "apps":
                ContentFrame.Navigate(typeof(SoftwarePage), "");
                return;
            case "home":
            default:
                if (tag.StartsWith("cat:", StringComparison.Ordinal))
                    ContentFrame.Navigate(typeof(SoftwarePage), tag.Substring(4));
                else
                    ContentFrame.Navigate(typeof(WelcomePage));
                return;
        }
    }

    /// <summary>内容包更新完了：把当前这一页重新铺一遍（软件页 / 首页会读新的清单）。</summary>
    public void RefreshCurrent() => NavigateTag(_currentTag);

    /// <summary>
    /// 进详情页（软件详情）。走同一个 Frame，返回键才能退回来。
    /// 软件下载页的卡片点进来走这儿。
    /// </summary>
    public void NavigateToDetail(Type pageType, object parameter)
    {
        try
        {
            ContentFrame.Navigate(pageType, parameter);
        }
        catch (Exception ex)
        {
            LogNavFailure(pageType.Name, ex);
        }
    }

    /// <summary>从详情页退回列表（详情页左上角那个返回按钮）。</summary>
    public void GoBackToList()
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        else NavigateTag(_currentTag == "home" ? "apps" : _currentTag);
        UpdateBackButton();
    }

    /// <summary>页面加载失败的兜底日志（和主题日志写同一个目录，事后好查）。</summary>
    private static void LogNavFailure(string tag, Exception ex)
    {
        try
        {
            var dir = AppPaths.EnsureDir();
            if (dir.Length == 0) return;
            File.AppendAllText(Path.Combine(dir, "nav.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 导航到「{tag}」失败: {ex}{Environment.NewLine}");
        }
        catch
        {
            // 日志写不进去就算了，别在这里再抛
        }
    }

    private void SelectTag(string tag)
    {
        _suppressNavEvents = true;
        try
        {
            var item = Nav.MenuItems.Concat(Nav.FooterMenuItems)
                .OfType<FANavigationViewItem>()
                .FirstOrDefault(i => i.Tag as string == tag);
            if (item is not null) Nav.SelectedItem = item;
        }
        finally
        {
            _suppressNavEvents = false;
        }
    }

    // ============================================================
    // 事件
    // ============================================================

    private void Nav_ItemInvoked(object? sender, FANavigationViewItemInvokedEventArgs e)
    {
        if (_suppressNavEvents) return;
        if (e.InvokedItemContainer is not FANavigationViewItem item) return;
        NavigateTag(item.Tag as string ?? "home");
    }

    private void Nav_SelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (_suppressNavEvents) return;
        if (e.SelectedItem is not FANavigationViewItem item) return;
        var tag = item.Tag as string;
        if (string.IsNullOrEmpty(tag) || tag == _currentTag) return;
        NavigateTag(tag);
    }

    private void Nav_BackRequested(object? sender, FANavigationViewBackRequestedEventArgs e)
    {
        if (ContentFrame.CanGoBack) ContentFrame.GoBack();
        UpdateBackButton();
    }

    private void OnFrameNavigated(object? sender, FluentAvalonia.UI.Navigation.FANavigationEventArgs e)
    {
        MemoryTrimmer.TrimLater(3000);
        UpdateBackButton();

        // FAFrame 默认不会把参数塞进页面，所以这里自己交接：
        // · 软件下载页靠它按分类打开（cat:xxx → 参数是分类 key，空串是「全部」）
        // · 详情页靠它认软件 id
        switch (e.Content)
        {
            case SoftwarePage software:
                software.SetCategory(e.Parameter as string);
                software.Reload();

                // 从「首页的分类入口」进来的，把导航高亮拨到「软件下载」
                if (e.Parameter is string key && key.Length > 0) SelectTag("apps");
                break;

            case DetailPage detail:
                detail.Load(e.Parameter as string);
                break;
        }
    }

    /// <summary>导航自带的返回按钮：能退就亮，退到头就灰。</summary>
    private void UpdateBackButton() => Nav.IsBackEnabled = ContentFrame.CanGoBack;

    // ============================================================
    // 主题按钮
    // ============================================================

    /// <summary>太阳/月亮按钮：图标显示「点了会切到的那一边」。</summary>
    private void UpdateThemeButton()
    {
        var dark = App.IsDark;
        ThemeIcon.Symbol = dark ? FASymbol.WeatherSunny : FASymbol.WeatherMoon;
        ToolTip.SetTip(ThemeButton, dark ? "切换到浅色模式" : "切换到深色模式");
    }

    private void ThemeButton_Click(object? sender, RoutedEventArgs e)
    {
        var next = App.IsDark ? "light" : "dark";
        App.Settings.Current.Theme = next;
        App.MainWindow?.SetTheme(next);
        UpdateThemeButton();
    }
}
