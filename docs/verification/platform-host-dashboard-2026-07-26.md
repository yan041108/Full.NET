# Platform Host 工作台汇总验证（2026-07-26）

## 摘要

交付 Host 工作台汇总 API，聚合租户、在线会话、访问日志与操作日志；双管理端 Overview 展示真实指标与最近活动。

| 维度 | 结果 |
| --- | --- |
| API | `GET /api/v1/platform/host-dashboard-summary` |
| 权限 | `platform.dashboard.read` |
| Integration 双库 | `Host_dashboard_summary` SQL Server/MySQL **2/2** → **162 → 164** |
| OpenAPI | `platform-host-dashboard-v1.json` |
| client-contracts | `platform-dashboard.ts` + Vitest |
| 双端 UI | `OverviewView.vue` + `overview-dashboard.js` |
| 四处 canonical 门槛 | **351/7/40/164** |

## 关联

- [实施计划](../superpowers/plans/2026-07-26-platform-host-dashboard-vertical-slice.md)
- [Admin.NET 对标矩阵](../roadmap/adminnet-feature-parity.md)
