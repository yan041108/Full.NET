# 独立日志消费者通知持续 60 秒 HTTP 故障验证

- 日期：2026-10-01；授权：用户继续推进日志开发，以实际本地测试验收。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`，分支 `codex/foundation-acceptance-20260926`，快照 `log-notification-outage-20261001`；保留既有改动，未提交或切流。
- 范围：显式测试接收端时间窗口故障、真实 Alertmanager 通知恢复及文档；没有 .NET、数据库或 AOT 变化。
- 环境：Windows、Docker Desktop、`kind-fullnet-local` 三节点 linux/amd64，共享一台主机；复用既有固定镜像和本地监控工具。

## 实现与边界

`pnpm test:log-consumer:notification-outage:live` 从首次有效告警请求开始，用接收端单调时钟连续 60000 毫秒返回 HTTP 503。故障期间不加入成功事件，无效请求不启动或消耗计时；部署等待不缩短故障窗口。接收端保留最多 100 条白名单尝试，额外记录整数 elapsedMs；超过上限失败关闭。

时间故障默认关闭，限 0–300000 毫秒整数，与按次数故障互斥。runner 的持续故障、重试及 Pod 重建模式互斥，分别发布工件。首次成功前必须有多次相同 firing 身份/指纹的 503，首次成功时间不得早于 60000 毫秒；之后才进行标签排除 resolved、恢复 firing、删除消费者 Pod 后 resolved。使用正式规则与一小时 repeat_interval，不手工向集群接收端注入成功事件。

此实验仅证明连续 60 秒 HTTP 503 后的实际恢复通知，不证明任意时长故障、通知持久恢复、HA、外部接收器或人的接收确认、健康消费者恢复、容量及请求 P99；独立消费者仍默认 disabled/experimental。

## 运行证据

先增加非法时长、模式互斥及有效请求起算/截止边界用例，实际 5/6 通过、1 项失败（缺少 RangeError）；实现后全部通过。可控时钟下 59999 毫秒仍返回 503 且成功集合为空，60000 毫秒返回 200。

| 命令 | 实际结果 |
| --- | --- |
| `pnpm test:observability-deploy` | 16/16，0 失败/跳过 |
| 三个修改脚本的 `node --check` | 退出 0 |
| `pnpm test:inner -- --snapshot log-notification-outage-20261001` | 受影响工具 64/64，0 失败/跳过 |
| `pnpm test:governance` | 57/57，0 失败/跳过 |
| `pnpm test:log-consumer:notification-outage:live` | 退出 0，持续故障恢复、四阶段交付、配置恢复及清理通过 |

报告 `artifacts/log-consumer-notification-outage/result.json`，namespace `fullnet-log-restart-7d7597d4`，完成时间 `2026-09-30T16:56:20.304Z`（本地 2026-10-01）：

- 实际消费者重启 3 次，正式告警触发、标签排除/恢复及删除消费者 Pod 后解除通过。
- `requestedMilliseconds=60000`，同指纹 firing 被拒绝 37 次，最后一次拒绝发生于 57243 毫秒；首次 HTTP 200 发生于 60000 毫秒。
- `deliveries=[firing,resolved,firing,resolved]`，指纹一致；持续故障期间未将 503 当作成功交付。
- `cleanupCompleted=true`、`alertingConfigurationRestored=true`；另行查询任务 namespace 已删除、专用 Prometheus 原通知字段为空。配置传播仍遵守 Operator 正常调谐；本地监控工具和镜像缓存保留。

完成检查覆盖默认关闭、模式互斥、单调计时从有效请求起算、时长/内存上限、白名单与同指纹、四阶段游标以及失败不发布通过工件。本任务差异检查通过。尝试上限是测试资源保护；较长或更频繁的故障可能触及上限而失败，不能据此推断生产投递策略。
