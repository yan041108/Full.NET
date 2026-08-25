# Host.Api Native AOT 开发指南

本文帮助开发者在编码阶段保持 `Full.NET.Host.Api` 的 Native AOT 兼容性。强制要求以 [`rules/native-aot.md`](../../rules/native-aot.md) 为准；运行边界和状态定义分别以 [`ADR-0008`](../architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md) 与 [`ADR-0009`](../architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md) 为准；特定提交是否通过以 [`docs/verification/`](../verification/) 和 CI 为准。

## 1. 先理解四个不同结果

Native AOT 不是“把 Release build 换一个参数”。它会在发布时把可达托管代码编译为目标平台机器码，并裁剪静态分析认为不可达的成员。JIT 运行时可以临时生成代码、扫描程序集或反射创建类型，Native AOT 没有这些兜底能力。

| 验证层 | 能证明什么 | 不能证明什么 |
|---|---|---|
| 普通 JIT build/test | 业务代码在 CoreCLR 下可编译、可运行 | 裁剪后元数据仍存在；native library 可加载 |
| `Aot-analysis-clean` | 完整 Host.Api 闭包没有未处理 AOT/Trim 分析告警 | 原生链接、启动与真实交互成功 |
| Linux Native AOT publish | 目标 RID 能链接出原生可执行文件 | DI、JSON、SQL、Provider 的每条运行路径可用 |
| 原生外部进程 E2E | 已发布文件在真实基础设施上走过指定链路 | 未执行的运行角色、Provider、凭据链或生产容量 |

因此，JIT 测试通过后仍需继续做 analyzer、publish 和原生进程验证。微软也明确建议频繁 publish，在开发早期发现依赖闭包问题。

## 2. 变更影响选择表

| 你改了什么 | 编码时必须同步检查 | 最低验证 |
|---|---|---|
| Endpoint、请求/响应 DTO、ProblemDetails 扩展 | HTTP `JsonSerializerContext`、集合闭包、错误响应 | Unit/Architecture + analyzer |
| Hub 消息、Realtime probe | HTTP JSON 与 SignalR JSON 两套 resolver chain | Realtime Unit + analyzer + 核心原生 E2E |
| Command/Query Validator 或 Pipeline Behavior | 实际消息/结果的闭合泛型 DI 注册 | Validation Unit + Architecture + analyzer |
| SQL 查询结果 DTO/record | 行物化器、列顺序、NULL、双库物理类型 | Dapper/Architecture + 双库原生 E2E |
| SQL 参数 | 静态参数容器；禁止匿名对象 | Architecture + analyzer +受影响原生 E2E |
| 配置 Options | Configuration Binding source generator 或显式键读取 | Options Unit + analyzer |
| NuGet、native library、RD.XML | AOT/Trim 声明、RID 文件、反射/native binding | analyzer + Linux publish +真实 Provider E2E |
| 测试容器与原生进程夹具 | readiness、宿主地址、日志泵、TRX | 对应 Linux 原生 E2E |

## 3. JSON：类型必须进入静态元数据闭包

### 3.1 推荐模式

在模块自己的 context 中登记所有具体输入、输出和集合类型：

```csharp
[JsonSerializable(typeof(CreateWidgetRequest))]
[JsonSerializable(typeof(WidgetResponse))]
[JsonSerializable(typeof(IReadOnlyList<WidgetResponse>))]
internal sealed partial class WidgetsJsonSerializerContext : JsonSerializerContext;
```

注册到实际使用的 Options 管道：

```csharp
services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(
        0,
        WidgetsJsonSerializerContext.Default));
```

显式调用序列化器时，传入生成的类型信息：

```csharp
var json = JsonSerializer.Serialize(
    response,
    WidgetsJsonSerializerContext.Default.WidgetResponse);
```

### 3.2 常见误区

```csharp
// 错误：JIT 可反射发现运行时类型，Native AOT 发布后不保证元数据仍存在。
var json = JsonSerializer.Serialize((object)response);
```

- 只登记元素类型，没有登记实际传输的集合或 wrapper。
- Hub Options 已加入 context，但 HTTP probe 使用另一套 `HttpJsonOptions`。
- 成功响应已登记，ProblemDetails 扩展或失败响应中的 DTO 未登记。
- 用 `object`、接口、运行时多态或字符串类型名把具体类型推迟到运行时。

SignalR 必须同时审查 `AddJsonProtocol` 的 `PayloadSerializerOptions` 与 HTTP JSON resolver chain。本仓库只支持 JSON Hub 协议，不以 MessagePack Hub 回避源生成问题。

## 4. DI：开放泛型不代表原生产物能构造所有闭包

JIT 下注册一个开放泛型 Behavior，容器可以在收到任意消息后反射创建闭合类型；Native AOT 下，对应闭包可能没有被静态保留。

存在 Validator 的消息应显式注册：

```csharp
services.AddFullNetFluentValidation<Command, LoginSessionResult>();
services.TryAddScoped<IValidator<Command>, LoginCommandValidator>();
```

审查所有类似模式：

- `IDispatchBehavior<TMessage, TResult>`；
- `IValidator<T>`、Mapper、Handler；
- 第三方 Provider factory；
- 运行时枚举 `IEnumerable<T>` 或数组并反射创建实现的扩展点。

静态注册表必须在首个请求前完成。不要用稍后启动的 `IHostedService` 注册 JSON metadata、SQL 物化器或参数绑定器，否则启动健康检查和首个请求之间存在竞态。

## 5. SQL 与 Dapper：参数和结果都要静态化

### 5.1 参数

错误模式：

```csharp
await executor.ExecuteAsync(statement, new { FileId, Status }, cancellationToken);
```

匿名对象需要运行时发现属性。Native AOT 路径只使用以下三类参数：

```csharp
var parameters = new Dictionary<string, object?>
{
    ["FileId"] = fileId,
    ["Status"] = status
};
```

```csharp
var parameters = new DynamicParameters();
parameters.Add("FileId", fileId);
parameters.Add("Status", status);
```

稳定领域参数 record 只有在 `DapperAotParameterRegistry` 注册显式 binder 后才可直接传入。不要假设经典 Dapper TypeHandler 或 `DynamicParameters(anonymousObject)` 在 Native AOT 下仍能反射展开。

### 5.2 查询结果

标量可由执行器直接读取；DTO/record 必须注册明确的行物化器：

```csharp
internal sealed class WidgetsDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<WidgetRecord>(reader => new WidgetRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 2)));
    }
}
```

注册顺序必须与 SQL 投影列顺序一致。修改 `SELECT` 列、别名、NULL 性或类型时，必须同步修改物化器和双库测试。

### 5.3 双库类型差异

重点检查：

- MySQL `DATETIME` 可能由驱动返回 `DateTime`，目标模型却是 `DateTimeOffset`；
- MySQL `tinyint` 与 SQL Server `bit` 的布尔读取；
- MySQL `binary(16)` UUID 与 SQL Server `uniqueidentifier`；
- 数据库 `NULL` 到可空标量或字符串；
- 聚合函数和数值类型在两种驱动中的具体 CLR 类型。

不要用 `Convert.ChangeType` 掩盖 Provider 差异。应通过 `AotDataReaderExtensions` 或小型 Provider shim 显式表达并测试语义。

## 6. 配置绑定

优先使用 `BindConfiguration`，让 Options 和 Configuration Binding source generator 建立静态闭包：

```csharp
services
    .AddOptions<WidgetOptions>()
    .BindConfiguration(WidgetOptions.SectionName)
    .ValidateOnStart();
```

简单且稳定的少量键也可显式读取。禁止在 Host.Api 可达路径使用运行时类型扫描、实例 `.Bind(section)` 或自定义反射式 Binder。若配置类型含接口、抽象类型、复杂多态集合或非公开 setter，应先重新设计静态契约。

## 7. 第三方依赖与 native binding

引入或升级库时按以下顺序审查：

1. 官方是否明确支持 Native AOT/Trim；是否含 `RequiresDynamicCode`、`RequiresUnreferencedCode` 或运行时代码生成。
2. 传递依赖是否引入反射式序列化、配置或程序集扫描。
3. 目标 RID 的 `.so` 文件是否进入 publish 产物，加载名称和 CPU 架构是否正确。
4. 托管层是否按字符串方法名/类型名反射绑定 native export。
5. analyzer、publish 和真实调用是否全部通过。

Confluent.Kafka 的 API Replay 路径会通过候选 `NativeMethods` 类型绑定 `librdkafka`。当前 `NativeAotRoots.xml` 只精确保留 Linux 候选类型的方法元数据，并由 Kafka Replay 原生 E2E 证明。这个方案不能复制成通配 root，也不能外推到 Worker Producer/Consumer、CDC Relay、DLQ 或 Lag Observer；完整范围见 [`ADR-0009`](../architecture/adr/ADR-0009-host-api-native-aot-provider-runtime-boundary.md)。

只有在静态 API、源生成、显式映射和受支持的注解都无法表达第三方反射机制时，才考虑精确 RD.XML 或 `DynamicDependency`。任何 suppression 都必须解释“保留了哪些成员、为什么运行时一定只访问这些成员、哪个原生 E2E 证明它”。

## 8. 外部进程与 Testcontainers

Native E2E 验证的是发布文件，不是测试进程本身。夹具应保证：

- 启动前清理旧日志，持续泵送 stdout/stderr，进程退出后再完成日志泵；
- health probe 与应用日志共同区分“仍在启动”和“已经崩溃”；
- 断言失败包含 HTTP status、body 和原生进程日志位置；
- 容器 readiness 使用真实服务健康端点，而不只等待端口打开；
- 嵌套 Docker/宿主覆盖场景使用 Testcontainers 提供的宿主地址语义；
- TRX 与日志进入 `artifacts/`，CI 失败时可上传。

本轮 MinIO 偶发失败的根因就是端口已开放但服务还未 ready；进程诊断缺失则来自 Dispose 过早取消 stdout/stderr pump。两者都不是业务 API 修复能够掩盖的问题。

## 9. 验证梯度

### 9.1 快速开发环

先跑受影响 Unit、governance 和 Native AOT Architecture tests：

```bash
node --test tests/governance/native-aot-guidance.test.mjs
pnpm test:dotnet:architecture --selection api-native-aot
```

### 9.2 分析闭包

修改 Host.Api 可达代码、JSON、配置、DI 或 Dapper AOT 后运行：

```bash
pnpm test:aot:analyzers
```

### 9.3 Linux publish

修改依赖、native 文件、MSBuild 条件、linker/RD.XML 或发布脚本后运行：

```bash
pnpm test:aot:publish:linux
```

### 9.4 原生运行门禁

按受影响路径选择：

```bash
pnpm test:aot:native:e2e
pnpm test:aot:native:s3:e2e
pnpm test:aot:native:kafka-replay:e2e
pnpm test:aot:native:providers:e2e
```

本地非 Linux 的测试发现与 skip 不能升级状态。关闭 `Aot-published` 或 `Native-provider-verified:*` 时必须引用 fresh Linux CI run、提交 SHA、成功步骤和明确未验证项。最低发现数、超时和产物阈值只读取 [`eng/testing/test-matrix.json`](../../eng/testing/test-matrix.json)。

## 10. 故障定位顺序

| 现象 | 优先检查 | 典型根因 |
|---|---|---|
| 进程启动即退出 | stderr、native `.so`、配置、DI | native binding 被裁剪；Options 绑定失败；开放泛型无法构造 |
| Endpoint 返回 500 | response body、JSON context、认证/会话 SQL | DTO 未登记；匿名参数；缺少行物化器 |
| 只在 MySQL 失败 | reader 实际 CLR 类型 | `DateTime`/`DateTimeOffset`、布尔或 UUID 转换差异 |
| SignalR probe 失败 | HTTP 与 Hub 两套 JSON Options | 只给一条管道注册 context |
| S3 偶发失败 | MinIO readiness、宿主地址 | 只等待端口；嵌套 Docker 地址错误 |
| Kafka 创建 client 失败 | `librdkafka.so` 与候选 NativeMethods | 方法元数据被裁剪或 RID 文件缺失 |
| CI 只有状态没有根因 | TRX、stdout/stderr pump | 日志泵提前 Dispose；断言未包含 body |

修复顺序遵循“最早失败阶段”：先让进程稳定启动，再处理 HTTP/JSON，再处理 SQL，最后处理 Provider。禁止一次扩大多项 suppression 后靠结果猜测是哪一项生效。

## 11. PR 审查清单

- 是否新增了 Native AOT 可达 DTO、集合或错误类型？对应 context 和使用管道在哪里？
- 是否新增开放泛型注册、Validator 或运行时实现扫描？实际闭包是否显式注册？
- SQL 参数是否仍有匿名对象？结果是否需要新物化器？
- SQL Server/MySQL 驱动返回类型是否都验证过？
- 新依赖是否包含运行时代码生成、反射发现或 RID native 文件？
- 是否新增 `NoWarn=IL*`、通配 root/descriptor 或无法解释的 suppression？若是，必须拒绝。
- 是否运行了与变更层级相称的 analyzer、publish 和原生 E2E？
- 状态声明是否精确，是否错误外推到 Worker、CDC、完整 Kafka 或 AWS 全凭据链？

## 12. 官方资料

- Microsoft：[Native AOT deployment](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
- Microsoft：[ASP.NET Core support for Native AOT](https://learn.microsoft.com/aspnet/core/fundamentals/native-aot/)
- Microsoft：[System.Text.Json source generation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)
- Microsoft：[Prepare .NET libraries for trimming](https://learn.microsoft.com/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)

这些资料提供平台通用原则；Full.NET 的具体状态、命令、Dapper 边界和 Provider 范围仍以仓库规则、ADR 与测试矩阵为准。
