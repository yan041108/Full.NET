# F16 预设发布验收清单

> 状态：企业预设收口批次（2026-09-17）已附证据；见 [closeout](2026-09-17-enterprise-preset-closeout.md)。

## 基础

- [x] `diagnose --profile production` 无 error — 仓库根 `dotnet exec … diagnose --workspace .` 为 warn（非应用模板）；应用模板场景见 `create-first-crud.md`
- [x] SQL Server 与 MySQL 迁移成对应用 — 221–227 成对 SQL；集成断言覆盖 SqlServer/MySql 测试类
- [x] Minimal / Platform / Full 预设模块闭包与 `FullNetModuleSelection` 一致 — `foundation-preset-release.test.mjs` 通过

## 功能抽样

- [x] 租户订阅 API（Tenancy ManageTenantSubscriptions）— `TenantSubscriptionAssertions` + `TenancyApiSqlServerTests`
- [x] Webhook 订阅 API（Webhooks ManageWebhookSubscriptions）— `WebhookDeliveryAssertions` + 单元投递测
- [x] Enterprise Request 样例 Schema 测试通过 — `samples/enterprise-request/tests/schema.test.mjs`
- [x] 管理端 `/tenancy/tenant-subscriptions` 路由可加载 — `ui/admin/src/router/index.ts`（既有）

## 部署

- [ ] Helm 三角色 overlay 单角色启用 — 未在本批次 spot-check
- [x] `tests/deployment/foundation-preset-release.test.mjs` 通过
- [ ] Native Api AOT 发布路径已验证（若本次发布包含）— 未执行

## 文档

- [x] `docs/operations/application-upgrade.md` — 已存在
- [x] `docs/operations/application-recovery.md` — 已存在
- [x] `docs/development/create-first-crud.md` 实走记录 — 见下文 2026-09-17 记录

## SaaS 子集（2026-09-17）

> 证据：[saas-preset-closeout](2026-09-17-saas-preset-closeout.md)

- [x] `FullNet:Modules:Preset=Saas` 闭包含 Payments + Webhooks（`SaasPresetModuleNames` + 模板 `saas`）
- [x] F12 运营 MVP：Host 创建试用/取消订阅 + `TenantPackageId` 联动（`TenantSubscriptionAssertions`；Docker 集成未跑）
- [x] F07 SaaS 切片：Preset=Saas 默认 Enforced 引导 + Enforced 下无套餐/订阅 reactivate 门禁
- [x] 管理端 Host 订阅/权益阶段可操作或可读（`TenantsView` / `TenantPackagesView`）
- [x] `ITenantSubscriptionPaymentFulfillmentPort` 占位（无真实渠道验收）

## 未验证声明

- 容量与 SLO：保持 `Capacity-not-verified` 直至有等价生产证据
- Docker API 集成分片：本地未跑；依赖 CI Testcontainers
- F12 真实支付 Provider / 回调对账：明确排除于本批次

## create-first-crud 实走记录（2026-09-17）

```text
命令：dotnet exec src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll diagnose --workspace .
结果：exit 0；DIAG_WORKSPACE_OK / DIAG_SDK_OK；连接串与 FullNet:Modules 为占位 warn（符合仓库根诊断预期）
备注：Enterprise 预设与 F09–F11 子集见 `docs/verification/2026-09-17-enterprise-business-integration-closeout.md`；Reporting/Printing 全量集成未验。
```
