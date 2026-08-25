# Full.NET Native AOT 开发规则

## 1. 来源、适用范围与状态

- 状态：强制。
- 来源：项目所有者于 2026-08-25 明确要求将 Native AOT 编码经验固化为长期开发规则；`api-native-aot-linux` 的连续修复进一步暴露了开放泛型 DI、JSON 元数据、Dapper 参数与物化、第三方 native binding 和外部进程测试夹具的可重复风险。
- 适用范围：任何修改 `Full.NET.Host.Api` 可达代码或依赖、AOT 编译条件、HTTP/SignalR JSON、配置绑定、Dapper 查询与命令、Provider native binding、Native AOT 测试、发布脚本或工作流的任务。
- 风险：JIT 构建和普通单元测试可以通过，但裁剪后的原生程序仍可能在启动、依赖注入、序列化、数据库访问或第三方 native 调用时失败；只修告警而不运行原生产物会产生假绿。
- 决策源：Host.Api 运行边界以 [`ADR-0008`](../docs/architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md) 为准；S3 与 Kafka Replay Provider 的精确范围以 [`ADR-0009`](../docs/architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md) 为准。
- 例外：无。需要扩大已验证运行角色、Provider 或 suppression 边界时，必须先修改对应 ADR 并补齐原生运行证据。

## 2. 完成状态不得混用

1. `Aot-analysis-clean` 只表示 Host.Api 完整可达闭包的 AOT/Trim 分析没有未处理告警，不证明已经链接、启动或完成运行时交互。
2. `Aot-published` 必须同时具备目标 RID 的真实 Native AOT publish、原生可执行文件启动/停止和关键外部进程 E2E；普通 `dotnet build`、JIT 集成测试或非 Linux discovery skip 均不能替代。
3. `Native-provider-verified: s3` 与 `Native-provider-verified: kafka-replay` 只能按 ADR-0009 的精确范围声明。禁止外推为 Worker/Migrator Native AOT、完整 Kafka Delivery、CDC Relay、DLQ、Lag Observer 或 AWS 全凭据链已验证。
4. 测试数量、最低发现数、产物阈值与超时只以 [`eng/testing/test-matrix.json`](../eng/testing/test-matrix.json) 为机器事实源，规则和开发文档不得复制可变数值。

## 3. 静态闭包与反射

1. Native AOT 可达路径必须能够由编译器、源生成器和分析器静态确定。禁止运行时代码生成、`System.Reflection.Emit`、字符串拼接类型名后动态加载、无界程序集扫描或依赖未声明成员的反射发现。
2. 确有反射需要时，应优先改为源生成、闭合泛型注册、显式映射或静态注册表。`DynamicallyAccessedMembers`、`RequiresUnreferencedCode`、`RequiresDynamicCode` 只能准确传播真实要求，不能用于隐藏不兼容路径。
3. `DynamicDependency`、RD.XML/linker descriptor 与 `UnconditionalSuppressMessage` 只能作为最后手段，并且必须精确到已证明的程序集、类型或成员，说明第三方机制、目标平台和原生 E2E。禁止通配程序集、命名空间、类型或成员。
4. 自有代码禁止使用 `NoWarn=IL*`、通配 linker root、通配 descriptor 或无依据 suppression 换绿。第三方 publish 告警必须先定位根因，再按“程序集 + 告警码”登记精确 allowlist；出现新程序集或新告警码必须失败关闭。

## 4. JSON、HTTP、SignalR 与二进制契约

1. 所有进入 HTTP body、Minimal API 返回值、ProblemDetails 扩展、SignalR Hub 消息、缓存持久化或其它 Native AOT 可达 JSON 路径的具体类型，必须登记到对应 `JsonSerializerContext`，并通过 context、`JsonTypeInfo<T>` 或注册后的 `TypeInfoResolverChain` 使用。
2. 新增 Endpoint、DTO、集合包装、枚举或多态形态时，必须同时检查输入与输出类型、错误响应和集合闭包；禁止因 JIT 下 `JsonSerializer.Serialize(value)` 可用而依赖反射默认值。
3. HTTP JSON 与 SignalR JSON 使用不同 Options 管道时，必须分别注册同一业务路径需要的源生成元数据。只配置 Hub Options 不能证明普通 HTTP probe 可序列化，反之亦然。
4. SignalR 在 JIT 与 Native AOT 下均只使用 JSON Hub 协议和源生成元数据；不得为原生发布重新引入 MessagePack Hub 协议或动态 Hub 代理。
5. Integration Event 继续遵循 ADR-0008 的 MemoryPack 受控具体类型协议；禁止 Typeless、`object` 载荷、接口集合、未约束泛型或运行时多态。

## 5. 依赖注入与泛型闭包

1. Native AOT 可达服务必须使用可静态闭合的注册。对 Validator、Pipeline Behavior、Mapper、Handler 或 Provider 等泛型扩展点，必须为实际消息类型注册闭合泛型；禁止只注册开放泛型后依赖容器在运行时构造未保留的 `IEnumerable<T>`、数组或实现类型。
2. 条件编译路径的注册集合必须在启动前完整且确定。不得通过延迟 `IHostedService` 与首个请求竞态注册序列化元数据、SQL 物化器或参数绑定器。
3. JIT 与 Native AOT 的业务语义必须一致；条件编译只能替换实现机制或排除已批准的不兼容能力，禁止关闭业务模块、权限、租户或失败关闭逻辑来换绿。

## 6. Dapper、ADO.NET 与双数据库

1. Native AOT SQL 参数禁止使用匿名对象或依赖反射展开的任意 record。只允许 `DynamicParameters`、`IReadOnlyDictionary<string, object?>`，或已在 `DapperAotParameterRegistry` 注册显式绑定器的稳定参数类型。
2. 所有非标量查询结果必须有可静态执行的行物化路径。通过 Full.NET 泛型 SQL 执行器读取 DTO/record 时，必须同步在模块 Contributor 或基础设施注册中登记 `DapperAotMaterializerRegistry` 物化器。
3. 物化器和参数绑定器必须在首个数据库请求前同步注册；模块注册必须覆盖 API 实际可达查询，也要审查共享基础设施的认证、会话、Inbox、Outbox、审计与文件状态机路径。
4. 列读取必须显式处理 SQL Server/MySQL 返回类型差异、数据库空值和可空标量。`DateTimeOffset`、`Guid`、布尔值与二进制 UUID 等类型不得依赖 `Convert.ChangeType` 或 JIT TypeHandler 的偶然行为。
5. 新增 SQL 查询或命令时，必须同时回答：参数容器是否静态；结果是标量还是需要物化器；SQL Server/MySQL 的物理返回类型是否一致；认证前、Host 与 Tenant 作用域是否会在原生 E2E 中真实到达。

## 7. 配置、第三方库与 native binding

1. 配置对象应使用 `BindConfiguration` 和 Configuration Binding source generator，或使用显式键读取；禁止在 AOT 可达路径回退到无法分析的实例 `.Bind(section)`、运行时类型扫描或反射式自定义 Binder。
2. 引入或升级第三方库前必须检查其 Native AOT/Trim 官方声明、传递依赖、运行时代码生成、反射发现和随 RID 发布的 native 文件。分析无告警不等于 native library 已能加载和调用。
3. 第三方库若按方法名、类型名或平台候选类型反射绑定 native export，只能用精确 RD.XML 或等价静态方式保留实际目标，并以目标 Linux RID 的原生 E2E 证明。Confluent.Kafka 当前边界以 Host.Api 的 `NativeAotRoots.xml` 与 ADR-0009 为准，不构成其它库的通用放行。
4. 外部依赖测试必须等待服务的真实 readiness，而非只等待容器启动或端口开放。MinIO、Kafka、Redis 和数据库夹具必须保留服务端健康证据，并在嵌套 Docker 环境正确解析宿主地址。

## 8. 验证梯度与门禁

1. 开发内循环先运行受影响 Unit、Architecture 和 governance 测试；新增规则必须先有可失败的防漂移验证。
2. 修改 Host.Api 可达代码、AOT 编译条件、JSON/配置源生成、Dapper AOT 或第三方闭包时，至少运行：
   - `pnpm test:aot:analyzers`
   - `pnpm test:dotnet:architecture --selection api-native-aot`
3. 修改发布闭包、第三方依赖、RID/native 文件或 linker 配置时，还必须运行 `pnpm test:aot:publish:linux`。
4. 修改运行时路径时必须执行对应原生外部进程门禁：核心路径使用 `pnpm test:aot:native:e2e`；S3 使用 `pnpm test:aot:native:s3:e2e`；Kafka Replay 使用 `pnpm test:aot:native:kafka-replay:e2e`；组合 Provider 可使用 `pnpm test:aot:native:providers:e2e`。
5. 非 Linux discovery skip 只证明测试可发现，不能证明原生运行通过。`Aot-published` 或 Provider 状态升级必须引用 fresh Linux CI run、提交 SHA、步骤结论和未验证边界。

## 9. 失败诊断顺序

1. 原生进程夹具必须持续泵送 stdout/stderr，并在断言失败或进程退出后保留日志与 TRX；禁止 Dispose 时提前取消日志泵而丢失根因。
2. 启动失败先检查 native library 加载、配置绑定和 DI 闭包；HTTP 500 再检查 JSON metadata、SQL 参数和结果物化；Provider 失败再检查 readiness、宿主地址、凭据注入与 native binding。
3. 断言必须包含响应状态、响应 body 和相关原生进程日志。禁止只保留 `Expected 200` 之类无法定位阶段的错误。
4. 修复必须针对最早失败的静态或运行时边界，并增加 Unit、Architecture、governance 或原生 E2E 防回归；不得通过扩大 suppression、降低最低发现数或跳过 Provider 继续执行来掩盖失败。

## 10. 完成检查

提交前必须确认：

- 新增类型已覆盖 HTTP、Hub、缓存或持久化 JSON 的全部 `JsonSerializerContext`；
- 新增泛型扩展点已按实际消息闭合注册；
- 新增 SQL 的参数、物化器和双库类型转换均为静态路径；
- 新增第三方依赖已检查分析、publish、native 文件与真实调用；
- 没有新增通配 root、`NoWarn=IL*` 或无依据 suppression；
- 运行了与变更层级相称的 analyzer、architecture、publish 和原生 E2E，并记录 fresh 输出；
- 能力状态只声明本轮真实验证范围，未把 Host.Api 证据外推到 Worker/Migrator、完整 Kafka Delivery 或生产容量。
