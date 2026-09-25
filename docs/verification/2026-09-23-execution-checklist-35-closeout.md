# 执行清单 35 — 个人日程 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **35**（当前用户个人日程 CRUD、状态切换、时间范围列表；提醒集成留后续 Notifications）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `/api/v1/calendar/my-personal-schedules`（列表含 `fromUtc`/`toUtc`/`status`） |
| 模块 | `Full.NET.Modules.Calendar` |
| Vue | `PersonalSchedulesView.vue`（`#/calendar/personal-schedules`） |
| 单元 | `PersonalScheduleManagementServiceTests` |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`PersonalScheduleManagementServiceTests` | 校验与状态/区间边界 |
| real-stack | `tenant-personal-schedules.spec.mjs`（租户上下文 API + Vue） |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
