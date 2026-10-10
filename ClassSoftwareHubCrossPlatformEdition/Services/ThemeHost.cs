using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using ClassSoftwareHubCrossPlatformEdition.Core;

namespace ClassSoftwareHubCrossPlatformEdition.Services;

/// <summary>
/// 主题（深浅色）分发。
///
/// 为啥要它：WinUI3 里 <c>RequestedTheme</c> **只作用于设它的那棵树**，主窗口设了不代表别的窗口跟着变 ——
/// 于是侧边栏 / 常用工具浮窗 / 截图的那几个窗会一直跟着**系统**主题走。
///
/// 跨平台版这边 Avalonia 12 的 <see cref="ThemeVariantScope.RequestedThemeVariantProperty"/> 语义完全一样
/// （同样只影响自己这棵视觉树），所以这套「登记根元素 → 设置一变全体跟着换」的写法原样搬过来。
/// 区别只是：顺手也会把**应用级**的主题设成同一个值，这样没被登记的窗口（比如对话框）也一起变。
///
/// 用法：每个自己开出来的窗口，构造时来一句 <c>ThemeHost.Apply(Root)</c>；
/// 用户改设置时主窗口叫一声 <c>ThemeHost.Notify()</c>，登记过的根一起换。
/// </summary>
public static class ThemeHost
{
    private static readonly List<WeakReference<StyledElement>> Roots = new();

    /// <summary>设置里的主题（system | light | dark）映射成 <see cref="ThemeVariant"/>。</summary>
    public static ThemeVariant Map(string? theme) => (theme ?? "").Trim().ToLowerInvariant() switch
    {
        "light" => ThemeVariant.Light,
        "dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };

    /// <summary>把当前设置的主题应用到某个窗口的根元素，并登记（之后改设置会一起变）。</summary>
    public static void Apply(StyledElement root)
    {
        try
        {
            Set(root, Map(CurrentSetting()));
            Roots.RemoveAll(r => !r.TryGetTarget(out _));
            Roots.Add(new WeakReference<StyledElement>(root));
        }
        catch (Exception ex)
        {
            Log("应用窗口主题失败: " + ex.Message);
        }
    }

    /// <summary>设置变了：应用级主题 + 所有登记过的窗口根元素一起换。</summary>
    public static void Notify()
    {
        var theme = Map(CurrentSetting());
        try
        {
            if (Application.Current is { } app) app.RequestedThemeVariant = theme;
        }
        catch { /* 应用级设置失败不影响单棵树的切换 */ }

        foreach (var r in Roots)
        {
            try
            {
                if (r.TryGetTarget(out var root) && root is not null)
                    Set(root, theme);
            }
            catch { }
        }
    }

    /// <summary>当前实际生效的主题。</summary>
    public static ThemeVariant Current =>
        Application.Current?.RequestedThemeVariant ?? ThemeVariant.Default;

    /// <summary>
    /// 设置某棵视觉树的主题。Avalonia 12 里 <c>RequestedThemeVariant</c> 是挂在
    /// <see cref="ThemeVariantScope"/> 上的附加属性（生成出来的 <c>Get/SetRequestedThemeVariant</c>
    /// 并不公开），所以走 <c>SetValue</c> 最稳。
    /// </summary>
    public static void Set(StyledElement target, ThemeVariant variant) =>
        target.SetValue(ThemeVariantScope.RequestedThemeVariantProperty, variant);

    private static string CurrentSetting()
    {
        try
        {
            var s = App.Settings.Current;
            if (s.SplitTheme)                                     // 分体：外部组件自己一套外观
                return string.IsNullOrWhiteSpace(s.ExternalTheme) ? "system" : s.ExternalTheme;
            return s.Theme;
        }
        catch { return "system"; }
    }

    /// <summary>日志写到数据目录；写不动就算了（主题本身不该因此失败）。</summary>
    public static void Log(string message)
    {
        try
        {
            var dir = AppPaths.EnsureDir();
            if (dir.Length == 0) return;
            File.AppendAllText(Path.Combine(dir, "theme.log"),
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
        }
        catch { }
    }
}
