# Notifications 平台内核验证（2026-08-31）

- **任务：** P1 统一消息中心平台内核（计划 Task 1 + Task 2）
- **基线：** `4edd1718c3713a3da2f0f5236ac4cfb4e6a4582e`（`main`）
- **快照：** `notifications-platform-kernel-20260831`
- **范围：** 政策/路由/状态机、独立权限码、成对迁移 `104_NotificationsPlatformExtension.sql`（14 张平台表）、静态 SQL/AOT 物化器、导航白名单占位。不含 Tenant Inbox、Template/Intent API、多 Profile 控制面、Delivery Worker、真实 Provider。

## 证据

| 命令 | 结果 |
|---|---|
| 平台 Unit（政策/路由/状态机/权限） | **15/15** |
| Architecture（104 表契约 + Notifications AOT 参数/物化器） | **4/4** |
| `pnpm test:naming` | **30/30** |
| `pnpm test:inner -- --snapshot notifications-platform-kernel-20260831` | 通过；inner 执行 integration-matrix + 104 MySQL 恢复 |
| `pnpm test:slice -- --snapshot notifications-platform-kernel-20260831` | 发现 34 项，**32/34**；失败 2 项为 Identity SQL Server 并发（会话切换竞态、资料唯一性死锁 1205）。隔离重跑这 2 项 **2/2 通过**。Notifications 与 104 双库恢复均在 32 项成功集内。 |
| 104 SQL 安全扫描 | **0** 条 104 违规；完整 `pnpm test:sql-safety` 仍报告 18 条历史迁移豁免行号偏差（009/011/051/093），本任务未修改这些文件 |
| 客户端导航/i18n | `@fullnet/client-contracts` 导航 3/3、`@fullnet/admin-i18n` 8/8、`ui/admin` catalog 3/3 |

## 结论

- 平台内核达到与当前切片相称的 **Build-verified**。Host 公告/Inbox 既有范围不降级，也不升 `Verified`。
- 生产 Provider 目录保持为空。容量继续 `Capacity-not-verified`。
- Vue 仅登记路由/权限占位页，完整管理后台属于计划 Task 7。
- 本任务未触发规则或 Skill 演进。
