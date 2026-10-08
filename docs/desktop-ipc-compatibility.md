# 桌面 IPC 兼容性维护

0.2.17 将固定包版本白名单改为每次连接验证官方包内的静态合约。Codex 更新后，服务进程路径自动指向新包，相同合约可继续工作；合约变化时停止发送并在管理面板显示原因。自动兼容不等于自动放宽安全门。

## 2026-10-07 本机验证

- 官方包：OpenAI.Codex 26.930.7945.0 x64，publisher ID 2p2nqsd0c76g0。
- `.vite/build/src-*.js`：state changed 11、following changed 1、owner discovery 1、follower start turn 2、archived 2、queued followups changed 2。
- `.vite/build/bootstrap-*.js`：owner 返回 supportsUntrustedAppInput；follower 调用原 startTurn；inheritThreadSettings 默认为 true，并传递给原设置处理逻辑。
- 原生 IPC initialize、当前已打开对话 snapshot 与只读 app-server 额度读取均成功；运行中对话被拒绝续跑。
- 只读扫描 85 个未归档对话，额度中断候选 0，真实发送 0。
- 隔离管道覆盖原 owner 发送、取消、缺少 capability、未知状态协议及排队消息变更。离线测试覆盖额度、最新轮次、审批、设置指纹、持久化意图、预算与暂停迁移。
- 尚未验证真实额度耗尽到自然恢复的端到端过程。真实发送测试必须使用用户明确指定的测试对话。

## 新版合约改变时

先只读检查安装包静态声明与实现，不修改 app.asar。更新适配代码与隔离测试，保留所有原有安全门；运行 test.ps1、build.ps1，随后只读握手/快照和扫描。不要只替换最近验证的版本号，也不要退回 CDP 注入、改审批策略或猜测写请求。

发布新的独立目录与便携/源码 ZIP，保留旧目录。源码包从当前工作区允许列表生成，包含未提交的新源码；禁止加入私密设置、运行缓存、额度账本、认证文件和下载的 MSIX。先打包再迁移本机显示设置。切换 Startup 快捷方式前备份，并核验实际 watcher 与 --codex-child 的版本。

## 已比较的公开实现

- [progressrdx/codex-auto-resume](https://github.com/progressrdx/codex-auto-resume)：MIT，无运行依赖；复用已有 framed follower transport 和安全策略思路。上游面向 macOS 且固定验证版本，Windows 合约必须本机核验，许可已保存在 docs/licenses。
- [JiaYang-BUAA/Codex-Desktop-Usage-Monitor-Windows](https://github.com/JiaYang-BUAA/Codex-Desktop-Usage-Monitor-Windows)：持续维护的 Windows 项目，可参考公开数据与升级经验；其 CDP 路线适配成本和权限边界不符合本项目要求，不运行或直接移植其发送代码。

本次优先复用本项目已有客户端、雷达、界面与测试。没有安装第三方工具或增加依赖。
