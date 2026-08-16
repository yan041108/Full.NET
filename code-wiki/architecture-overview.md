# 架构总览

## 1. 架构形态

Full.NET 1.0 采用 **强化型模块化单体 (Reinforced Modular Monolith)** 架构：

```text
┌──────────────────────────────────────────────────────────────────┐
│                        客户端 (Clients)                          │
│  Vue Admin  │  Layui Admin  │  uni-app  │  Flutter  │  OpenAPI  │
└──────────────────────────┬───────────────────────────────────────┘
                           │ HTTP / WebSocket / gRPC
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                      Host + 横切管道 (Pipelines)                 │
│  ┌─────────┐ ┌────────┐ ┌────────┐ ┌──────────┐ ┌────────────┐ │
│  │  CORS   │ │  限流  │ │ 认证   │ │  授权    │ │  审计日志  │ │
│  └─────────┘ └────────┘ └────────┘ └──────────┘ └────────────┘ │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                    Composition (组合根 + 模块目录)                │
│         IFullNetModuleCatalog → Host Profile 能力选择             │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                      业务模块 (Business Modules)                  │
│  ┌────────┐ ┌─────────┐ ┌────────────┐ ┌──────┐ ┌────────────┐ │
│  │Identity│ │ Tenancy │ │Organization│ │Files │ │  Auditing  │ │
│  └────────┘ └─────────┘ └────────────┘ └──────┘ └────────────┘ │
│  ┌────────┐ ┌─────────┐ ┌────────────┐ ┌──────┐ ┌────────────┐ │
│  │Settings│ │  Jobs   │ │    Docs    │ │Messag│ │  CodeGen   │ │
│  └────────┘ └─────────┘ └────────────┘ └──────┘ └────────────┘ │
└──────────────────────────┬───────────────────────────────────────┘
                           │ 公开 Contracts / 事务 Outbox
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                  BuildingBlocks (基础设施抽象 + 实现)              │
│  ┌──────────────┐ ┌──────────────────┐ ┌──────────────────────┐ │
│  │ Abstractions │ │ Modularity(CQRS) │ │ Data(Dapper+DbUp)    │ │
│  └──────────────┘ └──────────────────┘ └──────────────────────┘ │
│  ┌──────────────┐ ┌──────────────────┐ ┌──────────────────────┐ │
│  │   Caching    │ │  Messaging(Kafka)│ │  Realtime(SignalR)   │ │
│  └──────────────┘ └──────────────────┘ └──────────────────────┘ │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                     外部依赖 (External Systems)                   │
│     SQL Server / MySQL    │    Redis    │    S3 / MinIO         │
│         Kafka / CDC       │  OpenTelemetry Collector             │
└──────────────────────────────────────────────────────────────────┘
```

### 核心原则 (ADR-0002)

1. **模块化而非微服务化**：Full.NET 1.0 不进行全面微服务化，局部模块只有满足 6 项拆分门禁后才能独立部署
2. **明确的模块边界**：模块内部实现默认 `internal`，跨模块只依赖公开 Contracts；禁止直接访问其他模块内部表
3. **单一主项目原则**：每个业务模块默认只创建一个主项目；Contracts/Http/Worker 适配项目只能按真实消费者和隔离收益选择性增加
4. **运行角色分离**：API、Worker、Migrator 三种宿主按职责分离，但不等于业务服务拆分
5. **依赖方向**：Host/Module → BuildingBlocks（抽象与实现），抽象层不得反向依赖业务模块

---

## 2. 解决方案结构

```text
src/
├── BuildingBlocks/                          # 基础设施层（无反向依赖）
│   ├── Full.NET.Abstractions                # 核心抽象：Result、ICommand、Tenancy、Clock、Ids
│   ├── Full.NET.Modularity                  # 模块系统 + CQRS 分发器
│   ├── Full.NET.Data.Abstractions           # 数据访问抽象：Executor、Outbox、Inbox、SQL Scope
│   ├── Full.NET.Data.Dapper                 # Dapper 实现：事务、Scope Guard、Outbox/Inbox
│   ├── Full.NET.Data.MySql                  # MySQL 特有策略
│   ├── Full.NET.Data.CodeGeneration         # 代码生成命名/架构元数据内核
│   ├── Full.NET.Migrations.DbUp             # DbUp 迁移引擎封装
│   ├── Full.NET.Seeding.Abstractions        # 种子数据贡献者抽象
│   ├── Full.NET.Seeding.Dapper              # Dapper 种子编排器
│   ├── Full.NET.Caching.Fusion              # FusionCache 混合缓存实现
│   ├── Full.NET.Messaging.Abstractions      # 消息抽象：事件流所有权、Kafka 重放
│   ├── Full.NET.Messaging.Kafka             # Kafka Consumer/Producer、DLQ、Offset 管理
│   ├── Full.NET.Realtime.Abstractions       # 实时通信抽象
│   ├── Full.NET.Realtime.SignalR            # SignalR 实现 + Redis Backplane
│   ├── Full.NET.Serialization.MessagePack   # MessagePack 序列化
│   ├── Full.NET.Validation.FluentValidation # FluentValidation 集成
│   ├── Full.NET.Localization                # 全栈多语言基础设施
│   └── Full.NET.Hosting                     # 宿主横切：异常处理、日志、限流、OpenAPI、安全
│
├── Modules/                                 # 业务模块层（数据所有权边界）
│   ├── Full.NET.Modules.Identity            # 身份认证：用户、角色、菜单、授权、会话
│   ├── Full.NET.Modules.Identity.Contracts  # 跨模块消费者契约
│   ├── Full.NET.Modules.Tenancy             # 多租户：租户、租户包、解析、切换
│   ├── Full.NET.Modules.Organization        # 组织架构：部门、岗位、职级
│   ├── Full.NET.Modules.Organization.Contracts
│   ├── Full.NET.Modules.Settings            # 平台配置：参数、字典、枚举、网格偏好
│   ├── Full.NET.Modules.Settings.Contracts
│   ├── Full.NET.Modules.Auditing            # 审计：操作/访问/异常/出站日志、保留策略
│   ├── Full.NET.Modules.Files               # 文件管理：上传、下载、Blob 引用
│   ├── Full.NET.Modules.Files.Contracts
│   ├── Full.NET.Modules.Document            # 文档中心：分类、标签、分享、回收站
│   ├── Full.NET.Modules.Notifications       # 通知：站内消息、SignalR 推送
│   ├── Full.NET.Modules.Jobs                # 任务调度：定义、调度、执行记录
│   ├── Full.NET.Modules.Messaging           # 消息运维：事件流管理、重放、死信
│   ├── Full.NET.Modules.CodeGeneration      # 代码生成：模板、预览、运行、回滚
│   └── Full.NET.Modules.SerialNumbers       # 流水号：规则引擎、并发生成
│
├── Composition/
│   └── Full.NET.Composition                 # 组合根：模块目录 + Host Profile
│
├── Compatibility/
│   └── Full.NET.Compatibility.AdminNet      # Admin.NET 统一响应信封适配
│
├── Generators/
│   └── Full.NET.Messaging.Generators        # 消息相关源代码生成器
│
├── Hosts/
│   ├── Full.NET.Host.Api                    # API 宿主（HTTP Endpoint + 实时通信）
│   ├── Full.NET.Host.Worker                 # Worker 宿主（Outbox/Retention 后台处理）
│   ├── Full.NET.Host.Migrator               # Migrator 宿主（数据库迁移 + 种子数据）
│   └── Full.NET.AppHost                     # .NET Aspire 本地编排宿主
│
└── Tools/
    ├── Full.NET.CodeGeneration.Cli          # 代码生成 CLI
    └── Full.NET.Messaging.Cli               # 消息运维 CLI
```

---

## 3. 模块通信规则

### 3.1 模块内通信

- 使用 `ICommand<TResult>` / `IQuery<TResult>` + `ICommandDispatcher` / `IQueryDispatcher`
- 同进程直接调用，不序列化
- 模块内 SQL 可直接 JOIN 本模块拥有的 `fn_<module>_*` 表

### 3.2 跨模块同步读取

| 模式 | 适用场景 | 实现方式 |
|------|----------|----------|
| 最小只读 Port | 请求当下必须获得权威答案，频次低 | 消费方 A 在自身 Contracts 定义最小 Port，B 实现适配 |
| 版本化本地投影 | 高频读取、列表筛选搜索、事务内需要 | 所有者发布 Integration Event → 消费方维护本地投影表 |

**禁止事项**：
- ❌ 直接 SQL/JOIN/视图/同义词/存储过程读取其他模块表
- ❌ 跨模块数据库外键
- ❌ 共享 DbSession / 跨模块本地事务

### 3.3 跨模块写入

- **唯一数据所有者原则**：强不变量收敛到唯一所有者模块
- **最终一致性推进**：其他模块通过事务 Outbox + 集成事件 + 幂等消费者推进
- **跨模块长流程**：Saga/Process Manager + 各模块本地事务 + Outbox + 补偿

### 3.4 可靠消息交付 (ADR-0006)

```text
业务事务 ──同一 DB 事务──► 追加式 Outbox (fn_messaging_outbox_event)
                              │
                              ▼
              ┌───────────────────────────────────┐
              │  事件流所有权 (EventStreamOwnership) │
              │  ├── LegacyPolling: Worker 轮询    │
              │  ├── ShadowCdcKafka: 影子验证     │
              │  └── CdcKafka: CDC Relay → Kafka  │
              └───────────────────────────────────┘
                              │
                              ▼
                        Kafka Broker
                              │
                              ▼
                    Consumer Group (Inbox)
                    ├── 幂等去重 (ConsumerName, MessageId)
                    ├── 本地事务写入业务 + 下游 Outbox
                    └── 提交 Offset（DB 提交后）
```

---

## 4. 数据访问架构

### 4.1 Dapper-First 原则

权威 SQL 三入口说明见 [`dapper-sql-sources.md`](dapper-sql-sources.md)。

- **禁止引入 EF Core** 作为业务数据访问路径
- **所有 SQL 参数化**；表名/排序片段来自封闭白名单
- **禁止 SELECT \***；禁止无 WHERE 的 UPDATE/DELETE

### 4.2 SQL 作用域守卫

```csharp
SqlDataScope:
├── TenantRequired   // 租户作用域：必须绑定 CurrentTenantId
├── HostOnly         // Host 作用域：仅 Host 上下文执行
└── Global           // 全局作用域：必须在 SQL 自身显式行过滤
```

- 由 `SqlScopeGuard` 统一校验执行，防止越权
- 每条生产 Global Statement 必须在 `contracts/architecture/global-sql-statements.json` 登记

### 4.3 双数据库策略

- **一等支持**：SQL Server 与 MySQL，任何结构变更必须同时提供两份迁移 + 测试
- **统一命名**：表/列/索引/约束名称两库完全一致，Provider 语法差异不得改变领域命名
- **Provider 物理类型**：
  - SQL Server：`uniqueidentifier` (UUID)
  - MySQL：`BINARY(16)` RFC 9562 大端字节序
  - 应用端统一使用 C# `Guid`

---

## 5. 多租户架构

```text
请求管道:
  1. Trusted Proxy → 规范化 X-Forwarded-*
  2. TenantResolutionMiddleware
     ├── Host Header 解析 (域名租户)
     ├── 认证 Claim 中可信 TenantId
     └── 显式切换（ChangeTenantContext，需授权 + 审计）
  3. SqlScopeGuard → 所有租户 SQL 注入 @TenantId
  4. 缓存键自动包含 :{tenant_or_host}: 段
  5. Outbox 消息携带 TenantId → 消费方恢复租户上下文
```

---

## 6. 权限与安全架构

### 6.1 超级管理员边界

- 持久化 `host-administrator` 系统角色 + 服务端动态投影全部权限
- ❌ 禁止用户名判断、魔法字段、通配符权限
- ❌ 禁止绕过租户隔离、账号/会话状态、审计
- **最后一名保护**：并发下至少保留一名有效超级管理员

### 6.2 精确权限授权

```text
权限码格式: {module}.{plural_resource}.{action}
示例:
  tenancy.tenants.read
  identity.users.write
  jobs.definitions.trigger
```

- **每个管理操作绑定独立权限码**，禁止粗粒度 `*.write` 隐式覆盖
- Vue 无权限时不创建操作入口（只负责体验）
- **服务端 Endpoint 必须独立授权校验**（真正的安全边界）
- 角色授权按「模块 / 页面 / 操作」分层展示

---

## 7. 缓存架构

- **唯一实现**：FusionCache，通过 `.AsHybridCache()` 暴露双抽象
- **两级缓存**：L1 内存 + L2 Redis + Redis Backplane 广播失效
- **强一致类别**禁用 L1，只走 L2 + 权威源
- **失效闭环**：
  - 提交事务 → 本机同步 L1 删除
  - Outbox 事件 → 跨节点 Backplane 广播删除
  - TTL + 版本号 + 权威源兜底
- **指标**：暴露失效时延/失败、陈旧命中、Backplane 熔断恢复的低基数指标

---

## 8. 可观测性

| 维度 | 实现 |
|------|------|
| 结构化日志 | Serilog 有界异步输出（普通/高优先级独立队列） |
| 分布式追踪 | OpenTelemetry OTLP Export |
| 指标 | OpenTelemetry Metrics + Prometheus 抓取 |
| 健康检查 | 数据库连通性/Schema、缓存分布式、Kafka、SignalR Backplane |
| 审计 | 操作日志、访问日志、异常日志、出站调用日志（按可靠性分类写入） |
