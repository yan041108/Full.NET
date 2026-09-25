# 认证事件日志第二轮验证（2026-09-25）

基线提交：`6fa511a5`。共享工作区同时存在其他任务的改动；本记录只覆盖认证事件日志相关的候选改动，不代表整库验证。

## 已取得的证据

| 验证 | 命令或场景 | 结果 |
| --- | --- | --- |
| .NET 构建 | `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore -v quiet -m:1 -nodeReuse:false` | 通过，0 警告、0 错误 |
| .NET 单测构建 | `dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore -v quiet` | 通过，0 警告、0 错误 |
| 安全审计写入失败 | MSTest DLL 过滤 `AuthenticationSecurityEventWriterTests` | 1/1 通过；写入异常及受影响行数不等于 1 均不当作成功 |
| 双库游标查询 | 集成测试过滤 `Authentication_events_are_queryable` | SQL Server/MySQL 2/2 通过；覆盖相同时间戳、无效游标、筛选不匹配 |
| 双库保留并发 | 集成测试过滤 `Authentication_event_retention_deletes_only_expired_rows` | SQL Server/MySQL 2/2 通过；两个独立 Worker scope 并发，各守批次上限，近期记录保留 |
| 双库 TOTP | 集成测试过滤 `TotpStrongReauthTests` | SQL Server/MySQL 2/2 通过；覆盖错误确认与成功强制再认证事件 |
| 双库密码恢复 | 集成测试过滤 `Account_recovery_follows_contract` | SQL Server/MySQL 2/2 通过；覆盖成功与挑战重放拒绝事件 |
| 双库管理员重置 | 集成测试过滤 `Host_user_management_follows_contract` | SQL Server/MySQL 2/2 通过；检查操作者关联 |
| 客户端契约 | `pnpm openapi:client:snapshot -- --update --no-build`、`pnpm openapi:client:generate`、`pnpm openapi:client:snapshot -- --offline --check` | 通过，SQL Server/MySQL 导出各 1/1 |
| Vue 类型 | `pnpm --filter @fullnet/admin typecheck` | 通过 |
| 真实浏览器 | `FULLNET_E2E_API_PORT=25149` 下运行 `playwright test tests/host-authentication-events.spec.mjs --project vue-admin` | 2/2 通过；隔离 API/Worker、真实 Vue 页面及详情弹窗；延迟旧查询的竞态回归先失败后通过 |
| Native AOT 分析器 | `pnpm test:aot:analyzers` | 当前候选代码通过，0 警告、0 错误 |
| Linux Native AOT 发布 | `pnpm test:aot:publish:linux` | 通过；生成 132267024 字节原生 API，17 条已准许的依赖警告；产物清单见 `artifacts/native-aot/linux-x64/publish-manifest.json` |
| 保留任务故障后重试 | MSTest 过滤 `AuthenticationEventRetentionFaultTests` | 1/1 通过；首轮数据库超时不计成功，下一轮正常删除 |
| 双客户端 SSO 审计关联 | MSTest 过滤 `Oidc_application_revoke_preserves_center_sso_for_other_apps` | SQL Server/MySQL 2/2 通过；两个应用事件共用中心会话 ID、应用会话 ID 各不相同 |
| 本地开发栈 | Aspire Migrator Development Seed、`GET /health/ready`、`POST /api/v1/auth/login`、Vue `GET /login#/` | 迁移 238 条、播种完成；API 200，管理员登录 200 且返回访问令牌，前端 200 |
| 影响集计划 | `pnpm test:integration:affected:plan -- --snapshot authentication-event-logs-phase2 --phase slice` | 计划生成成功；集合含共享工作区的 Workflow/DataApproval 改动，预计约 38 分钟；本次只执行本任务 Identity 定向双库场景，完整集合待 main CI 分片 |

## 未完成的门禁

- Linux 原生可执行文件已发布，但原生 API 运行门禁**未通过**：在运行时容器中启动到连接初始化后，Redis 报无法连接，SQL Client 分别报 11002/10051 网络错误，进程退出 139；将连接串改为容器 DNS 和 IP 均未成功。普通 SDK 容器在相同 Docker 网络对 SQL 1433、Redis 6380 的 TCP 探针可达；Docker Desktop 的 host 网络模式又无法访问 Aspire 本地代理端口。需要在 Linux CI 的生产等价网络重跑、收集原生进程诊断，不能以发布通过替代运行通过。先前与浏览器测试并行时 Docker `unexpected EOF`（退出码 125）失败，串行重试后仅发布通过。
- 多实例进程/网络级故障演练与完整认证事件目录尚未验收；保留任务的模拟数据库超时恢复只验证 runner 边界，不代替真实 Worker 故障演练。生产容量状态保持 `Capacity-not-verified`。

验证中重启了本地开发 Aspire AppHost；其 SQL Server 使用无持久卷的临时容器。原开发容器被删除后，旧本地开发数据可能丢失。该操作不涉及任何生产数据库。新容器已重新完成迁移、Development Seed 和 API 登录检查，但不能据此声称旧开发数据已恢复。
