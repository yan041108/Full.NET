# Notifications Profile / Binding 验证（2026-08-31）

- **任务：** P1 统一消息中心 Task 5（多 Profile 与显式 Binding 控制面）
- **基线：** `4edd1718c3713a3da2f0f5236ac4cfb4e6a4582e`（`main`）
- **快照：** `notifications-profile-binding-20260831`
- **范围：** 同 ProviderType 多 Profile、Enabled 不自动 FanOut、SecretReference 只返回 `configured`/`not-configured`、Host 默认不共享、Binding 发布固定 ProfileVersion 与 CAS Revision、Intent 固定 BindingVersion。测试 DI 仅登记 `TestNotificationProvider`（渠道键 `test`）。不含 Delivery Worker、Attempt/Receipt、真实外部 Provider、新 Vue 管理页。未新建迁移 106（沿用 104 表）。

## 证据

| 命令 | 结果 |
|---|---|
| Notifications Unit（含空目录 + Stub Adapter Catalog 2 项） | **40/40** |
| Architecture `FullyQualifiedName~Notifications`（含生产程序集不含 Test Provider） | **6/6** |
| `pnpm test:naming` | **30/30** |
| OpenAPI 夹具 `渠道配置与绑定...一致` | **1/1** |
| `pnpm test:inner -- --snapshot notifications-profile-binding-20260831` | 本阶段执行 **none**（inner 只跑 immediate + `migration-*`；本切片无新迁移，合法） |
| `pnpm test:slice -- --snapshot notifications-profile-binding-20260831` | **2/2 通过**（SQL Server + MySQL Notifications API） |

slice 覆盖：无 create 权限 403、GET provider-types 含 `test.notification`、未知类型 `smtp.mailgun` 400、`nonSecretConfig.apiToken` 400 且错误体不含 token、同类型两 Profile 201 且启用后不调用 Adapter（Task 6 起非 inbox Intent 会写入 `accepted` Delivery、Attempt=0）、PUT SecretReference 后 GET `secretStatus=configured` 且响应不含引用、后续仅更新非密钥配置并发送 `secretReference=null` 时保留已配置引用、Binding 发布固定 ProfileVersion、过期 Version 409、同 Producer/Scene/Channel 再发布 409 `binding_scene_conflict`、`test` 渠道模板 + Intent 201 且 `BindingVersionId` 已钉、Disable 后新 Intent 422 `route.profile_unavailable`、旧 Intent GET 版本不变、租户建 Profile 201 且 Host GET 404、租户 Binding 引用 Host `profileKey` 发布 404 `provider_profile_not_found`。

接管复核已运行完整 `pnpm test:openapi`，结果 **122/122**，并将 Profile 发布收紧为独立 `notifications.provider_profiles.publish` 权限。未跑完整 `pnpm test:sql-safety`（无新迁移；历史 009/011/051/093 豁免行号偏差仍在）。

## 结论

- Profile / Binding 控制面达到与当前切片相称的 **Build-verified**。Template/Intent（仅 inbox）、Host 公告/Inbox 与 Tenant Inbox 既有范围不降级，也不升 `Verified`。
- 生产 `NotificationProviderTypeCatalog` 允许为空；未知 ProviderType 由 API 失败关闭。`TestNotificationProvider` 只存在于 IntegrationTests，`SendAsync` 未被 Task 5 调用。
- Profile Disable 只阻止新路由。Task 6 起非 inbox Intent 会写入 `accepted` Delivery 行但不调用 Adapter；排空/切换与 Worker 领取见[Delivery Worker 验证](2026-08-31-notifications-delivery-worker.md)。
- 容量继续 `Capacity-not-verified`。本机 Windows 不把 Linux Native AOT publish 标为通过。未改 Layui。未实现 Vue 管理页（导航占位仍指向既有 Placeholder）。
- 本任务未触发规则或 Skill 演进。
