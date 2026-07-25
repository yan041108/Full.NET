# Notifications Host 公告纵向切片

## 范围

- Host 作用域纯文本公告：草稿创建、更新、发布。
- 发布时经 `IRealtimePublisher` 向 `host:broadcast` 推送 `notifications.announcement.published`。
- Vue/Layui 双管理端列表、创建、编辑草稿与发布。

## 非目标

- 富文本、租户公告、站内信、未读数、Vue SignalR 客户端。

## 交付清单

1. [x] 双库迁移 `028_NotificationsAnnouncement.sql`
2. [x] `Full.NET.Modules.Notifications` 模块与 API
3. [x] `RealtimeGroups.HostBroadcast` + Hub 入组
4. [x] Integration **158 → 160**（`Host_announcement_management` SQL Server/MySQL）
5. [x] OpenAPI 夹具 + client-contracts
6. [x] Vue `HostAnnouncementsView` + Layui `host-announcements.js`
7. [x] `shell-parity`「Host 公告列表与创建发布」× 双端 → **56 → 58**
