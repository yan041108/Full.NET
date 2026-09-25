# 执行清单 42 — Host 公告收件与阅读统计 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **42**（`my-host-announcements` 收件列表/未读数/已读；发布方 `read-stats` 与 `read-receipts`；Host 范围闭合，Tenant 公告不在本槽）。

## 交付锚点

| 层 | 位置 |
|----|------|
| 收件 API | `/api/v1/notifications/my-host-announcements`（列表、未读数、`/{id}/read`、`/read-all`） |
| 统计 API | `/api/v1/notifications/host-announcements/{id}/read-stats`、`read-receipts` |
| Vue | `MyHostAnnouncementsView.vue`、`HostAnnouncementsView.vue`（阅读统计对话框） |
| 迁移 | `142` 已读回执表、`143` 统计权限（双库） |
| 集成 | `NotificationsHostAnnouncementAssertions.VerifyReceivedAnnouncementReadLifecycleAsync` |
| 契约 | `notifications-host-announcement-receipts-v1.json` |

## NT01 核验结论

- **已有**：发布后方已读统计与回执分页；收件方未读计数与幂等已读；与清单「我收到的公告、发布方统计」一致。
- **本槽**：新增 real-stack `host-announcements-receipts.spec.mjs`（原 `host-announcements.spec.mjs` 仍覆盖管理列表与 Viewer 403）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~HostAnnouncement`（UnitTests） | **8/8** |
| `pnpm exec vitest run` …`my-host-announcements` + `host-announcements` + `HostAnnouncementsView` | **10/10** |
| real-stack | `host-announcements-receipts.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
