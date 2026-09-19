# 应用恢复

## 场景

| 场景 | 首要动作 |
| --- | --- |
| 数据库损坏 | 从最近备份恢复；Migrator 校验迁移版本 |
| Api 不可用 | 检查 Api Pod/进程；Worker 继续消费可靠事件 |
| Worker 积压 | 扩容 Worker；检查 Outbox/Jobs 深度 |
| 秘密泄露 | 轮换 ConnectionStrings、Redis、Webhook 签名密钥 |

## 恢复顺序

1. 恢复数据库到一致时间点
2. 部署已知良好版本的 Migrator 并验证
3. 启动 Worker 直至队列清空
4. 启动 Api 并执行冒烟测试

## 禁止事项

- 不要在生产直接修改业务表绕过模块边界
- 不要跳过 Migrator 手工执行未登记 SQL
- 恢复脚本不得输出或记录凭据原文

## 相关文档

- [应用升级](./application-upgrade.md)
- [F16 发布清单](../verification/f16-release-checklist.md)
