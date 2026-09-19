# SaaS 预设收口验收记录（2026-09-17）

> 基线 HEAD：`a6d3d02eb38e6384a123e39f89dfc7bbe8e90699`（开工快照；本批次在其上增量交付）

## 三预设叙事

| 预设 | 含义 | 证据 |
|------|------|------|
| Minimal | 基础管理底座 | 模板 `minimal`、F00 B01 |
| Enterprise | Platform + 成员/配额/Webhook 抽样 | [enterprise-preset-closeout](2026-09-17-enterprise-preset-closeout.md) |
| SaaS | Platform + Payments + Webhooks + 订阅/权益门禁 | 本文件 |

## 交付摘要

| 项 | 位置 |
|----|------|
| `SaasPresetModuleNames` / `Presets.Saas` | `FullNetModuleSelection.cs`、`FullNetModuleSelectionOptions.cs` |
| 模板 `saas` | `preset-modules.mjs`、`template.json` |
| 订阅创建同步 `TenantPackageId` | `TenantHostPackageBinder` + `TenantSubscriptionManagementService` |
| F07 SaaS 切片 | `TenancyCommercialOptions`、reactivate 门禁、`TenancySaasDefaultsBootstrapHostedService` |
| 支付履约占位 Port | `ITenantSubscriptionPaymentFulfillmentPort` + `NullTenantSubscriptionPaymentFulfillmentPort`（Payments 真实渠道后续切片） |
| Vue | `TenantPackagesView` 权益阶段、`TenantsView` Host 订阅操作 |

## 验证矩阵（2026-09-17）

- Release build: 0 error
- ArchitectureTests: 230/230
- UnitTests (`TenantSubscription|TenantEntitlement|FullNetModuleSelection`): 12/12
- `foundation-preset-release.test.mjs`: 4/4（含 Saas 闭包断言）
- `tests/templates/*.test.mjs`: 通过
- `pnpm test:integration:api:sqlserver`: 未通过（2026-09-17 复试：`docker info` 有客户端，Testcontainers 连接 `npipe://./pipe/docker_engine` 超时，150/150 在容器启动阶段失败）
- 真实支付 Provider / 回调对账：明确排除
- Capacity-not-verified: 保持