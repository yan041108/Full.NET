---
name: fullnet-module-delivery
description: Use when adding or extending a Full.NET module, CRUD feature, endpoint, command/query, Dapper persistence, SQL Server/MySQL migration, Admin.NET parity capability, or end-to-end product slice in this repository.
---

# Full.NET 模块交付

## 核心原则

先交付一条可运行、可验证的纵向切片，再扩展横向能力。每个切片同时满足模块边界、租户与授权、Dapper 双数据库、标准 API、中文注释和真实测试要求。

开始前必须读取根目录 `AGENTS.md`、相关 `rules/`、架构规格和功能对标路线。需要精确路径、参考切片和验证命令时，读取 [交付地图](references/delivery-map.md)。

## 1. 定义交付契约

1. 将用户需求拆成可验收能力，标明成功、失败、权限、租户、并发和兼容场景。
2. 在 Admin.NET 对标矩阵中找到对应项；按能力和用户流程对标，不复制源码、表结构或耦合方式。
3. 明确归属为 `Core`、Official Module、Provider、Compatibility、Sample 或 Client。
4. 区分本次必须交付与后续能力；没有真实消费者的抽象不要提前创建。
5. 记录会变化的公共契约、数据库结构、事件格式和缓存语义。

## 2. 设计纵向切片

按需选择以下组成部分，禁止为目录完整而创建空层：

| 组成 | 使用条件 | 责任 |
| --- | --- | --- |
| `Contracts` | 跨模块或外部消费者需要稳定契约 | DTO、公开接口、集成事件 |
| `Domain` | 存在业务不变量和状态转换 | 实体、值对象、领域规则 |
| `Features/<UseCase>` | 每个命令或查询 | Command/Query、Validator、Handler、Endpoint |
| `Persistence` | 访问业务数据 | Dapper SQL、映射、Resolver/Repository |
| `Serialization` | DTO 或事件进入线格式 | System.Text.Json 源生成、MessagePack Resolver |
| 模块注册 | 组件需要运行 | DI、Validator、Endpoint、序列化与事件处理器注册 |

保持 HTTP Request、内部 Command/Query 和持久化模型分离。业务规则进入 Handler/Domain，Endpoint 只处理传输、授权、映射和结果转换。

## 3. 先建立 RED 证据

1. 为新行为选择最小但真实的测试层：纯规则用 Unit，依赖方向用 Architecture，Admin.NET 响应用 Compatibility，SQL/迁移/事务/Outbox 用 Integration。
2. 先写一个能表达预期行为的测试并运行，确认它因为能力缺失而失败，不是因为拼写、环境或测试发现错误。
3. 测试名称表达场景与结果；测试注释仅在需要解释历史原因或非显然时序时使用，并使用中文。
4. 实现最小正确切片使测试通过，再补失败、边界、取消、重复和并发场景。

## 4. 实现业务与数据边界

### 租户和授权

- 从受信任上下文取得租户，不信任普通请求提供的 TenantId。
- 在 Endpoint 声明授权，在 Handler 或领域边界再次保护敏感业务不变量。
- SQL、缓存键、Outbox 和实时分组都必须包含租户边界。
- 管理员跨租户能力必须显式命名、授权和审计。

### Dapper 与双数据库

- 使用 Dapper、参数化 SQL 和现有 SQL Scope；不要引入 EF Core 捷径。
- 数据库行为变化时同时实现 SQL Server 与 MySQL 的 SQL、DbUp 迁移、索引和集成测试。
- 非事务或隐式提交的 DDL 必须按结构探测、回填和约束收紧分别收敛；双库集成测试要模拟 DbUp 未记账且迁移部分完成后的重跑恢复。
- 只读端点若不改变结构，不要创建迁移；只为实际查询添加 SQL 和测试。
- 评估排序稳定性、分页、索引和最坏数据量，不凭感觉宣称性能提升。

### 事务、事件与缓存

- 业务写入与 Outbox 在同一事务中提交，外部调用不得进入数据库事务。
- 跨进程可靠事件使用带版本元数据的 MessagePack；进程内调用不要序列化。
- 只有存在重复读取与明确失效点时才使用 FusionCache。
- 缓存失效由提交后的 Outbox 事件触发，保证多实例 L1/L2 与 Backplane 一致；不要在提交前删除缓存。
- 明确幂等、重试、租约、毒消息、取消和失败恢复，不把至少一次交付写成恰好一次。

## 5. 保持 API 与兼容边界

1. 默认 HTTP Endpoint 使用真实状态码、强类型成功响应和 ProblemDetails 错误。
2. 应用层返回 `Result<T>`/`PagedResult<T>`，传输层负责映射；不要让业务层依赖 HTTP。
3. 只有明确需要旧客户端兼容时才通过 Compatibility 层启用 Admin.NET 包络，且保留真实状态码。
4. 文件、流、SignalR、Webhook、健康检查和 `204 No Content` 不进入统一包络。
5. JSON 使用 System.Text.Json；公开热路径 DTO 加入模块源生成上下文。

## 6. 接入运行时

1. 在模块入口显式注册 Command/Query Handler、FluentValidation Validator、事件处理器、序列化上下文和持久化组件。
2. 审查 DI 生命周期，禁止 Singleton 捕获 Scoped 服务或请求级租户上下文。
3. 映射 Endpoint 与中间件顺序，确认验证位于事务之前，异常进入统一处理管道。
4. 为数据库、缓存和必要依赖注册真实就绪检查；空检查成功不能作为证据。
5. 使用结构化日志和稳定错误码，不记录敏感数据；高频日志遵循项目源生成与有界异步策略。
6. 新增或修改的所有手写源代码注释和 XML 文档注释必须使用清晰中文，并解释意图、不变量和风险。

## 7. 完成验证

1. 先运行受影响测试，再运行 Release 构建和四套测试程序集；命令见交付地图。
2. 使用 `--minimum-expected-tests` 防止零测试假通过；增删测试后同步 README、开发文档和 CI 的测试数量。
3. 数据变更必须实际运行 SQL Server/MySQL 集成测试。依赖不可用时报告未验证项，不得写成通过。
4. 检查 `git diff --check`、架构依赖、UTF-8、许可证和工作区状态。
5. 更新功能对标状态时严格区分 Mapped、Implementing、Implemented 与 Verified。
6. 执行 rules 复盘，再执行 Skills 复盘；达到门槛时在同一任务更新相应治理文件。

## 按需决策速查

| 变化 | 必须加入 | 不要加入 |
| --- | --- | --- |
| 新业务状态与写入 | Domain、Command、Validator、Handler、双库 SQL/测试 | 与当前用例无关的通用仓储 |
| 新数据库结构 | SQL Server/MySQL DbUp 迁移与回归测试 | 单库迁移或运行时自动建表 |
| 跨模块可靠通知 | Contract 事件、MessagePack、事务 Outbox、Handler | 事务提交前直接推送 |
| 高频读且有失效事件 | FusionCache 键、提交后失效、多实例验证 | 没有失效策略的永久缓存 |
| 标准 Web API | 授权、验证、Result 映射、ProblemDetails | 默认 Admin.NET 包络 |
| 旧管理端迁移 | Compatibility 适配与兼容测试 | 让兼容模型反向进入核心 |
| 无结构变化的只读查询 | Query、Handler、参数化 SQL、API/单元测试 | 不需要的迁移、Outbox 或缓存 |

## 常见错误

- 只完成 Endpoint 和表，遗漏注册、权限、租户、序列化或测试。
- SQL Server 通过后假定 MySQL 等价，未运行真实集成测试。
- 把 FluentValidation 当授权系统，或信任客户端传入的租户标识。
- 在事务内调用外部服务，或提交前发布事件、删除缓存。
- 把所有结果包装成 HTTP 200，破坏 ProblemDetails 和客户端语义。
- 更新代码后忘记更新中文注释、路线图状态、许可证通知或测试数量。
- 创建没有真实消费者的 Provider、SignalR、gRPC 或 AI 抽象。
