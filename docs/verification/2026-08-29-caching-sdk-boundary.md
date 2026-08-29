# Caching SDK Boundary Verification

## 范围与结果

- 基线：`2831926cc2ebb67e04cece9131565bde772f8dda`
- 计划：`docs/superpowers/plans/2026-08-29-caching-sdk-boundary.md`
- 新增 `Full.NET.Caching.Abstractions`，由 Tenancy、Settings 与 Fusion Provider 真实消费。
- `ICacheInvalidator` 只暴露稳定条目名、键/标签和 `CurrentNodeOnly` / `AllLayersSynchronous` 两种明确传播语义。
- Tenancy 与 Settings 的缓存失效器不再引用 `IFusionCache`、`FusionCacheEntryOptions` 或 Fusion SDK namespace。
- Get/Set 通用适配器因 Set 微基准约 `+5.4%` 超过 `2%` 门槛而 No-Go，生产读写路径未修改。

## RED / GREEN

- Provider RED：新增 Unit 首次因 `Full.NET.Caching.Abstractions` 不存在而 `CS0234` 失败。
- Provider GREEN：DI、CurrentNodeOnly 与 AllLayersSynchronous 三项 Unit `3/3` 通过。
- 模块 RED：Architecture 门禁首次列出 Tenancy、Settings 两个直接使用 Fusion SDK 的失效器。
- 模块 GREEN：迁移后门禁通过，并升级为动态扫描所有模块 `*CacheInvalidator.cs`。

## 新鲜验证记录

- `FusionCacheInvalidatorTests`：`3/3`。
- Tenancy 缓存失效相关 Unit：`8/8`。
- 缓存与 Tenancy MySQL affected Integration：`8/8`。
- API Native AOT analyzer：`0` 警告、`0` 错误。
- API Native AOT Architecture selection：`73/73`。
- Governance：`52/52`。
- BenchmarkDotNet：Get Adapter Ratio `1.01`、Set Adapter Ratio `1.06`，两组分配 Ratio 均 `1.00`；最终 No-Go。

## 调试说明

第一次 affected Integration 的三个用例均在启动 Host.Api 时报告缺少 `Full.NET.Caching.Abstractions.dll`。文件与 Host.Api `.deps.json` 实际存在，但 IntegrationTests 的增量 `project.assets.json` 仍保留旧的 Fusion 传递依赖图。执行 `dotnet restore tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --force-evaluate` 后资产图登记新程序集，同一测试转为通过；因此没有给 IntegrationTests 增加虚假的生产依赖。

## 未验证边界

- 未进行生产等价容量测试；`Capacity-not-verified`。
- Windows 本地 Native AOT analyzer 与 Architecture 通过，不外推为本轮重新完成 Linux 原生产物运行认证。
- SQL、数据库结构、缓存 TTL、序列化与 Provider 配置均未变化。
