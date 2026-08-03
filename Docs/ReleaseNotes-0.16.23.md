# 0.16.23

- 修复 Windows 进程检索自测的启动竞态：插件卸载测试会在有限时间内等待 WMI 提供新启动进程的命令行元数据，避免 GitHub Runner 偶发误报。
- 保留 `0.16.22` 对稳定更新源发布的改进：以 GitHub Contents API 的 SHA-256 回读作为强校验，Raw CDN 传播延迟仅记录为警告。
