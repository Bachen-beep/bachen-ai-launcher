# BaChen AI Launcher

用于统一管理本地 AI 插件的 Windows 启动器。插件可以是音频、图像、视频、LLM、视觉、编程、3D 或其他本地 AI 服务。

当前内置插件：

- Woosh-DFlow
- Stable Audio 3：small-sfx、small-music、medium
- IndexTTS2

## 当前能力

- 按名称、说明和分类搜索或筛选插件
- 启动前统一检查端口、GPU 显存、系统内存和已运行 AI 进程
- 从 RSA 签名清单和 SHA-256 校验的 ZIP 安装插件
- 卸载时把托管插件移动到备份，不直接永久删除
- 显示插件版本、发布者、依赖和信任状态
- GitHub 源码更新预览、依赖变化检测和版本回滚
- RSA 签名的启动器自更新、SHA-256 下载校验和上一版本回滚
- 持久轮转日志、全局崩溃日志和一键脱敏诊断导出
- 轻量 Inno Setup 安装包，升级和卸载默认保留用户配置与插件数据
- 内置插件、自定义本地配置和签名第三方插件采用不同信任来源

插件清单规范、依赖格式和发布者签名流程参见 `Docs/PluginManifest.md`。

## 开发环境

- Windows 10/11 x64
- .NET 10 SDK
- WinForms

## 本地编译

```powershell
dotnet build ".\BaChen AI Launcher.csproj" -c Release
```

## 自动自检

```powershell
dotnet ".\bin\Release\net10.0-windows\BaChen AI Launcher.dll" --self-test "$env:TEMP\bachen-launcher-self-test.txt"
Get-Content "$env:TEMP\bachen-launcher-self-test.txt"
```

自检不会启动真实 AI 模型，会在临时目录验证清单签名、篡改拦截、安全安装、资源冲突、配置恢复和可恢复卸载。

## 单文件发布

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Publish.ps1"
```

发布结果位于 `artifacts\release\BaChen AI Launcher.exe`。发布脚本会验证目录中只有一个 EXE，不包含 PDB、用户设置、模型清单或本机路径文件。

## 安装包

安装 Inno Setup 6 后运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Build-Installer.ps1" -Version "0.13.0"
```

安装包位于 `artifacts\installer`。安装包只包含启动器、许可证和第三方声明，不包含插件、Python 环境或模型权重。阶段二发布边界与验收标准参见 `Docs/StageTwoReleasePlan.md`，签名配置参见 `Docs/ReleaseSigning.md`。

发布前还应检查 `Docs/CleanMachineTestMatrix.md`、`Docs/ReleaseChecklist.md` 和 `Docs/ReleaseSigning.md`。当前 GitHub 预览版说明位于 `Docs/ReleaseNotes-0.13.0-stage2-preview.md`。

公开 Release 会为单文件启动器和安装包生成 GitHub Artifact Attestation。下载后可验证构建来源：

```powershell
gh attestation verify ".\BaChen.AI.Launcher.exe" --repo Bachen-beep/bachen-ai-launcher
gh attestation verify ".\BaChen-AI-Launcher-Setup-0.13.0.exe" --repo Bachen-beep/bachen-ai-launcher
```

Attestation 用于证明文件来自指定仓库、提交和 GitHub Actions 工作流，不能代替 Authenticode，也不会消除 Windows SmartScreen 提示。

## 用户数据

- 配置：`%LocalAppData%\BaChen AI Launcher`
- 新安装默认数据目录：`%UserProfile%\Documents\BaChen AI Launcher Data`
- 新环境变量：`BACHEN_AI_CONFIG_DIR`、`BACHEN_AI_DATA_ROOT`
- 旧版配置目录和 `BACHEN_AI_AUDIO_*` 环境变量仍可自动迁移或兼容读取
- 旧安装会保留原插件位置，不会复制或自动移动模型权重

三个内置服务的默认端口分别为 `7860`、`7861`、`7862`。受显存限制，建议同一时间只运行一个高显存模型。

## 源码结构

- `Configuration/`：路径、设置、模型清单和原子配置存储
- `Plugins/`：插件清单、依赖检查、安装、卸载和恢复数据
- `Security/`：发布者公钥与 RSA 清单签名验证
- `Services/`：插件进程、端口、GPU 和系统内存调度
- `Updates/`：GitHub 更新检查、版本状态和回滚元数据
- `UI/`：WinForms 界面、搜索筛选和交互流程
