# 0.16.12

- Stable Audio 3 启动前按 `small-sfx`、`small-music`、`medium` 验证对应 Hugging Face 门控仓库的实际文件访问权限。
- 新电脑缺少令牌、令牌失效或账号尚未获得模型权限时，先显示授权处理界面，不再启动 Python 后以 `401 GatedRepoError` 立即退出。
- 验证通过的只读令牌仅通过当前子进程的 `HF_TOKEN` 环境变量传递，不写入插件参数、模型配置或运行日志。
