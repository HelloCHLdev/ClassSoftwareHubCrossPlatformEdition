using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassSoftwareHubCrossPlatformEdition.Core;
using ClassSoftwareHubCrossPlatformEdition.Data;
using ClassSoftwareHubCrossPlatformEdition.Services;
using FluentAvalonia.UI.Controls;

namespace ClassSoftwareHubCrossPlatformEdition.Pages;

/// <summary>
/// 「侧边布局」：搭积木那样拼侧边栏。
/// 左 = 模块库（卡片），右 = 按真实侧边栏比例画的预览条。
/// 加点：把卡片拖进右边 / 点一下卡片（放到末尾）；排序：在右边拖；移除：拖出右边松手，或格子上的 ×。
///
/// 拖动是**自己画的**（Pointer 捕获 + 浮在上面的替身卡片），所以卡片会跟着指针走、
/// 预览里的模块会实时"让开位置"，松手才落位。结果存在 AppSettings.SidebarModuleIds，改完立刻生效。
///
/// ⚠️ 跟 Windows 版的差别：那边改完会立刻 <c>ToolSidebarWindow.ApplyModules()</c>（真侧边栏跟着变）。
/// 跨平台版还没有那条常驻侧边浮窗，所以这里只落盘（SettingsStore）—— 清单格式一模一样，
/// 侧边栏搬过来之后直接读这份 <c>SidebarModuleIds</c> 就能生效。
/// </summary>
public sealed partial class SidebarLayoutPage : UserControl
{
    /// <summary>指针挪过这么多像素才算"在拖"，否则当点击。</summary>
    private const double DragSlop = 4;

    /// <summary>让位开的那条缝 = 格子高 56 + 间距 3。</summary>
    private const double GapDip = 59;

    /// <summary>当前拼好的模块 id，顺序 = 侧边栏上从上到下。</summary>
    private readonly List<string> _ids = new();

    /// <summary>预览条里的模块格子（顺序同上）。</summary>
    private readonly List<Control> _tiles = new();

    /// <summary>按下那一刻各格子的中心 Y（不含偏移）—— 算落点用它，免得被让位动画带着来回抖。</summary>
    private readonly List<double> _tileBaseY = new();

    private string? _dragId;                // 正被按住的模块
    private bool _dragFromPreview;          // 是从预览里拖的，还是从库里拖的
    private bool _dragging;                 // 已越过阈值，真在拖了
    private Point _pressInRoot;
    private Point _grabOffset;              // 按下点在卡片内的位置（替身要按这个对齐指针）
    private Control? _pressSource;
    private Border? _ghost;                 // 跟着指针跑的那张卡
    private Border? _spacer;                // 让位用的"空档"（在缝的位置插进去，自己长高）
    private int _gapIndex = -1;             // 现在让开的是第几个缝
    private bool _swapping;                 // ↑/↓ 的滑动动画正在进行（这段时间别再点）
    private double _pageScrollOffset;       // 拖起来之前的滚动位置（拖的时候要把整页滚动锁掉）
    private ScrollBarVisibility _pageScrollBar = ScrollBarVisibility.Auto;

    public SidebarLayoutPage()
    {
        InitializeComponent();
        LoadFromSettings();

        // ⚠️ 别在构造函数里就画：那会儿页面还没挂到窗口的主题树上，RequestedThemeVariant 还是 Default，
        //    颜色会按"系统主题"画。等挂上去（AttachedToVisualTree）再画，主题已经确定；
        //    以后主题一变（ActualThemeVariantChanged）也重画。
        AttachedToVisualTree += (_, _) => Refresh();
        ActualThemeVariantChanged += (_, _) => Refresh();
    }

    // ── 数据 ──────────────────────────────────────────────────────────────

    private void LoadFromSettings()
    {
        _ids.Clear();
        foreach (var id in App.Settings.Current.SidebarModuleIds ?? Array.Empty<string>())
        {
            // 认不出来的 id（老设置里存了已删除的模块）丢掉；重复的也丢掉
            if (SidebarModules.Find(id) is not null && !_ids.Contains(id)) _ids.Add(id);
        }
    }

    private void Save()
    {
        App.Settings.Current.SidebarModuleIds = _ids.ToArray();
        App.Settings.Save();
    }

    private void Refresh()
    {
        BuildLibrary();
        BuildPreview();

        if (SidebarButton is not null)
            SidebarButton.Content = App.Settings.Current.SidebarEnabled ? "隐藏侧边栏" : "显示侧边栏";
    }

    private void AddToEnd(string id)
    {
        if (_ids.Contains(id)) return;
        _ids.Add(id);
        Save();
        Refresh();
    }

    // ── 主题取色 ──────────────────────────────────────────────────────────
    //  ⚠️ 键名跟 App.axaml 里那份 ThemeDictionaries 对齐。走 Application.Current 取（不是硬编码），
    //     主题一变这些画笔自己跟着换；万一键没了也只是退回灰色，绝不抛。

    private static IBrush Br(string shortName)
    {
        var key = shortName switch
        {
            "ModCardBg" => "CardBackgroundFillColorDefaultBrush",
            "ModCardStroke" => "CardStrokeColorDefaultBrush",
            "ModHoverBg" => "ControlFillColorSecondaryBrush",
            "ModAccent" => "AccentFillColorDefaultBrush",
            "ModTextDim" => "TextFillColorSecondaryBrush",
            _ => "TextFillColorPrimaryBrush",
        };
        try
        {
            if (App.Current?.FindResource(key) is IBrush b) return b;
        }
        catch
        {
            // 键不存在（主题字典还没挂上）—— 下面兜底
        }
        return Brushes.Gray;
    }

    // ── 左：模块库 ────────────────────────────────────────────────────────

    private void BuildLibrary()
    {
        LibraryPanel.Children.Clear();

        Grid? row = null;
        var col = 0;
        foreach (var m in SidebarModules.All)
        {
            if (col == 0)
            {
                row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
                LibraryPanel.Children.Add(row);
            }

            var card = BuildLibraryCard(m, _ids.Contains(m.Id));
            Grid.SetColumn(card, col);
            row!.Children.Add(card);
            col = col == 0 ? 1 : 0;
        }
    }

    /// <summary>库里的卡片。可用的能拖能点；已经在侧边栏里的变灰、只做展示。</summary>
    private Border BuildLibraryCard(SidebarModule m, bool used)
    {
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        text.Children.Add(new TextBlock
        {
            Text = m.Name,
            FontSize = 14.5,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Br("ModText"),
        });
        text.Children.Add(new TextBlock
        {
            Text = used ? "已经在侧边栏里了" : m.Hint,
            FontSize = 11.5,
            Opacity = 0.65,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Br("ModTextDim"),
        });

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        grid.Children.Add(new FASymbolIcon
        {
            Symbol = m.Symbol,
            FontSize = 20,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var badge = new FASymbolIcon
        {
            Symbol = used ? FASymbol.Checkmark : FASymbol.Add,   // 已加 = 对勾 / 未加 = 加号
            FontSize = 13,
            Opacity = used ? 0.55 : 0.8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);

        var card = new Border
        {
            Height = 72,
            Padding = new Thickness(14, 0),
            Background = Br("ModCardBg"),
            BorderBrush = Br("ModCardStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = m.Id,
            Opacity = used ? 0.5 : 1,
            Child = grid,
        };
        ToolTip.SetTip(card, used
            ? $"{m.Name}（已在侧边栏里）"
            : $"{m.Name} —— 点一下加进侧边栏，也可以拖到右边");

        if (used) return card;                          // 已经在了：只展示，不加不拖

        card.PointerEntered += (_, _) => card.BorderBrush = Br("ModAccent");
        card.PointerExited += (_, _) => card.BorderBrush = Br("ModCardStroke");
        card.PointerPressed += (s, e) => BeginPress(s, e, m.Id, false);
        card.PointerMoved += OnPointerMoved;
        card.PointerReleased += OnPointerReleased;
        card.PointerCaptureLost += (_, _) => CancelDrag();
        return card;
    }

    // ── 右：侧边栏预览 ────────────────────────────────────────────────────

    private void BuildPreview()
    {
        PreviewPanel.Children.Clear();
        _tiles.Clear();
        _spacer = null;

        // 顶部那条"常用工具"标题（照侧边栏的样子来）
        PreviewPanel.Children.Add(new TextBlock
        {
            Text = "常用工具",
            FontSize = 10,
            Opacity = 0.55,
            Width = 96,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 2),
        });

        foreach (var id in _ids)
        {
            if (SidebarModules.Find(id) is { } m) PreviewPanel.Children.Add(BuildRow(m));
        }

        if (_ids.Count == 0)
        {
            PreviewPanel.Children.Add(new TextBlock
            {
                Text = "把左边\n卡片拖进来",
                FontSize = 10.5,
                Opacity = 0.5,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Width = 96,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Margin = new Thickness(4, 10),
            });
        }

        // 注：侧边栏自带的「收起 / 位置复原 / 隐藏」在这里**不画**（不能拼不能删，画出来只会挤位置）。
    }

    /// <summary>
    /// 预览里的一行：左边是模块格子（照侧边栏的样子），右边三个独立按钮（上移 / 下移 / 移除）。
    /// 按钮做得大（38×38）且常显 —— 之前挤在格子里 20×17，触屏根本点不准。
    /// 整行一起做让位动画，所以带按钮一起挪。
    /// </summary>
    private Control BuildRow(SidebarModule m)
    {
        var index = _ids.IndexOf(m.Id);
        var tile = BuildTile(m);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        actions.Children.Add(ActionButton(FASymbol.ChevronUp, "往上挪一位", m.Id, MoveUp_Click, index > 0));
        actions.Children.Add(ActionButton(FASymbol.ChevronDown, "往下挪一位", m.Id, MoveDown_Click, index < _ids.Count - 1));
        actions.Children.Add(ActionButton(FASymbol.Delete, "从侧边栏移除", m.Id, Remove_Click, true));

        var row = new Grid { Height = 56, ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.Children.Add(tile);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);

        _tiles.Add(row);
        return row;
    }

    /// <summary>右边那几个按钮：38×38，常显，悬停给个底色（触屏没有悬停也能用）。</summary>
    private Button ActionButton(FASymbol symbol, string tip, string id, EventHandler<RoutedEventArgs> onClick, bool enabled)
    {
        var b = new Button
        {
            Content = new FASymbolIcon { Symbol = symbol, FontSize = 15 },
            Width = 38,
            Height = 38,
            MinWidth = 0,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Tag = id,
            IsEnabled = enabled,
            Opacity = enabled ? 0.85 : 0.28,
        };
        ToolTip.SetTip(b, tip);
        b.PointerEntered += (_, _) => { if (enabled) b.Background = Br("ModHoverBg"); };
        b.PointerExited += (_, _) => b.Background = Brushes.Transparent;
        b.Click += onClick;
        return b;
    }

    /// <summary>预览里的一个模块格子：图标 + 短名，跟真侧边栏一个模样。</summary>
    private Border BuildTile(SidebarModule m)
    {
        var content = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        content.Children.Add(new FASymbolIcon
        {
            Symbol = m.Symbol,
            FontSize = 19,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = m.ShortName,
            FontSize = 11,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = Br("ModText"),
        });

        var tile = new Border
        {
            Width = 96,
            Height = 56,
            CornerRadius = new CornerRadius(6),
            Background = Br("ModCardBg"),
            BorderBrush = Br("ModCardStroke"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 0),
            Tag = m.Id,
            Child = content,
        };
        ToolTip.SetTip(tile, $"{m.Name} —— 拖着重排，拖到别处松手就移除");

        tile.PointerEntered += (_, _) =>
        {
            tile.Background = Br("ModHoverBg");
            tile.BorderBrush = Br("ModAccent");
        };
        tile.PointerExited += (_, _) =>
        {
            tile.Background = Br("ModCardBg");
            tile.BorderBrush = Br("ModCardStroke");
        };
        tile.PointerPressed += (s, e) => BeginPress(s, e, m.Id, true);
        tile.PointerMoved += OnPointerMoved;
        tile.PointerReleased += OnPointerReleased;
        tile.PointerCaptureLost += (_, _) => CancelDrag();

        return tile;
    }

    // ── 自己画的拖拽：按下 → 跟手 → 让位 → 松手落位 ────────────────────────

    private void BeginPress(object? sender, PointerPressedEventArgs e, string id, bool fromPreview)
    {
        if (InsideButton(e.Source)) return;             // 小按钮的点击自己处理，别抢
        if (sender is not Control src) return;
        if (_swapping) return;                          // ↑/↓ 正在滑动，等它落位

        Log($"按下 {id} 来源={(fromPreview ? "预览" : "模块库")} 行数={_tiles.Count}");

        _dragId = id;
        _dragFromPreview = fromPreview;
        _dragging = false;
        _gapIndex = -1;
        _pressSource = src;
        _pressInRoot = e.GetPosition(WorkspaceRoot);
        _grabOffset = e.GetPosition(src);

        CaptureBaseY();

        // 拖的时候把整页滚动锁掉：不然触摸拖拽会被外层 ScrollViewer 当"滚动"处理，
        // 整页跟着跑 —— 看起来就像模块全没了。
        // Avalonia 12 没有 WinUI 那个 ScrollMode，ScrollBarVisibility.Disabled 就是「不许滚」。
        _pageScrollOffset = PageScroll.Offset.Y;
        _pageScrollBar = PageScroll.VerticalScrollBarVisibility;
        PageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        PageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        e.Pointer.Capture(src);
        e.Handled = true;                               // 别让外层的滚动条抢走
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragId is null) return;

        var inRoot = e.GetPosition(WorkspaceRoot);
        if (!_dragging)
        {
            if (Math.Abs(inRoot.X - _pressInRoot.X) + Math.Abs(inRoot.Y - _pressInRoot.Y) < DragSlop) return;

            _dragging = true;
            if (_pressSource is not null) _pressSource.Opacity = 0.45;   // 原位置变淡，视觉上"被抠起来了"
            ShowGhost();
        }

        e.Handled = true;
        MoveGhost(e.GetPosition(DragLayer));

        var inStrip = e.GetPosition(PreviewStrip);
        var inside = inStrip.X >= 0 && inStrip.Y >= 0
                     && inStrip.X <= PreviewStrip.Bounds.Width && inStrip.Y <= PreviewStrip.Bounds.Height;

        UpdateGap(inside ? IndexFromPointer(e.GetPosition(PreviewPanel).Y) : -1);

        // 从预览里往外拖：提示"松手就移除"
        RemoveHint.IsVisible = !inside && _dragFromPreview;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragId is null) return;

        var id = _dragId;
        var fromPreview = _dragFromPreview;
        var dragged = _dragging;

        var inStrip = e.GetPosition(PreviewStrip);
        var inside = inStrip.X >= 0 && inStrip.Y >= 0
                     && inStrip.X <= PreviewStrip.Bounds.Width && inStrip.Y <= PreviewStrip.Bounds.Height;
        var index = inside ? IndexFromPointer(e.GetPosition(PreviewPanel).Y) : -1;

        e.Pointer.Capture(null);
        EndDrag();

        if (!dragged)
        {
            if (!fromPreview) AddToEnd(id);             // 没拖动 = 点了一下 → 加到末尾
            return;
        }

        if (inside && index >= 0)
        {
            MoveTo(id, index);
        }
        else if (fromPreview)
        {
            _ids.Remove(id);                            // 拖出预览 = 移除
            Save();
            Refresh();
        }
        else
        {
            Refresh();                                  // 从库里拖到空处：当没干（顺手把视觉复原）
        }
    }

    /// <summary>拖到一半被系统打断（比如窗口失焦）：收拾干净，不动数据。</summary>
    private void CancelDrag()
    {
        if (_dragId is null) return;
        EndDrag();
        Refresh();
    }

    private void EndDrag()
    {
        HideGhost();
        RemoveHint.IsVisible = false;
        if (_pressSource is not null) _pressSource.Opacity = 1;

        // 滚动解锁 + 把位置放回去（某些情况下锁滚动会把偏移归零）
        PageScroll.VerticalScrollBarVisibility = _pageScrollBar;
        PageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        PageScroll.Offset = new Vector(0, _pageScrollOffset);

        _dragId = null;
        _dragging = false;
        _pressSource = null;
        _gapIndex = -1;
    }

    private void ShowGhost()
    {
        if (_dragId is null || SidebarModules.Find(_dragId) is not { } m) return;

        var content = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        content.Children.Add(new FASymbolIcon
        {
            Symbol = m.Symbol,
            FontSize = 19,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = m.ShortName,
            FontSize = 11,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = Br("ModText"),
        });

        var ghost = new Border
        {
            Width = 92,
            Height = 56,
            CornerRadius = new CornerRadius(8),
            Background = Br("ModCardBg"),
            BorderBrush = Br("ModAccent"),
            BorderThickness = new Thickness(1.5),
            Opacity = 0.95,
            Child = content,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 20,
                OffsetY = 6,
                Color = Color.FromArgb(60, 0, 0, 0),
            }),
        };

        _ghost = ghost;
        DragLayer.Children.Add(ghost);
    }

    private void MoveGhost(Point inLayer)
    {
        if (_ghost is null) return;
        Canvas.SetLeft(_ghost, inLayer.X - _grabOffset.X);
        Canvas.SetTop(_ghost, inLayer.Y - _grabOffset.Y);
    }

    private void HideGhost()
    {
        if (_ghost is null) return;
        DragLayer.Children.Remove(_ghost);
        _ghost = null;
    }

    /// <summary>算落点：指针落在第几个格子的上半边，就插到它前面（全在下面 = 插到最后）。</summary>
    private int IndexFromPointer(double yInPanel)
    {
        for (var i = 0; i < _tiles.Count && i < _tileBaseY.Count; i++)
        {
            if (yInPanel < _tileBaseY[i]) return i;
        }
        return _tiles.Count;
    }

    /// <summary>拖起来那一刻记下每个格子的原始位置（让位动画会挪它们，落点判断不能受它影响）。</summary>
    private void CaptureBaseY()
    {
        _tileBaseY.Clear();
        foreach (var t in _tiles)
        {
            var p = t.TranslatePoint(new Point(0, t.Bounds.Height / 2), PreviewPanel);
            _tileBaseY.Add(p?.Y ?? 0);
        }
    }

    /// <summary>
    /// 把缝让出来：在 index 之前插一个"空档"，让它自己长到一格高 —— 后面的模块是被**真实布局**顶下去的，
    /// 不是靠 transform 平移（平移那套会飘、会被裁，之前"三个组件下滑消失"就是它）。
    /// index &lt; 0 = 合上缝。
    /// </summary>
    private void UpdateGap(int index)
    {
        if (index == _gapIndex) return;
        _gapIndex = index;
        Log($"缝 -> {index}（当前 {_tiles.Count} 行）");

        if (index < 0)
        {
            CloseSpacer();
            return;
        }

        var spacer = EnsureSpacer();
        if (PreviewPanel.Children.Contains(spacer)) PreviewPanel.Children.Remove(spacer);

        // 插到第 index 个模块前面（index = 个数时插到最后）
        var anchor = index < _tiles.Count ? _tiles[index] : null;
        var pos = anchor is null ? PreviewPanel.Children.Count : PreviewPanel.Children.IndexOf(anchor);
        PreviewPanel.Children.Insert(pos, spacer);

        // 空档 56 + StackPanel 间距 3 = 59，正好一格
        AnimateSpacer(spacer, GapDip - 3);
    }

    private Border EnsureSpacer() =>
        _spacer ??= new Border
        {
            Width = 96,
            Height = 0,
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Background = Br("ModHoverBg"),
            BorderBrush = Br("ModAccent"),
            BorderThickness = new Thickness(1),
        };

    private void CloseSpacer()
    {
        if (_spacer is null) return;
        var spacer = _spacer;
        AnimateSpacer(spacer, 0, () =>
        {
            if (_gapIndex < 0 && spacer.Bounds.Height <= 0.5) PreviewPanel.Children.Remove(spacer);
        });
    }

    /// <summary>空档高度动画（130ms，EaseOut）。</summary>
    private static void AnimateSpacer(Control spacer, double to, Action? done = null)
    {
        var from = spacer.Bounds.Height;
        Tween(from, to, 130, k => spacer.Height = from + (to - from) * k, done);
    }

    /// <summary>指针是不是按在某个按钮上（那些小按钮要自己处理点击，别被拖拽抢走）。</summary>
    private static bool InsideButton(object? src)
    {
        for (Visual? d = src as Visual; d is not null; d = d.GetVisualParent())
        {
            if (d is Button) return true;
        }
        return false;
    }

    /// <summary>拖拽过程的流水账（出问题看 &lt;数据目录&gt;/layout.log）。</summary>
    private void Log(string text)
    {
        try
        {
            var dir = AppPaths.EnsureDir();
            if (dir.Length == 0) return;
            File.AppendAllText(Path.Combine(dir, "layout.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {text}{Environment.NewLine}");
        }
        catch
        {
            // 日志写不进去就算了
        }
    }

    private void MoveTo(string id, int index)
    {
        var from = _ids.IndexOf(id);
        if (from >= 0)
        {
            if (from < index) index--;                  // 先摘后插，位置要减一
            _ids.RemoveAt(from);
        }

        index = Math.Clamp(index, 0, _ids.Count);
        _ids.Insert(index, id);
        Save();
        Refresh();
    }

    // ── 格子上的小操作 ───────────────────────────────────────────────────

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        if (_ids.Remove(id)) { Save(); Refresh(); }
    }

    private void MoveUp_Click(object? sender, RoutedEventArgs e) => Move(sender, -1);

    private void MoveDown_Click(object? sender, RoutedEventArgs e) => Move(sender, +1);

    private void Move(object? sender, int delta)
    {
        if (sender is not Button { Tag: string id }) return;
        var i = _ids.IndexOf(id);
        if (i < 0) return;
        var j = i + delta;
        if (j < 0 || j >= _ids.Count) return;

        AnimateSwap(i, j);
    }

    /// <summary>
    /// ↑ / ↓ 点一下：两行**滑过去**再落位，不是瞬间跳（瞬跳太不显眼了）。
    /// 做法：给这两行套上 TranslateTransform，一行 +59、另一行 −59 同时跑完；
    /// 动画结束后把 transform 归零再重建列表 —— 重建后的静态位置跟动画终点重合，看不出接缝。
    /// 动画期间不接受新的点击（180ms，很短）。
    /// </summary>
    private void AnimateSwap(int i, int j)
    {
        if (_swapping) return;
        if (i == j || i < 0 || j < 0 || i >= _tiles.Count || j >= _tiles.Count) return;

        var a = _tiles[i];
        var b = _tiles[j];
        var dy = (j - i) * GapDip;      // 相邻两行 = 59

        var ta = Shift(a);
        var tb = Shift(b);
        ta.Y = 0;
        tb.Y = 0;
        _swapping = true;

        Tween(0, dy, 180, k => { ta.Y = dy * k; tb.Y = -dy * k; }, () =>
        {
            _swapping = false;
            ta.Y = 0;                   // 先归零，免得重建那一刻闪一下
            tb.Y = 0;
            (_ids[i], _ids[j]) = (_ids[j], _ids[i]);
            Save();
            Refresh();
        });
    }

    /// <summary>拿这一行的位移动画对象（没有就配一个）。</summary>
    private static TranslateTransform Shift(Control el)
    {
        if (el.RenderTransform is TranslateTransform t) return t;
        t = new TranslateTransform();
        el.RenderTransform = t;
        return t;
    }

    private void ResetDefault_Click(object? sender, RoutedEventArgs e)
    {
        _ids.Clear();
        _ids.AddRange(SidebarModules.DefaultIds);
        Save();
        Refresh();
    }

    private void ToggleSidebar_Click(object? sender, RoutedEventArgs e)
    {
        // 跨平台版：只落盘（常驻侧边浮窗还没搬过来），先把用户的意图留住
        App.Settings.Current.SidebarEnabled = !App.Settings.Current.SidebarEnabled;
        App.Settings.Save();
        Refresh();
        _ = UiKit.ShowInfoAsync("侧边栏",
            App.Settings.Current.SidebarEnabled
                ? "已经记下「显示侧边栏」。\n\n跨平台版暂时还没有挂在屏幕边上的常驻侧边浮窗，" +
                  "这一页排好的模块清单会先存在设置里（SidebarModuleIds / SidebarEnabled），" +
                  "侧边栏搬过来之后直接读它就能生效。"
                : "已经记下「隐藏侧边栏」。\n\n真正的侧边浮窗还没进跨平台版（见上面那段说明），" +
                  "所以这个开关现在只影响设置里存的值。");
    }

    // ── 一点点动画：130~180ms 的 EaseOut 补间 ─────────────────────────────
    //  WinUI 那边是 DoubleAnimation + Storyboard（动 Height 这种"依赖动画"还得开 EnableDependentAnimation，
    //  忘了就整段被忽略）。Avalonia 这边没有 Storyboard，这里用 DispatcherTimer 走一条 cubic ease-out。

    private static void Tween(double from, double to, int ms, Action<double> step, Action? done = null)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var start = DateTime.UtcNow;
        timer.Tick += (_, _) =>
        {
            var e = (DateTime.UtcNow - start).TotalMilliseconds / ms;
            if (e >= 1)
            {
                timer.Stop();
                step(1);
                done?.Invoke();
                return;
            }
            step(1 - Math.Pow(1 - e, 3));    // CubicEase, EaseOut
        };
        timer.Start();
    }
}
