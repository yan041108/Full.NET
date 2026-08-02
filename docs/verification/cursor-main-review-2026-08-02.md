# Cursor 多任务主分支审查与修复验证（2026-08-02）

## 1. 审查边界

- 仓库：`Full.NET`
- 分支：本地 `main`
- 审查起点：`bd92ea7`
- 开始时 HEAD：`39b7b3c6203c0571c32a05e45c0a19b21af70ab2`
- 任务快照：`cursor-main-review-20260802`
- 重点：Admin.NET 吸收 Tasks 8–10、CodeGeneration、Settings 限时诊断策略、迁移 043、共享门禁和双管理端。

本记录只陈述本次窗口获得的新鲜证据。Docker Desktop 未运行，因此不把双库运行时恢复验证描述成通过。

## 2. 已确认并修复的问题

| 级别 | 问题 | 修复 |
|---|---|---|
| P0 | CodeGeneration 模块直接引用 SQL Server/MySQL 驱动和连接工厂，违反模块到提供程序依赖边界 | 把数据库会话锁抽到 `Full.NET.Data.Abstractions`，在 `Full.NET.Data.Dapper` 实现并注册，CodeGeneration 只依赖抽象 |
| P0 | 迁移 043 只在表首次创建时建索引；表存在但索引缺失或形状错误时无法恢复 | SQL Server/MySQL 脚本增加无损索引检查与修复；新增双库恢复测试并登记 043 迁移选择器 |
| P0 | SQL Server 043 在错误提供方索引占用聚集索引时可能先创建时间线聚集索引而失败 | 先删除两类错误索引，再按时间线聚集、提供方非聚集顺序重建；回归测试构造错误聚集索引 |
| P0 | 请求签名时间戳通过 `long.TryParse` 后直接调用 `FromUnixTimeSeconds`，极端值可抛异常并返回 500 | 增加 DateTimeOffset 范围校验和回归测试，非法值稳定返回 `invalid_timestamp` |
| P1 | Settings 缓存失效绕过统一缓存策略，直接创建 FusionCache 配置 | 改为通过 `ICachePolicyRegistry` 生成配置，再施加失效路径所需的前台异常语义 |
| P1 | CodeGeneration rollback-chain DTO 未登记 System.Text.Json 源生成 | 补齐请求/响应类型登记 |
| P1 | 单元测试、Integration 分片和迁移选择器计数落后于代码 | 更新唯一测试矩阵和矩阵契约：Unit 1052，Integration 302（49/49/98/106） |
| P1 | `settings.diagnostic-policy.*` 不符合权限码命名门禁，但直接改名会破坏已持久化角色和 API Key 授权 | 保留兼容值并登记精确、限期命名债务；要求后续用 052 双库迁移完成规范化 |
| P1 | Vue 管理端 Skip Link 激活后没有把焦点移到主内容 | Vue 壳层和登录页显式聚焦 `#main-content`，补充组件断言，聚焦 Playwright 用例通过 |

## 3. 新鲜验证结果

| 命令 | 结果 |
|---|---|
| `dotnet build Full.NET.slnx --configuration Release --no-restore` | 通过，0 warning / 0 error |
| `pnpm test:dotnet:unit` | 1052/1052 通过 |
| `pnpm test:dotnet:architecture` | 54/54 通过 |
| `pnpm test:dotnet:compatibility` | 7/7 通过 |
| `pnpm test:naming` | 24/24 通过 |
| `pnpm test:sql-safety` | 5/5 通过 |
| `pnpm test:governance` | 16/16 通过 |
| `pnpm test:skills` | Skill 59/59，合同 48/48 通过 |
| `pnpm test:integration:tooling` | 32/32 通过 |
| Integration fresh discovery | SQL Server API 49、MySQL API 49、migrations 98、infrastructure 106；总计 302，分片无缺失/重复 |
| `pnpm test:clients` | admin-i18n 8、client-contracts 108、uni-app 103、Layui 126、Vue 263，全部通过 |
| `pnpm build:clients` | 通过；仅第三方 Sass deprecation 提示 |
| `pnpm test:openapi` | 69/69 通过 |
| `pnpm test:helm` | 12/12 通过 |
| `pnpm test:observability-deploy` | 5/5 通过 |
| `pnpm test:load-profiles` | 6/6 通过 |
| `pnpm test:performance-governance` | 9/9 通过 |
| Vue Skip Link 聚焦 Playwright | 1/1 通过 |
| `pnpm test:e2e` | 107 通过、5 个按项目分流跳过、0 失败 |

## 4. 尚未关闭的验证缺口

1. `docker info` 无法连接 `dockerDesktopLinuxEngine`，所以本窗口不能运行迁移 043 的 SQL Server/MySQL 恢复测试、affected merge 和容器镜像门禁。
2. 请求签名仍缺请求体硬上限、配置启动校验和损坏 KeyHash 失败关闭；已进入后续计划，不把当前能力提升为 `Verified`。
3. 迁移 052 尚未创建。诊断策略权限码债务只有在角色权限和 API Key JSON 均完成双库迁移后才能删除。

## 5. 结论

当前修复恢复了编译、架构、命名、SQL 安全、治理、单元测试、客户端和静态 Integration 门禁。合并候选仍需完成 Docker 依赖的 043 双库恢复、affected merge、容器镜像检查和完整 E2E 复跑；完成前状态应保持“代码门禁通过、运行时双库待验证”。
