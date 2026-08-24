# Host.Api Native AOT 就绪评估（2026-08-23）

> 评估范围：`Full.NET.Host.Api` net10.0 完整引用闭包。
> 关联 ADR：[`ADR-0008-api-native-aot-runtime-boundary.md`](../architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md)

## 1. 基线（Phase 1 启动前）

在 `FullNetAotAnalysis=true` 且 `TreatWarningsAsErrors=true` 条件下，对 Host.Api 完整闭包启用 AOT/Trim 分析器，基线为 **41** 个 `IL2026`/`IL3050` 错误：

| 类别 | 数量 | 主要根因 |
|---|---:|---|
| Hosting 配置绑定 | 12 | `ConfigurationBinder.Bind` / 实例 `.Bind(section)` |
| FusionCache 配置 | 3 | 动态 `Bind` / `GetValue<T>` |
| CDC Delivery Position JSON | 4 | 反射式 `JsonSerializer` |
| CodeGeneration JSON / JsonNode | 16 | 非源生成 JSON 与 `JsonArray` 泛型路径 |
| SignalR | 6 | `Hub<T>`、MessagePack、`MapHub`/Probe 委托、配置绑定 |
| **合计** | **41** | |

Windows 环境缺少 C++ 链接器，**不阻塞** Phase 1 的 `Aot-analysis-clean` 目标。

## 2. Phase 1 Task 1–4 结果

| Task | 目标 | 结果 |
|---|---|---|
| Task 1 | 发布边界 + `FullNetAotAnalysis` 门禁 | 已完成 |
| Task 2 | Hosting/Caching/Messaging 静态绑定 | 已完成 |
| Task 3 | CodeGeneration JSON 源生成 | 已完成 |
| Task 4 | SignalR JSON Profile + 条件 MessagePack | 已完成 |

Task 1–4 完成后，在 **BuildingBlocks + Hosts 限定闭包** 下曾达到分析零告警；该范围不满足 ADR-0008 对完整 API 闭包的要求。

## 3. Phase 1 修正后（完整闭包）

修正项：

1. `Directory.Build.targets` 使用跨平台路径归一化，不再依赖 `\BuildingBlocks\` 等 Windows 条件；
2. `FullNetAotAnalysis` 与 `FullNetPublishMode=NativeAot` 共享同一 AOT 编译条件（CBG、RDG、Trim/AOT 分析器）；
3. Realtime 在 AOT 条件下不定义 `FULLNET_SIGNALR_MESSAGEPACK`，且通过 `#if` 排除 MessagePack 注册路径（包引用保留以稳定 JIT 还原图）；
4. 模块 JSON/审计/参数合并与 Identity DI 注解补齐源生成或 AOT 安全实现；
5. 新增 MSBuild Architecture Tests 验证上述条件。

**当前状态：`Aot-analysis-clean`（完整 API 闭包，`pnpm test:aot:analyzers` = 0）。**

## 4. 仍待 Phase 5+ 验证

| 项 | 状态 |
|---|---|
| Linux `dotnet publish -r linux-x64` 链接与启动 | 未验证 |
| Native 发布下 SignalR JIT MessagePack 端到端 | 不适用（Native 仅 JSON） |
| Kafka / S3 Provider Native 闭包 | 未验证 |
| 完整集成/E2E 在 Native 产物上运行 | 未验证 |

## 5. 命令与预期

```powershell
pnpm test:aot:analyzers          # 0 errors
pnpm test:dotnet:architecture -- --selection api-native-aot
dotnet build Full.NET.slnx -c Release
```

## 6. Suppression 清单

Phase 1 修正后：**无** `NoWarn=IL*`、无 `#if FULLNET_AOT_ANALYSIS` 隐藏路径、无无依据 `UnconditionalSuppressMessage`。

Phase 2 起，Host.Api `FullNetPublishMode=NativeAot` 发布闭包允许 `IlcTreatWarningsAsErrors=false`，仅用于 ADR-0008 §3.1 登记的第三方程序集级 `IL2104`/`IL3053`；自有代码仍由 `pnpm test:aot:analyzers` 维持 `Aot-analysis-clean`。

## 7. 规则演进

本次为已批准 ADR/计划实施，不新增 `rules/` 候选。
