# 独立日志消费者通知 HTTP 503 重试验证

- 日期：2026-10-01；授权：用户继续推进日志开发，使用本地测试验收。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`，分支 `codex/foundation-acceptance-20260926`，快照 `log-notification-retry-20261001`；保留既有工作区改动，未提交或切流。
- 范围：测试接收端故障注入、真实 Alertmanager 重试证明和对应文档；没有 .NET、业务数据库或 AOT 变化。
- 环境：Windows、Docker Desktop、`kind-fullnet-local` 三节点 linux/amd64，共享一台主机；复用本地 Operator/Prometheus/KSM。

## 实现与失败证据

接收端故障预算默认 0，仅允许整数 0–8。显式重试入口设置为 2：前两次有效请求返回 503，不加入成功事件；无效请求不消耗预算。尝试证据最多 100 条，仅保存状态、指纹、namespace、Pod、告警名和响应码，不保留额外标签、注解或原始请求。

先新增失败测试，实际得到 4 项通过、1 项失败（期待 503，实际 200）；实现故障预算后接收端 5/5 通过。真实实验没有手工向集群接收端 POST 成功事件；只有 Alertmanager 通知能推进阶段。首次三次有效尝试必须为同一 firing 的 `503 → 503 → 200`，且 namespace、Pod、指纹一致。测试 repeat_interval 为 1 小时，本次在数分钟内完成，避免把定期重复当成重试。HTTP 5xx 可恢复处理依据固定 [Alertmanager v0.34.0 Webhook 源码](https://raw.githubusercontent.com/prometheus/alertmanager/v0.34.0/notify/webhook/webhook.go)。

## 实际验收

| 命令 | 实际结果 |
| --- | --- |
| `pnpm test:observability-deploy` | 14/14，9 项部署检查与 5 项接收端测试，0 失败/跳过 |
| `pnpm test:inner -- --snapshot log-notification-retry-20261001` | 受影响工具目标 64/64，0 失败/跳过 |
| 三个受影响脚本的 `node --check` | 退出 0 |
| `pnpm test:log-consumer:notification-retry:live` | 退出 0，同指纹 503/503/200、四阶段交付、恢复与清理通过 |
| `pnpm test:governance` | 57/57，0 失败/跳过 |

最终另行查询确认任务 namespace 不存在、专用 Prometheus 的 `spec.alerting` 为空；本任务差异检查通过。

报告 `artifacts/log-consumer-notification-retry/result.json`，namespace `fullnet-log-restart-0aa3e17c`，完成时间 `2026-09-30T16:36:19.649Z`（本地 2026-10-01）：

- 消费者实际缺配置退出 1，重启 3 次；KSM 标签与工作负载 namespace 保留，正式规则触发。
- `retryProof.rejectedAttempts=2`、`responseCodes=[503,503,200]`、`sameFingerprint=true`；前两次未确认接收。
- 成功交付依次为 `firing → resolved → firing → resolved`，四阶段同指纹，各阶段推进接收游标。
- `cleanupCompleted=true`、`alertingConfigurationRestored=true`；任务 namespace 及临时通知资源删除，原 Prometheus `spec.alerting` 字段恢复。运行配置传播仍遵守 Operator 正常调谐，本地监控工具和镜像缓存保留。

独立增量审查未发现阻塞问题，复核默认关闭、有效请求预算、白名单与容量上限、重试身份和清理逻辑；审查者单独执行接收端测试 5/5 通过。

本记录验收短暂 HTTP 503 后的实际通知重试。通知跨进程重启、HA、长期故障、外部邮件/聊天与人的接收确认、健康服务恢复、容量及请求 P99 尚未在此验证。独立消费者默认 disabled/experimental 不变；固定镜像与准备步骤见[平台说明](../../deploy/observability/README.md#independent-log-consumer-monitoring)。
