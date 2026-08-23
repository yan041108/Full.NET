# OpenAPI 客户端迁移：Notifications Host Announcements

**Goal:** 将 `ui/admin/src/api/host-announcements.ts` 迁入 OpenAPI 生成客户端（`notifications-host-announcements`）。

**Architecture:** 主 Tag `NotificationsHostAnnouncements`；手写守卫保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-notifications-host-announcements-2026-08-22.md`](../verification/openapi-client-notifications-host-announcements-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `notificationsListHostAnnouncements` | `listHostAnnouncements` |
| `notificationsCreateHostAnnouncement` | `createHostAnnouncement` |
| `notificationsUpdateHostAnnouncement` | `updateHostAnnouncement` |
| `notificationsPublishHostAnnouncement` | `publishHostAnnouncement` |

清单 171→175（4 条 pilot）。
