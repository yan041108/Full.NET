# 外部静态分析复核与吸收记录

- 复核日期：2026-07-18
- 复核起点：`d5c109c`
- 输入：项目所有者提供的第三方静态分析报告
- 方法：逐项对照当前代码、测试、能力矩阵和既有硬化计划；外部报告未运行构建与测试，因此不把其推断直接视为缺陷

## 结论摘要

报告对项目成熟度的总体判断基本准确：Full.NET 当前仍是 Identity/Tenancy 与工程底座，不是 Admin.NET 全功能后台。报告也正确识别了 Outbox 死信、真实浏览器 E2E、租户 SQL 守卫和存量命名债务等后续风险。不过，它使用的代码状态早于当前 `main`，并误判了访问令牌实时校验、Refresh 事务边界、MySQL Outbox 租约和命名治理状态。

本轮没有照单全收。已处理三个能够由当前证据直接闭环的问题：

1. Refresh/Logout 增加共享匿名会话变更限流、标准 429 状态和精确 Origin 校验；
2. 删除没有实现者和调用者的 `IFullNetModule.InitializeAsync`，不保留虚假生命周期承诺；
3. 新增 `Full.NET.Composition`、`FullNetHostProfile` 与共享模块目录，Api/Worker/Migrator 不再复制模块注册清单。

## 逐项真实性判定

| 报告结论 | 判定 | 当前证据与处置 |
|---|---|---|
| 业务模块和 Admin.NET 对标能力很少 | 存在，但属于成熟度边界 | 当前只有 Identity/Tenancy，状态矩阵已禁止宣传为完整后台；继续按纵向切片交付，不为追求数量降低安全和双端门禁 |
| `InitializeAsync` 是死接口 | 原先存在，已处理 | 全仓没有实现者或调用者；本轮按 YAGNI 删除接口，并以 `ModuleLifecycleTests` 锁定决定 |
| 三宿主手工装配容易漂移 | 原先存在，已处理当前官方模块 | 新增 Composition 层与 Api/Worker/Migrator 显式 Profile；Worker 只注册后台最小能力，Unit 与 Architecture Tests 阻止宿主绕过目录 |
| Outbox 无最大重试、死信和跨版本升级链 | 存在 | 保留为 P1；`SchemaVersion`、整数 Key 和精确版本路由已经存在，真正缺口是版本共存/升级、退役和毒消息闭环 |
| Outbox 版本必须写入 MessagePack payload | 不接受 | 版本已与 MessageType、ContentType 一起保存为不可分割的 Envelope 元数据；重复写入正文会制造双重事实源 |
| Playwright 主要 Mock API、uni-app 缺开发者工具/真机 | 存在 | Mock E2E 与真实 API 集成测试职责不同；真实浏览器栈和小程序工具验收继续保留为 P1 门禁 |
| Refresh 重用与会话撤销缺测试 | 已过期 | 当前真实 API 测试已覆盖重用检测、family 撤销、Logout 后 Refresh 拒绝；仍应补跨 Tab 和故障注入专项 |
| 命名门禁和生成器仍是 Designing | 已过期 | `contracts/naming/`、`pnpm test:naming` 与 `Full.NET.Data.CodeGeneration` 已实现；90 项精确存量债务仍待 1.0 前迁移 |
| JWT 权限无法实时吊销 | 不存在于当前实现 | `FullNetJwtBearerEvents` 在每次认证后调用 `AccessSessionValidator` 回查 Session、账号状态、安全戳和有效租户；Logout、轮换、禁用和安全戳变化可即时使旧 Access Token 失效 |
| Refresh consume/insert/audit 未处于事务 | 不成立 | Refresh 与 Logout Command 均实现 `ITransactionalCommand`，`CommandDispatcher` 在 Handler 外统一调用 `ICommandTransaction`；本轮增加命令契约测试防回归 |
| Refresh/Logout 无限流和 Origin 校验 | 原先存在，已处理 | 两端点使用 `identity-session-mutation` 固定窗口策略，30 次/分钟/IP、无排队，并复用精确 Origin 验证；SQL Server/MySQL 真实 API 场景验证不可信 Origin 返回 ProblemDetails 403，超限返回带稳定错误码 `identity.authentication.rate_limited` 的 ProblemDetails 429 |
| `@TenantId` 只是文本包含、Global 缺自动限制 | 存在 | 当前守卫能阻止缺上下文和完全漏参数，但不能证明参数进入有效谓词；保留为 P1，后续采用 Statement 元数据/精确目录与架构 Allowlist，不引入脆弱的通用 SQL 解析器 |
| 认证只能在 Host 范围运行 | 存在，是当前产品边界 | 当前只交付 Host 管理员认证，租户级账号登录尚未实现；在设计租户用户模型前不把 HostOnly 静默改成 Global |
| MySQL Outbox 两步领取天然弱于 SQL Server | 结论过度 | MySQL 的 UPDATE 与按唯一 LockId SELECT 位于同一 `ICommandTransaction`，并发更新由数据库行锁串行化；真实缺口是缺专门的多 Worker 并发压力/崩溃恢复测试，不是已证实的数据竞争 |
| Provider switch 重复 | 部分存在 | 小型、显式分支符合双库语义目录决策；只有出现第三处同类分支或行为漂移时再抽取，禁止演化成上帝方言接口 |
| `DbConnectionFactory` 快照 Options 是热更新缺陷 | 不接受 | Provider 与连接串属于启动期验证配置；运行中切换会破坏连接/事务一致性，不把热更新作为承诺 |
| CacheOptions 手工绑定会绕过验证 | 表述不准确 | 当前代码在注册时显式执行等价验证并立即失败；统一 Options 管线可改善一致性，但不是现有生产绕过 |
| 两次 `AddOpenTelemetry()` 会生成两个 Provider | 不成立 | OpenTelemetry 的 DI Builder 调用用于合并同一服务集合的仪表注册；未发现重复 Provider 或重复导出证据 |
| FluentValidation 模块 opt-in 容易遗漏 | 风险存在、机制有意 | Validator 属于模块显式能力，不能由 Modularity 猜测注册；后续用模块交付 Skill 与架构测试检查声明/注册一致性 |
| 文档领先实现 | 存在且已治理 | 状态矩阵继续作为唯一能力总览；设计、计划和实现必须分别标记，本文也不把 P1 风险写成已修复 |

## 保留的后续顺序

1. P0：Seed Baseline/Overlay 生产闭环；
2. P1：Outbox 最大重试、死信、版本共存与重放审计；
3. P1：TenantRequired 语义元数据和 Global Statement 精确目录；
4. P1：Vue/Layui 真实后端浏览器安全链路；
5. P1：认证故障注入与跨 Tab Refresh 协调；
6. 1.0 前：执行 90 项存量命名债务的 Expand/Contract 迁移。

## 本轮验证证据

- Release 构建：0 警告、0 错误；
- Unit Tests：209/209；Compatibility Tests：5/5；Architecture Tests：16/16；
- 前端工作区与命名门禁：`pnpm test:workspace`、`pnpm test:naming` 均通过；
- 项目 Skill 契约：39 项检查通过，官方 Skill 结构校验通过；
- 双库 Integration Tests：首次因 Docker Desktop 未运行而在容器创建前失败；恢复 Docker 后全套执行为 16/18，唯一同源失败是既有租户测试未携带新要求的 Origin；修正测试请求后，SQL Server/MySQL 两个原失败场景定向重跑 2/2。该组合证明本轮 18 个场景均有新鲜成功输出，但不表述为一次全量 18/18。
