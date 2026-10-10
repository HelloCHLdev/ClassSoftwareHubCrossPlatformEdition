using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ClassSoftwareHubCrossPlatformEdition.Core;
using ClassSoftwareHubCrossPlatformEdition.Pages;
using ClassSoftwareHubCrossPlatformEdition.Services;
using ClassSoftwareHubCrossPlatformEdition.Services.Updating;
using FluentAvalonia.UI.Controls;

namespace ClassSoftwareHubCrossPlatformEdition;

/// <summary>
/// 主窗口。
///
/// 跟 Windows 版比，少了三块 Windows 专属的东西，各自有跨平台替代：
/// · 背景 mica / acrylic → <see cref="Window.TransparencyLevelHint"/> + 半透明画笔（<see cref="ApplyBackdrop"/>）
/// · 托盘（自己 P/Invoke Shell_NotifyIcon）→ Avalonia 的 <see cref="TrayHost"/>
/// · WebView2 网页浮层 → <see cref="ShowWebSheet"/> 改成对话框 +「在浏览器打开」
/// 其余（开链接 / 开文件 / 下载 / 自启 / 置顶 / 收托盘 / 更新检查）逻辑原样搬过来。
/// </summary>
public partial class MainWindow : Window
{
    private readonly SettingsStore _settings = App.Settings;
    private readonly bool _startMinimized;
    private readonly bool _startPalette;

    private TrayHost? _tray;
    private bool _exitRequested;
    private bool _booted;

    /// <summary>原生外壳（首页卡片这些要从这里跳导航）。</summary>
    public ShellPage Shell => NativeShell;

    public MainWindow()
    {
        InitializeComponent();

        Title = ShellConfig.WindowTitle;

        // 窗口尺寸：跟着设置走（WinUI 那边记的是 AppWindow 的 size，这里一样存 Width/Height）
        Width = Math.Max(900, _settings.Current.WindowWidth);
        Height = Math.Max(600, _settings.Current.WindowHeight);
        MinWidth = 900;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var icon = EmbeddedAssets.IconPath;
        if (!string.IsNullOrEmpty(icon) && File.Exists(icon))
        {
            try { Icon = new WindowIcon(icon); } catch { /* 图标读不到就用系统的 */ }
        }

        // 开机自启的启动参数
        var argv = Environment.GetCommandLineArgs();
        _startMinimized = argv.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        _startPalette = argv.Any(a => a.Equals("--palette", StringComparison.OrdinalIgnoreCase));

        Closing += OnClosing;
        Opened += OnOpened;

        // 主题：先落到这棵树上，再让别的登记过的根一起换
        ThemeHost.Apply(RootGrid);
        ThemeHost.Notify();

        ApplyBackdrop(_settings.Current.Backdrop);
        ApplyAlwaysOnTop(_settings.Current.AlwaysOnTop);
        SystemIntegration.SetAutoStart(_settings.Current.AutoStart);

        InitTray();

        // 内容包已经在 App 里 Load 过了；这里把外壳的图标/title 铺上，然后进首页
        NativeShell.SetTitle(ShellConfig.AppName);
        NativeShell.Init();

        // 记一笔「这台机器跑过的版本」（设置页的「历史版本」读的就是这份记录）
        VersionHistory.Record();

        // 后台同步内容包；失败不打扰用户（本机数据照样能用）
        _ = SyncContentAsync();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_booted) return;
        _booted = true;

        // --palette / --minimized：主窗口不露脸，直接收进托盘
        if (_startPalette || _startMinimized)
            HideToTray();

        _ = CheckUpdateOnStartupAsync();
    }

    /// <summary>窗口位置/大小变化时记住（下次启动还原）。</summary>
    private void RememberSize()
    {
        try
        {
            if (Width < 1 || Height < 1) return;
            _settings.Current.WindowWidth = (int)Math.Round(Width);
            _settings.Current.WindowHeight = (int)Math.Round(Height);
        }
        catch { }
    }

    // ============================================================
    // 背景（mica / acrylic / 纯色）
    // ============================================================

    /// <summary>
    /// 背景材质。
    ///
    /// Windows 那边是 <c>SystemBackdrop</c>（MicaBackdrop / DesktopAcrylicBackdrop），只有 Win11 有。
    /// 跨平台版改成 Avalonia 的 <see cref="Window.TransparencyLevelHint"/>：系统能给的层级里挑一个，
    /// 拿不到（Linux / 老系统）就自动落到纯色，画笔和 hint 都是同一套逻辑，调用方不用区分。
    /// </summary>
    private void ApplyBackdrop(string? kind)
    {
        kind = (kind ?? "acrylic").Trim().ToLowerInvariant() switch
        {
            "mica" => "mica",
            "solid" => "solid",
            _ => "acrylic",
        };
        _settings.Current.Backdrop = kind;

        // 暗色下玻璃层本身偏暗，用同一个 accent 画笔靠 alpha 透出后面的内容
        var tint = App.IsDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);

        if (kind == "solid")
        {
            TransparencyLevelHint = Array.Empty<WindowTransparencyLevel>();
            RootGrid.Background = new SolidColorBrush(tint);
            return;
        }

        TransparencyLevelHint = new[]
        {
            kind == "mica" ? WindowTransparencyLevel.Mica : WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.Transparent,
        };

        // 半透明：底下透出来的层次就是系统的模糊/亚克力
        RootGrid.Background = new SolidColorBrush(tint, 0.72);
    }

    /// <summary>换窗口背景材质（设置页用；顺手落盘）。</summary>
    public void SetBackdrop(string? kind)
    {
        ApplyBackdrop(kind);
        _settings.Save();
    }

    // ============================================================
    // 主题 / 窗口状态
    // ============================================================

    /// <summary>切主题（设置页、标题栏那个太阳/月亮按钮都走这里）。</summary>
    public void SetTheme(string? theme)
    {
        theme = string.IsNullOrWhiteSpace(theme) ? "system" : theme.Trim().ToLowerInvariant();
        if (theme is not ("system" or "light" or "dark")) theme = "system";

        _settings.Current.Theme = theme;
        ThemeHost.Notify();                 // 应用级 + 登记过的根一起换
        ApplyBackdrop(_settings.Current.Backdrop);   // 主题变了，玻璃层的 tint 也要跟着重算
        _settings.Save();
    }

    private void ApplyAlwaysOnTop(bool value) => SystemIntegration.SetAlwaysOnTop(this, value);

    public void SetAlwaysOnTop(bool value)
    {
        _settings.Current.AlwaysOnTop = value;
        ApplyAlwaysOnTop(value);
        _settings.Save();
    }

    public void SetAutoStart(bool value)
    {
        _settings.Current.AutoStart = value;
        SystemIntegration.SetAutoStart(value);
        _settings.Save();
    }

    /// <summary>点关闭时收进托盘（true）/ 直接退出（false）。</summary>
    public void SetCloseToTray(bool value)
    {
        _settings.Current.CloseToTray = value;
        _settings.Save();
    }

    public void SetPaletteOnTop(bool value)
    {
        _settings.Current.PaletteOnTop = value;
        _settings.Save();
    }

    // ============================================================
    // 托盘
    // ============================================================

    private void InitTray()
    {
        try
        {
            _tray = new TrayHost(EmbeddedAssets.IconPath ?? "");
            _tray.LeftClick += ToggleMainWindow;
            _tray.CommandInvoked += OnTrayCommand;
            _tray.MenuItems.Add(new TrayMenuItem { Text = "打开主界面", Command = "show", IsDefault = true });
            _tray.MenuItems.Add(new TrayMenuItem { Text = "常用工具", Command = "palette" });
            _tray.MenuItems.Add(new TrayMenuItem { Separator = true });
            _tray.MenuItems.Add(new TrayMenuItem { Text = "检查更新", Command = "update" });
            _tray.MenuItems.Add(new TrayMenuItem { Separator = true });
            _tray.MenuItems.Add(new TrayMenuItem { Text = "退出", Command = "exit" });

            if (!_tray.Setup(ShellConfig.WindowTitle))
                ThemeHost.Log("[tray] 托盘图标没挂上（挂不上时关闭窗口仍然直接退出）");
        }
        catch (Exception ex)
        {
            ThemeHost.Log("[tray] 初始化失败: " + ex.Message);
        }
    }

    private void OnTrayCommand(string command)
    {
        switch (command)
        {
            case "show":
                ShowFromTray();
                break;
            case "palette":
                // 工具浮窗在跨平台版里没搬过来，统一指到「内置工具」页
                ShowFromTray();
                NativeShell.NavigateTo("tools");
                break;
            case "sidebar":
                ShowFromTray();
                NativeShell.NavigateTo("sidebar");
                break;
            case "update":
                ShowFromTray();
                _ = CheckUpdateManualAsync();
                break;
            case "exit":
                ExitApp();
                break;
        }
    }

    /// <summary>左键点托盘：主窗口显示/收起来回切。</summary>
    public void ToggleMainWindow()
    {
        try
        {
            if (IsVisible) HideToTray();
            else ShowFromTray();
        }
        catch (Exception ex)
        {
            ThemeHost.Log("[tray] 切换窗口失败: " + ex.Message);
        }
    }

    /// <summary>收进托盘（任务栏上不留最小化按钮）。</summary>
    public void HideToTray()
    {
        try
        {
            Hide();
            // 藏起来没人看的时候把内存还给系统（教学机 8G，不能白白占着）
            MemoryTrimmer.TrimLater(1200);
        }
        catch (Exception ex)
        {
            ThemeHost.Log("[tray] 隐藏窗口失败: " + ex.Message);
        }
    }

    /// <summary>从托盘把主窗口叫回来。</summary>
    public void ShowFromTray()
    {
        try
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            SystemIntegration.Show(this);
        }
        catch (Exception ex)
        {
            ThemeHost.Log("[tray] 显示窗口失败: " + ex.Message);
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        RememberSize();

        // 「关窗口收托盘」开着 + 不是从托盘菜单点的退出 → 吃掉这次关闭，藏起来
        if (!_exitRequested && _settings.Current.CloseToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        try { _settings.Save(); } catch { }
        try { _tray?.Dispose(); } catch { }
        _tray = null;
    }

    private void ExitApp()
    {
        _exitRequested = true;
        RememberSize();
        try { _settings.Save(); } catch { }
        try { _tray?.Dispose(); } catch { }
        _tray = null;

        // 主窗口可能正藏在托盘里：先把它叫出来再关，否则 ShutdownMode 会等它
        try
        {
            if (!IsVisible) Show();
            Close();
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    // ============================================================
    // 开链接 / 开文件 / 定位
    // ============================================================

    /// <summary>把链接交给系统默认程序（浏览器、协议处理器）。</summary>
    public void OpenExternal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return;
        if (uri.Scheme is not ("http" or "https" or "mailto" or "ftp")) return;

        _settings.Current.LastUrl = uri.ToString();
        if (!SystemIntegration.Open(uri.ToString()))
            ThemeHost.Log("[shell] 打开外部链接失败: " + uri);
    }

    /// <summary>
    /// 打开 Microsoft Store 链接。
    ///
    /// Windows 版是转成 <c>ms-windows-store://</c> 拉起商店应用；这套协议只有 Windows 有，
    /// 跨平台版用 <see cref="StoreLink.ToLaunchableUrl"/>：Windows 走商店应用，
    /// 其它系统退到 <c>apps.microsoft.com</c> 网页（网页版商店在哪儿都能开）。
    /// </summary>
    public void OpenStore(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            OpenExternal(url);
            return;
        }

        var target = StoreLink.ToLaunchableUrl(url);
        if (string.IsNullOrEmpty(target))
        {
            // 不是商店链接：退一步用网页浮层的方式处理
            _ = ShowWebSheet(url);
            return;
        }

        if (!SystemIntegration.Open(target))
            OpenExternal(target);
    }

    /// <summary>用系统默认关联打开一个本地文件（刚下完的安装包之类）。</summary>
    public void OpenFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (!SystemIntegration.Open(path))
            ThemeHost.Log("[shell] 打开文件失败: " + path);
    }

    /// <summary>在文件管理器里定位到某个文件。</summary>
    public void RevealFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (!SystemIntegration.Reveal(path))
            OpenExternal(path);
    }

    // ============================================================
    // 应用内网页：WinUI3 那边是 WebView2「窗中窗」，这里改成「对话框 + 浏览器」
    // ============================================================

    /// <summary>
    /// 打开一个网页。
    ///
    /// ⚠️ 跨平台版**没有内嵌浏览器**：Avalonia 12 不带 WebView，WinUI3 的 WebView2 也带不过来。
    /// 与其塞一个白屏，不如说清楚「这个链接要用浏览器看」，顺带把标题/域名给出来。
    /// 非 http(s) 的协议（商店链接之类）直接交给系统。
    /// </summary>
    public async Task ShowWebSheet(string? url, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var href = url.Trim();

        if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            OpenExternal(href);
            return;
        }

        var host = "网页";
        try { host = new Uri(href).Host; } catch { /* 域名都解析不了就写「网页」 */ }

        var head = string.IsNullOrWhiteSpace(title) ? host : title!;
        await UiKit.ShowAskAsync(
            $"在浏览器里打开「{head}」",
            $"{href}\n\n跨平台版没有内嵌网页窗口，内容用系统浏览器打开是一样的。",
            "用浏览器打开");
    }

    // ============================================================
    // 原生下载（不跳浏览器）
    // ============================================================

    /// <summary>
    /// 下载一个文件：进度对话框 → 完成后可「立即打开 / 打开所在文件夹」。
    /// 下载中允许取消。
    /// </summary>
    public async Task DownloadFile(string? url, string? suggestedName = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        var fileName = DownloadService.ResolveFileName(url, suggestedName);

        var bar = new ProgressBar { Minimum = 0, Maximum = 100, IsIndeterminate = true };
        var status = new TextBlock { Text = "正在连接…", FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        var hint = new TextBlock
        {
            Text = "保存到：" + DownloadService.DefaultDir,
            FontSize = 11.5,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
        panel.Children.Add(bar);
        panel.Children.Add(status);
        panel.Children.Add(hint);

        using var cts = new CancellationTokenSource();
        var dialog = new FAContentDialog
        {
            Title = "下载 " + fileName,
            Content = panel,
            CloseButtonText = "取消",
        };
        dialog.Closing += (_, args) => cts.Cancel();     // 关对话框 = 取消下载

        var showTask = dialog.ShowAsync(App.TopLevel);

        var progress = new Progress<DownloadProgress>(p =>
        {
            bar.IsIndeterminate = p.Indeterminate;
            if (p.Indeterminate)
            {
                status.Text = "已接收 " + p.SizeText + (p.SpeedText.Length > 0 ? "  ·  " + p.SpeedText : "");
            }
            else
            {
                bar.Value = p.Percent;
                status.Text = $"{p.Percent:0}%   {p.SizeText}" + (p.SpeedText.Length > 0 ? "  ·  " + p.SpeedText : "");
            }
        });

        DownloadedFile? file = null;
        string? error = null;
        try
        {
            file = await DownloadService.DownloadAsync(url, suggestedName, null, progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 用户自己取消的，不用报错
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            try { await showTask; } catch { }
        }

        if (error is not null)
        {
            await UiKit.ShowInfoAsync("下载失败", error);
            return;
        }
        if (file is null) return;   // 取消了

        var choice = await UiKit.ShowThreeWayAsync(
            "下载完成",
            $"{file.FileName}\n{file.SizeText}\n\n{file.Path}",
            "立即打开", "打开所在文件夹", "完成");

        if (choice == 0) OpenFile(file.Path);
        else if (choice == 1) RevealFile(file.Path);
    }

    // ============================================================
    // 更新
    // ============================================================

    /// <summary>启动时查一次更新：不强制，有新版就问用户（主窗口藏起来时只走托盘提示）。</summary>
    private async Task CheckUpdateOnStartupAsync()
    {
        try
        {
            if (!_settings.Current.AutoCheckUpdate) return;

            var service = UpdateService.CreateDefault();
            if (!service.Source.IsConfigured) return;

            var channel = UpdateChannels.Parse(_settings.Current.UpdateChannel);
            var result = await service.CheckAsync(channel, ShellConfig.ShellVersion);
            if (result is not { HasUpdate: true, Release: { } release }) return;

            if (!IsVisible)
            {
                // 主窗口没露脸（--minimized / 开机自启 / 直接收托盘）→ 弹框没人看
                _tray?.Notify($"发现新版本 {release.Tag}", "当前不是最新版，点这里看看要不要更新。");
                return;
            }

            if (!await UpdateFlow.AskAsync(App.TopLevel, release)) return;
            UpdateFlow.Run(release);
        }
        catch
        {
            // 启动时查更新失败就安静放过，别影响正常使用
        }
    }

    /// <summary>设置页 / 托盘菜单的「检查更新」：查到新版走更新流程，没有就明确说一声。</summary>
    public async Task CheckUpdateManualAsync()
    {
        try
        {
            var service = UpdateService.CreateDefault();
            if (!service.Source.IsConfigured)
            {
                await UiKit.ShowInfoAsync("检查更新", "更新源还没配置好。");
                return;
            }

            var channel = UpdateChannels.Parse(_settings.Current.UpdateChannel);
            var result = await service.CheckAsync(channel, ShellConfig.ShellVersion);

            if (result is { HasUpdate: true, Release: { } release })
            {
                // 一样先问，绝不替用户做主
                if (!await UpdateFlow.AskAsync(App.TopLevel, release)) return;
                UpdateFlow.Run(release);
                return;
            }

            await UiKit.ShowInfoAsync("已经是最新版本",
                $"当前：{ShellConfig.VersionPrefix}{ShellConfig.ShellVersion}\n" +
                $"通道：{UpdateChannels.ToDisplay(channel)}\n\n{result.Message}");
        }
        catch (Exception ex)
        {
            ThemeHost.Log("[update] 检查更新失败: " + ex.Message);
            await UiKit.ShowInfoAsync("检查更新失败", ex.Message);
        }
    }

    /// <summary>把「这台机器跑过的版本」记一笔（更新流程下载成功后也会调）。</summary>
    public void RecordVersion() => VersionHistory.Record();

    // ============================================================
    // 内容包
    // ============================================================

    /// <summary>
    /// 后台同步内容包：远端 manifest 变了就把变化的文件拖到本地缓存，
    /// 下次启动 <see cref="ContentStore"/> 优先用它。
    /// </summary>
    private async Task SyncContentAsync()
    {
        try
        {
            var before = App.Content.SourceKind;
            var result = await ContentUpdater.SyncAsync();
            if (!result.Updated && before == "cache") return;

            var oldSource = App.Content.Source;
            var oldCount = App.Content.Apps.Count;
            App.Content.Load();

            var changed = App.Content.Source != oldSource || App.Content.Apps.Count != oldCount;
            if (changed)
                ThemeHost.Log($"[content] {result.Message}；{oldSource} → {App.Content.Source}");

            // 清单变了：让当前还开着的页面重新铺一遍
            NativeShell.RefreshCurrent();
        }
        catch
        {
            // 内容同步失败不打扰用户：本机数据照样能用
        }
    }
}
