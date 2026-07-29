# 0.16.8

- 修复 Woosh 官方模型 ZIP 自带 `checkpoints` 目录时被重复解压为 `checkpoints/checkpoints` 的问题。
- 自动迁移 `0.16.7` 已下载到双层目录中的六个 DFlow checkpoint 文件，不重复下载模型资产。
- 缺失文件错误现在会列出具体路径，便于定位不完整或被安全软件拦截的文件。
