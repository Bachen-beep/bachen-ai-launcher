# 0.16.14

- 修复 Stable Audio 3 三档切换配置重新带入官方 `run_gradio.py` 不支持的 `--port` 参数而以退出码 2 结束的问题。
- 启动前同时规范化安装记录和当前运行配置；旧版本残留的 `--port` 会自动清除，端口继续仅通过 `GRADIO_SERVER_PORT` 环境变量传递。
- Small SFX、Small Music、Medium 使用独立的运行名称，错误日志标题与实际 `--model` 参数保持一致。
