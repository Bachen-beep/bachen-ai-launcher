# BaChen Plugin Manifest v6

BaChen AI Launcher 支持两种可信安装来源：经过发布者 RSA 公钥验证的独立清单，或由 BaChen RSA 签名插件索引提供的清单。源码包、运行时和模型资产都必须通过固定大小与 SHA-256 校验。

## Package layout

插件使用 ZIP 包分发。ZIP 可以直接包含插件文件，也可以只有一个顶层目录。`executable` 和 `requiredFiles` 必须是包内相对路径，不能包含 `..` 或绝对路径。

清单模板位于 `examples/plugin-manifest.example.json`。

## Required fields

- `schemaVersion`: 新插件使用 `6`；启动器继续兼容已签名的 v2-v5 清单。
- `id`: 小写稳定 ID，只能包含字母、数字、`-` 和 `_`。
- `displayName`, `version`, `publisher`, `category`, `description`。
- `executable`: 相对于插件目录的启动程序。
- `arguments`: 支持 `{root}` 和 `{port}` 占位符。
- `runtime`, `runtimeVersion`: 结构化运行时和版本约束，例如 `python` 与 `>=3.10,<3.13`。
- `port`: `1024` 到 `65535` 的独立端口。
- `packageSha256`: ZIP 文件的 64 位十六进制 SHA-256。
- `packageSizeBytes`: ZIP 文件的精确字节数，下载和安装时都会校验。
- `packageMirrors`: 可选 HTTPS 镜像列表；主地址失败后按顺序重试。
- `preservedPaths`: 更新时必须保留的插件内相对目录或文件，例如模型、输出和用户配置。
- `signature`: `keyId`、`RSA-SHA256` 和 Base64 签名。
- `licenseName`: 插件或模型适用的许可证名称。
- `licenseUrl`: 指向完整上游条款的 HTTPS 地址。
- `requiresLicenseAcceptance`: v3 及以上必须为 `true`，安装前需要用户主动接受。

## Installation fields

- `createVirtualEnvironment`: 为 Python 插件自动建立独立虚拟环境。
- `virtualEnvironmentPath`: 虚拟环境在插件目录中的安全相对路径。
- `requirementsFile`: 可选依赖文件；创建环境后使用 pip 安装。
- `minimumFreeDiskBytes`: 除安装包展开空间外要求保留的最低磁盘空间。
- `requiresExternalAuthorization`: 模型是否需要用户在外部平台自行完成授权。
- `modelProvider`, `modelId`: 当前支持 `huggingface` 和对应模型 ID。
- `authorizationUrl`: 用户自行注册、登录和接受条款的官方 HTTPS 页面。
- `authorizationProbePath`: 用于验证实际模型读取权限的小文件路径。
- `managedRuntimeId`: 由启动器管理的固定运行时 ID。当前支持 `python-3.11.9-x64` 和 `python-3.12.10-x64`；GitHub 仓库自动导入会依据 `project.requires-python` 选择最高兼容版本。
- `pythonInstallArguments`: 在插件虚拟环境中执行的参数数组，例如 `-m pip install -e .`。
- `assetPackages`: 模型或其他大文件包列表；每项声明稳定 ID、HTTPS 主地址、镜像、SHA-256、精确大小与安全目标目录。

启动器不会替用户注册账户、提交许可协议或申请 gated model 权限。令牌只以只读用途保存到 Windows Credential Manager，不写入插件清单、配置文件或诊断日志。

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

## Signed catalog

官方插件目录位于 `Catalog/plugin-index.json`，启动器会验证整个索引的 RSA-SHA256 签名。远程索引不可用时回退到 EXE 内嵌的同一份签名索引。安装后会在插件目录保留签名索引证据；每次启动插件前重新验证索引签名、清单内容和实际启动命令。

插件索引私钥不得提交到仓库。当前本地发布操作使用 Windows Credential Manager 中的 `BaChenAILauncher/PluginIndexSigningKey`，GitHub Release 发布的是已经签名的公开索引文件。
