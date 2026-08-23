# Host.Api Native AOT 实施计划（Phase 1）

> 日期：2026-08-23
> 分支：`cursor/api-native-aot-phase1`
> 范围：Task 1–4（`Aot-analysis-clean`）；不执行 Task 5–8（`Aot-published`）

## 目标

使 Host.Api 完整 net10.0 依赖闭包在 `FullNetAotAnalysis=true` 与 NativeAot 编译条件下达到 AOT/Trim 分析零未处理告警。

## Task 1：API 专属发布开关

- `FullNetPublishMode=Jit|NativeAot`（默认 Jit）
- 仅 `Full.NET.Host.Api` 在 NativeAot 时设置 `PublishAot=true`
- `pnpm test:aot:analyzers` 脚本与 `NativeAotPublishingRulesTests`

## Task 2：Hosting / Caching / Messaging 静态绑定

- `BindConfiguration` 替代动态 `Bind`
- `MessagingJsonSerializerContext` 覆盖 CDC Delivery Position

## Task 3：CodeGeneration JSON

- `CodeGenerationJsonSerializerContext`
- 消除 `JsonArray` 泛型与反射 JSON

## Task 4：SignalR JSON Profile

- `Hub` 非泛型客户端代理
- AOT 条件排除 MessagePack
- `RealtimeJsonSerializerContext` + AOT-safe Probe

## Phase 1 修正（追加）

- 完整闭包 AOT 条件移至 `Directory.Build.targets`（跨平台）
- `EnableRequestDelegateGenerator` 覆盖模块 Minimal API
- 模块 JSON/审计/Dapper 参数合并 AOT 化
- MSBuild Architecture Tests（MessagePack 常量、CBG/RDG、Linux 路径）
- 回退 `@fullnet/client-contracts` MessagePack 依赖（Phase 1 不需要）

## 门禁

```powershell
pnpm test:aot:analyzers
pnpm test:dotnet:architecture -- --selection api-native-aot
pnpm test:dotnet:unit -- --selection code-generation-realtime
pnpm --filter @fullnet/client-contracts test
pnpm test:governance
pnpm test:naming
dotnet build Full.NET.slnx -c Release
```

## 完成定义

- **允许声明**：`Aot-analysis-clean`
- **禁止声明**：`Aot-published`（Task 5+）
