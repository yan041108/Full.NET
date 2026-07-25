# Notifications 站内信收件箱纵向切片

## 范围

- Host 管理员向指定 Host 用户发送纯文本站内信。
- 当前用户收件箱：分页列表、未读计数、单条已读、全部已读。
- 送达与未读数变更经 `IRealtimePublisher` 推送到用户私有组。

## 非目标

- 租户收件箱、富文本、通知模板、多渠道投递、顶栏 SignalR 客户端接线。

## 交付清单

1. [x] 双库迁移 `029_NotificationsInboxMessage.sql`
2. [x] API：`/my-inbox-messages` + `/host-inbox-messages`
3. [x] 扩展现有 Integration 双库用例（门槛仍为 **160**）
4. [x] OpenAPI 夹具 + client-contracts
5. [x] Vue `InboxMessagesView` + Layui `inbox-messages.js`
6. [x] `shell-parity`「消息中心列表与发信」× 双端 → **58 → 60**
