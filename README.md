# Bachen AI Audio Launcher

用于统一管理本地 AI 音频插件的 Windows 启动器。

当前内置插件：

- Woosh-DFlow
- Stable Audio 3: small-sfx, small-music, medium
- IndexTTS2

## 开发环境

- Windows 10/11 x64
- .NET 10 SDK
- WinForms

## 本地编译

```powershell
dotnet build ".\AI Audio Launcher.csproj" -c Release
```

## 单文件发布

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Publish.ps1"
```

发布结果位于 `artifacts\release\AI Audio Launcher.exe`。发布脚本会验证目录中只有一个 EXE，不包含 PDB、用户设置、模型清单或本机路径文件。

## 用户数据

- 配置：`%LocalAppData%\Bachen AI Audio`
- 新安装默认数据目录：`%UserProfile%\Documents\Bachen AI Audio Data`
- 旧安装会保留原插件位置，不会自动移动模型权重。

三个内置服务的默认端口分别为 `7860`、`7861`、`7862`。受显存限制，建议同一时间只运行一个高显存模型。
