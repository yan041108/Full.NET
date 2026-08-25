# ADR-0008：Host.Api Native AOT 运行时边界

- 状态：已批准
- 决策日期：2026-08-23
- 适用范围：`Full.NET.Host.Api` 及其 net10.0 完整项目引用闭包（Composition、BuildingBlocks、Modules）；不覆盖 Worker、Migrator、AppHost、CLI 与 netstandard2.0 源码生成器
- 关联文档：[`2026-08-23-api-native-aot.md`](../../superpowers/plans/2026-08-23-api-native-aot.md)、[`api-native-aot-readiness-2026-08-23.md`](../../verification/api-native-aot-readiness-2026-08-23.md)

## 1. 上下文

Full.NET 1.0 默认以 JIT 模块化单体交付 API。随着 Host.Api 依赖闭包趋于稳定，需要在不拆分进程边界、不关闭业务模块的前提下，为 API 宿主建立可验证的 Native AOT 发布路径。

Native AOT 与 Trim 分析会暴露反射式配置绑定、匿名 Minimal API 委托、动态 JSON 和泛型 DI 替换等路径。若缺少明确边界，容易出现“分析模式通过但真实发布仍编译危险代码”或“为清零告警而缩小闭包范围”的漂移。

## 2. 决策

1. **发布开关**：仅 `Full.NET.Host.Api` 在 `FullNetPublishMode=NativeAot` 时设置 SDK `PublishAot=true`；仓库根目录与其他宿主不得无条件启用。
2. **分析开关**：`FullNetAotAnalysis=true` 与 `FullNetPublishMode=NativeAot` 共享同一套 AOT 编译条件，由 `Directory.Build.targets` 统一控制；不得使用 Windows 路径分隔符限定闭包范围。
3. **闭包范围**：分析/发布必须覆盖 Host.Api 的完整 net10.0 引用闭包，包括 Composition 与全部 Modules；仅排除不适用的 netstandard2.0 Messaging Generator。
4. **生成器**：API 可达项目在 AOT 编译条件下启用 `EnableAotAnalyzer`、`EnableTrimAnalyzer`、`EnableConfigurationBindingGenerator` 与 `EnableRequestDelegateGenerator`。
5. **配置绑定**：禁止回退到 `ConfigurationBinder.Bind` 或实例 `.Bind(section)`；使用 `BindConfiguration`、显式读取或源生成 `Get<T>()`。
6. **JSON**：HTTP、审计、CDC、CodeGeneration 与模块持久化 JSON 必须使用 `JsonSerializerContext`/`JsonTypeInfo`；禁止以 `#if FULLNET_AOT_ANALYSIS` 隐藏 NativeAot 仍会编译的路径。
7. **SignalR**：全平台仅启用 JSON Hub 协议与源生成元数据；不得注册 `AddMessagePackProtocol` 或引用 SignalR MessagePack 协议包。
8. **Integration Event**：JIT 与 Native AOT API 统一使用 MemoryPack（`application/x-memorypack`）序列化 Outbox 载荷；不得为 AOT 单独分叉 JSON 集成事件序列化器。MemoryPack 在 AOT 闭包内按**受控二进制协议**执行（§4.6），禁止反射式 Typeless/Union/多态载荷。
9. **客户端**：`@fullnet/client-contracts` 使用 JSON SignalR 连接。
10. **完成定义分两级**：
   - `Aot-analysis-clean`：完整 API 闭包在 `FullNetAotAnalysis=true` 与 NativeAot 编译条件下 AOT/Trim 分析零未处理告警；
   - `Aot-published`：Linux 原生 publish、链接、启动与关键集成/E2E 证据通过（Phase 1 不宣称）。

## 3. 非目标

- 不对 Worker、Migrator、Kafka Provider、S3 Provider 或第三方 SDK 闭包宣称 Native Aot 就绪；
- 不通过 `NoWarn=IL*`、通配 linker descriptor 或无依据 `UnconditionalSuppressMessage` 清零**自有代码**告警；
- 不在 Phase 1 关闭 CodeGeneration、Files、SignalR、权限或租户模块；
- 不修改 `ui/admin-layui` 存量冻结边界。

### 3.1 第三方 ILC 已知边界（`Aot-published` 发布阶段）

`Aot-analysis-clean` 仍要求 Host.Api 完整闭包在 `FullNetAotAnalysis=true` 下零未处理告警；但真实 `dotnet publish` 的 ILC 链接阶段会对**已列入下表的第三方程序集**产生程序集级 `IL2104`/`IL3053`，在库侧补齐 AOT 标注前视为可接受的发布边界：

| 程序集 | 告警 | 根因与缓解 | 跟踪 |
|---|---|---|---|
| `MemoryPack.Core` | `IL2104`、`IL3053` | 上游程序集级 Trim/AOT 标注不完整；Integration Event 已使用 `[MemoryPackable]` 与显式序列化器 | [MemoryPack#211](https://github.com/Cysharp/MemoryPack/issues/211)、[#251](https://github.com/Cysharp/MemoryPack/issues/251) |
| `Microsoft.Data.SqlClient` | `IL2104`、`IL3053` | 认证 Provider 反射发现；Native 发布通过 `EnableReflectionBasedAuthenticationProviderDiscovery=false` 收窄 | SqlClient Native AOT 文档 |
| `Microsoft.Data.SqlClient.Internal.Logging` | `IL2104` | SqlClient 的内部日志传递依赖；只允许当前程序集级告警，不扩大到自有代码 | 随 SqlClient 版本共同跟踪 |
| `System.Configuration.ConfigurationManager` | `IL2104` | SqlClient 的传递依赖仍包含配置反射路径；Host.Api 不通过该路径发现认证 Provider | 随 SqlClient 依赖树共同跟踪 |
| `Dapper` | `IL2104`、`IL3053`（经 `Dapper.AOT` 拦截后） | 数据访问经 `Dapper.AOT` 源生成拦截器与 `FULLNET_AOT_COMPILE` TypeHandler 排除 | [Dapper.AOT](https://github.com/DapperLib/DapperAOT) |
| `Confluent.Kafka` | `IL2104` | API Replay 使用的 native binding 由 `NativeAotRoots.xml` 精确保留；SQL Server/MySQL Kafka Replay 原生 E2E 已通过 | [`ADR-0009`](ADR-0009-host-api-native-aot-provider-runtime-boundary.md) |

**允许且仅限 Host.Api `FullNetPublishMode=NativeAot` 发布闭包：**

1. `IlcTreatWarningsAsErrors=false`——防止上述第三方程序集级告警在 ILC 阶段升级为失败；不得用于 JIT 或分析构建；
2. 对单程序集 `TrimmerRootAssembly`（如 `MemoryPack.Core`）仅作辅助，不得替代上表登记；
3. 禁止通配 `TrimmerRootAssembly`、通配 linker descriptor 或 `NoWarn=IL*` 掩盖自有代码告警。

`pnpm test:aot:publish:linux` 必须保存本轮 publish 日志，并按“程序集 + 告警码”精确校验上表；任何自有程序集、未知第三方程序集或新 IL 告警码都失败关闭，禁止仅依赖 `IlcTreatWarningsAsErrors=false` 判断发布成功。

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
| JIT（默认） | 仅 JSON + 源生成元数据 | JSON |
| NativeAot / FullNetAotAnalysis | 仅 JSON + 源生成元数据 | JSON |

## 4.6 MemoryPack 受控二进制协议（Integration Event 载荷）

Phase 2 Native AOT API 将 MemoryPack 视为**严格定义的传输层**，而非可反射遍历对象图的“黑盒序列化器”。放弃运行时多态与接口分派，换取编译期可裁剪、可验证的确定性字节契约。

### 架构模式

Full.NET 已采用**双层信封**（与 Kafka/Outbox 长期契约一致）：

1. **传输 Envelope**——<c>IntegrationEventEnvelope</c>（C# 契约，非 MemoryPack 序列化）：承载 <c>MessageType</c>、<c>SchemaVersion</c>、<c>ContentType</c>、租户/追踪元数据与 <c>ReadOnlyMemory&lt;byte&gt; Payload</c>；
2. **载荷 DTO**——各模块 <c>*.Contracts</c> 中的具体 <c>[MemoryPackable] partial record</c>，由 <c>IIntegrationEventSerializer</c> 单独序列化为 <c>Payload</c> 字节块。

消费方按 <c>MessageType + SchemaVersion</c> 路由到已知具体类型后再反序列化载荷；**禁止**在载荷内使用继承、<c>[MemoryPackUnion]</c> 或 <c>object</c> 多态。

### 白名单（Integration Event 载荷 DTO）

| 类别 | 要求 |
|---|---|
| 类型形态 | 具体 <c>sealed</c> 或 record；<c>[MemoryPackable]</c> + <c>partial</c>；定义于模块 Contracts 项目 |
| 值类型 | <c>struct</c>、基础类型、<c>Guid</c>、<c>DateTime</c>、<c>DateTimeOffset</c>、<c>string</c> |
| 集合 | 具体 <c>List&lt;T&gt;</c>、<c>T[]</c>、<c>Dictionary&lt;K,V&gt;</c>、<c>HashSet&lt;T&gt;</c>（<c>T/K/V</c> 均为可序列化具体类型） |
| 枚举 | 显式底层类型（如 <c>: byte</c>、<c>: int</c>） |

### 黑名单（Integration Event 载荷 DTO）

| 禁止项 | 原因 |
|---|---|
| 接口类型属性（<c>IEnumerable&lt;T&gt;</c>、<c>IList&lt;T&gt;</c>、<c>IDictionary&lt;K,V&gt;</c> 等） | AOT 无法确定运行时实现类 |
| 继承/多态（基类事件、<c>[MemoryPackUnion]</c>） | 虚方法表与动态类型识别不可裁剪 |
| <c>object</c>、<c>Dictionary&lt;string, object&gt;</c> | 无编译期类型信息 |
| 未约束泛型（运行时决定 <c>T</c>） | 格式化器无法在编译期闭合 |
| 循环引用对象图 | MemoryPack 不支持引用跟踪 |
| Contractless/Typeless API、<c>MemoryPackFormatterProvider.Register</c> 掩盖动态类型 | 破坏确定性，掩盖设计缺陷 |

### 验证

- `SerializationRulesTests` 与 `MemoryPackControlledProtocolRulesTests` 扫描全部生产 Integration Event 契约；
- `pnpm test:aot:analyzers` 约束 API 闭包自有代码零未处理告警；
- `pnpm test:aot:publish:linux` + Native AOT 集成冒烟验证运行时往返（见 `nativeAotIntegration` 门禁）。

## 5. 验证

- `pnpm test:aot:analyzers`：Host.Api 完整闭包 AOT/Trim Rebuild；
- `pnpm test:dotnet:architecture -- --selection api-native-aot`：发布边界、静态绑定与 MSBuild 编译条件；
- 聚焦 Unit（CodeGeneration / Realtime）与 `@fullnet/client-contracts` 回归；
- Release 构建与 governance/naming 门禁保持通过。

## 6. 后果

- _positive_：API 闭包获得可重复的静态分析与发布边界，SignalR/JSON/配置绑定漂移可被架构测试捕获；
- _negative_：模块 Minimal API 与 JSON 必须持续维护源生成类型；`Aot-published` 只覆盖 Host.Api 已验证闭包，Worker/Migrator、生产容量及未在 ADR-0009 精确列明的 Provider 路径不得外推。

## 7. 规则演进

本次为已批准 ADR 落地，不新增 `rules/` 候选；若后续 Phase 5+ 引入 Provider 闭包或 linker 策略，需单独 ADR 与 verification 记录。
