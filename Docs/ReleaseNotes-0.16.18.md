# 0.16.18

- 修复从 GitHub 安装 IndexTTS2 时遗漏 `webui` 可选依赖、导致启动时报 `ModuleNotFoundError: No module named 'gradio'` 的问题。
- 已有 IndexTTS2 安装在启动前会自动检查并修复缺失或版本不兼容的 Python 环境，无需删除模型权重或重新下载仓库。
- 自动迁移旧版保存的裸 `webui.py` 启动命令，显式传递 `--host 127.0.0.1 --port {port}`，确保实际监听端口与启动器状态检测一致。
