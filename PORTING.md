# ClassSoftwareHub 跨平台移植规范（WinUI 3 → Avalonia 12 + FluentAvalonia 3.0.2）

本文件是**移植工作的唯一规范**。任何子任务都要严格遵守。

> ⚠️ 这是临时工作文档，移植完成后会删除，**不要**把它当项目文档提交。

---

## 0. 路径与命名空间

| | 值 |
|---|---|
| 源（WinUI，只读） | `/Users/lichenghao/Documents/yz/ClassSoftwareHub-Desktop` |
| 目标（Avalonia，要改） | `/Users/lichenghao/RiderProjects/ClassSoftwareHubCrossPlatformEdition/ClassSoftwareHubCrossPlatformEdition` |
| 源命名空间 | `ClassSoftwareHub.Desktop` |
| 目标命名空间 | `ClassSoftwareHubCrossPlatformEdition` |

**目录结构保持一致**：源的 `Core/X.cs` → 目标 `Core/X.cs`；`Pages/X.axaml.cs` → 目标 `Pages/X.axaml.cs`（注意 WinUI 里同一页面是 `X.xaml` + `X.xaml.cs`，目标是 `X.axaml` + `X.axaml.cs`）。

## 1. 硬性约束

1. **不新增 NuGet 依赖**。只能用项目已有的：Avalonia 12.0.2、FluentAvaloniaUI 3.0.2、`System.Text.Json`、BCL。
2. **`ImplicitUsings` 未启用** —— 每个文件都要显式 `using System;` / `using System.IO;` / `using System.Linq;` / `using System.Collections.Generic;` / `using System.Threading;` / `using System.Threading.Tasks;`。
3. **文件命名陷阱**：WinUI 的 `DetailPage.xaml.cs` 在目标里叫 `DetailPage.axaml.cs`。新增页面时**两个文件都要建**：`X.axaml` 和 `X.axaml.cs`。
4. **跨平台策略（用户定案）**：
   - Windows 专有功能（注册表、winget/AppX、`%APPDATA%` 清理、WMI、`ms-windows-store://`、Windows 托盘、虚拟键盘、亮度调节、残留清理、已安装应用枚举、AppData 扫描、希沃/百应守护）→ **在 macOS/Linux 上禁用**：UI 上显示为不可用/隐藏，代码里用 `OperatingSystem.IsWindows()` 闸住，**不要**造假的跨平台替代实现。
   - 能用纯 .NET / HTTP / 文件系统实现的 → **正常移植**。
5. **不要动**这些已完成且验证过的东西的既有行为：字体注册（`Program.cs` 的 `ConfigureFonts(AddAppFonts)`）、`App.axaml` 的主题字典、`Pages/UiKit.cs` 的公共 API。
6. 移植完**必须**保证 `dotnet build` 0 error / 0 warning（正常 warning 也别引进新的）。

## 2. 类型映射表

| WinUI / UWP | Avalonia / FluentAvalonia |
|---|---|
| `Microsoft.UI.Xaml.*` | `Avalonia.Controls.*` / `Avalonia.*` |
| `Visibility.Visible/Collapsed` | `IsVisible`（bool） |
| `Visibility="{x:Bind ...}"`（bool→Visibility） | 用 `IsVisible="{Binding ...}"`（bool） |
| `Microsoft.UI.Xaml.Controls.FontIcon` | `FluentAvalonia.UI.Controls.FASymbolIcon`（用 `Symbol` 属性，见 §3） |
| `SymbolIcon`（MDL2 码点） | `FASymbolIcon` + `FASymbol` 枚举 |
| `NavigationView` | `FANavigationView` |
| `NavigationViewItem` | `FANavigationViewItem` |
| `Frame` | `FAFrame` |
| `InfoBar` | `FAInfoBar` |
| `ContentDialog` | `FAContentDialog` |
| `SettingsExpander` | `FASettingsExpander` |
| `ProgressRing` | `FAProgressRing` |
| `GridView` | `ItemsControl` + `WrapPanel`（见 §4） |
| `ListView` | `ListBox` |
| `ScrollViewer` | `ScrollViewer` |
| `ThemeResource Key` | `{DynamicResource Key}`（键定义在 `App.axaml` 的 `ThemeDictionaries`） |
| `StaticResource Key`（颜色/画刷） | `{DynamicResource Key}` |
| `AccentButtonStyle` | 加 `Classes="accent"` |
| `Style="{StaticResource X}"` | `Classes="x"` 或 `Classes` 绑定 |
| `x:Bind` | `Binding`（`{Binding Path}`），或代码里直接赋值 |
| `ToolTipService.SetToolTip(x, y)` | `ToolTip.SetTip(x, y)` |
| `DispatcherQueue.TryEnqueue` | `Dispatcher.UIThread.Post` / `InvokeAsync` |
| `DispatcherQueueTimer` | `DispatcherTimer`（Avalonia.Threading） |
| `CompositionTarget.Rendering` | `TopLevel.RequestAnimationFrame` 或 `DispatcherTimer` |
| `ActualTheme` / `ElementTheme` | `ActualThemeVariant` / `ThemeVariant` |
| `AppWindow` / `SetTitleBar` | Avalonia 原生标题栏（本项目**不用**自绘标题栏） |
| `Window.AppWindow.Resize` | `Window.Width/Height`（DIP） |
| `WindowId` / `HWND` | `Window` 实例 |
| `StorageFile` / 文件选择器 | `TopLevel.StorageProvider`（`CanSave` / `CanOpen` / `SaveFilePickerAsync` / `OpenFolderPickerAsync` / `IStorageFolder.TryGetLocalPath()`） |
| `Application.Current.Windows` | `((IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime).Windows` |
| `Clipboard.SetContent` | `TopLevel.Clipboard` |
| `Launcher.LaunchUriAsync` | `Services.SystemIntegration.Open(url)` |
| `DataTemplateSelector` | **不存在** —— 合并成一个 `DataTemplate` + `Classes` 条件样式 |
| `IContainer` | **不存在** —— 同上 |
| `ItemContainerStyle` | **没有等价物** —— 用全局 `Style` 选择器去容器化 |

### 事件 / 属性差异（Avalonia 12 实测）

- `RadioButton.Checked` → **`IsCheckedChanged`**
- `ToggleSwitch.Toggled` → **`IsCheckedChanged`**
- 处理器签名保持 `(object? sender, RoutedEventArgs e)`（XAML 编译器会校验）
- `Expander.Expanding` → `EventHandler<CancelRoutedEventArgs>`（**不是** `ExpanderExpandingEventArgs`）
- `ImageBrush.ImageSource` → `ImageBrush.Source`（需要显式把 `IImage` 转过去）
- `TextBox.Watermark` → **`PlaceholderText`**；`TextBox` **没有** `Header` / `Description`
- `Grid` / `StackPanel` **没有** `Padding` 属性 —— WinUI 里的 `Padding` 要就地改成 `Margin`（语义等价；转换前确认该元素**没有** `Background`/`BorderBrush`）
- `ScrollViewer` **有** `Padding`（继承自 ContentControl），保持 `Padding`
- `ScrollViewer` 有 `Offset` / `ScrollBarVisibility` / `IsScrollChainingEnabled`，**没有** `IsScrollEnabled`
- `RoutedEventHandler` 类型**不存在**，用 `EventHandler<RoutedEventArgs>`
- `ToggleSwitch` 有 `OnContent` / `OffContent`
- `FAInfoBar` 有 `IsOpen` / `Title` / `Message` / `Severity` / `IsClosable`
- `FAContentDialog` 结果枚举是 `None | Primary | Secondary`（**没有** `Close`）
- `FontFamily` 是**继承属性**；`Control` **没有** `FontFamily`（只有 `Window` / `UserControl` / `TemplatedControl` 有）

### XAML 结构陷阱

- **`Style` 不能放在 `<UserControl.Resources>`**（AVLN3000：`StyledElement` 没有 `Style` 的 adder）—— 放进 `<UserControl.Styles>`。`DataTemplate` 仍在 `Resources`。
- **带命名空间的类型做样式选择器要用 `|` 形式**：`Selector="Border.qllink.accent ui|FASymbolIcon"`（写成 `ui:FASymbolIcon` 解析不了）。
- `FASymbolIconSource` 用 `Symbol` 属性：`new FASymbolIconSource { Symbol = FASymbol.Home }`；XAML 里 `<ui:FASymbolIcon Symbol="{Binding Symbol}" />`。

## 3. 图标：MDL2 码点 → FASymbol

数据模型（`SidebarModule` / `ToolDef` / `QuickLink`）直接持有 `FASymbol` 值，属性名 `Symbol`。**不要**做码点查表。

| 语义 | MDL2 | FASymbol |
|---|---|---|
| 首页 | E80F | `Home` |
| 下载 | E896 | `Download` |
| 工具 | E71D | `AllApps` |
| 侧边栏 | E7AB | `DockRight` |
| 提交 | E724 | `Send` |
| 设置 | E713 | `Settings` |
| 网页 | E774 | `Globe` |
| 右箭头 | E76C | `ChevronRight` |
| 代码 | E943 | `Code` |
| 联系人 | E77B | `Contact` |
| 星标 | E734 | `Star` |
| 消息 | E8BD | `Message` |
| 文档 | E8A5 | `Document` |
| 商店 | E719 | `Shop` |
| 信息 | E946 | `Help` |
| 返回 | E72B | `Back` |
| 关闭 | E711 | `Dismiss` |
| 太阳 | E706 | `WeatherSunny` |
| 月亮 | E708 | `WeatherMoon` |
| 颜色 | E790 | `ColorFill` |
| 抽号 | E8D6 | `Shuffle` |
| 计时器 | E916 | `Clock` |
| 秒表 | E916 | `Stop` |
| 全屏时钟 | E740 | `FullScreen` |
| 新增 | E710 | `Add` |
| 删除 | E74D | `Delete` |
| 搜索/放大 | E8A3 | `Zoom` |
| 复制 | E8C8 | `Copy` |
| 勾选 | E73E | `Checkmark` |
| 上折 | E70E | `ChevronUp` |
| 下折 | E70D | `ChevronDown` |
| 触摸 | E7C9 | `ZoomIn` |

**⛔ `FASymbol` 里没有字形（用了会编译报错或显示空白）的成员，永远别用：**
`OpenPane, ClosePane, Trim, VideoChat, MusicInfo, Caption, ShowBcc, HideBcc, DisconnectDrive, OpenWith, ContactPresence, Priority, Placeholder, SetLockScreen, SetTile, DisableUpdates, LikeDislike, Street, ReShare, PostUpdate, UnSyncFolder, SwitchApps, TouchPointer, GoToStart`

**⛔ `FASymbol` 里完全不存在：** `Gantt, Chart, Robot, Info, Language, Bug, Store, Search, Grid`
（对应语义改用 `Document` / `Help` / `Shop` / `Zoom` / `AllApps` 等。）

## 4. 现有架构（必须遵守）

### 页面布局约定
- 内容页是 3 列 Grid：`1* / 998*（MaxWidth=960） / 1*`，内边距用 `Margin`：`24,36,24,44`（DetailPage 用 `24,20,24,44`；SubmitPage 用 `24,14,24,20`）。
- 页面是 `UserControl`；导航走 `FAFrame.Navigate(type, param)`。
- **`FAFrame` 不会把参数递给页面** —— 由 `ShellPage.OnFrameNavigated` 手动转交（`SoftwarePage.SetCategory(...)` + `Reload()`、`DetailPage.Load(id)`）。新增带参数的页面要在这里补一行。
- 卡片网格：`ItemsControl` + `WrapPanel`，`Classes="cshcard"`；全局规则 `ItemsControl.cshcard > ContentPresenter` 去掉容器外框。`ItemsControl` **没有** `ItemClick` —— 卡片上挂字符串 `Tag`，页面用 `Tapped` 处理器 + `UiKit.FindTag` 沿**视觉**父链找。
- 快捷链接模板：合并进一个 `DataTemplate`，`Classes="qllink"` + `Classes.accent="{Binding Accent}"`，配 `Style` 规则。

### 工具方法（已存在，直接用）
`Pages/UiKit.cs`：
- `ShowInfoAsync(...)` / `ShowAskAsync(...)` / `ShowThreeWayAsync(...)` / `ShowAsync(...)`
- `FindTag(...)`（沿视觉树找带 Tag 的祖先）
- `OpenExternal(...)`
- `BuildInfoRows(...)`

`Services/SettingsStore.cs`：`App.Settings.Current` 是 `AppSettings`；`SettingsStore.Dir` 是数据目录。

`Services/SystemIntegration.cs`：`Open / Reveal / CopyText / SetAutoStart / SetAlwaysOnTop / Show / TryClaimSingleInstance`。

`MainWindow.axaml.cs` 门面：`SetTheme / SetBackdrop / SetAlwaysOnTop / SetAutoStart / SetCloseToTray / SetPaletteOnTop / OpenExternal / OpenStore / OpenFile / RevealFile / ShowWebSheet / DownloadFile / CheckUpdateManualAsync / SyncContentAsync / Shell / ExitApp`。

`Services/Updating/`：`UpdateService.CreateDefault()`（`.Source.IsConfigured` / `.DisplayName`）、`CheckAsync(channel, currentVersion, ct)`、`GetHistoryAsync(channel, max, ct)`；`UpdateRelease(Tag, Version, Channel, Prerelease, Notes, PublishedAt?, Packages)` 带 `.Primary`；`UpdateChannels.Parse/ToDisplay`；`UpdateFlow.AskAsync(TopLevel?, UpdateRelease)→Task<bool>`、`UpdateFlow.Run(UpdateRelease)→bool`。

`Services/ContentUpdater.SyncAsync(IProgress<string>?, CancellationToken)` → `ContentSyncResult(Updated, Downloaded, Total, Error, BaseUrl, Note)` 带 `.Message`。

### 日志体系（本次新增，见下）
- `Core/AppLog.cs`：`AppLog.Info/Warning/Error/Debug(module, message)`、`AppLog.Exception(module, ex, what)`、`Begin/Step/Done/Fail/Warn`、`Secs/Size`。
- `Core/PerfLog.cs`：`Mark / NavBegin / NavEnd / Scope`（用 Avalonia 的帧回调实现）。
- `Services/LogHome.cs`：会话化日志目录。
- `Services/CacheHome.cs`：版本化缓存目录。

### 数据目录
`Core/ShellConfig.cs` 的 `AppPaths.DataDir` 支持环境变量 **`CSH_DATA_DIR`** 覆盖（测试/沙箱用，务必利用）。

## 5. 编译与验证

```sh
cd /Users/lichenghao/RiderProjects/ClassSoftwareHubCrossPlatformEdition/ClassSoftwareHubCrossPlatformEdition
dotnet build -v q --nologo
```
必须 0 error / 0 warning。

**不要**在这台机器上做会写真实用户数据的冒烟测试 —— 要用 `CSH_DATA_DIR=$(mktemp -d)` 沙箱。真实用户数据在 `~/Library/Application Support/ClassSoftwareHub/`。

**不要**用 `screencapture`（本机权限不允许）。

## 6. 交付方式

- 全部改完**一次性** commit（用户要求）。
- 中途保持工作树可编译。
