using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassSoftwareHubCrossPlatformEdition.Data;
using ClassSoftwareHubCrossPlatformEdition.Services;

namespace ClassSoftwareHubCrossPlatformEdition.Pages;

/// <summary>软件下载页：搜索 + 分类筛选 + 软件卡片墙（全原生，不加载任何网页）。</summary>
public sealed partial class SoftwarePage : UserControl
{
    private string _category = "";
    private string _keyword = "";
    private string _view = "tile";          // tile | grid
    private bool _loading;
    private readonly List<ToggleButton> _chips = new();

    public SoftwarePage()
    {
        InitializeComponent();
    }

    /// <summary>FAFrame 带着参数进来时（ShellPage 的 <c>cat:xxx</c> / 空串）取分类。</summary>
    public void SetCategory(string? key) => _category = key ?? "";

    /// <summary>按当前分类/关键词重新铺一遍（拿到导航参数、内容包更新后都要调）。</summary>
    public void Reload()
    {
        BuildChips();
        Apply();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _view = App.Settings.Current.AppCardView == "grid" ? "grid" : "tile";
        _loading = true;
        ViewTile.IsChecked = _view != "grid";
        ViewGrid.IsChecked = _view == "grid";
        _loading = false;
        UpdateView();

        Reload();
    }

    /// <summary>磁贴（3 列，带简介）/ 网格（5 列，紧凑）切换，选择会记进设置。</summary>
    private void ViewChoice_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var grid = sender as RadioButton == ViewGrid;
        _view = grid ? "grid" : "tile";
        App.Settings.Current.AppCardView = _view;
        App.Settings.Save();
        UpdateView();
    }

    private void UpdateView()
    {
        var tile = _view != "grid";
        TileGrid.IsVisible = tile;
        CompactGrid.IsVisible = !tile;
    }

    private void BuildChips()
    {
        Chips.Children.Clear();
        _chips.Clear();

        AddChip("全部", "");
        foreach (var c in App.Content.Categories)
            AddChip(c.Name, c.Key);
    }

    private void AddChip(string text, string key)
    {
        var chip = new ToggleButton
        {
            Content = key.Length == 0 ? $"{text} {App.Content.Apps.Count}" : $"{text} {App.Content.CountInCategory(key)}",
            Tag = key,
            IsChecked = key == _category,
        };
        chip.Click += (s, _) =>
        {
            if (s is not ToggleButton tb || tb.Tag is not string key) return;
            _category = key;
            foreach (var other in _chips)
                if (!ReferenceEquals(other, s)) other.IsChecked = false;
            tb.IsChecked = true;
            Apply();
        };
        _chips.Add(chip);
        Chips.Children.Add(chip);
    }

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _keyword = SearchBox.Text ?? "";
        Apply();
    }

    private void Apply()
    {
        var list = App.Content.Apps
            .Where(a => _category.Length == 0 || a.Category == _category)
            .Where(a => a.Matches(_keyword))
            .ToList();

        TileGrid.ItemsSource = list;
        CompactGrid.ItemsSource = list;

        TitleText.Text = _category.Length == 0
            ? (_keyword.Length == 0 ? "软件下载" : $"搜索：{_keyword}")
            : App.Content.CategoryName(_category);

        UpdateEmptyState(list.Count);
    }

    /// <summary>
    /// 空清单时别只写一句「没有匹配的软件」——要分清两种情况：
    /// ① 搜索/分类筛掉了 ② 整体就没内容（内容包没同步下来，教学机断网最容易踩）
    /// </summary>
    private void UpdateEmptyState(int shown)
    {
        if (shown > 0)
        {
            EmptyPanel.IsVisible = false;
            return;
        }

        EmptyPanel.IsVisible = true;

        var hasFilter = _keyword.Length > 0 || _category.Length > 0;
        if (hasFilter)
        {
            EmptyTitle.Text = "没找到匹配的软件";
            EmptyText.Text = "换个关键词或点「全部」试试。";
            EmptyText.IsVisible = true;
            EmptyDetail.Text = $"当前内容来源：{App.Content.SourceLabel}（共 {App.Content.Apps.Count} 个软件）";
            EmptyRetry.IsVisible = false;
            return;
        }

        EmptyTitle.Text = "软件清单还是空的";
        EmptyText.Text = "清单来自「内容包」：安装包里自带一份，联网后会自动从站点更新。" +
                         "如果这里一直是空的，多半是内容包没同步下来（没网 / 站点还没发布内容包）。";
        EmptyText.IsVisible = true;
        EmptyDetail.Text = $"当前内容来源：{App.Content.SourceLabel}" +
                           (App.Content.Issues.Count > 0 ? $"\n读取问题：{App.Content.Issues[0].Message}" : "");
        EmptyRetry.IsVisible = true;
        EmptyRetry.IsEnabled = true;
        EmptyRetry.Content = "重新同步内容包";
    }

    /// <summary>空状态里的「重新同步内容包」：拉一次远端内容包再重读（失败就照实说）。</summary>
    private async void EmptyRetry_Click(object? sender, RoutedEventArgs e)
    {
        EmptyRetry.IsEnabled = false;
        EmptyRetry.Content = "正在同步…";
        EmptyTitle.Text = "正在同步内容包";
        EmptyText.Text = "从站点拉最新清单，稍等一下。";
        EmptyDetail.Text = "";

        try
        {
            var result = await ContentUpdater.SyncAsync(
                new Progress<string>(text => EmptyDetail.Text = text));

            if (result.Updated) App.Content.Load();

            BuildChips();
            Apply();

            if (App.Content.Apps.Count == 0)
            {
                EmptyTitle.Text = "还是没拉到内容";
                EmptyText.Text = "站点那边可能没有网络 / 内容包还没发布。装好的这份里自带的内容会在没网时兜底；" +
                                 "实在不行把这一页截图给维护的同学。";
                EmptyDetail.Text = result.Message;
            }
        }
        catch (Exception ex)
        {
            EmptyTitle.Text = "同步失败了";
            EmptyText.Text = "网络不通或者站点暂时不可用，等会儿再试。";
            EmptyDetail.Text = ex.Message;
        }
        finally
        {
            EmptyRetry.IsEnabled = true;
            EmptyRetry.Content = "重新同步内容包";
        }
    }

    /// <summary>点卡片 → 详情页（ItemsControl 没有 ItemClick，卡片 Tag 是软件 id）。</summary>
    private void AppGrid_Tapped(object? sender, TappedEventArgs e)
    {
        var id = UiKit.FindTag(e);
        if (string.IsNullOrEmpty(id)) return;

        var shell = App.MainWindow?.Shell;
        if (shell is null) return;

        // 复用外壳的 Frame：SoftwarePage → DetailPage，返回键能正常退回来
        shell.NavigateToDetail(typeof(DetailPage), id!);
    }

    // 卡片自己的悬停反馈（容器 chrome 已由 App.axaml 里的 ItemsControl.cshcard 规则关掉）
    private void Card_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border b) b.Opacity = 0.88;
    }

    private void Card_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border b) b.Opacity = 1.0;
    }

    // 占位字形（购物袋）的显隐由 SoftwareApp.IconPlaceholder 驱动（见 Data/SoftwareApp.cs）：
    // 没图 / 加载中 / 加载失败都显示，加载成功才收掉。用属性 + INotifyPropertyChanged 而不是
    // Image 元素事件，是因为 WrapPanel 回收容器时属性会重新求值，不会被上一次的状态卡住。
}
