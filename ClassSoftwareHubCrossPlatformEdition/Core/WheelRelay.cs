using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ClassSoftwareHubCrossPlatformEdition.Core;

/// <summary>
/// 滚轮接力（2026-10-13 建。Nick：「首页左右两边的空白区还能滚，别的页面只有中间那一条能滚」）。
///
/// 现象：除了首页，其它页面为了把内容钉在中间 960 宽，滚动容器**只盖住中间那一列** ——
///   两侧留白、标题行、页脚这些"看着有地方、其实没有可滚的东西"的区域，
///   滚轮命中的是页面根节点；根节点默认没有背景、不参与命中测试，
///   事件从滚动容器**外面**开始冒泡，容器自己永远收不到 ⇒ 鼠标在那儿滚，页面纹丝不动。
///
/// 做法（两步，缺一不可）：
///   ① XAML：给**页面根 Grid** 加 <c>Background="Transparent"</c> —— 不画任何像素，
///      但把整块面积纳入命中测试（与 App.axaml 给全站 ScrollViewer 加 Transparent 是同一招）；
///   ② 代码：构造函数里 <c>Core.WheelRelay.Attach(this, 主滚动容器)</c>。
///
/// ⚠️ 接在**这一页（UserControl）**上、而不是根 Grid 上：UserControl 是冒泡路线上离得最近的
///    共同祖先，一条 AddHandler 就够，也不用给根 Grid 起名字。
/// ⚠️ <c>handledEventsToo</c> 传 false：指针落在**真的**滚动容器里时事件已被它自己消化，
///    这里不能再动一次（否则一格里滚两倍）。
/// ⚠️ 只改 Offset、不碰滚动开关 —— 页面自己把滚动锁掉时自然不生效。
///
/// Avalonia 与 WinUI 的差异（移植时改的）：
///   · 事件用 <c>InputElement.PointerWheelChangedEvent</c> + <c>PointerWheelEventArgs</c>；
///   · 位移量取 <c>e.Delta.Y</c>（WinUI 是 <c>GetCurrentPoint().Properties.MouseWheelDelta</c>）；
///   · 没有 <c>ChangeView</c> / <c>ScrollableHeight</c> / <c>VerticalOffset</c>，
///     直接读写 <c>Offset</c>，可滚高度用 <c>Extent.Height - Viewport.Height</c>；
///   · 没有 <c>VisualTreeHelper</c>，改用 <c>GetVisualChildren()</c>（<c>Avalonia.VisualTree</c>）。
/// </summary>
public static class WheelRelay
{
    /// <summary>滚轮「一档」的位移。WinUI 的 ScrollViewer 默认一档＝3 行 ≈ 48 DIP，这里对齐它。</summary>
    private const double NotchDip = 48.0;

    /// <summary>一次挂载记录：接在谁身上、能转交给哪些容器、以及用来摘除的委托实例。</summary>
    private sealed class Hook
    {
        public EventHandler<PointerWheelEventArgs> Handler = null!;
        public Visual?[] Targets = Array.Empty<Visual?>();
    }

    private static readonly ConditionalWeakTable<InputElement, Hook> Hooks = new();

    /// <summary>
    /// 把 <paramref name="scrollHost"/> 上收到的滚轮，转交给 <paramref name="targets"/> 里
    /// 第一个「指针压在上面」或「真的滚得动」的容器。
    /// </summary>
    /// <param name="scrollHost">接事件的元素，传 <c>this</c>（页面自身）即可。</param>
    /// <param name="targets">主滚动容器，按优先级排。可直接给 ScrollViewer，
    /// 也可以给 ListBox / ItemsControl —— 会自己找到它内部的滚动区。
    /// 同页互斥的两个滚动区（如抽号的抽取/分组视图）都传进来，指针压在哪个就转给哪个。</param>
    public static void Attach(InputElement scrollHost, params Visual?[]? targets)
    {
        if (Hooks.TryGetValue(scrollHost, out var old))
            scrollHost.RemoveHandler(InputElement.PointerWheelChangedEvent, old.Handler);

        var hook = new Hook
        {
            Targets = targets is { Length: > 0 } ? targets : Array.Empty<Visual?>(),
        };
        hook.Handler = (_, e) => OnWheel(scrollHost, hook, e);

        Hooks.Remove(scrollHost);
        Hooks.Add(scrollHost, hook);
        scrollHost.AddHandler(InputElement.PointerWheelChangedEvent, hook.Handler, handledEventsToo: false);
    }

    private static void OnWheel(InputElement host, Hook hook, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (delta == 0) return;

        // 指针压在哪条滚动区上就转给哪条（同页有两个时才分得清）；都没压中就落到第一条。
        ScrollViewer? pick = null;
        ScrollViewer? first = null;
        foreach (var target in hook.Targets)
        {
            var sv = Resolve(target);
            if (sv is null) continue;
            var scrollable = sv.Extent.Height - sv.Viewport.Height;
            if (scrollable <= 0) continue;              // 收起 / 没内容 / 装得下 ⇒ 跳过
            first ??= sv;
            if (Over(sv, e))
            {
                pick = sv;
                break;
            }
        }

        pick ??= first;
        if (pick is null) return;

        var max = pick.Extent.Height - pick.Viewport.Height;
        var to = pick.Offset.Y - Notches(delta) * NotchDip;
        if (to < 0) to = 0;
        else if (to > max) to = max;

        e.Handled = true;
        pick.Offset = new Vector(pick.Offset.X, to);
    }

    /// <summary>
    /// 把滚轮增量统一折算成「一格」。
    ///
    /// ⚠️ Avalonia 各后端给 <c>PointerWheelEventArgs.Delta.Y</c> 的口径并不统一：桌面端常见
    /// 「一格 = ±1」，个别后端回的是 1/120 制的原值（WM_MOUSEWHEEL 那套）。量级差 120 倍 ——
    /// 折错就变成「一格滚满屏」或「滚不动」，所以这里按量级判一次：明显大于 1 格的当作原值除 120。
    /// </summary>
    private static double Notches(double delta) => Math.Abs(delta) > 5 ? delta / 120.0 : delta;

    /// <summary>指针此刻是否落在这个容器的实际范围内（同页有多个滚动区时用来挑一个）。</summary>
    private static bool Over(ScrollViewer sv, PointerEventArgs e)
    {
        try
        {
            var p = e.GetCurrentPoint(sv).Position;
            return p.X >= 0 && p.Y >= 0 && p.X <= sv.Bounds.Width && p.Y <= sv.Bounds.Height;
        }
        catch { return false; }
    }

    /// <summary>把目标解析成真正的 ScrollViewer：给的本来就是就直接用，给的是列表控件就找它内部的滚动区。</summary>
    private static ScrollViewer? Resolve(Visual? target)
    {
        if (target is null) return null;
        if (target is ScrollViewer direct) return direct;

        // 广度优先 ⇒ 拿到的是**最外层**那个滚动区（列表控件模板里的那个）
        var queue = new Queue<Visual>();
        queue.Enqueue(target);
        for (var guard = 0; queue.Count > 0 && guard < 512; guard++)
        {
            var node = queue.Dequeue();
            foreach (var child in node.GetVisualChildren())
            {
                if (child is ScrollViewer sv && sv.Bounds.Height > 0) return sv;
                queue.Enqueue(child);
            }
        }
        return null;
    }
}
