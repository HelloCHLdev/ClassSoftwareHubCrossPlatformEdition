using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using ClassSoftwareHubCrossPlatformEdition.Core;
using ClassSoftwareHubCrossPlatformEdition.Services;

namespace ClassSoftwareHubCrossPlatformEdition;

public partial class App : Application
{
    /// <summary>用户设置（全局单例；<see cref="Services.SettingsStore"/> 在这里初始化）。</summary>
    public static Services.SettingsStore Settings { get; private set; } = new();

    /// <summary>软件数据（<see cref="Services.ContentUpdater"/> 会在启动时补内容）。</summary>
    public static ContentStore Content { get; } = new();

    /// <summary>主窗口（页面里要用「开链接 / 开文件 / 下载」这些都得经过它）。</summary>
    public static MainWindow? MainWindow { get; internal set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 数据目录先建出来：后面设置、内容缓存、版本记录都要往里写
        AppPaths.EnsureDir();

        Settings.Load();
        Content.Load();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var window = new MainWindow();
            MainWindow = window;
            desktop.MainWindow = window;
        }

        // 设置里的主题（system / light / dark）落到应用级，登记过的窗口根也跟着换
        ThemeHost.Notify();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>当前是不是深色（跟系统时按系统算）。</summary>
    public static bool IsDark
    {
        get
        {
            var app = Current;
            if (app is null) return false;
            return app.RequestedThemeVariant == ThemeVariant.Dark
                || (app.RequestedThemeVariant == ThemeVariant.Default && app.ActualThemeVariant == ThemeVariant.Dark);
        }
    }

    /// <summary>找主窗口 / 任意一个顶层（页面里拿 TopLevel 用；对话框、剪贴板都要）。</summary>
    public static TopLevel? TopLevel
    {
        get
        {
            if (MainWindow is not null) return MainWindow;
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.Windows.FirstOrDefault();
            return null;
        }
    }
}
