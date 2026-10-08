# 从链接安装 Codex Quota Pills

给 Windows 本机 Codex 发这一句话即可表达完整意图：

> 帮我安装并启动 https://github.com/jinfu0111-gif/CodexQuotaPills ，按仓库 INSTALL.md 执行，确认额度连接和胶囊显示是否成功。

只发送链接没有说明用途，Agent 可能询问是安装、审查还是开发。仓库文档不能替用户授权执行程序；明确说“帮我安装”后，不需要再逐项确认普通下载、用户目录安装和只读诊断。

## Agent 安装路线

1. 读取 README.md、本文和 install.ps1，确认 Windows 10/11、.NET Framework 4.7.2+ 和 Windows Codex。普通安装优先使用正式 Release 便携包，不编译源码、不运行开发测试。
2. 下载并保存仓库根目录 `install.ps1` 与 `install-release.json` 到同一个可写目录。先阅读脚本，再运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1`。ExecutionPolicy 只作用于本次进程；不要更改系统策略，不使用下载后直接执行的管道。
3. 脚本先查正式 Release，校验指定 ZIP 的 GitHub SHA-256、大小、安全路径及程序版本。匿名 API 不可用时使用 manifest 固定的正式版本，并在结果中明确标记，不能声称确认了最新版。Agent 也可通过连接的 GitHub 工具读取正式 Release 元数据；不得为安装索取或复制用户凭据。
4. 默认安装到 `%LOCALAPPDATA%\Programs\Codex Quota Pills\versions\…`，创建开始菜单入口。同版本同摘要复用已有安装；保留旧版本、显示设置及共享续跑记录，拒绝降级。开机启动仅在用户要求时加 `-Startup`；不额外创建永久计划任务。
5. 新安装的续跑记录先设为暂停，安装请求不包含向其他聊天发送消息的授权。已有记录的开关、取消、计数及身份摘要保持不变。启动已有开启续跑的安装前，Agent 应核对当前会话是否已授权这一行为；未授权时使用 `-NoLaunch` 安装并说明原有设置，让用户自行启动或明确授权。不要清空或重建账本。
6. 正常无参数启动 EXE，复用它自带的临时交互桌面启动与监听机制。不要使用 `--standalone` 绕过启动器，不修改或重启 Codex 本体。
7. 按结果分开报告 `Installed`、`ConnectionReady`、`Running`、`Visible`。进程存在或 `--snapshot` 成功均不代表胶囊实际可见。`Visible=false` 时说明显示尚未确认，并继续按下方路线排查；无法跨越工具桌面隔离时，请用户从资源管理器双击已校验的 EXE、将 Codex 置于前台确认。

`install-result.json` 保存在安装根目录 `.staging\<本次编号>` 下，不含邮箱、额度数字、Token 汇总或认证信息。安装目录中的 `snapshot.txt` 仍是程序自带的私密额度快照，不用于分享。

## 不显示时检查什么

- 检查 `CodexWindow`、`DataSource` 和 `LastError`，回复只需说明窗口是否找到、额度连接是否成功。不要转述账户数据。
- 区分后台 watcher 和 `--codex-child` 显示进程，检查各自的用户、SessionId、程序路径及启动时间。其他目录的实例占用锁时，先从其 PLUS 菜单退出；不批量终止 `codex.exe`、ChatGPT 或其他项目进程。
- 查看 `%LOCALAPPDATA%\CodexQuotaPills\lifecycle.log`：`independent-watcher-ready` 表示启动握手，`independent-launch-failed`、超时或没有显示子进程都应继续诊断，不能宣布成功。
- 胶囊随 Codex 前台窗口显示，最小化或切换程序会隐藏。当前窗口识别要求 `ChatGPT.exe` 和 `Chrome_WidgetWin_1`；识别不符时报告兼容性问题，不盲目放宽匹配。
- 工具会话读不到交互桌面时，从资源管理器双击程序是直接验证方式。仍不显示时保留诊断结果和日志，定位问题后修复；不要把源码构建成功当作显示成功。

## 可选参数

```powershell
# 指定已记录 SHA-256 的正式版本，适合 API 暂时限流
.\install.ps1 -PinnedRelease
# 已下载便携包：仍按固定 manifest 核对大小和摘要
.\install.ps1 -PackagePath 'C:\Downloads\CodexQuotaPills-0.2.24-portable.zip' -NoLaunch
# 用户明确要求登录 Windows 后自动跟随 Codex
.\install.ps1 -Startup
# 隔离验证，不启动程序、不创建入口、不修改续跑设置
.\install.ps1 -InstallRoot 'C:\Temp\Quota-install-check' -NoLaunch -NoShortcuts
```

`-InstallRoot` 是隔离/指定目录模式，不发现目录以外的现有安装。普通用户应使用默认目录，以便复用已有 Startup 入口。

## 维护与发布

发布前运行 `tests\InstallTests.ps1`。正式包打包时包含本文；源码包同时包含安装脚本、固定 manifest 与公开 Agent 安装指引。发布者在新 Release 资产摘要确认后更新 `install-release.json`，不能填写未验证的摘要。默认仍使用在线 latest，manifest 仅是明确标记的固定版本选择或网络回退。

公开仓库根目录 `AGENTS.md` 使用 `installer-assets/AGENTS.public.md` 的内容；本机维护 AGENTS.md 包含工作区约定，应保留，不整份覆盖或直接公开。本机完成和 GitHub 已发布是两个状态，只有用户明确要求发布时才同步。
