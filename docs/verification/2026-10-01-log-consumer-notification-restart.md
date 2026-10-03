# 独立日志消费者通知 Pod 重建验证

- 日期：2026-10-01；授权：用户继续推进日志开发，以实际本地测试验收。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`，分支 `codex/foundation-acceptance-20260926`，任务快照 `log-notification-restart-20261001`；保留既有改动，未提交或切流。
- 范围：显式本地通知实验、重建证据校验、工具测试和说明；没有 .NET、数据库或 AOT 变化。
- 环境：Windows、Docker Desktop、`kind-fullnet-local` 三节点 linux/amd64，共享一台主机，复用本地监控工具和既有固定镜像。

## 实验边界

`pnpm test:log-consumer:notification-restart:live` 在首个 firing 实际交付后删除并重建任务命名空间内的 Alertmanager Pod。校验原/新 namespace 和名称、非空且不同的 UID、新 Pod Ready；接收端保持运行，后续 firing 必须同指纹且位于已消费游标之后，随后才进入标签排除/恢复及消费者 Pod 删除阶段。沿用正式告警表达式和一小时 repeat_interval，不手工向接收端注入成功通知。

重建删除原 emptyDir，验证依靠 Prometheus 重发仍触发的告警。该实验不证明通知持久队列跨重启恢复、不保证去重或恰好一次，也不覆盖 Alertmanager HA、接收端自身重启、长期故障、健康消费者恢复、容量或请求 P99。独立消费者仍默认 disabled/experimental。

结果仅在任务资源清理与原 Prometheus `spec.alerting` 字段恢复成功后发布至 `artifacts/log-consumer-notification-restart/result.json`；运行配置传播依照 Operator 正常调谐。可复用监控工具及镜像缓存保留。

## 测试证据

先运行新增重建身份用例，因证明函数缺失实际失败 1/1；实现后通过，覆盖原 UID 未变、新 Pod 未就绪、跨任务、缺 UID 和非法 namespace 的拒绝。

| 命令 | 实际结果 |
| --- | --- |
| `pnpm test:observability-deploy` | 15/15，0 失败/跳过 |
| `pnpm test:inner -- --snapshot log-notification-restart-20261001` | 受影响工具 64/64，0 失败/跳过 |
| 两个修改脚本的 `node --check` | 退出 0 |
| `pnpm test:governance` | 57/57，0 失败/跳过 |
| `pnpm test:log-consumer:notification-restart:live` | 退出 0，Pod 重建、同指纹重发、后续恢复及清理通过 |

真实工件完成时间 `2026-09-30T16:49:17.292Z`（本地 2026-10-01），namespace `fullnet-log-restart-f8bb6ca7`：

- 实际消费者重启 3 次，正式规则触发；标签排除/恢复和删除消费者 Pod 后解除通过。
- 原 Alertmanager UID `b082201f-75da-47de-80ad-38650e574ab2`，新 UID `c2a80be9-2ebd-4ee1-a23f-b982d082a735`；`podReplaced=true`、`ready=true`、`firingRedelivered=true`。
- 交付顺序 `firing → firing → resolved → firing → resolved`，指纹一致；第二次 firing 在 Pod 重建后等待成功，再推进后续阶段。
- `cleanupCompleted=true`、`alertingConfigurationRestored=true`。另行查询确认任务 namespace 已删除、专用 Prometheus `spec.alerting` 为空。

完成检查覆盖默认模式不触发重建、重试与重建实验互斥、任务身份、阶段游标、原配置恢复及失败不发布通过工件。本任务差异检查通过。
