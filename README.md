# BaChen AI Launcher

用于统一管理本地 AI 插件的 Windows 启动器。插件可以是音频、图像、视频、LLM、视觉、编程、3D 或其他本地 AI 服务。

当前内置插件：

- Woosh-DFlow
- Stable Audio 3：small-sfx、small-music、medium
- IndexTTS2

启动器也支持通过模型清单添加自定义插件，包括程序路径、启动参数、固定端口、显存建议、必需文件和 GitHub 源码仓库。

## 开发环境

- Windows 10/11 x64
- .NET 10 SDK
- WinForms

## 本地编译

```powershell
dotnet build ".\BaChen AI Launcher.csproj" -c Release
```

## 单文件发布

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Publish.ps1"
```

发布结果位于 `artifacts\release\BaChen AI Launcher.exe`。发布脚本会验证目录中只有一个 EXE，不包含 PDB、用户设置、模型清单或本机路径文件。

## 用户数据

- 配置：`%LocalAppData%\BaChen AI Launcher`
- 新安装默认数据目录：`%UserProfile%\Documents\BaChen AI Launcher Data`
- 新环境变量：`BACHEN_AI_CONFIG_DIR`、`BACHEN_AI_DATA_ROOT`
- 旧版配置目录和 `BACHEN_AI_AUDIO_*` 环境变量仍可自动迁移或兼容读取
- 旧安装会保留原插件位置，不会复制或自动移动模型权重

三个内置服务的默认端口分别为 `7860`、`7861`、`7862`。受显存限制，建议同一时间只运行一个高显存模型。
