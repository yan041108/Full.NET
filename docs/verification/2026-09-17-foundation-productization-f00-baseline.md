# F00 底座完善基线核对（2026-09-17）

> 任务 ID：`foundation-f00`
> 核对提交：`a6d3d02eb38e6384a123e39f89dfc7bbe8e90699`（`main`）
> 总计划：[2026-09-16-foundation-productization.md](../superpowers/plans/2026-09-16-foundation-productization.md)

## 1. 资产核对表（F01–F16）

| 任务 | 状态 | 现有资产 | 增量 |
| --- | --- | --- | --- |
| F01 应用创建 | MISSING | Composition 预设、Hosts、CodeGeneration.Cli | templates/fullnet-app、scripts/templates、tests/templates |
| F02 诊断与 CRUD | PARTIAL | CodeGeneration.Cli、Settings 诊断策略 | 应用级 diagnose 命令、create→CRUD 教程 |
| F03 验证挑战 | PARTIAL | Notifications VerifyRecipientEndpoints | Identity 挑战 + SendIdentityChallenge Port |
| F04 注册恢复 | PARTIAL | ManageRegistrationPolicy（布尔） | 三态政策、RegisterAccount/RecoverAccount/RegistrationInvitations |
| F05 企业成员 | PARTIAL | ProvisionTenant、ManageHostTenants | 多步编排、邀请/接受、ManageTenantMembers |
| F06 企业生命周期 | MISSING | 租户 enable/disable | 状态机、所有者交接、成员退出 |
| F07 套餐权益 | PARTIAL | fn_tenancy_tenant_package | ManageTenantEntitlements、三阶段切换 |
| F08 统一配额 | PARTIAL | AI 配额预留 | ReserveTenantQuota、席位与存储消费者 |
| F09 业务单据 | MISSING | Codegen 夹具 | samples/enterprise-request |
| F10 审批通知 | PARTIAL | Workflow 模块 | 样板端到端 |
| F11 导入报表打印 | PARTIAL | 三模块已存在 | 样板接入 |
| F12 订阅运营 | PARTIAL | Payments | ManageTenantSubscriptions |
| F13 Webhook | MISSING | Outbox 模式 | Full.NET.Modules.Webhooks |
| F14 SDK 样例 | PARTIAL | client-contracts | samples/webhook-consumer |
| F15 升级恢复 | PARTIAL | Helm、框架 runbook | application-upgrade.md |
| F16 预设验收 | PARTIAL | deployment 测试 | 按预设发布门禁 |

## 2. C01–C07 门禁矩阵

| 编号 | 阻塞切片 | 当前重点 |
| --- | --- | --- |
| C01 OIDC | F04、F06、F14 | P0 双库/Native/Vue |
| C02 数据交付 | F11 | 租户谓词、Worker 恢复 |
| C03 Workflow | F10 | 业务键回写 |
| C04 Notifications | F03、F05、F10 | 邮件闭环 |
| C05 权限 | 全部新 Endpoint | 数据与字段权限一致 |
| C06 AI | F16 选装 | 不阻塞核心预设 |
| C07 生产运行 | F15、F16 | 升级与恢复 |

## 3. 首批契约登记

见 `templates/fullnet-app/framework-manifest.schema.json` 与 `scripts/templates/preset-modules.mjs`。

模板参数：`--name`、`--owner-key`、`--database`（sqlserver|mysql）、`--preset`（minimal|platform）、`--http-port`。

Composition 投影输出 `FullNet:Modules:Preset` 及 Host ProjectReference 闭包。

## 4. 复用 vs 演进

- 注册政策：布尔 → Disabled/InvitationOnly/Open，默认 InvitationOnly
- 通知：VerifyRecipientEndpoints 不授权密码恢复；F03 独立挑战 Port
- 套餐：扩展 fn_tenancy_tenant_package，不新建平行库
- 配额：Tenancy 拥有 ReserveTenantQuota；AI 账本不双扣
- 开通：扩展 ProvisionTenant 编排

## 5. 最小可发布预设

首版选定 **Minimal**（基础管理）；企业/SaaS 预设待 F03–F12 切片关闭后提升。

## 6. F00 验收

- B01–B08 可定位资产与增量
- C01–C07 已映射
- test-matrix 已登记 foundation-f00 … foundation-f16
