# 0.16.7

- 修复通过 GitHub 自动导入 `SonyResearch/Woosh` 后只安装源码、未安装 DFlow 外部权重的问题。
- 启动已有 Woosh-DFlow 导入时，检测到缺失权重会下载、校验 SHA-256 并解压官方的 `Woosh-AE`、`TextConditionerA` 和 `Woosh-DFlow` 资产。
- 启动前检查改为校验 `config.yaml` 与 `weights.safetensors`，不会再把空的 checkpoint 目录判定为可运行。
