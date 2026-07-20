# 1.0 前命名规范化验证记录

- 日期：2026-07-21
- 类型：双库 Expand/Contract 自动化矩阵与 Runbook 映射
- 状态：`Build-verified`（自动化证据）；生产维护窗口与备份升级演练未实跑
- 代码基线：`7a25b34`（Task 6 文档交付 + Task 5 Step 5 自动化门禁）
- 范围：010 Expand、011 Contract、应用 SQL/Outbox 切换、协议别名兼容、`PreV1NameMapV1` 债务收敛
- 方法：Release 构建、Testcontainers 双库、`pnpm test:naming`、Integration 全量与聚焦 filter

## 声明结论

- 仓库内 **自动化** 双库 010/011 矩阵、半完成恢复路径与 [1.0 前命名规范化 Runbook](../development/pre-v1-naming-migration-runbook.md) 已对齐，可作为发布前核对清单的技术依据。
- **不能** 将本能力整体提升为 `Verified`：真实生产维护窗口、备份恢复 RPO/RTO、旧 Outbox `MessageType` 排空计时、Vue/Layui/uni-app 在升级路径上的端到端演练尚未完成。
- 能力矩阵保持 `Build-verified`；缺口见 [capability-status.md](../roadmap/capability-status.md)。

## 新鲜自动验证

环境：Windows 10、.NET SDK 10.0、Docker Desktop（Linux containers）、Testcontainers；Integration Workers=2、共享单实例 SQL Server/MySQL。

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Release 构建 | `dotnet build Full.NET.slnx -c Release` | 0 警告、0 错误 |
| Unit | `--minimum-expected-tests 314` | **314/314** |
| Compatibility | `--minimum-expected-tests 7` | **7/7** |
| Architecture | `--minimum-expected-tests 26` | **26/26** |
| Integration 全量 | `--minimum-expected-tests 85 --timeout 90m` | **85/85**，约 22m 41s |
| 命名合同 | `pnpm test:naming` | **23/23** |
| Naming 聚焦矩阵 | `--filter "NamingExpand\|NamingContract\|NamingPartialRecovery"` | **19/19** |
| 治理 / Skills / Workspace | `pnpm test:governance` / `test:skills` / `test:workspace` | **6/6**、**44** 项、通过 |
| 客户端 | `pnpm test:clients` | **230** 项（contracts 29、uni-app 96、Vue 46、Layui 51、i18n 8 等） |
| 依赖审计 | `pnpm audit:clients`、`dotnet list package --vulnerable` | 无未登记 critical/high；NuGet 无已知漏洞 |
| 仓库卫生 | `git diff --check` | 通过 |
| 迁移恢复边界 | localization Through008、UUID Contract Through009 runner | 6 项先前失败用例已收敛 |

**未纳入本次门禁**：`pnpm test:e2e` / `test:e2e:real` / `test:e2e:uniapp`（需独立环境与时长）；Task 6 Step 2 发布候选备份升级演练；生产维护窗口人工签字。

## 数据库对象最终状态

权威映射：`contracts/naming/pre-v1-name-map.json`（`PreV1NameMapV1`）。011 Contract 后规范对象成为唯一持久化结构；001–009 历史脚本未改写。

| 旧值 | 规范值 | Expand（010） | Contract（011） | 首次兼容写入 | 停止产生旧值 | 最后接受旧值 |
| --- | --- | --- | --- | --- | --- | --- |
| `fn_tenant_tenant` | `fn_tenancy_tenant` | 创建并幂等复制 | DROP legacy 表 | `1.0.0-pre-v1-switch` | `1.0.0-pre-v1-switch` | 仅 010 窗口内只读共存；011 后不存在 |
| `fn_tenant_tenant.CreatedAt` | `fn_tenancy_tenant.CreatedAtUtc` | 镜像列 | legacy 表删除 | 同上 | 同上 | 同上 |
| `fn_tenant_tenant.UpdatedAt` | `fn_tenancy_tenant.UpdatedAtUtc` | 镜像列 | legacy 表删除 | 同上 | 同上 | 同上 |
| `fn_outbox_message.Type` | `MessageType` | 可空镜像列 + 回填 | DROP `Type` | 同上 | 同上 | Handler 仍接受 legacy MessageType 直至排空 |
| `fn_outbox_message.OccurredAt` | `OccurredAtUtc` | 镜像列 | DROP legacy 列 | 同上 | 同上 | 同上 |
| `fn_outbox_message.ProcessedAt` | `ProcessedAtUtc` | 镜像列 | DROP legacy 列 | 同上 | 同上 | 同上 |
| `fn_outbox_message.NextAttemptAt` | `NextAttemptAtUtc` | 镜像列 | DROP legacy 列 | 同上 | 同上 | 同上 |
| `fn_outbox_message.LockedUntil` | `LockedUntilUtc` | 镜像列 | DROP legacy 列 | 同上 | 同上 | 同上 |

**债务清单**：`contracts/naming/naming-debt.json` 自 90 项收敛至 **83** 项；上述 8 项数据库映射对应的登记项已在 011 交付中清除。其余 83 项（动态 SQL、未命名主键、协议别名窗口等）仍保留至各自 `removalMilestone`。

## 协议与稳定机器码最终状态

应用与资源层已切换为规范值；公共别名按 `compatibilityMode` 保留。

| 类别 | 旧值示例 | 规范值示例 | 兼容模式 | 停止产生旧值 | 最后接受旧值 |
| --- | --- | --- | --- | --- | --- |
| Outbox MessageType | `fullnet.tenancy.tenant-provisioned` | `fullnet.tenancy.tenant.provisioned` | `alias_until_drained` | `1.0.0-pre-v1-switch` | Pending 队列排空 + 退役窗口结束前 |
| ErrorCode | `tenancy.domain-exists` | `tenancy.domain_exists` | `client_resource_alias` | `1.0.0-pre-v1-switch` | 客户端资源双键窗口结束前 |
| StatementId | `outbox.acquire.sql-server` | `outbox.acquire.sql_server` | `observability_dual_emit` | `1.0.0-pre-v1-switch` | 观测双发窗口结束前 |

完整枚举见 `pre-v1-name-map.json` 的 `protocol` 数组（**37** 项）。

## Runbook 步骤与自动化证据映射

| Runbook 章节 | 自动化证据 | 说明 |
| --- | --- | --- |
| §3 Go/No-Go | `NamingContract_*_rejects_*` | 维护证据缺失、Tenant 行数不一致、Legacy Pending Outbox 时 011 拒绝 |
| §4.3 010 Expand | `NamingExpand_*`、`NamingPartialRecovery_*` | 双库复制、部分列/表恢复、Journal 未记账重跑 |
| §4.4 应用切换 | 模块 SQL、`Outbox` 路由别名测试（Task 3–4 提交） | 新写入走规范列/表与 MessageType |
| §4.5 011 Contract | `NamingContract_*`、`NamingContractPartialRecovery_*` | 收紧 Outbox 列、删除 legacy 表/列、半完成重收敛 |
| §4 行数/摘要 | Expand/Contract 测试内嵌断言 | Tenant/Outbox 行数、Payload、UTC 列一致性 |

## 恢复演练路径（自动化等价）

下列路径在 Testcontainers 双库环境执行，**不等同**于生产备份介质恢复。

### 路径 A：010 半完成 → Migrator 重收敛

1. 模拟 `fn_tenancy_tenant` 缺失或 Outbox 镜像列部分回填（`NamingPartialRecovery_*`）。
2. 重跑 010；断言单脚本执行且数据收敛。

### 路径 B：011 半完成 → Migrator 重收敛

1. 模拟 legacy Outbox 列已删或 `fn_tenant_tenant` 已 DROP 但 Journal 未记账（`NamingContractPartialRecovery_*`）。
2. 重跑 011；断言规范对象与约束恢复。

### 路径 C：跨阶段恢复边界（011 后不得误用全量 runner）

1. **004 Localization** 恢复：SQL Server `MigrateSqlServerThrough008Async`；MySQL Expand runner 排除 009+。
2. **009 UUID Contract** 恢复：`UuidBinaryContractTestMigrationRunner` 排除 010/011，避免访问已 DROP 的 `fn_tenant_tenant`。
3. 证据：`a7c7439` 聚焦 6 项 + 全量 85/85。

## 未验证项

- 真实生产（或生产等价数据量）维护窗口：冻结发布、备份恢复 RPO/RTO、人工 Go/No-Go。
- **发布候选升级演练**（Task 6 Step 2）：从上一发布版本备份恢复 → 010 → 应用切换 → 011 → 双端冒烟。
- Legacy `MessageType` 与公共 ErrorCode/StatementId 别名 **排空时间与退役签字**。
- Playwright / 真实栈 E2E（`pnpm test:e2e`、`test:e2e:real`、`test:e2e:uniapp`）未在本记录日重跑。

## 关联文档

- [实施计划 Task 5–6](../superpowers/plans/2026-07-18-pre-v1-naming-normalization.md)
- [维护窗口 Runbook](../development/pre-v1-naming-migration-runbook.md)
- [PreV1NameMapV1](../../contracts/naming/pre-v1-name-map.json)
- [命名治理验证记录](naming-governance.md)
- [UUID Binary16 验证记录](uuid-v7-primary-key-storage-2026-07-19.md)
