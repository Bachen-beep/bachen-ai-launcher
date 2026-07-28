# BaChen Plugin Manifest v4

BaChen AI Launcher 只会从经过信任的 RSA 公钥验证成功的清单安装插件。安装包还必须通过清单中的 SHA-256 校验。

## Package layout

插件使用 ZIP 包分发。ZIP 可以直接包含插件文件，也可以只有一个顶层目录。`executable` 和 `requiredFiles` 必须是包内相对路径，不能包含 `..` 或绝对路径。

清单模板位于 `examples/plugin-manifest.example.json`。

## Required fields

- `schemaVersion`: 新插件使用 `4`；启动器继续兼容已签名的 v2 和 v3 清单。
- `id`: 小写稳定 ID，只能包含字母、数字、`-` 和 `_`。
- `displayName`, `version`, `publisher`, `category`, `description`。
- `executable`: 相对于插件目录的启动程序。
- `arguments`: 支持 `{root}` 和 `{port}` 占位符。
- `runtime`, `runtimeVersion`: 结构化运行时和版本约束，例如 `python` 与 `>=3.10,<3.13`。
- `port`: `1024` 到 `65535` 的独立端口。
- `packageSha256`: ZIP 文件的 64 位十六进制 SHA-256。
- `packageSizeBytes`: ZIP 文件的精确字节数，下载和安装时都会校验。
- `preservedPaths`: 更新时必须保留的插件内相对目录或文件，例如模型、输出和用户配置。
- `signature`: `keyId`、`RSA-SHA256` 和 Base64 签名。
- `licenseName`: 插件或模型适用的许可证名称。
- `licenseUrl`: 指向完整上游条款的 HTTPS 地址。
- `requiresLicenseAcceptance`: v3 必须为 `true`，安装前需要用户主动接受。

## Dependencies

可自动验证的依赖格式：

- `command:ffmpeg`
- `env:VARIABLE_NAME`
- `file:relative/path`
- `python>=3.10`
- `cuda`

其他依赖会显示给用户，但不会自动判定为缺失。

## Signing

1. 为 ZIP 计算 SHA-256，并写入清单。
2. 使用启动器生成规范化签名载荷：

```powershell
& ".\BaChen AI Launcher.exe" --canonicalize-manifest manifest.json canonical-payload.json
```

3. 使用 RSA 私钥和 SHA-256 PKCS#1 v1.5 对 `canonical-payload.json` 的原始 UTF-8 字节签名。
4. 将 Base64 签名写入 `signature.value`。
5. 用户在“工具 > 受信任发布者”导入对应 PEM 公钥后才能安装。

仓库提供的 `scripts/Sign-PluginManifest.ps1` 可完成第 2 到第 4 步，需要 PowerShell 7.2 或更高版本。

私钥不得放入插件 ZIP、源码仓库或启动器配置目录。
