using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using System;

namespace ClassSoftwareHubCrossPlatformEdition;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .ConfigureFonts(AddAppFonts)
            .LogToTrace();

    /// <summary>
    /// 把 HarmonyOS Sans 注册进字体管理器。
    ///
    /// 目录形式（一个 avares 目录 = 一个字族）而不是单个 ttf：Avalonia 会把目录里同族的
    /// 6 个字重（Thin/Light/Regular/Medium/Bold/Black）合成一个字族，
    /// 之后按各自的 FontWeight 自动挑。
    ///
    /// 用它替换 WithInterFont()：WithInterFont 注册的是 Inter，而 Inter 没有中文字形，
    /// 中文会落到系统兜底字体；换成 HarmonyOS Sans 之后整个应用（含弹出层）的中英文都是同一套字。
    /// 控件侧的字族在 App.axaml 里用 <c>avares://…/Assets/fonts/#HarmonyOS Sans</c> 指定
    /// （Window / ContextMenu / ToolTip 各一条，弹出层继承链会断，所以得单独写）。
    /// </summary>
    private static void AddAppFonts(FontManager fontManager)
    {
        // key 必须是 fonts: 方案（FontManager.AddFontCollection 强制要求），
        // source 才是真正读字体的 avares: 路径。
        const string src = "avares://ClassSoftwareHubCrossPlatformEdition/Assets/fonts/";
        fontManager.AddFontCollection(new EmbeddedFontCollection(
            new Uri("fonts://ClassSoftwareHubCrossPlatformEdition/HarmonyOSSans"),
            new Uri(src)));
    }
}