using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Services.Updating;

/// <summary>
/// 更新流程：**先问用户要不要更新**（绝不强制），用户点了才去下载页。
/// 设置页的「检查更新」和启动时的自动检查都走这里。
///
/// ⚠️ 与 Windows 版的关键差别：原来是「下载 → 校验 → 静默跑安装程序 → 自动重启自己」。
/// 那套是 Inno Setup + msiexec 专属的，macOS（要过 Gatekeeper / 要 .app 包）和 Linux
/// （deb / rpm / AppImage / Flatpak 各不相同）根本没法照搬 ——「偷偷把安装程序跑起来、
/// 跑完把自己重启」在非 Windows 上既做不到也不该做。
/// 跨平台版改成：把对应的安装包（拿不到就退到 Release 页面）交给系统默认浏览器，
/// 用户自己下载、自己装。查询 / 版本比较 / 下载 + 校验的能力原样保留在
/// <see cref="UpdateService"/> 里，将来要做应用内更新直接接上即可。
/// </summary>
public static class UpdateFlow
{
    /// <summary>
    /// 第一步：把「发现新版本」摆给用户看，让他自己决定。
    /// 返回 true = 用户点了「去下载」；false = 选了「稍后」。
    /// </summary>
    public static async Task<bool> AskAsync(TopLevel? top, UpdateRelease release)
    {
        if (top is null) return false;

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = $"新版本：{release.Tag}",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        var detail = "通道：" + UpdateChannels.ToDisplay(release.Channel);
        if (release.Primary is { } package && package.SizeText.Length > 0)
            detail = $"安装包：{package.SizeText}　·　通道：{UpdateChannels.ToDisplay(release.Channel)}";
        if (release.PublishedAt is { } when) detail += $"　·　{when.LocalDateTime:yyyy-MM-dd}";
        body.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(release.Notes))
        {
            body.Children.Add(new TextBlock
            {
                Text = "这次更新：",
                FontSize = 12,
                Opacity = 0.7,
                Margin = new Avalonia.Thickness(0, 4, 0, 0),
            });
            body.Children.Add(new TextBlock
            {
                Text = release.Notes.Trim(),
                FontSize = 12,
                Opacity = 0.8,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxLines = 8,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            });
        }

        body.Children.Add(new TextBlock
        {
            Text = "跨平台版不替你静默安装：点了会用系统默认浏览器打开下载页，你自己下、自己装。",
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
        });

        var dialog = new FAContentDialog
        {
            Title = "发现新版本",
            Content = body,
            PrimaryButtonText = "去下载",
            CloseButtonText = "稍后",
            DefaultButton = FAContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync(top);
        return result == FAContentDialogResult.Primary;
    }

    /// <summary>
    /// 第二步（用户已经同意）：把安装包地址交给系统浏览器。
    /// 拿不到具体安装包就退到该版本的 Release 页面 —— 反正跨平台版本来就得让用户自己选包。
    /// </summary>
    public static bool Run(UpdateRelease release)
    {
        var target = release.Primary?.Url?.ToString();
        if (string.IsNullOrWhiteSpace(target))
            target = $"https://github.com/{Core.ShellConfig.UpdateRepoOwner}/{Core.ShellConfig.UpdateRepoName}/releases/tag/{release.Tag}";

        return SystemIntegration.Open(target);
    }
}
