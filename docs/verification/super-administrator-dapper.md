# 超级管理员与 Dapper 多结果集验证记录

- 日期：2026-07-18
- 状态：自动化验证通过；能力边界见下文
- 数据库：SQL Server 2022、MySQL 8（Testcontainers）

## 已验证范围

### 受保护超级管理员

- `005_SuperAdministrator.sql` 在 SQL Server/MySQL 中创建非空角色标记、默认值和作用域检查约束；
- Bootstrap 幂等创建或升级唯一 `host-administrator` 系统角色，不再写入逐项权限；
- 登录、刷新和租户切换从持久化角色关系签发 `fullnet_super_administrator`，超级管理员 Token 不枚举权限 Claim；
- 权限处理、`/api/v1/me`、导航和租户切换统一通过代码权限目录解析，并拒绝未知权限、缺失有效作用域和作用域不匹配；
- SQL Server/MySQL 真实 API 链路均验证 Host/Tenant 动态权限；
- 专用授予/撤销服务锁定唯一系统角色，双库并发竞争均证明只能撤销一名，并保留最后一名有效超级管理员；成功变更同步轮换 SecurityStamp、撤销目标 Session 并写入事务内审计；
- JWT 验证完成后逐请求读取权威 Session/User，并核对安全戳、账号/锁定状态、Refresh Session 活性和 Actor/Effective/Tenant Scope；刷新消费、授予、撤销或上下文切换后旧 Access Token 立即返回 `401 identity.session_not_active`；
- `006_SuperAdministratorAuditActor.sql` 为两库增加可追责的 `ActorUserId` 与事件时间索引；授予/撤销审计与角色、安全戳、Session 变更处于同一事务；
- 远程列表、审计、授予和撤销 Endpoint 使用 Host 专属精确权限，写操作要求当前密码重认证；Development/Testing 可显式开启，Production 配置验证硬性禁止；
- Vue 与 Layui 均实现本地白名单导航、列表、审计、授予、撤销确认与 ProblemDetails，并通过共享契约、单测和双端 Mock E2E。

当前状态为 `Implemented`，不是完整 `Verified`。Production MFA/强认证 Provider、账号禁用/删除及普通角色 CRUD 的系统角色保护、真实后端浏览器链路仍待后续用户/角色切片。当前会话判定不使用缓存，因此没有缓存失效消费者，也不写无消费者的空 Outbox；未来引入 S0 缓存时必须同步补可靠传播与故障注入验证。

### Dapper QueryMultiple

- `IMultiResultQueryExecutor` 与 `IMultiResultReader` 不暴露 `GridReader`、连接或事务；
- 执行器复用 `DbSession`、当前事务、租户作用域守卫、超时、取消和 SQL 日志；
- 结果集只能串行读取，投影器返回前必须完整消费；异常或未完整消费后连接仍可继续使用；
- SQL Server/MySQL 真实多 Statement、多结果集测试通过；
- 架构测试阻止业务模块引用 Dapper/ADO.NET Provider，并阻止已拒绝的 Dapper 扩展包进入项目依赖。

`Dapper.SqlBuilder` 未引入。当前仓库没有同时包含两个以上可选条件或动态 JOIN/排序的真实列表消费者，继续保持门禁状态可避免无需求依赖和隐藏 SQL 构建层。

## 验证命令与结果

```powershell
dotnet build Full.NET.slnx -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --minimum-expected-tests 186
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --minimum-expected-tests 5
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 11
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --minimum-expected-tests 18 --timeout 15m
pnpm test:clients
pnpm test:e2e
```

本次切片新鲜验收结果：Release 全量构建 0 警告/0 错误，Unit 186、Compatibility 5、Architecture 11、Integration 18 全部通过；SQL Server/MySQL 真实 API 均完成错误密码、授予、目标旧令牌失效、列表、ActorUserId 审计、撤销和最后一名保护流程；客户端单元测试为 admin-i18n 8、client-contracts 17、uni-app 96、Vue 46、Layui 51，Vue/Layui 与 uni-app H5/微信/支付宝生产构建通过，Playwright 双端 Mock E2E 30 项全部通过；客户端工作区、本地化契约和依赖安全审计门禁也已通过。
