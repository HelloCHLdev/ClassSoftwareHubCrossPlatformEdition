using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Services;

/// <summary>托盘右键菜单里的一项。</summary>
public sealed class TrayMenuItem
{
    public string Text { get; set; } = "";

    /// <summary>点中后通过 <see cref="TrayHost.CommandInvoked"/> 发出来的命令名。</summary>
    public string Command { get; set; } = "";

    /// <summary>true 就是一条分隔线（忽略 Text/Command）。</summary>
    public bool Separator { get; set; }

    /// <summary>双击托盘图标时默认执行的那项（各系统后端一般会把它标成默认项）。</summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// 系统托盘图标。
///
/// 原来 Windows 版是自己 P/Invoke <c>Shell_NotifyIcon</c> + 造一个隐藏消息窗口实现的（441 行 Win32）。
/// Avalonia 12 自带 <see cref="TrayIcon"/>（Windows / macOS / Linux 都有后端实现），
/// 所以这里只做一层薄封装：把原来那份菜单定义搬过来、把 <see cref="TrayIcon.Clicked"/> 和
/// 每一项的 Click 转成统一的 <see cref="CommandInvoked"/> 命令，上层（MainWindow）一行不用改。
/// </summary>
public sealed class TrayHost : IDisposable
{
    /// <summary>左键单击托盘图标（一般用来显示/隐藏主窗口）。</summary>
    public event Action? LeftClick;

    /// <summary>右键菜单选中某项，参数是菜单 id（show / palette / update / exit）。</summary>
    public event Action<string>? CommandInvoked;

    /// <summary>右键菜单内容（按顺序显示）。留空就用默认那几项。</summary>
    public List<TrayMenuItem> MenuItems { get; } = new();

    private readonly TrayIcon _icon = new();
    private NativeMenu? _menu;
    private bool _disposed;

    public bool IsReady { get; private set; }

    public TrayHost(string iconPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                _icon.Icon = new WindowIcon(iconPath);
        }
        catch (Exception ex)
        {
            ThemeHost.Log("托盘图标加载失败: " + ex.Message);
        }
    }

    /// <summary>建菜单 + 把图标挂上托盘。失败返回 false（不会抛，托盘挂了不该拖垮主程序）。</summary>
    public bool Setup(string tip)
    {
        try
        {
            _icon.ToolTipText = tip;
            _icon.IsVisible = true;
            _icon.Clicked += OnClicked;

            RebuildMenu();

            // TrayIcons 本身就是一个 AvaloniaList<TrayIcon>，作为附加属性挂到 Application 上
            TrayIcon.SetIcons(Application.Current!, new TrayIcons { _icon });
            IsReady = true;
            return true;
        }
        catch (Exception ex)
        {
            ThemeHost.Log("托盘图标挂载失败: " + ex.Message);
            return false;
        }
    }

    private void OnClicked(object? sender, EventArgs e) => LeftClick?.Invoke();

    /// <summary>菜单内容变了就重建一次（各系统后端都要重新导出原生菜单）。</summary>
    public void RebuildMenu()
    {
        var items = MenuItems.Count > 0 ? MenuItems : DefaultMenu();

        _menu = new NativeMenu();
        foreach (var item in items)
        {
            if (item.Separator)
            {
                _menu.Add(new NativeMenuItem { Header = "-" });
                continue;
            }

            var command = item.Command;
            var node = new NativeMenuItem { Header = item.Text };
            node.Click += (_, _) => CommandInvoked?.Invoke(command);
            _menu.Add(node);
        }

        _icon.Menu = _menu;
    }

    /// <summary>改提示文字（鼠标悬停显示）。</summary>
    public void SetTip(string tip)
    {
        try { _icon.ToolTipText = tip; }
        catch { }
    }

    /// <summary>临时藏起来 / 再放出来。</summary>
    public void SetVisible(bool visible)
    {
        try { _icon.IsVisible = visible; }
        catch { }
    }

    /// <summary>
    /// 弹一条系统通知。
    /// ⚠️ Avalonia 12 的 <see cref="TrayIcon"/> 没有通知 API（原来那是 Win32 的 <c>NIF_INFO</c>），
    /// 所以这里退回成"在日志里记一笔"——绝不能因此抛异常打断调用方。
    /// </summary>
    public void Notify(string title, string text)
    {
        ThemeHost.Log($"[通知] {title}：{text}");
    }

    private static List<TrayMenuItem> DefaultMenu() =>
    [
        new TrayMenuItem { Text = "打开主界面", Command = "show", IsDefault = true },
        new TrayMenuItem { Text = "常用工具", Command = "palette" },
        new TrayMenuItem { Separator = true },
        new TrayMenuItem { Text = "检查更新", Command = "update" },
        new TrayMenuItem { Separator = true },
        new TrayMenuItem { Text = "退出", Command = "exit" },
    ];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _icon.Clicked -= OnClicked;
            _icon.IsVisible = false;
            _icon.Dispose();
            _menu = null;
            IsReady = false;
        }
        catch { }
    }
}
