# UUID v7 主键存储验证记录

- 日期：2026-07-19
- 类型：自动化恢复演练与 Runbook 映射
- 状态：已完成（自动化证据）；生产等价维护窗口未实跑
- 代码基线：`55e82d4`（`docs: mark uuid task 6 plan steps complete`）
- 范围：MySQL `BINARY(16)` 008/009 迁移、半完成恢复、009 门禁、应用连接模式、SQL Server 聚集索引显式治理、Task 5–6 应用/生成器治理，以及 Task 7 发布门禁
- 方法：Release 构建、Testcontainers（SQL Server 2022 / MySQL 8.0）、`pnpm test:uuid-storage`、Integration 聚焦 filter

## 声明结论

- 仓库内 **自动化** 恢复矩阵与 [MySQL UUID Binary16 迁移维护窗口 Runbook](../development/uuid-binary-migration-runbook.md) 步骤已对齐，可作为发布前演练脚本与 Go/No-Go 核对清单的技术依据。
- **不能** 将 UUID 能力整体提升为 `Verified`：真实生产维护窗口、备份恢复 RPO/RTO 实测、SQL Server 页分裂/碎片率与执行计划基准尚未完成。
- 能力矩阵保持 `Build-verified`；缺口见 [capability-status.md](../roadmap/capability-status.md)。

## 新鲜自动验证

环境：Windows 10、.NET SDK `10.0.400-preview.0.26322.102`、Docker Desktop（Linux containers）、Testcontainers。

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| UUID 存储合同 | `pnpm test:uuid-storage` | **3/3** 通过 |
| 迁移/恢复矩阵 | `--filter "FullyQualifiedName~UuidBinary"`，`--minimum-expected-tests 25` | **31/31** 通过，约 17m 22s |
| 应用持久化与契约 | 见 [test-threshold-audit-2026-07-19.md](test-threshold-audit-2026-07-19.md) | Unit **304/304**；Integration **66/66**（约 26m） |

## Task 7 发布门禁（2026-07-19，基线 `55e82d4`）

| 验证 | 结果 |
| --- | --- |
| `dotnet build Full.NET.slnx -c Release` | 0 警告、0 错误 |
| Unit / Compatibility / Architecture | **304/6/26** 通过 |
| Integration（Testcontainers 双库） | **66/66** 通过，26m 06s |
| `pnpm test:naming` | **20/20** |
| `pnpm test:governance` | **6/6** |
| `pnpm test:skills` | **44/44** |
| `pnpm test:workspace` | 通过 |
| `pnpm test:clients` | 通过（含 client-contracts、Vue、Layui、uni-app **96+51+46** 等） |
| `pnpm audit:clients` | 无未登记 critical/high |
| `dotnet list package --vulnerable` | 无已知漏洞 |
| `git diff --check` | 通过 |

**未纳入本次门禁**：`pnpm test:e2e`、`pnpm test:e2e:real`、`pnpm test:e2e:uniapp`（需独立环境/时长，由 CI `real-stack-e2e` 等作业覆盖）。**不能**据此将 UUID 提升为 `Verified`。

## Runbook 步骤与自动化证据映射

| Runbook 章节 | 自动化证据 | 说明 |
| --- | --- | --- |
| §2 合同与清单 | `tests/database/uuid-storage-contract.test.mjs`、`contracts/database/uuid-storage-v1.json` | 23 列登记、固定字节序向量、008/009 编号冻结 |
| §4.3 008 Expand 与核对 | `UuidBinaryExpandMigrationTests`、`UuidBinaryPartialRecoveryTests` | 存量 Identity/Outbox/Seed 图回填；影子列/触发器/索引半完成重跑；非法 UUID、重复、孤立引用拒绝 |
| §4.3 只读核对 SQL | `docs/development/sql/uuid-binary-expand-verification.mysql.sql` | 聚合行数、Distinct、往返、固定抽样 SHA-256；不输出业务 UUID |
| §4.4 009 Contract 门禁 | `UuidBinaryContractMigrationTests` | `MaintenanceMode`/`BackupVerified`/`LegacyWritersStopped`/`DestructiveDdlApprovalId` 缺失或 008 未完成时拒绝 |
| §4.5 应用 Binary16 部署 | `UuidBinaryContractRecoveryTests`（schema mode mismatch）、`GuidPrimaryKeyApplicationTests`、`GuidPrimaryKeyReadPathTests` | API/Worker 模式与 `fn_uuid_contract_state.SchemaMode` 不一致时启动失败；业务层仅 `Guid` |
| §5.1 009 前回退边界 | `UuidBinaryPartialRecoveryTests`（`DropExpandObjectsAsync` 后重跑 008） | 008 仅影子对象；删除影子后可继续 Legacy 应用（测试通过 Migrator 重收敛，非生产备份） |
| §5.2 009 后回退边界 | `UuidBinaryContractRecoveryTests`（部分约束删除、部分列重命名后重跑 009） | 证明半完成 009 可重收敛；**未**在本文档中执行真实备份恢复——生产必须按 Runbook §5.2 恢复备份 |
| SQL Server 聚集索引 | `UuidBinaryContract_SqlServer_governs_explicit_clustered_indexes` | Outbox、Auth Audit 为 `NONCLUSTERED` PK + `(OccurredAt*, Id)` 显式 `CLUSTERED` 索引 |

## 恢复演练路径（自动化等价）

下列路径在 Testcontainers 双库环境执行，含真实关系数据（用户/角色/会话/审计/Outbox/Seed），不等同于生产备份介质恢复。

### 路径 A：008 半完成 → Migrator 重收敛（009 前）

1. 执行 001→007 并写入 Identity/Outbox/Seed 图（`UuidBinaryPartialRecovery_existing_identity_outbox_and_seed_graph_is_backfilled`）。
2. 模拟影子列缺失、部分回填、触发器缺失或 DbUp 未记账（`missing_shadow_column`、`partial_backfill`、`missing_triggers`、`unjournaled_complete_expand`）。
3. 重跑 Migrator 008；断言单脚本执行且影子/触发器/引用恢复一致。

**结论**：009 前可通过删除影子对象并重跑 008 收敛；与 Runbook §5.1 一致，不要求数据库整库恢复。

### 路径 B：009 半完成 → Migrator 重收敛（009 后局部失败）

1. 完成 008 后执行 009；模拟外键/主键删除或列重命名中途失败（MySQL `recovers_partial_constraint_deletion`、`recovers_partial_column_rename`；SQL Server `recovers_unjournaled_index_state`）。
2. 删除 009 Journal 记账后重跑 Migrator；断言约束/索引恢复。

**结论**：已测试的 009 恢复路径可在 **停止写入** 前提下由 Migrator 重收敛；**不能**替代 Runbook §5.2 的整库备份恢复承诺。

### 路径 C：应用与 Schema 模式不一致

1. 数据库处于 Expand 或 Contract 状态，应用配置 `Binary16` 或 `LegacyChar36` 与库不一致。
2. `AddFullNetDatabaseSchemaModeGuard` 在 Host 启动时失败；Production 显式 `LegacyChar36` 由 Options 验证拒绝。

**结论**：禁止仅靠改连接串回退；009 后 Legacy 应用无法安全运行。

## SQL Server 聚集索引证据（当前范围）

`009_UuidBinaryContract.sql` 显式声明：

- `fn_outbox_message`、`fn_identity_auth_audit`：UUID 主键 **NONCLUSTERED**
- `IX_fn_outbox_message_OccurredAt_Id`、`IX_fn_identity_auth_audit_OccurredAtUtc_Id`：**CLUSTERED**
- 其余低写入实体表：UUID 主键 **CLUSTERED**（租户、用户、会话、角色、Seed 等）

集成测试 `UuidBinaryContract_SqlServer_governs_explicit_clustered_indexes` 验证上述 2+2 对象存在且类型正确。

**未验证**：页分裂率、碎片率、典型写入/清理查询的执行计划与基准对比（ADR-0003 §3.5 要求的性能证据）。

## 未验证项

- 真实生产（或生产等价数据量）维护窗口：冻结发布、业务通知、备份恢复 RPO/RTO 计时、人工 Go/No-Go 签字。
- 009 后 **整库备份恢复** 与旧应用镜像回退（仅验证 Migrator 半完成重收敛，未挂载备份文件）。
- SQL Server 高写入表聚集索引 **性能** 基准与执行计划证据。
- 真实栈 E2E（`pnpm test:e2e:real` / `mysql`）与浏览器 E2E 未在本记录 Task 7 门禁中重跑；最近证据见 [test-threshold-audit-2026-07-19.md](test-threshold-audit-2026-07-19.md) `9760590` 增补（各 **16/16**）。

## 关联文档

- [ADR-0003：UUID v7 主键存储](../architecture/adr/ADR-0003-uuid-v7-primary-key-storage.md)
- [实施计划 Task 5–7](../superpowers/plans/2026-07-18-uuid-v7-primary-key-storage.md)
- [MySQL Binary16 迁移 Runbook](../development/uuid-binary-migration-runbook.md)
- [测试门槛核对记录](test-threshold-audit-2026-07-19.md)
- [当前能力状态矩阵](../roadmap/capability-status.md)
