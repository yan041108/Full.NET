# Realtime 基础纵向切片实施计划

> **For agents:** 基础设施切片，非业务模块；遵循架构 §17 与 `IRealtimePublisher` 边界。

- 建立日期：2026-07-26
- 状态：**Build-verified**
- 验证：[验证记录](../../verification/realtime-signalr-foundation-2026-07-26.md)

**Goal:** 交付 `IRealtimePublisher` 抽象、SignalR Hub（JWT 鉴权 + 用户/租户分组）、MessagePack 协议、可选 Redis Backplane 与 Testing 探针。

**Architecture:** `Full.NET.Realtime.Abstractions` + `Full.NET.Realtime.SignalR`；业务模块只依赖发布器；Hub 路径 `/hubs/notifications`。

**Tech Stack:** ASP.NET Core SignalR、MessagePack Hub Protocol、StackExchange Redis Backplane（可选）。

---

## 范围与非目标

### 必须交付

1. [x] `IRealtimePublisher`、`RealtimeMessage`、`RealtimeGroups`
2. [x] `FullNetNotificationHub` + `SignalRRealtimePublisher`
3. [x] JWT `access_token` 查询字符串 + Bearer 协商
4. [x] API Host 注册 `AddFullNetRealtimeSignalR` / `MapFullNetRealtime`
5. [x] Integration 双库 Hub 协商 + Testing 探针 **156 → 158**
6. [x] Architecture：业务模块禁止直接依赖 SignalR **38 → 40**

### 非目标

- Notifications 公告/站内信模块、Vue/Layui SignalR 客户端、Redis Backplane 双实例 E2E
- Outbox 触发推送、Presence Store、标记 `Verified`

---

## 任务分解

### Task 1: 抽象与 SignalR 实现

1. [x] Abstractions 项目与空发布器
2. [x] Hub、发布器、JWT 扩展、DI 扩展

### Task 2: 宿主接线与测试

1. [x] API `Program.cs` 注册
2. [x] `RealtimeApiAssertions` SQL Server/MySQL
3. [x] 门槛与验证记录
