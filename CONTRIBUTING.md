# Contributing

感谢你参与 Codex Quota Pills。

## 开发流程

1. 从 Issue 或一个边界明确的改动开始。
2. 修改前运行 `.\test.ps1`。
3. 保持额度访问只读，不得加入账户或额度写操作。
4. 不要记录或提交 access token、refresh token、Cookie、认证请求头、邮箱或本机路径。
5. 修改额度解析、窗口定位或鼠标穿透逻辑时必须补测试。
6. 提交前再次运行 `.\test.ps1` 和 `.\build.ps1`。

## 代码约束

- 目标运行时为 Windows .NET Framework 4.x。
- 避免引入仅在新 .NET Runtime 可用的 API。
- 保持程序不修改 Codex `app.asar`。
- 网络地址必须有严格的 scheme、host、path 白名单。
- 新依赖必须说明许可证、维护状态和引入理由。

## Pull Request

PR 描述请包含：问题、实现、风险、测试结果和必要的界面截图。贡献内容按仓库的 AGPL-3.0 许可证发布。

