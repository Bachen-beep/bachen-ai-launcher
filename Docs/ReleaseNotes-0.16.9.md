# 0.16.9

- 修复 GitHub 自动导入 Stable Audio 3 时普通 `uv sync` 未安装 `ui` extra，导致缺少 `gradio` 的问题。
- 为 Stable Audio 3 提供 Small SFX、Small Music 和 Medium 三个带完整 `--model`、`--port` 参数的启动项，默认推荐 Small SFX。
- 启动已有的失败安装时自动补装 UI 依赖，并将旧的裸 `run_gradio.py` 命令迁移为 Small SFX 启动命令。
