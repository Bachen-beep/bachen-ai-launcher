# 0.16.22

- 发布稳定更新源后，先通过认证 GitHub Contents API 读回 `stable.json` 并校验 SHA-256，确保写入结果可立即可靠确认。
- GitHub Raw CDN 可见性检查延长至两分钟；若仅 CDN 传播延迟，则记录警告但不再将已经成功发布的版本标记为失败。
