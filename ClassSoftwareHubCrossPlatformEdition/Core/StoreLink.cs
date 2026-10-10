using System;
using System.Text.RegularExpressions;

namespace ClassSoftwareHubCrossPlatformEdition.Core;

/// <summary>
/// Microsoft Store 链接转换。
///
/// ⚠️ 跨平台版的差异：Windows 上可以把网页地址转成 <c>ms-windows-store://</c> 协议直接拉起商店应用；
/// macOS / Linux 上没有这个协议，硬发只会「没反应」。所以这里只负责**认出来**，
/// 由 <see cref="OpenUrl"/> 决定走协议还是开网页。
/// </summary>
public static class StoreLink
{
    private static readonly Regex AppsDetail = new(
        @"apps\.microsoft\.com/(?:[A-Za-z-]+/)?detail/([A-Za-z0-9]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LegacyStore = new(
        @"(?:www\.)?microsoft\.com/store/(?:apps|detail|pdp)/([A-Za-z0-9]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>认出来的话返回产品 id（认不出来返回 null）。</summary>
    public static string? ToProductId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var text = url.Trim();

        var match = AppsDetail.Match(text);
        if (!match.Success) match = LegacyStore.Match(text);
        if (!match.Success) return null;

        var id = match.Groups[1].Value;
        return id.Length == 0 ? null : id;
    }

    /// <summary>
    /// 要交给系统打开的那个地址：
    ///   · Windows → <c>ms-windows-store://pdp/?productid=xxx</c>（直接拉起商店应用）
    ///   · 其它系统 → apps.microsoft.com 上的网页地址（系统商店根本不存在，开网页最靠谱）
    /// </summary>
    public static string? ToLaunchableUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var text = url.Trim();

        if (text.StartsWith("ms-windows-store:", StringComparison.OrdinalIgnoreCase))
        {
            // 非 Windows 拿到 ms-windows-store: 只能转成网页
            if (!OperatingSystem.IsWindows())
            {
                var fromProtocol = ToProductId(text);
                return fromProduct(fromProtocol ?? "") ?? ShellConfig.StoreWebFallback;
            }
            return text;
        }

        var id = ToProductId(text);
        if (id is null) return text;   // 不是商店链接 → 当普通地址处理

        return fromProduct(id);

        static string fromProduct(string productId) => OperatingSystem.IsWindows()
            ? "ms-windows-store://pdp/?productid=" + productId
            : ShellConfig.StoreWebFallback + "/detail/" + productId;
    }
}
