# 0.16.10

- 修复官方 Stable Audio 3 `run_gradio.py` 不接受 `--port` 参数而退出的问题；端口继续通过 `GRADIO_SERVER_PORT` 环境变量传递。
- 自动移除 `0.16.9` 已保存启动命令中的 `--port` 参数，无需重新安装插件。
- GitHub 自动导入现在立即建立标准更新基线；既有导入会回退读取 `.bachen-github-source.json`，不再显示“尚未建立更新记录”。
