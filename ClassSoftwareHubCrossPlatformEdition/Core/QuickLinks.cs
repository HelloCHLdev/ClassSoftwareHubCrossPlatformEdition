using System.Collections.Generic;
using FluentAvalonia.UI.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Core;

/// <summary>首页 / 设置页那一行快捷入口的数据。</summary>
public sealed class QuickLink
{
    public string Name { get; set; } = "";

    /// <summary>
    /// 图标。跨平台版直接存 <see cref="FASymbol"/>（FluentAvalonia 自带的 Segoe Fluent Icons 字体），
    /// 不用再做「码位 → 符号名」的查表转换，XAML 里 &lt;FASymbolIcon Symbol="{x:Bind Symbol}"/&gt; 直接用。
    /// </summary>
    public FASymbol Symbol { get; set; } = FASymbol.Star;

    public string Url { get; set; } = "";

    /// <summary>要突出的那颗（「赞助作者」）：模板换成"主题色底 + 反白字"那套。</summary>
    public bool Accent { get; set; }
}

/// <summary>快捷入口清单（两处页面共用，改一处两边都变）。</summary>
public static class QuickLinks
{
    public static List<QuickLink> Build(Data.UiText ui)
    {
        // 「项目仓库」= 桌面版自己的仓库（跟更新通道同一个仓库）
        var repoUrl = $"https://github.com/{ShellConfig.UpdateRepoOwner}/{ShellConfig.UpdateRepoName}";
        if (ShellConfig.UpdateRepoOwner.Length == 0 || ShellConfig.UpdateRepoName.Length == 0)
            repoUrl = ui.T("about.repository-url", "https://github.com/c1201y/ClassSoftwareHub-Desktop");

        return new List<QuickLink>
        {
            new() { Name = "项目仓库", Symbol = FASymbol.Code, Url = repoUrl },
            new()
            {
                Name = "作者主页", Symbol = FASymbol.Contact,
                Url = ui.T("about.author-home-url", "https://space.bilibili.com/1274920807"),
            },
            new()
            {
                Name = "赞助作者", Symbol = FASymbol.Star, Accent = true,
                Url = ui.T("about.reward-url", "https://ifdian.net/a/TinyNickCSHub"),
            },
            new()
            {
                Name = "加入Q群", Symbol = FASymbol.Message,
                Url = ui.T("about.qq-group-url", "https://qm.qq.com/q/wByO7XG8Wk"),
            },
            new() { Name = "更新日志", Symbol = FASymbol.Document, Url = repoUrl + "/releases" },
        };
    }
}
