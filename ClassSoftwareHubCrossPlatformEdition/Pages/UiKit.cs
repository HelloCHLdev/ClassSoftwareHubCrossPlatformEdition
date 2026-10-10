using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Pages;

/// <summary>
/// 页面层共用的小工具：对话框、点选回溯、跨平台提示。
/// WinUI 那边是 MessageDialog / InfoBar + 事件，这边统一收敛到这里，页面里只管调。
/// </summary>
internal static class UiKit
{
    /// <summary>宿主：优先主窗口（对话框是它的 overlay）。</summary>
    private static TopLevel? Host => App.TopLevel;

    /// <summary>纯提示（一个「知道了」按钮）。</summary>
    public static async Task ShowInfoAsync(string title, string message)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 420,
            },
            CloseButtonText = "知道了",
        };
        await ShowAsync(dialog);
    }

    /// <summary>带「主 / 次」两个按钮的询问；primary=true 表示按了主按钮。</summary>
    public static async Task<bool> ShowAskAsync(string title, string message, string primary, string close = "取消")
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 420,
            },
            PrimaryButtonText = primary,
            CloseButtonText = close,
            DefaultButton = FAContentDialogButton.Primary,
        };
        var result = await ShowAsync(dialog);
        return result == FAContentDialogResult.Primary;
    }

    /// <summary>
    /// 三选一对话框。返回 0=主 1=次 2=关（和 <see cref="FAContentDialogResult"/> 对齐）。
    /// 设置页里「保留旧版 / 安装 / 忽略」这种三选一用它。
    /// </summary>
    public static async Task<int> ShowThreeWayAsync(string title, string message, string primary, string secondary, string close)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 460,
            },
            PrimaryButtonText = primary,
            SecondaryButtonText = secondary,
            CloseButtonText = close,
            DefaultButton = FAContentDialogButton.Primary,
        };
        var result = await ShowAsync(dialog);
        return result switch
        {
            FAContentDialogResult.Primary => 0,
            FAContentDialogResult.Secondary => 1,
            _ => 2,
        };
    }

    /// <summary>直接弹一个自己拼的对话框（带容错：已经有对话框在显示时安静放过）。</summary>
    public static async Task<FAContentDialogResult> ShowAsync(FAContentDialog dialog)
    {
        var host = Host;
        if (host is null) return FAContentDialogResult.None;
        try
        {
            return await dialog.ShowAsync(host);
        }
        catch
        {
            // 已经有对话框在显示（用户手快点两下）时 ShowAsync 会抛，别把页面带崩
            return FAContentDialogResult.None;
        }
    }

    /// <summary>
    /// 从被点中的元素往上回溯，找第一个带字符串 <c>Tag</c> 的控件。
    ///
    /// 卡片墙用的是 ItemsControl（没有 WinUI 那个 ItemClick），
    /// 所以在 ItemsControl 上接 Tapped，再靠 Tag 认回是哪一张卡 —— 顺序稳定的部分交给 DataContext。
    /// </summary>
    public static string? FindTag(Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (e.Source is not Visual start) return null;

        // 沿命中测试的父链往上找第一个带字符串 Tag 的控件
        for (Visual? node = start; node is not null; node = node.GetVisualParent())
        {
            if (node is Control { Tag: string tag } && tag.Length > 0) return tag;
        }
        return null;
    }

    /// <summary>把字符串 Tag 拆成 "前缀:值"（卡片 Tag 一律这么写：app:xxx / tool:xxx / mod:xxx）。</summary>
    public static (string Prefix, string Value) SplitTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return ("", "");
        var i = tag.IndexOf(':');
        return i < 0 ? (tag, "") : (tag[..i], tag[(i + 1)..]);
    }

    /// <summary>提示条用的纯文本（WinUI 那边是 InfoBar 的 XamlRoot，这边页面自己拼一个）。</summary>
    public static TextBlock Hint(string text, bool muted = true) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Classes = { "muted" },
        IsVisible = muted,
    };

    /// <summary>统一的「开外部链接」入口（所有页面都走 MainWindow，方便记 LastUrl）。</summary>
    public static void OpenExternal(string? url) => App.MainWindow?.OpenExternal(url);

    /// <summary>把当前系统信息（硬件 / 系统）铺成一列行控件，首页和设置页共用。</summary>
    public static IEnumerable<Control> BuildInfoRows(IReadOnlyList<Core.SystemInfo.InfoLine> lines, IReadOnlyDictionary<string, FluentAvalonia.UI.Controls.FASymbol> glyphs)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                yield return new Border
                {
                    Height = 1,
                    Background = (Avalonia.Media.IBrush?)App.Current?.FindResource("DividerStrokeColorDefaultBrush"),
                };
            }
            yield return BuildInfoRow(lines[i], glyphs);
        }
    }

    private static Control BuildInfoRow(Core.SystemInfo.InfoLine line, IReadOnlyDictionary<string, FluentAvalonia.UI.Controls.FASymbol> glyphs)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(148)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };

        var icon = new FASymbolIcon
        {
            Symbol = glyphs.TryGetValue(line.Label, out var sym) ? sym : FluentAvalonia.UI.Controls.FASymbol.Help,
            FontSize = 14,
            Opacity = 0.75,
            Width = 20,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var label = new TextBlock
        {
            Text = line.Label,
            FontSize = 13,
            Opacity = 0.65,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var value = new TextBlock
        {
            Text = line.Value,
            FontSize = 14,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        ToolTip.SetTip(value, line.Value);

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(value, 2);
        grid.Children.Add(icon);
        grid.Children.Add(label);
        grid.Children.Add(value);

        var border = new Border
        {
            Padding = new Thickness(0, 9, 0, 9),
            Child = grid,
        };
        return border;
    }
}
