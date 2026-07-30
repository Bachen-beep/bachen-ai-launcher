# 0.16.20

- 修复 Python 安装器返回成功码 `0`、但没有在指定目录生成 `python.exe` 时显示 `Managed Python installation failed (0)` 的问题。
- 托管 Python 改用 Python Software Foundation 发布的固定版本 NuGet 便携包，不再调用受系统既有 Python 注册状态影响的 Windows 安装器。
- 便携运行时下载后会验证固定大小与 SHA-256，并在临时目录中检查 Python 版本、`pip` 和 `venv`，全部通过后才原子替换正式运行时目录。
- 已有托管 Python 如果损坏或缺少 `pip` / `venv` 会自动重建；替换失败时恢复旧目录。
