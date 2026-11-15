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

## 更新订阅源已换成跨平台版自己的仓库

`Core/ShellConfig.cs` 的 `UpdateRepoOwner = "HelloCHLdev"` /
`UpdateRepoName = "ClassSoftwareHubCrossPlatformEdition"`（原来是 `c1201y/ClassSoftwareHub-Desktop`）。
全工程只有这两个常量管更新源，`QuickLinks` 的「项目仓库 / 更新日志」快捷入口也由它拼。

**注意别搞混三类仓库**：
- 更新源 = `HelloCHLdev/ClassSoftwareHubCrossPlatformEdition`（CI 在这里发 Release）；
- 内容/软件数据源 = `c1201y/ClassSoftwareHub`（`SiteRepoOwner`，`GithubContentSync` 读它）；
- 提交软件落点 = `c1201y/ClassSoftwareHub` 的 `submissions/` 目录（`SubmitPage.RepoNewFileUrl`）。

**已知落差**：CI 快照版本号是 `0.0.0-ci.<sha>`，比当前的 `dv1.0.0` 低 →
Insider 通道虽然能拉到 CI 包，但版本比较判定「不是更新」，不会提示。
要让 Insider 真能收到 CI 包，CI 版本号得抬到 `1.0.1-ci.<sha>` 这种高于当前版本的号。

## 更新器的版本比较（决定了 Release 怎么打 tag）

`Services/Updating/UpdateModels.cs` 的 `VersionCompare` 把 Release 的 tag 当版本号比大小
（剥掉 `v` / `dv` 前缀，数字段按数字比，预发布后缀按字符串比）。
→ **tag 必须是真实版本号**，不能用 `latest` 这种滚动 tag，否则用户会收到假更新。
→ CI 快照用 `v0.0.0-ci.<sha7>`（低于正式版），正式版用 `v1.0.0` 这种。

`GitHubReleaseSource` 按 `prerelease` 字段分流：Stable 通道跳过预发布，
Insider 通道预发布+正式版都收 → CI 快照标成 prerelease 不会污染正式通道。

## 写 bash 片段的硬规则（本项目注释全中文）

- `$VAR` 后面紧跟**中文全角标点**会被 bash 吞进变量名（`$VER）。` → `VER?: unbound variable`）。
  一律写 `${VAR}`。
- **bash 3.2（macOS 自带）和 5.x（Ubuntu runner）行为不同**，本地测不出来：
  `${VER//-/~}` 在 5.x 上会把 `~` 展开成 `$HOME`。→ 用 `tr` 做字符替换。
  `${VER//-/\~}` 也不行（3.2 下会留字面反斜杠）。
- YAML block scalar 里的 heredoc 终止符必须与命令行同缩进，否则静默吞掉文件剩余部分、退出码仍是 0。
- macOS `.app` 打包用 `ditto -c -k --norsrc --keepParent`，否则 zip 里全是 `__MACOSX/._xxx`。
- .NET 单文件应用**不要 codesign**：bundle 追加在 Mach-O 尾部，签名会重写文件末尾、
  把 bundle 偏移量写坏。用 `xattr -dr com.apple.quarantine` 绕 Gatekeeper。

## CI

`.github/workflows/build.yml` 一个文件搞定构建 + 发版（`release.yml` 已删）。
5 目标矩阵：windows-2022 / macos-15 / macos-15-intel / ubuntu-24.04 / ubuntu-24.04-arm。
构建参数：单文件自包含、`PublishTrimmed=false`（反射式 JsonSerializer 不能裁剪）。

发布规则：push 任意分支 → 发 `v0.0.0-ci.<sha7>` 预发布 Release；push `v*` tag → 发正式版；
PR 不发；手动 dispatch 可关。发布说明取仓库的 `CHANGELOG.md`（`.md`/`.MD` 都找，
Linux 大小写敏感），没有就自动生成摘要。

- `push` 要同时写 `branches` 和 `tags`，只写 branches 的话推 tag 不触发。
- **GitHub 上传 Release 资产会把文件名里的 `~` 换成 `.`** → 上传前自己先换掉，
  否则 checksums.md5 和资产名对不上，`md5sum -c` 会失败。
- 测打包脚本要在**全新空目录**里跑，否则会被残留的目录掩盖问题。
- `concurrency.cancel-in-progress: false`：每次推送都发 Release，中途取消会留下
  资产不全的 Release。

## 读 CI 日志（本机没装 gh CLI）

```bash
TOKEN=$(printf "protocol=https\nhost=github.com\n\n" | git credential fill | sed -n 's/^password=//p')
curl -sL -H "Authorization: token $TOKEN" \
  -o job.log "https://api.github.com/repos/<owner>/<repo>/actions/jobs/<job_id>/logs"
```

`actions/jobs/{id}/logs` 匿名访问 403，带 token 才行。`check-runs/{id}/annotations`
只能拿到 "Process completed with exit code N"，真报错要看 job 日志。
上传 Release 资产要走 release 对象里的 `upload_url`（uploads.github.com），
不是 api.github.com。
