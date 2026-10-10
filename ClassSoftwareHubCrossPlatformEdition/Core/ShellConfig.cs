using System;
using System.IO;

namespace ClassSoftwareHubCrossPlatformEdition.Core;

/// <summary>外壳常量集中处（改这里就行）。</summary>
public static class ShellConfig
{
    /// <summary>站点地址。</summary>
    public const string SiteUrl = "https://classsoftwarehub.us.ci";

    public const string AppName = "ClassSoftwareHub";

    /// <summary>窗口标题里补一句「跨平台版」，免得跟 Windows 版混淆。</summary>
    public const string WindowTitle = "ClassSoftwareHub 跨平台版";

    /// <summary>
    /// 外壳版本（跟站点版本无关）。
    /// ⚠️ 规则：基数不随便抬（否则旧包会被强制顶掉）。以后要出 beta 就在后面接 `-insiderN`。
    /// </summary>
    public const string ShellVersion = "1.0.0";

    /// <summary>桌面版的版本号前缀（dv）。</summary>
    public const string VersionPrefix = "dv";

    // ════════════════════════════════════════════════════════════════
    // 更新（双通道：正式版 / 预览版）= GitHub Releases（公开仓库，匿名读，不用令牌）
    //   · 正式版（stable）→ 只看【非预发布】的 Release
    //   · 预览版（insider）→ 预发布 Release ＋ 非预发布（预览用户也能跟上正式版）
    // ════════════════════════════════════════════════════════════════

    /// <summary>更新仓库 owner。</summary>
    public const string UpdateRepoOwner = "c1201y";

    /// <summary>更新仓库名。</summary>
    public const string UpdateRepoName = "ClassSoftwareHub-Desktop";

    /// <summary>可选：GitHub 令牌（留空走匿名）。</summary>
    public const string UpdateToken = "";

    /// <summary>本版本默认走哪条通道：版本号里带 insider 的内部构建默认预览版。</summary>
    public static string DefaultUpdateChannel =>
        ShellVersion.Contains("insider", StringComparison.OrdinalIgnoreCase) ? "insider" : "stable";

    /// <summary>与站点对齐的适配版本号（内容包里读不到 app.version 时的兜底）。</summary>
    public const string SiteVersionTarget = "v2.3.3";

    public const string NetworkErrorMessage = "网络出现错误，请稍后重试";

    /// <summary>标题栏高度。</summary>
    public const int TitleBarHeight = 48;

    /// <summary>导航超时（秒），超时视为网络错误。</summary>
    public const int LoadTimeoutSeconds = 25;

    /// <summary>单实例互斥名（各系统都拿它做唯一标识）。</summary>
    public const string MutexName = "ClassSoftwareHub.Desktop.SingleInstance";

    // ════════════════════════════════════════════════════════════════
    // 内容（软件数据）
    // ════════════════════════════════════════════════════════════════

    public const string SiteRepoOwner = "c1201y";
    public const string SiteRepoName = "ClassSoftwareHub";
    public const string SiteRepoBranch = "main";

    /// <summary>仓库里软件数据所在目录（子目录 apps/ 一个软件一个 json，根上还有 categories.json）。</summary>
    public const string SiteRepoDataDir = "软件数据";

    /// <summary>正式来源：站点上的内容清单。</summary>
    public const string ContentManifestUrl = "https://classsoftwarehub.us.ci/content/manifest.json";

    /// <summary>备用来源（主站挂了可以从网盘拿）。</summary>
    public const string ContentManifestUrlFallback =
        "https://pan.132614.xyz/dav/%E7%BD%91%E7%AB%99/content/manifest.json";

    /// <summary>增量同步下来的内容缓存目录。</summary>
    public static string CachedContentDir => Path.Combine(AppPaths.DataDir, "content");

    /// <summary>安装包 / 可执行文件旁边自带的内容（离线也有清单，联网后自动更新）。</summary>
    public static string BundledContentDir => Path.Combine(AppContext.BaseDirectory, "content");

    /// <summary>
    /// 开发用：直接读站点工程的内容包。跨平台版不再写死某台 Windows 机器的路径 ——
    /// 想用就设环境变量 <c>CSH_CONTENT_DIR</c>，正式用户机器上不存在，自动跳过。
    /// </summary>
    public static string DevContentDir => Environment.GetEnvironmentVariable("CSH_CONTENT_DIR") ?? "";

    /// <summary>提交软件页（网页版地址；跨平台版里只用它做「去网站提交」的兜底）。</summary>
    public const string SubmitPageUrl = "https://classsoftwarehub.us.ci/#/submit";

    /// <summary>Microsoft Store 网页（Windows 专有协议 <c>ms-windows-store://</c> 在非 Windows 上拉不起应用，跨平台版统一开网页）。</summary>
    public const string StoreWebFallback = "https://apps.microsoft.com";
}

/// <summary>应用数据目录（设置、缓存、内容）。跨平台各系统的惯例位置不一样。</summary>
public static class AppPaths
{
    /// <summary>
    /// 数据目录：
    ///   · Windows → %LOCALAPPDATA%\ClassSoftwareHub
    ///   · macOS   → ~/Library/Application Support/ClassSoftwareHub
    ///   · Linux   → $XDG_DATA_HOME/ClassSoftwareHub（一般 ~/.local/share/…）
    /// </summary>
    public static string DataDir { get; } = ResolveDataDir();

    private static string ResolveDataDir()
    {
        // 显式覆盖优先（便携模式 / 测试用）
        var forced = Environment.GetEnvironmentVariable("CSH_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(forced)) return forced.Trim();

        var os = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos"
            : OperatingSystem.IsLinux() ? "linux"
            : "other";

        var root = os switch
        {
            "windows" => SafeFolder(Environment.SpecialFolder.LocalApplicationData),
            "macos" => Path.Combine(
                SafeFolder(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support"),
            "linux" => Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
                ? xdg
                : Path.Combine(SafeFolder(Environment.SpecialFolder.UserProfile), ".local", "share"),
            _ => SafeFolder(Environment.SpecialFolder.UserProfile),
        };

        return Path.Combine(root, AppNameFolder);
    }

    /// <summary>目录名各平台统一（用户手动找文件时不用猜）。</summary>
    public const string AppNameFolder = "ClassSoftwareHub";

    private static string SafeFolder(Environment.SpecialFolder folder)
    {
        try
        {
            var path = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(path)) return path;
        }
        catch
        {
            // 某些精简系统会抛，兜底到临时目录
        }
        return Path.Combine(Path.GetTempPath(), ".csh");
    }

    /// <summary>保证目录存在，返回目录本身（失败返回空串，调用方自己判）。</summary>
    public static string EnsureDir()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            return DataDir;
        }
        catch
        {
            return "";
        }
    }
}
