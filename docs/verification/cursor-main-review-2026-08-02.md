# Cursor 多任务主分支审查与修复验证（2026-08-02）

## 1. 审查边界

- 仓库：`Full.NET`
- 分支：本地 `main`
- 审查起点：`bd92ea7`
- 开始时 HEAD：`ffe19ec`（审查修复提交）
- Task 0 完成 HEAD：`33a5e99`
- 任务快照：`cursor-main-review-20260802`
- 重点：Admin.NET 吸收 Tasks 8–10、CodeGeneration、Settings 限时诊断策略、迁移 043、共享门禁和双管理端。

本记录只陈述本次窗口获得的新鲜证据。Task 0 于 2026-08-02 在 Docker Desktop 可用环境完成双库运行时验证。

## 2. 已确认并修复的问题

| 级别 | 问题 | 修复 |
|---|---|---|
| P0 | CodeGeneration 模块直接引用 SQL Server/MySQL 驱动和连接工厂，违反模块到提供程序依赖边界 | 把数据库会话锁抽到 `Full.NET.Data.Abstractions`，在 `Full.NET.Data.Dapper` 实现并注册，CodeGeneration 只依赖抽象 |
| P0 | 迁移 043 只在表首次创建时建索引；表存在但索引缺失或形状错误时无法恢复 | SQL Server/MySQL 脚本增加无损索引检查与修复；新增双库恢复测试并登记 043 迁移选择器 |
| P0 | SQL Server 043 在错误提供方索引占用聚集索引时可能先创建时间线聚集索引而失败 | 先删除两类错误索引，再按时间线聚集、提供方非聚集顺序重建；回归测试构造错误聚集索引 |
| P0 | 请求签名时间戳通过 `long.TryParse` 后直接调用 `FromUnixTimeSeconds`，极端值可抛异常并返回 500 | 增加 DateTimeOffset 范围校验和回归测试，非法值稳定返回 `invalid_timestamp` |
| P1 | Settings 缓存失效绕过统一缓存策略，直接创建 FusionCache 配置 | 改为通过 `ICachePolicyRegistry` 生成配置，再施加失效路径所需的前台异常语义 |
| P1 | CodeGeneration rollback-chain DTO 未登记 System.Text.Json 源生成 | 补齐请求/响应类型登记 |
| P1 | 单元测试、Integration 分片和迁移选择器计数落后于代码 | 更新唯一测试矩阵和矩阵契约：Unit 1052，Integration 308（49/49/104/106） |
| P1 | `settings.diagnostic-policy.*` 不符合权限码命名门禁，但直接改名会破坏已持久化角色和 API Key 授权 | 保留兼容值并登记精确、限期命名债务；要求后续用 052 双库迁移完成规范化 |
| P1 | Vue 管理端 Skip Link 激活后没有把焦点移到主内容 | Vue 壳层和登录页显式聚焦 `#main-content`，补充组件断言，聚焦 Playwright 用例通过 |
| P1 | 生命周期 SQL 运行时 MySQL 测试未使用 Binary16 Guid 连接策略 | `GeneratedLifecycleSqlRuntimeIntegrationTests` 改用 `MySqlConnectionStringPolicy.Create(..., Binary16)` |

## 3. 新鲜验证结果

| 命令 | 结果 |
|---|---|
| `dotnet build Full.NET.slnx --configuration Release --no-restore` | 通过，0 warning / 0 error |
| `pnpm test:dotnet:unit` | 1057/1057 通过（含签名选项校验、CanonicalRequest 与模块注册回归） |
| `pnpm test:dotnet:architecture` | 54/54 通过 |
| `pnpm test:dotnet:compatibility` | 7/7 通过 |
| `pnpm test:naming` | 24/24 通过 |
| `pnpm test:sql-safety` | 5/5 通过 |
| `pnpm test:governance` | 16/16 通过 |
| `pnpm test:skills` | Skill 59/59，合同 48/48 通过 |
| `pnpm test:integration:tooling` | 32/32 通过 |
| Integration fresh discovery | SQL Server API 49、MySQL API 49、migrations 104、infrastructure 106；总计 308，分片无缺失/重复 |
| `pnpm test:clients` | admin-i18n 8、client-contracts 108、uni-app 103、Layui 126、Vue 263，全部通过 |
| `pnpm build:clients` | 通过；仅第三方 Sass deprecation 提示 |
| `pnpm test:openapi` | 69/69 通过 |
| `pnpm test:helm` | 12/12 通过 |
| `pnpm test:observability-deploy` | 5/5 通过 |
| `pnpm test:load-profiles` | 6/6 通过 |
| `pnpm test:performance-governance` | 9/9 通过 |
| `Migration052IdentityDiagnosticPolicyPermissionRecoveryTests`（SQL Server/MySQL ×3） | 6/6 通过 |
| `pnpm test:naming` | 24/24 通过（含 OPENJSON/JSON_TABLE 扫描器豁免） |
| `pnpm test:integration:affected -- --snapshot settings-diagnostic-permission-normalization-20260802 --phase merge` | 60/60 通过（Identity + Settings + migration-052 + smoke，双库非零发现） |
| `Host_signature_authentication_follows_contract_with_sql_server/mysql` | 2/2 通过（请求体上限、重复头、损坏 KeyHash 失败关闭） |
| `pnpm test:integration:affected -- --base 33a5e99 --phase merge` | 60/60 通过（Task 1+2 合并影响集：Identity、Settings、migration-052、smoke，双库非零发现） |
| Task 3 聚焦 Integration（签名/出站/模块目录/043/保留/可信代理） | 12/12 通过（SQL Server/MySQL 非零发现） |
| Task 3 Architecture（Roslyn/ApplicationPart 门禁） | 35/35 通过 |
| `pnpm test:openapi`（Task 3 窗口） | 69/69 通过 |
| `pnpm test:container-images` | 3/3 契约通过 |
| `pnpm test:e2e` | 107 通过、5 跳过、0 失败 |
| Docker/Ryuk/Testcontainers 残留 | `docker ps -a` 空（residual=0） |

## 4. 尚未关闭的验证缺口

1. ~~请求签名仍缺请求体硬上限、配置启动校验和损坏 KeyHash 失败关闭。~~ **Task 2 已于 2026-08-02 完成实现与双库签名 Integration（2/2）、Unit 1057 与合并影响集 affected merge（60/60）；待提交。**
2. ~~迁移 052 尚未创建。诊断策略权限码债务只有在 Task 1 双库迁移完成后才能删除。~~ **Task 1 已于 2026-08-02 完成双库 052 恢复测试（6/6）与命名门禁；合并影响集 affected merge（60/60）已通过；待提交。**
3. ~~Tasks 8–10 真实栈验收（Task 3）与 Document 规格（Task 4）尚未开始。~~ **Task 3 已于 2026-08-02 完成双库聚焦验收（12/12 Integration + Architecture 35/35 + OpenAPI 69/69）；三项能力维持 `Build-verified`；见 [`adminnet-tasks-8-10-realstack-2026-08-02.md`](adminnet-tasks-8-10-realstack-2026-08-02.md)。Task 4 尚未开始。**

## 5. 结论

**Task 0 合并门禁已关闭（Build-verified）。** 审查修复（`ffe19ec`）与 MySQL 生命周期运行时 Binary16 连接修复已通过双库 043 恢复、affected merge（109）、容器镜像契约、静态门禁与 E2E（107）。

**Task 1（052 权限码规范化）** 与 **Task 2（请求签名失败关闭）** 均已实现并通过合并影响集验证（`--base 33a5e99 --phase merge`，60/60，双库非零发现）。**Task 3（Tasks 8–10 真实栈验收）** 已补齐双库聚焦证据并更新路线图；三项能力维持 `Build-verified`。当前工作区仍含 Task 1/2 未提交改动；建议按任务切片分别提交。
