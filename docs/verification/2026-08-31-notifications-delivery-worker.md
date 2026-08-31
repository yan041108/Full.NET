# Notifications Delivery Worker 验证（2026-08-31）

- **任务：** P1 统一消息中心 Task 6（Delivery Worker、Attempt、Receipt 与对账）
- **基线：** `4edd1718c3713a3da2f0f5236ac4cfb4e6a4582e`（`main`）
- **快照：** `notifications-delivery-worker-20260831`
- **范围：** Intent 非 inbox 渠道写入 `accepted` Delivery；Worker 短事务领取、事务外调用 Adapter、LeaseGeneration/Revision CAS 提交 Attempt；回执先验签再去重；人工重试权限；Test Provider 幂等键。不含 Vue 管理页、真实外部 Provider、新迁移 106。未把 Test Provider 编入生产程序集。

## 证据

| 命令 | 结果 |
|---|---|
| Notifications Unit（含退避/死信、Worker Options、状态机）`--minimum-expected-tests 40` | **46/46** |
| Architecture `FullyQualifiedName~Notifications`（含 HostedService 仅 Worker、生产程序集不含 Test Provider）`--minimum-expected-tests 6` | **7/7** |
| `pnpm test:naming` | **30/30** |
| OpenAPI 夹具 `投递与回执...一致` | **1/1** |
| `pnpm test:inner -- --snapshot notifications-delivery-worker-20260831` | 本阶段执行 **none**（inner 只跑 immediate + `migration-*`；本切片无新迁移，合法） |
| `pnpm test:slice -- --snapshot notifications-delivery-worker-20260831` | **2/2 通过**（SQL Server + MySQL Notifications API，约 1m 09s） |

slice 覆盖：非 inbox Intent 后 Delivery=`accepted`、Attempt=0 且 Adapter 未调用；领取后 `sent` 与幂等键去重；回执验签后 `delivered`、重复回执 `duplicate`、乱序 `sent` 不回退、坏签名 `receipt_invalid` 且响应不含 ProviderMessageId、未知 ProviderType 404、超大 Body `receipt_too_large`；瞬时失败保持 `accepted` 并写 NextAttemptAtUtc；永久失败 `failed`、只读权限重试 403、运维重试后再次 `sent`；崩溃窗口不 complete、租约过期后同幂等键重领为 `sent`；并发领取只产生 1 次 Attempt；慢 Provider 不阻塞另一领取循环；租户 GET Host Delivery 404。Enabled Profile 不自动 FanOut 仍成立：写 Delivery 行不等于调用 Adapter。

频控 `rate_limited` 与耗尽 `dead_lettered` 由 Unit `NotificationDeliveryRetryTests` 覆盖；双库 Integration 用 Transient/Permanent/Crash 验证同类退避、失败与崩溃语义。

未重跑完整 `pnpm test:openapi`：此前完整套件中失败来自工作区 P0 Workflow 脏文件，本任务不修。未跑完整 `pnpm test:sql-safety`（无新迁移；历史 009/011/051/093 豁免行号偏差仍在）。本机 Windows 不把 Linux Native AOT publish 标为通过。

MySQL 首次 slice 失败是测试用裸连接按 `Guid` 比较 `BINARY(16)` 得到 COUNT=0，以及无 FROM 的 `INSERT SELECT` 在 MySQL 上可能插入 0 行。已改为 `IQueryExecutor`/`ICommandExecutor` 走 Guid TypeHandler，并把 `InsertDelivery` 改为与 Recipient 相同的 `INSERT VALUES`。

## 结论

- Delivery Worker / Attempt / Receipt / 人工重试达到与当前切片相称的 **Build-verified**。Template/Intent、Profile/Binding、Host 公告/Inbox 与 Tenant Inbox 既有范围不降级，也不升 `Verified`。
- 生产 `NotificationProviderTypeCatalog` 允许为空。`TestNotificationProvider` 只存在于 IntegrationTests。HostedService 只在 Worker `AddBackgroundServices` 注册；Integration 通过直接调用 `NotificationDeliveryBatchProcessor` 验证领取语义。
- 真实邮件/短信/企微/公众号/钉钉仍为 **Planned**。容量继续 `Capacity-not-verified`。未改 Layui。未实现 Vue 管理页（Task 7）。
- 本任务未触发规则或 Skill 演进。
