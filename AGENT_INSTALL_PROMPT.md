# Codex Quota Pills 本地安装提示词

下面内容可复制给本机 Codex，让它从当前源码目录构建并验证 Codex Quota Pills。

```text
请在当前 Codex Quota Pills 源码目录执行只读检查，然后运行 .\test.ps1 和 .\build.ps1。
测试全部通过后，确认 bin\CodexQuotaPills.exe 已生成。不要修改或关闭 Codex 本体，不要读取、输出或复制任何 API key、OAuth token、Cookie、localStorage 或认证请求头。
如需验证额度连接，只运行 bin\CodexQuotaPills.exe --snapshot，并只报告 CodexWindow、DataSource 和 LastError 是否为空；不要在回复中转述具体额度、累计 Token、邮箱或其他账户数据。
不要安装到系统目录，也不要创建开机启动项，除非我当次明确批准。
```

正式 GitHub Release 发布后，可再补充带 SHA-256 校验的下载安装提示词。
