using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ClassSoftwareHubCrossPlatformEdition.Core;
using ClassSoftwareHubCrossPlatformEdition.Data;
using ClassSoftwareHubCrossPlatformEdition.Services;
using FluentAvalonia.UI.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Pages;

/// <summary>
/// 软件详情页（原生）。版式照站点 DownloadDetailPage.vue：
/// 头部（图标 / 名称 / 标语 / 分类徽标）→ 应用介绍 → 详细信息（版本·体积·系统·官网·GitHub）
/// → 小提示 → 下载（应用商店卡 + 下载项：平台·备注·体积·校验值 + 下载 + GitHub 加速）。
/// 所有链接都交给系统（浏览器 / 下载工具）。
///
/// ⚠️ 跟 Windows 版的差别：「微软商店本体」那个「一键装回来」（<c>wsreset -i</c>，P/Invoke + 提权）
/// 没有搬过来 —— 那是纯 Windows 修复手段，跨平台版不适用。
/// 商店链接统一走 <see cref="MainWindow.OpenStore"/>：Windows 拉起商店应用，其它系统开商店网页。
/// </summary>
public sealed partial class DetailPage : UserControl
{
    private string _storeUrl = "";
    private string _appId = "";
    private Button? _lastCopyButton;
    private DispatcherTimer? _copyTimer;
    private SoftwareApp? _app;

    public DetailPage()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => _copyTimer?.Stop();
    }

    /// <summary>FAFrame 带进来的参数 = 软件 id（FAFrame 默认不把参数塞进页面，由外壳交接）。</summary>
    public void Load(string? id)
    {
        _appId = id ?? "";

        // 换软件时先把上一个的图标通知退订，避免占位字形被旧软件的状态带歪
        if (_app is not null) _app.PropertyChanged -= App_PropertyChanged;

        var ui = App.Content.Ui;
        IntroTitle.Text = ui.T("detail.intro", "应用介绍");
        InfoTitle.Text = ui.T("detail.info", "详细信息");
        DownloadsTitle.Text = ui.T("detail.downloads", "下载");
        StoreTitle.Text = ui.T("detail.store-title", "使用 Microsoft Store 下载");
        StoreDesc.Text = ui.T("detail.store-desc", "由应用商店托管，安装后自动更新，不用手动跟版本。");
        StoreButton.Content = ui.T("detail.store-button", "下载");
        StoreOnlyHint.Text = ui.T("detail.store-only", "该软件通过 Microsoft Store 分发，点上方按钮打开商店页面即可获取");

        var app = App.Content.FindById(_appId);
        if (app is null)
        {
            AppName.Text = ui.T("detail.not-found", "未找到该软件。");
            AppTagline.IsVisible = false;
            CategoryBadge.IsVisible = false;
            IntroTitle.IsVisible = false;
            AppDescription.IsVisible = false;
            InfoTitle.IsVisible = false;
            DownloadsTitle.IsVisible = false;
            StoreCard.IsVisible = false;
            DownloadList.IsVisible = false;
            return;
        }

        // ── 头部 ──
        AppName.Text = app.Name;
        AppTagline.Text = app.Tagline;
        AppTagline.IsVisible = app.Tagline.Length > 0;

        var category = app.CategoryDisplay.Length > 0 ? app.CategoryDisplay : App.Content.CategoryName(app.Category);
        CategoryText.Text = category;
        CategoryBadge.IsVisible = category.Length > 0;

        // 占位字形先兜底（没图/加载中/失败都显示），图片加载成功后由 IconPlaceholder 通知收掉
        _app = app;
        app.PropertyChanged += App_PropertyChanged;
        IconBox.Background = app.IconImage is null
            ? null
            : new ImageBrush
            {
                Source = (IImageBrushSource)app.IconImage,
                Stretch = Stretch.UniformToFill,   // 填满 72×72，多的部分裁掉
                AlignmentX = AlignmentX.Center,    // 关键：从正中间裁，不是左上角
                AlignmentY = AlignmentY.Center,
            };
        IconFallback.IsVisible = app.IconPlaceholder;

        // ── 应用介绍 ──
        AppDescription.Text = app.Description;
        AppDescription.IsVisible = app.Description.Length > 0;
        IntroTitle.IsVisible = AppDescription.IsVisible;

        // ── 小提示 ──
        if (app.Notice.Length > 0)
        {
            NoticeBar.Title = ui.T("detail.notice-title", "小提示");
            NoticeBar.Message = app.Notice;
            NoticeBar.IsOpen = true;
        }

        // ── 详细信息 ──
        InfoList.Children.Clear();
        AddInfoRow(ui.T("detail.version", "软件版本"), app.Version);
        AddInfoRow(ui.T("detail.size", "软件体积"), app.Size);
        AddInfoRow(ui.T("detail.system", "系统限制"), app.System);
        AddLinkRow(ui.T("detail.website", "官方网站"), app.Website);
        AddLinkRow(ui.T("detail.github", "GitHub"), app.Github);

        // ── 下载 ──
        _storeUrl = app.Store.Length > 0
            ? app.Store
            : app.Downloads.FirstOrDefault(d => GithubMirror.IsStoreUrl(d.Url))?.Url ?? "";
        var others = app.Downloads.Where(d => !GithubMirror.IsStoreUrl(d.Url)).ToList();

        StoreCard.IsVisible = _storeUrl.Length > 0;
        StoreOnlyHint.IsVisible = _storeUrl.Length > 0 && others.Count == 0;
        DownloadList.ItemsSource = others;
        DownloadList.IsVisible = others.Count > 0;

        // ── 数据问题 ──
        var issues = App.Content.Issues
            .Where(i => i.File.Contains(app.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (issues.Count > 0)
        {
            IssueBar.Title = "这个软件的数据有提示";
            IssueBar.Message = string.Join("\n", issues.Select(i => i.File + "：" + i.Message));
            IssueBar.IsOpen = true;
        }
    }

    /// <summary>详细信息里的一行（左侧标签固定宽度，右侧值可选中复制）。</summary>
    private void AddInfoRow(string label, string value)
    {
        var row = NewInfoRow(label);
        var text = new TextBlock
        {
            Text = value.Length > 0 ? value : App.Content.Ui.T("detail.pending", "待补充"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Opacity = value.Length > 0 ? 1.0 : 0.6,
        };
        ToolTip.SetTip(text, text.Text ?? "");
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        InfoList.Children.Add(row);
    }

    private void AddLinkRow(string label, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var row = NewInfoRow(label);
        var link = new HyperlinkButton { Content = url, Padding = new Thickness(0), Tag = url };
        link.Click += (s, _) => { if (s is HyperlinkButton { Tag: string u }) Open(u); };
        Grid.SetColumn(link, 1);
        row.Children.Add(link);
        InfoList.Children.Add(row);
    }

    private static Grid NewInfoRow(string label)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(96)));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        row.Children.Add(new TextBlock { Text = label, Opacity = 0.65, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        return row;
    }

    private void Store_Click(object? sender, RoutedEventArgs e)
    {
        if (_storeUrl.Length == 0) return;
        App.MainWindow?.OpenStore(_storeUrl);
    }

    private void Download_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && url.Length > 0)
            OpenDownload(url, (Button)sender);
    }

    /// <summary>
    /// 下载按钮分流：
    ///   · 链接本身就是文件（.exe/.zip/…）→ 原生下载
    ///   · 链接其实是官网页面（很多软件更新频繁，站点就丢个官网地址）→ 交给浏览器
    /// </summary>
    private void OpenDownload(string url, Button? source = null)
    {
        if (DownloadService.IsDirectFileUrl(url))
            _ = App.MainWindow?.DownloadFile(url, source is null ? null : DownloadNameFor(source));
        else
            _ = App.MainWindow?.ShowWebSheet(url, "官网下载");
    }

    /// <summary>下载文件名：优先「软件名 + 版本」，实在没有就让 DownloadService 从 URL 猜。</summary>
    private string? DownloadNameFor(Button b)
    {
        if (_app is null) return null;

        var item = b.DataContext as DownloadItem;
        var platform = item?.Platform ?? "";

        // 用软件名当文件名，扩展名从 URL 里取 —— 这样下载下来是「微信 4.0.exe」而不是一串 hash
        var name = _app.Name;
        if (!string.IsNullOrWhiteSpace(platform)) name += " " + platform;
        if (!string.IsNullOrWhiteSpace(_app.Version)) name += " " + _app.Version;
        return name.Trim();
    }

    /// <summary>校验值：点一下把完整哈希复制走，2 秒后文案还原。</summary>
    private void CopyHash_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string hash || hash.Length == 0) return;

        // 剪贴板拿不到就什么都不做，完整值在 tooltip 里还能手选
        if (!SystemIntegration.CopyText(hash)) return;

        b.Content = App.Content.Ui.T("detail.hash-copied", "已复制");
        _lastCopyButton = b;

        _copyTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _copyTimer.Tick -= CopyTimer_Tick;
        _copyTimer.Tick += CopyTimer_Tick;
        _copyTimer.Start();
    }

    private void CopyTimer_Tick(object? sender, EventArgs e)
    {
        if (_lastCopyButton?.DataContext is DownloadItem item)
            _lastCopyButton.Content = item.HashChipText;
        _lastCopyButton = null;
        _copyTimer?.Stop();
    }

    /// <summary>GitHub 直链才有的「加速下载」：列出所有镜像通道，点哪条走哪条。</summary>
    private async void Mirror_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string url || url.Length == 0) return;

        var ui = App.Content.Ui;
        var panel = new StackPanel { Spacing = 8, MaxWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = ui.T("detail.mirror-desc", "下面的镜像站会把上面的链接原样转发一份，国内下载通常快很多。"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            FontSize = 12.5,
        });

        foreach (var channel in GithubMirror.Channels)
        {
            var target = GithubMirror.MirrorUrl(url, channel);
            var button = new Button
            {
                Content = channel.Name,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Tag = target,
            };
            button.Click += (_, _) => OpenDownload(target, b);
            panel.Children.Add(button);
        }

        panel.Children.Add(new TextBlock
        {
            Text = ui.T("detail.mirror-note", "镜像由第三方公益提供：本站只做跳转，不中转、不修改文件，也不保证它们一直可用。"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.6,
            FontSize = 11.5,
        });

        var dialog = new FAContentDialog
        {
            Title = "加速下载",
            Content = panel,
            CloseButtonText = "关闭",
        };
        await UiKit.ShowAsync(dialog);
    }

    /// <summary>打开链接：跨平台版没有内嵌网页窗口，一律交给系统（浏览器 / 协议处理器）。</summary>
    private void Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            _ = App.MainWindow?.ShowWebSheet(url);
            return;
        }
        SystemIntegration.Open(url);
    }

    private void Back_Click(object? sender, RoutedEventArgs e) => App.MainWindow?.Shell.GoBackToList();

    // 图标加载状态变了（成功 → 收掉占位字形；失败 → 继续顶着）
    private void App_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SoftwareApp.IconPlaceholder) && _app is not null)
            IconFallback.IsVisible = _app.IconPlaceholder;
    }
}
