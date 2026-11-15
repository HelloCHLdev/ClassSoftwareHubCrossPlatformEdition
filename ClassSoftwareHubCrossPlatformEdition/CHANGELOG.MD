# 更新日志

<!--
这份文件的内容会作为 GitHub Release 的发布说明 —— 每次构建完成后自动取用
（见 .github/workflows/build.yml 的「准备发布说明」步骤）。
新版本往下面加一节即可，标题里写上版本号。

下面这段说明写成注释，是为了让发布页上直接从版本条目开始，不露出这些内部约定。
-->

## v1.0.0

修正更新逻辑。

- 界面从 WinUI 3 移植到 Avalonia 12 + FluentAvalonia 3，一套代码跑 Windows / macOS / Linux
- 三平台构建与自动发版流水线（GitHub Actions）：每次推送出一个 Release，附 MD5 校验和
- 更新器改为订阅本仓库的 Releases，稳定通道 / Insider 通道照旧
- 「关于」页补上项目说明与「去 GitHub 提 issue」入口

### 已知问题

- 本版本仍处于测试状态，可能并不稳定；遇到问题请到
  [Issues](https://github.com/HelloCHLdev/ClassSoftwareHubCrossPlatformEdition/issues) 理性反馈
- macOS 包没有签名和公证，第一次打开会被 Gatekeeper 拦（解决办法见下方说明）
