# 项目长期记忆

## 项目

ClassSoftwareHub 跨平台桌面版：WinUI 3 → Avalonia 12.0.2 + FluentAvalonia 3.0.2 + .NET 10。
`Core/ShellConfig.cs` 里 `AppName = "ClassSoftwareHub"`、`ShellVersion = "1.0.0"`、
`VersionPrefix = "dv"`（版本显示形如 `dv1.0.0`）。

## 应用内更新器的资产命名约定（发版必须遵守）

`Services/Updating/GitHubReleaseSource.cs` 从 GitHub Releases API 拉资产，按下面规则挑：

- 只认后缀 `.exe` / `.msi` / `.zip` 的资产；
- 名字里含 `setup` / `installer` / `-install` 的**优先**当安装包（多个时取最大的）；
- 校验文件：含 `checksum` 或 `md5` 的资产，或 `<安装包名>.md5` 同名 sidecar。
  `ResolveMd5Async` 会逐行找含资产名的那行，取第一个 32 位 hex → 所以用
  `md5sum -b` 的 `hash *文件名` 格式最稳。

→ 发版命名：`ClassSoftwareHub-Setup-dv<ver>-win-x64.exe` /
`ClassSoftwareHub-dv<ver>-macos-arm64.zip` / `classsoftwarehub_<ver>_amd64.deb` +
`checksums.md5`。**只有 Windows 的包能带 setup 字样**，否则会被 macOS zip 抢走。

已知缺口：`PickInstallers` 没按平台过滤，macOS/Linux 用户会下到 Windows 的 exe。

## 写 bash 片段的硬规则（本项目注释全中文）

- `$VAR` 后面紧跟**中文全角标点**会被 bash 吞进变量名（`$VER）。` → `VER?: unbound variable`）。
  一律写 `${VAR}`。
- YAML block scalar 里的 heredoc 终止符必须与命令行同缩进，否则静默吞掉文件剩余部分、退出码仍是 0。
- macOS `.app` 打包用 `ditto -c -k --norsrc --keepParent`，否则 zip 里全是 `__MACOSX/._xxx`。
- .NET 单文件应用**不要 codesign**：bundle 追加在 Mach-O 尾部，签名会重写文件末尾、
  把 bundle 偏移量写坏。用 `xattr -dr com.apple.quarantine` 绕 Gatekeeper。

## CI

`.github/workflows/build.yml`（可复用）+ `release.yml`（tag 发版）。
构建参数：单文件自包含、`PublishTrimmed=false`（反射式 JsonSerializer 不能裁剪）。
