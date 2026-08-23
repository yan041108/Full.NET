# ADR-0008：Host.Api Native AOT 运行时边界

- 状态：已批准
- 决策日期：2026-08-23
- 适用范围：`Full.NET.Host.Api` 及其 net10.0 完整项目引用闭包（Composition、BuildingBlocks、Modules）；不覆盖 Worker、Migrator、AppHost、CLI 与 netstandard2.0 源码生成器
- 关联文档：[`2026-08-23-api-native-aot.md`](../../superpowers/plans/2026-08-23-api-native-aot.md)、[`api-native-aot-readiness-2026-08-23.md`](../../verification/api-native-aot-readiness-2026-08-23.md)

## 1. 上下文

Full.NET 1.0 默认以 JIT 模块化单体交付 API。随着 Host.Api 依赖闭包趋于稳定，需要在不拆分进程边界、不关闭业务模块的前提下，为 API 宿主建立可验证的 Native AOT 发布路径。

Native AOT 与 Trim 分析会暴露反射式配置绑定、匿名 Minimal API 委托、动态 JSON、SignalR MessagePack 协议和泛型 DI 替换等路径。若缺少明确边界，容易出现“分析模式通过但真实发布仍编译危险代码”或“为清零告警而缩小闭包范围”的漂移。

## 2. 决策

1. **发布开关**：仅 `Full.NET.Host.Api` 在 `FullNetPublishMode=NativeAot` 时设置 SDK `PublishAot=true`；仓库根目录与其他宿主不得无条件启用。
2. **分析开关**：`FullNetAotAnalysis=true` 与 `FullNetPublishMode=NativeAot` 共享同一套 AOT 编译条件，由 `Directory.Build.targets` 统一控制；不得使用 Windows 路径分隔符限定闭包范围。
3. **闭包范围**：分析/发布必须覆盖 Host.Api 的完整 net10.0 引用闭包，包括 Composition 与全部 Modules；仅排除不适用的 netstandard2.0 Messaging Generator。
4. **生成器**：API 可达项目在 AOT 编译条件下启用 `EnableAotAnalyzer`、`EnableTrimAnalyzer`、`EnableConfigurationBindingGenerator` 与 `EnableRequestDelegateGenerator`。
5. **配置绑定**：禁止回退到 `ConfigurationBinder.Bind` 或实例 `.Bind(section)`；使用 `BindConfiguration`、显式读取或源生成 `Get<T>()`。
6. **JSON**：HTTP、审计、CDC、CodeGeneration 与模块持久化 JSON 必须使用 `JsonSerializerContext`/`JsonTypeInfo`；禁止以 `#if FULLNET_AOT_ANALYSIS` 隐藏 NativeAot 仍会编译的路径。
7. **SignalR**：NativeAot 与分析构建仅启用 JSON Hub 协议与源生成元数据；不得定义 `FULLNET_SIGNALR_MESSAGEPACK`，不得编译 `AddMessagePackProtocol` 注册路径；MessagePack NuGet 可保留在还原图以稳定 JIT 门禁顺序，但 AOT 编译闭包不得生成 MessagePack 协议注册 IL。
8. **客户端**：`@fullnet/client-contracts` 在 Phase 1 保持 JSON SignalR 连接；MessagePack 客户端依赖待服务端 JIT 路径与公开选项稳定后再引入。
9. **完成定义分两级**：
   - `Aot-analysis-clean`：完整 API 闭包在 `FullNetAotAnalysis=true` 与 NativeAot 编译条件下 AOT/Trim 分析零未处理告警；
   - `Aot-published`：Linux 原生 publish、链接、启动与关键集成/E2E 证据通过（Phase 1 不宣称）。

## 3. 非目标

- 不对 Worker、Migrator、Kafka Provider、S3 Provider 或第三方 SDK 闭包宣称 Native Aot 就绪；
- 不通过 `NoWarn=IL*`、整程序集 Root、通配 linker descriptor 或无依据 `UnconditionalSuppressMessage` 清零告警；
- 不在 Phase 1 关闭 CodeGeneration、Files、SignalR、权限或租户模块；
- 不修改 `ui/admin-layui` 存量冻结边界。

## 4. 运行时边界

| 运行角色 | Phase 1 Native AOT |
|---|---|
| `Full.NET.Host.Api` | 目标宿主；可切换 `FullNetPublishMode=NativeAot` |
| `Full.NET.Host.Worker` | 明确排除 |
| `Full.NET.Host.Migrator` | 明确排除 |
| `Full.NET.AppHost` / CLI | 明确排除 |
| `Full.NET.Messaging.Generators` (netstandard2.0) | 不参与 AOT 分析 |

## 4.5 SignalR 协议

| 发布模型 | Hub Protocol | 管理端客户端 |
|---|---|---|
| JIT（默认） | JSON + 可选 MessagePack | 默认 JSON |
| NativeAot / FullNetAotAnalysis | 仅 JSON + 源生成元数据 | 仅 JSON |

## 5. 验证

- `pnpm test:aot:analyzers`：Host.Api 完整闭包 AOT/Trim Rebuild；
- `pnpm test:dotnet:architecture -- --selection api-native-aot`：发布边界、静态绑定与 MSBuild 编译条件；
- 聚焦 Unit（CodeGeneration / Realtime）与 `@fullnet/client-contracts` 回归；
- Release 构建与 governance/naming 门禁保持通过。

## 6. 后果

- _positive_：API 闭包获得可重复的静态分析与发布边界，SignalR/JSON/配置绑定漂移可被架构测试捕获；
- _negative_：模块 Minimal API 与 JSON 必须持续维护源生成类型；Native 发布仍受 Windows 链接器、Linux publish 与外部 SDK 闭包约束，Phase 1 不得宣称 `Aot-published`。

## 7. 规则演进

本次为已批准 ADR 落地，不新增 `rules/` 候选；若后续 Phase 5+ 引入 Provider 闭包或 linker 策略，需单独 ADR 与 verification 记录。
