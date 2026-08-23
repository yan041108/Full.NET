# OpenAPI 客户端迁移：Notifications Inbox Messages

**Goal:** 将 `ui/admin/src/api/inbox-messages.ts` 迁入 OpenAPI 生成客户端（`notifications-inbox-messages`）。

**Architecture:** 主 Tag `NotificationsMyInboxMessages` / `NotificationsHostInboxMessages`；手写守卫保留。

**Status:** `Slice-passed`

| operationId | Vue 导出 |
| --- | --- |
| `notificationsListMyInboxMessages` | `listInboxMessages` |
| `notificationsGetMyInboxUnreadCount` | `getInboxUnreadCount` |
| `notificationsMarkMyInboxMessageRead` | `markInboxMessageRead` |
| `notificationsMarkAllMyInboxMessagesRead` | `markAllInboxMessagesRead` |
| `notificationsSendHostInboxMessage` | `sendHostInboxMessage` |

清单 175→180（5 条 pilot）。
