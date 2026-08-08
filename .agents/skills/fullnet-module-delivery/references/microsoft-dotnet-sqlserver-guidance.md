# 微软 .NET 10 与 SQL Server 指导映射

## 使用边界

把微软官方资料作为实现与审查依据，把 Full.NET 的 `AGENTS.md`、`rules/`、Spec 和 ADR 作为项目决策依据。两者冲突时服从项目已批准基线，不得用通用云建议静默改变模块边界、Dapper、SQL Server/MySQL 双提供程序、API 契约或安全要求。

微软官方 `microsoft/skills` 当前可直接复用的能力主要是 `cloud-solution-architect`、`azure-identity-dotnet`、`azure-security-keyvault-keys-dotnet` 与 `azure-resource-manager-sql-dotnet`。其中 SQL Skill 只覆盖 Azure SQL 管理平面；执行查询、事务和连接仍使用 `Microsoft.Data.SqlClient` 与 Full.NET 自有 Dapper 边界。

## 按任务读取

| 任务 | 必须检查 | 微软官方依据 |
| --- | --- | --- |
| .NET 10 类库、公共契约或框架扩展点 | 命名、类型与成员设计、扩展性、异常、资源释放、兼容性 | [Framework Design Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/) |
| 应用或模块架构 | 职责边界、依赖方向、运行角色、可靠性、可观测性和演进成本 | [.NET architecture documentation](https://learn.microsoft.com/en-us/dotnet/architecture/) |
| ASP.NET Core 10 Endpoint 或宿主 | Authentication、Authorization、Data Protection、HTTPS、Secret、CSRF、CORS、XSS | [ASP.NET Core 10 security](https://learn.microsoft.com/en-us/aspnet/core/security/?view=aspnetcore-10.0) |
| .NET 性能问题 | CPU、分配、GC、线程池、锁、Trace、Dump 与持续监控 | [.NET diagnostics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/) |
| ASP.NET Core 热路径 | 异步 I/O、缓存、内存、限流、超时、压测 | [ASP.NET Core 10 performance](https://learn.microsoft.com/en-us/aspnet/core/performance/overview?view=aspnetcore-10.0) |
| SQL Server 数据访问 | 参数化、连接池、事务、取消、超时、TLS 和证书验证 | [SqlClient connection pooling](https://learn.microsoft.com/en-us/sql/connect/ado-net/sql-server-connection-pooling?view=sql-server-ver17)、[encryption and certificate validation](https://learn.microsoft.com/en-us/sql/connect/ado-net/encryption-and-certificate-validation?view=sql-server-ver17) |
| SQL Server 性能 | Query Store、执行计划、DMV、Extended Events、等待、锁和 DTA | [Performance monitoring and tuning tools](https://learn.microsoft.com/en-us/sql/relational-databases/performance/performance-monitoring-and-tuning-tools?view=sql-server-ver17)、[Query Store best practices](https://learn.microsoft.com/en-us/sql/relational-databases/performance/best-practice-with-the-query-store?view=sql-server-ver17) |
| SQL Server 安全 | 最小权限、参数化、TLS、Always Encrypted、RLS、审计和补丁 | [SQL Server security best practices](https://learn.microsoft.com/en-us/sql/relational-databases/security/sql-server-security-best-practices?view=sql-server-ver17) |

## 框架与公共 API 设计

1. 先确定调用方、稳定期和兼容承诺，再决定是否公开类型或拆出 Contracts 项目。
2. 保持命名、类型、成员和异常语义一致；公开 API 不暴露传输、数据库或第三方实现细节。
3. 优先组合与窄接口；只有真实扩展者和稳定不变量证明需要时才开放继承、虚成员或回调。
4. 对取消、异步、资源释放、空值和失败语义建立契约测试；公共 API 变化必须执行兼容性检查。
5. Framework Design Guidelines 中较早的建议必须与 .NET 10、ASP.NET Core 10 和仓库现行分析器交叉验证，不能仅因其来自旧版指南而机械采用。

## ASP.NET Core 10 安全检查

- 在 Endpoint 失败关闭并使用稳定权限码；客户端隐藏入口不能替代服务端授权。
- 不信任请求中的租户、角色或资源所有者标识；从受信任会话解析后再次验证资源归属。
- Secret 不进入仓库、日志、ProblemDetails 或普通配置样例；生产环境使用受控 Secret 注入。
- 对 Cookie/Token、CSRF、CORS、上传、重定向、限流和敏感响应分别建立失败测试。
- 使用标准状态码和 ProblemDetails；禁止向外暴露堆栈、SQL、连接字符串或内部标识。

## Microsoft.Data.SqlClient 与 Dapper 检查

- 所有值使用参数化 SQL；动态标识符只能来自受控目录并经过白名单或生成器。
- 及时释放连接、命令和 Reader，让连接返回池；连接字符串差异必须有意图，避免连接池碎片。
- 生产连接要求加密并验证服务器证书，基线为 `Encrypt=True;TrustServerCertificate=False`；不得用跳过证书验证修复部署问题。
- 为超时、取消、死锁、瞬态失败和事务回滚声明语义；重试必须保持幂等且不能包围未知外部副作用。
- SQL Server 专项优化不得删除 MySQL 对等实现或验证；Provider 差异通过既有 Resolver/SQL Scope 隔离。

## 性能路由

遇到请求延迟、吞吐、数据库往返、Query Store、执行计划、等待、锁、缓存、Worker 或包体问题时，必须改用 `fullnet-performance-hardening` 执行证据驱动流程。本参考只提供官方入口，不替代性能 Skill 的基线、实验、双库和回归门禁。

## 来源状态

以上链接于 2026-08-08 核对。外部版本、默认值或产品范围可能变化；实施前重新核对目标 .NET、ASP.NET Core、Microsoft.Data.SqlClient 与 SQL Server 版本，不在 Skill 中复制易漂移的版本号或测试数量。
