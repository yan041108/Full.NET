# 1.0 前命名规范化维护窗口 Runbook

## 1. 目的与边界

本 Runbook 约束 Full.NET 在 009 UUID Contract 完成后，将 Tenancy 表、Outbox 时间列/MessageType 与连字符协议值迁移为规范命名的维护窗口。机器清单以 [`PreV1NameMapV1`](../../contracts/naming/pre-v1-name-map.json) 为准；**010** 只执行 Expand（新增 `fn_tenancy_tenant` 与 Outbox 规范镜像列），**011** 才执行不可兼容的 Contract（切换 canonical 列/表并退役 legacy 对象）。

本 Runbook 不是生产发布授权。仓库在合入 010/011 迁移与应用切换前，实际窗口仍须由发布负责人依据 Go/No-Go 证据单独批准。

## 2. 角色与必备证据

- 目标提交、应用版本、数据库实例与 DbUp Journal（须已完成 **009**）；
- `PreV1NameMapV1` 与 `pnpm test:naming` 通过记录；
- 生产等价备份及独立环境恢复成功记录；
- 010/011 双库集成测试与半完成恢复测试记录；
- 未处理 Outbox 行数、Pending lease 与 legacy `MessageType` 分布基线；
- 已批准的数据变更豁免标识、维护窗口起止时间与业务通知。

证据不得包含连接串、Secret、Token、Payload 或业务数据样本。

## 3. Go/No-Go 门禁

只有以下条件全部为“是”才允许开始：

1. **009** 已在目标环境完成且 `fn_uuid_contract_state.SchemaMode = 'Binary16'`（MySQL）；
2. 迁移编号已冻结，**010/011** 未被其他分支占用；
3. 备份已完成，且恢复演练证明可在目标 RTO/RPO 内恢复；
4. API、Worker、Migrator、导入器与第三方直写工具均能在窗口内停止；
5. 在途事务可排空，Outbox lease 与 Seed 锁超时已知；
6. `fn_tenant_tenant` 与 `fn_outbox_message` 行数、关键字段摘要与 Pending 计数已记录；
7. 新应用镜像、旧应用镜像与数据库恢复介质均可用；
8. Vue/Layui 双端冒烟与回退负责人已就位。

任一条件为“否”或“未知”都必须 No-Go。

## 4. 维护窗口执行顺序

### 4.1 冻结与备份

1. 冻结迁移与结构变更；记录目标提交与 DbUp Journal。
2. 创建一致性备份，并在隔离环境恢复验证。
3. 记录 Tenant/Outbox 聚合行数与 Pending Outbox 计数。

### 4.2 停止写入并排空

1. 摘除 API 流量，停止 Worker、Migrator 与所有写入者。
2. 等待在途事务与 Outbox lease 超过最大有效期。
3. 通过数据库连接与锁确认无残留写入者。

### 4.3 执行或核对 010 Expand

1. 若 010 尚未执行，运行 Migrator；若已执行，核对 Journal 与真实对象一致。
2. 验证 `fn_tenancy_tenant` 已创建且自 `fn_tenant_tenant` 幂等复制完成。
3. 验证 Outbox 规范镜像列（`MessageType`、`OccurredAtUtc` 等）已回填且与 legacy 列一致。
4. 发现行数不一致、冲突或孤立引用时立即停止。

### 4.4 部署应用切换（Expand 后、Contract 前）

1. 部署只写入规范列/新表、但仍能读取 legacy 列的应用版本。
2. 验证新 Tenant 写入 `fn_tenancy_tenant`；Outbox 新消息填充 `MessageType` 与 `*Utc` 列。
3. 验证 Worker 同时路由 legacy 与 canonical `MessageType`（见 `PreV1NameMapV1.protocol`）。
4. 执行双库冒烟与 Vue/Layui 关键流程。

### 4.5 执行 011 Contract

1. 再次确认写入者停止、备份有效、批准编号已登记。
2. 在 Migrator 配置中设置 `PreV1NamingContract` 全部门禁为 `true`，并填写已批准的 `DestructiveDdlApprovalId`（格式见 `UuidBinaryContractOptions.IsApprovalIdValid`）。
3. 运行 Migrator 执行 011；不得手工拆分。
4. 验证 `fn_pre_v1_naming_contract_state.SchemaMode = 'Contracted'`；`fn_tenant_tenant` 与 Outbox legacy 列已删除；canonical 列/索引为 NOT NULL。
5. 部署最终应用并恢复流量；持续监控 Outbox backlog 与错误率。

## 5. 停止与回退边界

### 5.1 011 之前

010 只增加新表/镜像列，legacy 表/列仍为 canonical。失败时可停止写入，在核对 legacy 完整性后删除 Expand 对象并回退应用版本，**不要求**整库恢复。

### 5.2 011 开始之后

011 会切换 canonical 并删除 legacy 对象。此后禁止只回滚应用。失败时必须：

1. 保持流量与写入关闭；
2. 保存迁移日志与聚合诊断；
3. 恢复维护窗口前已验证的数据库备份；
4. 部署与该备份匹配的旧应用；
5. 完成双库冒烟后再决定是否恢复流量。

## 6. 完成记录

记录目标提交、数据库版本、开始/结束时间、Tenant/Outbox 聚合核对、Pending 排空结果、双端冒烟、RPO/RTO 与异常处置。只有发布计划要求的证据全部存在，命名规范化能力才可从 `Implemented` 向 `Build-verified` / `Verified` 提升。

## 关联文档

- [PreV1NameMapV1 合同](../../contracts/naming/pre-v1-name-map.json)
- [1.0 前命名规范化实施计划](../superpowers/plans/2026-07-18-pre-v1-naming-normalization.md)
- [UUID Binary16 迁移 Runbook](uuid-binary-migration-runbook.md)（009 前置）
