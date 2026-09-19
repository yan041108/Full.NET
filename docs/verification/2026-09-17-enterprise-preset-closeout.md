# 企业预设收口验收记录（2026-09-17）

> 基线 HEAD：`a6d3d02eb38e6384a123e39f89dfc7bbe8e90699`（开工快照；本批次在其上增量交付）

## 范围

本批次关闭 F08b（席位配额消费者）、F05/F06 集成抽样、F13 Webhook 投递硬化、F16 企业子集证据。不含 F07 Enforced、F12 支付、F09–F11 完整 CRUD、容量认证。

## 交付摘要

| 项 | 证据 |
|----|------|
| F08b ITenantMemberSeatQuotaPort | src/Modules/Full.NET.Modules.Identity.Contracts/ITenantMemberSeatQuotaPort.cs |
| Tenancy Adapter | src/Modules/Full.NET.Modules.Tenancy/Features/ReserveTenantQuota/TenantMemberSeatQuotaPort.cs |
| AcceptInvitation Reserve/Confirm/Release | AcceptTenantInvitationService.cs |
| 架构债务登记 | contracts/architecture/module-local-transaction-debt.json |
| F05 成员邀请集成 | TenantMembershipAssertions.cs |
| F06 Suspend 阻断 | TenantLifecycleAssertions.cs |
| F04 注册 token | ui/admin/src/views/RegisterView.vue |
| F13 Webhook SSRF | WebhookDeliverySsrfGuard.cs |

## 验证矩阵（2026-09-17）

- Release build: 0 error
- ArchitectureTests: 230/230
- UnitTests (TenantMembership|TenantQuota|TenantMemberSeat|WebhookDelivery): 18/18
- foundation-preset-release.test.mjs: 3/3
- enterprise-request schema test: 1/1
- pnpm test:integration:api:sqlserver: 未执行（Docker/Testcontainers 不可用）
- Capacity-not-verified: 保持
## 续做（2026-09-17 下午）

- `TenantMembershipAssertions`：Host 切换租户上下文后发邀请 → 受邀人接受 → `identity.seats` Used +1
- `IntegrationTestTenantContextHelper` + `FullNetApiFactory.EnsureHostUserProfileEmailAsync`
- Webhook 单元：`ProcessPendingAsync_marks_failed_when_target_url_is_blocked`
- `pnpm test:integration:api:sqlserver`：仍因 Docker/Testcontainers 不可用失败（148/148 在容器启动阶段失败）