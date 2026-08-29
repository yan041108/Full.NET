# Realtime 发布能力边界验证

## 范围与基线

- 基线提交：`8802e1ee`
- 日期：2026-08-29
- 范围：`IRealtimePublisher` 的 Group 发布契约、SignalR 发布目标校验与 DI 生命周期。
- 非目标：不改变 Hub 加组授权、客户端消息协议、Redis Backplane、Outbox 重试或用户私有消息语义。

## 变更

- 删除接受任意字符串组名的 `PublishToGroupAsync`，改为 `PublishToTenantAsync` 与 `PublishToHostBroadcastAsync` 两个受限能力。
- 租户广播必须与当前 Scoped 租户上下文精确一致；Host 广播必须处于明确的 Host 上下文，校验失败时不会访问 SignalR Hub。
- `SignalRRealtimePublisher` 从 Singleton 改为 Scoped，避免捕获 Scoped `ICurrentTenant`，并保持 API/Worker 在各自请求或任务作用域内读取同一上下文。
- Notifications 的公告发布改用 Host 广播能力，不再由业务模块构造基础设施 Group 名称。
- Realtime 关闭时的空实现继续静默丢弃消息；由于不会产生实际投递，它不解析或校验租户状态。

## 兼容性说明

这是 pre-v1 安全边界的有意公共 API 收窄。仓库外若调用 `PublishToGroupAsync`，必须按真实目标迁移到租户广播或 Host 广播；不再提供任意 Group 字符串逃生口。稳定消息码、消息 JSON 和实际 `tenant:{id}` / `host:broadcast` 组名保持不变。

## 验证结果

- Realtime Unit：66/66 通过。
- Realtime/Notifications 聚焦 Unit：17/17 通过，覆盖跨租户、上下文缺失、租户态 Host 广播拒绝、合法投递、取消与遥测。
- Realtime/Native AOT 相关 Architecture：87/87 通过。
- `pnpm test:dotnet:architecture -- --selection api-native-aot`：71/71 通过。
- `pnpm test:aot:analyzers`：0 警告、0 错误。
- `pnpm test:aot:worker:analyzers`：AOT 分析与强制 JIT 重建均为 0 警告、0 错误。
- `pnpm test:inner -- --snapshot realtime-publish-capability`：Release 构建 0 警告、0 错误；MySQL Realtime Integration 3/3 通过。
- `pnpm test:aot:publish:linux`：Docker Linux SDK 完成 API `linux-x64` Native AOT 链接，warning gate 接受 9 个既有精确告警；原生可执行文件 72,114,192 bytes。
- `pnpm test:aot:native:notifications:e2e`：Windows 精确发现 2 项，2 项按 Linux-only 规则跳过；不声明本地原生进程交互通过。
- `pnpm test:governance`：52/52 通过。

## 已发现的独立基线缺口

`pnpm test:aot:worker:publish:linux` 没有进入原生代码生成，输出的是 78,256 bytes 的托管 apphost，并被 8 MB 产物门禁正确拒绝。根因是 Worker 项目尚未像 API 项目一样把 `FullNetPublishMode=NativeAot` 映射为 `PublishAot=true`；该问题早于本切片且与 Realtime 契约实现无关，应作为独立 Worker Native AOT 构建切片修复，禁止通过降低产物门槛掩盖。

完整 Architecture 套件另有当前工作区中的 Dapper 依赖扫描、SerialNumbers SQL 声明、错误码和 `node_modules` 目录遍历失败；本切片没有修改或吸收这些并发问题，以任务快照和相关选择集作为影响验证依据。

## 演进检查

本切片落实既有租户隔离、最小能力与 Native AOT 静态闭包规则，没有出现新的规则或 Skill 缺口，不修改规则或 Skill。
