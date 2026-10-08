# 杀毒软件误报处理

Codex Quota Pills 是一个未加壳的 .NET Framework 桌面程序。它使用 Windows layered window、目标进程范围内的 WinEvent 位置监听，并启动本机 `codex app-server` 读取额度；部分启发式杀毒引擎可能把这些行为组合误判为木马。

## 发布者处理顺序

1. 只从本项目的固定构建环境生成正式版本，不使用 UPX、加壳器、混淆器或自解压封装。
2. 对 EXE 和安装器使用受信任 CA 签发的 Authenticode 代码签名证书，以 SHA-256 签名并添加 RFC 3161 时间戳。
3. 发布到固定 HTTPS/GitHub Release 地址，同时提供源代码、版本说明和 SHA-256 校验值。
4. 每个正式版本把原始 EXE、误报截图、软件说明、源码/Release 地址和 SHA-256 提交给 360 软件误报反馈平台：<https://open.soft.360.cn/report.php>。
5. 如果 Microsoft Defender 也报毒，再以“Software developer / incorrectly detected”提交到 Microsoft Security Intelligence：<https://www.microsoft.com/en-us/wdsi/filesubmission>。

360 官方说明的常规处理时间约为 1–2 个工作日。每次重新编译都会改变文件哈希，因此应先冻结正式二进制，再提交申诉；不要反复重新打包后提交不同样本。

## 不建议的做法

- 不要让接收者直接关闭杀毒软件或把整个下载目录加入白名单。
- 不要使用自签名证书冒充公共信任签名；接收者未安装该证书时不会获得可信发布者身份。
- 不要仅修改文件名、压缩密码或换压缩格式规避扫描，这不会解决信誉与启发式检测问题。
- 不要把私有版本随意上传到公共多引擎扫描服务；部分服务会把样本共享给安全厂商。公开发行版可在确认后再使用。

## 签名示例

取得代码签名证书并安装 Windows SDK 后，可按证书服务商提供的时间戳地址执行：

```powershell
signtool sign /fd SHA256 /tr https://timestamp.example.com /td SHA256 /a CodexQuotaPills.exe
signtool verify /pa /v CodexQuotaPills.exe
```

时间戳地址应替换为证书颁发机构提供的 RFC 3161 服务地址。
