# Full.NET 技术集成路线与验证管道设计

- 状态：已批准
- 日期：2026-07-17
- 决策来源：用户确认按技术清单分析的推荐方案执行
- 适用范围：Full.NET M2 及后续里程碑

## 1. 目标

Full.NET 继续采用“轻量核心 + 官方模块 + 可选 Provider”的结构。新组件只有在解决当前真实需求、许可证条件明确、不会复制已有能力且拥有自动化验收时才能进入默认依赖。

本轮只实现 M2 验证管道。后台任务、对象映射、消息中间件、网关和工作流分别进入后续独立规格与实施计划，不能在本轮预装运行时依赖。

## 2. 已批准的组件决策

| 技术 | 决策 | 落点 |
|---|---|---|
| ASP.NET Core Minimal API | 保持默认 HTTP 编程模型 | Core/M2 |
| FastEndpoints | 不进入默认核心；需要时进行独立 PoC | 可选 HTTP Provider |
| MediatR | 不引入 | Full.NET 自有 Command/Query Dispatcher |
| FluentValidation | 作为默认验证引擎 | M2，本规格实施范围 |
| Mapster | 不作为运行时默认 Mapper | 可选代码生成工具 |
| Mapperly | M3 优先评估 | CodeGeneration/模板 |
| Hangfire | 不作为默认任务引擎 | 用户显式选择的可选 Provider |
| Quartz.NET | M3 默认调度器候选 | Jobs Provider |
| Kafka / CDC Relay / EventBus Provider | 当前不引入；真实跨进程高吞吐事件命中门禁后再选 Provider | M5+，排在当前硬化与核心业务模块之后 |
| YARP | 服务拆分、多上游或 BFF 出现后引入 | M4+ Gateway Provider |
| Envoy/Linkerd | 不作为 NuGet 依赖 | M5+ 部署模板 |
| NSubstitute | 保持唯一 Mock 框架 | Test Infrastructure |
| Moq | 不引入 | 避免重复测试依赖 |
| Microsoft.Extensions.Http.Resilience | 保持现有实现 | Hosting，M1 已实现 |
| StackExchange.Redis | 只通过缓存/Redis Provider 使用 | FusionCache/Provider，M1 已实现 |
| Swashbuckle | 不引入文档生成器 | Microsoft OpenAPI + Scalar |
| Elsa Workflows | 身份、组织、权限稳定后独立集成 | M5+ Workflow Module |
| Workflow Core/Durable Task | 特定场景备选 | 非默认依赖 |

## 3. 验证架构

验证必须位于 Full.NET Dispatcher 管道中，而不是只位于 HTTP Endpoint。这样 Minimal API、Worker、SignalR、gRPC、后台任务和模块内部调用可以共享同一套规则。

```text
Transport Request
    -> Endpoint/Adapter
    -> Command or Query
    -> IDispatchBehavior<TMessage, TResult>[]
         -> FluentValidationBehavior
         -> future authorization/audit/trace behaviors
    -> transaction when the command is transactional
    -> Handler
    -> Result<TResult>
    -> ProblemDetails or another transport adapter
```

`IDispatchBehavior<TMessage, TResult>` 位于 `Full.NET.Abstractions`。它不依赖 FluentValidation，允许后续权限、审计和追踪行为复用同一管道。Command 和 Query Dispatcher 按 DI 注册顺序执行行为：先注册的行为位于外层，Handler 位于最内层。

事务继续由现有 `ITransactionalCommand` 触发。验证行为包裹事务调用，因此无效命令不得打开数据库事务。

## 4. FluentValidation Provider

新增 `Full.NET.Validation.FluentValidation` BuildingBlock，职责仅包括：

1. 提供 `FluentValidationBehavior<TMessage, TResult>`；
2. 将所有 `IValidator<TMessage>` 的失败结果转换为 Full.NET `Error`；
3. 提供幂等服务注册扩展；
4. 不扫描程序集，不使用运行时反射注册 Validator；
5. 不引用任何业务模块。

模块显式注册自己的 Validator。代码生成器后续负责生成对应注册代码。Validator 使用 Scoped 生命周期，以便安全使用租户上下文或只读查询服务；同一消息的多个 Validator 按注册顺序串行执行。

验证失败返回：

```text
Code: validation.failed
Type: Validation
Message: One or more validation errors occurred.
ValidationErrors: 以属性名分组的去重消息数组
```

验证失败时不得调用下一个 Behavior、事务或 Handler。没有注册 Validator 时，行为直接调用下一个委托。

## 5. 租户垂直切片迁移

`ProvisionTenantCommand` 的结构性规则迁移到 `ProvisionTenantCommandValidator`：

- `Identifier` 去除首尾空白并转为小写后必须匹配 `^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$`，即 3–64 个小写字母、数字或中划线且首尾不能为中划线；
- `Name` 去除首尾空白后不能为空，最大 128 个字符；
- `Domain` 去除首尾空白后不能为空，最大 253 个字符。

Handler 保留以下业务规则：

- 标识或域名是否已存在；
- 数据库写入和并发冲突；
- Outbox 事件写入；
- 缓存失效所需事件。

外部 `ProvisionTenantRequest` 与内部 `ProvisionTenantCommand` 继续分离。不得让 HTTP DTO 同时实现 FastEndpoints、MediatR 或 Full.NET 的多套消息接口。

## 6. 错误与兼容性

标准 HTTP API 继续输出真实 400 状态和 ProblemDetails，`errors` 扩展字段保存属性错误。Admin.NET 兼容适配器只改变响应形状，不改变真实状态码。

属性名沿用 Validator 的 `PropertyName`，不在验证层绑定 JSON 命名策略；HTTP Adapter 可在未来按契约需要转换属性名。

## 7. AOT 与性能约束

- 不使用 FluentValidation 程序集扫描扩展；
- 不引入 FastEndpoints 或 MediatR 作为验证实现的间接依赖；
- 无 Validator 的消息只产生一次空集合枚举和管道委托调用；
- Validator 串行执行，避免带 Scoped 依赖的 Validator 并发访问同一数据库会话；
- 新行为必须有单元测试，真实租户链路必须继续通过 SQL Server/MySQL 集成测试。

## 8. 验收标准

1. Command 和 Query Dispatcher 都支持通用行为管道；
2. 行为按注册顺序执行；
3. 无 Validator 时 Handler 正常执行；
4. 任一验证失败时 Handler 和事务均不执行；
5. 多个 Validator 的错误按属性聚合且消息去重；
6. 租户无效请求仍返回标准验证错误；
7. 合法租户创建、Outbox 和双数据库测试无回归；
8. 架构测试证明 Validation BuildingBlock 不依赖业务模块；
9. Release 构建无警告、无错误；
10. 依赖漏洞审计通过，第三方许可清单包含 FluentValidation Apache 2.0。

## 9. 后续独立规格顺序

本规格完成后，后续按真实业务模块需要分别设计：

1. M2 Identity/RBAC 与 SignalR；
2. M3 Jobs 抽象、Quartz Provider 和任务管理页面；
3. M3 Mapperly 与 Full.NET 代码生成器；
4. M4 YARP Gateway Provider（仅在服务拆分门禁通过后）；
5. M5+ Elsa Workflow Module；
6. M5+ Envoy/Linkerd Kubernetes 部署模板；
7. M5+ 事件交付演进 Decision Gate：先以真实 SLA 和压测复核 Outbox，再决定是否建立 Kafka Provider，以及是否以 CDC Relay 替代特定事件流的轮询发布。

每一项必须有独立规格、依赖许可复核、真实消费者和验收测试，不得仅为未来假设预装。

事件交付的长期边界以[总体架构 Spec §9.1](2026-07-17-fullnet-architecture-design.md#91-事件交付演进基线)为准。当前阶段只完成事务 Outbox 的版本、死信、重放、多 Worker 与运维闭环；`1000 QPS` 不是固定切换阈值，直接 Kafka 只允许承载可丢失、可重算且不要求业务事务原子性的流量。
