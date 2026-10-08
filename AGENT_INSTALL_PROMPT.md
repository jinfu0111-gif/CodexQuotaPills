# Codex Quota Pills 安装提示词

普通用户向 Windows 本机 Codex 发送：

> 帮我安装并启动 https://github.com/jinfu0111-gif/CodexQuotaPills ，按仓库 INSTALL.md 执行，确认额度连接和胶囊显示是否成功。

Agent 先读 [INSTALL.md](INSTALL.md) 与根目录 `install.ps1`，使用正式 Release 便携包；不要默认克隆、编译或运行开发测试。安装、额度连接、运行和实际可见性必须分别说明。

下面的源码验证路线只用于用户明确要求开发、构建或源码验证的任务：

```text
请在当前 Codex Quota Pills 源码目录执行只读检查，然后运行 .\test.ps1 和 .\build.ps1。
测试全部通过后，确认 bin\CodexQuotaPills.exe 已生成。不要修改或关闭 Codex 本体，不要读取、输出或复制任何 API key、OAuth token、Cookie、localStorage 或认证请求头。
如需验证额度连接，只运行 bin\CodexQuotaPills.exe --snapshot，并只报告 CodexWindow、DataSource 和 LastError 是否为空；不要在回复中转述具体额度、累计 Token、邮箱或其他账户数据。
不要安装到系统目录，也不要创建开机启动项，除非我当次明确批准。
```

测试失败时保留详情，区分环境限制和程序缺陷，不跳过失败后声称验证通过。首次安装、已有设置保留与桌面隔离处理见 INSTALL.md。
