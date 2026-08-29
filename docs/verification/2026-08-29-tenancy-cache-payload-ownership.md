# Tenancy 缓存载荷归属修正验证

## 范围与基线

- 基线提交：`c0060021`
- 日期：2026-08-29
- 范围：租户解析 HybridCache L2 的 `TenantCachePayload` 与 `TenantResolutionCacheEntry` 类型归属。
- 非目标：不修改缓存键、TTL、FusionCache/HybridCache 行为、Redis 数据格式、租户解析语义或数据库访问。

## 变更

- 从最底层 `Full.NET.Abstractions` 删除两个 Tenancy 专属业务缓存形状。
- 在 `Full.NET.Modules.Tenancy.Persistence` 内以 internal 类型承载同一字段结构。
- `TenancyJsonSerializerContext` 继续显式登记两种类型，保持 Native AOT 源生成闭包。
- Architecture Test 要求两个类型只存在于 Tenancy 程序集且不得公开。
- Unit Test 用既有 camelCase L2 JSON 字节反序列化新类型，锁定 namespace/可见性调整不改变 Redis 载荷契约。

## 验证结果

- `FusionCacheRegistrationTests`：8/8 通过。
- 缓存归属、源生成 Context 与 BuildingBlocks 依赖聚焦 Architecture：3/3 通过。
- `pnpm test:aot:analyzers`：0 警告、0 错误。
- `pnpm test:dotnet:architecture --selection api-native-aot`：71/71 通过。
- `pnpm test:inner -- --base c0060021`：Release 构建 0 警告、0 错误；MySQL smoke/Tenancy Integration 8/8 通过。
- `pnpm test:aot:publish:linux`：Docker Linux SDK 完成真实 `linux-x64` Native AOT 链接，9 个既有精确告警通过 warning gate；原生可执行文件 72,114,192 bytes。
- `pnpm test:governance`：52/52 通过。

## 状态

该切片只修正类型所有权并收窄 public API，不声明性能收益。缓存 JSON 字段与键保持兼容；仓库外若直接引用这两个本应为模块私有的 pre-v1 类型，需要迁移到 Tenancy 自有实现，不再把它们视为跨模块契约。

本任务落实现有模块所有权与 Native AOT 静态闭包规则，没有触发新的规则或 Skill 演进。
