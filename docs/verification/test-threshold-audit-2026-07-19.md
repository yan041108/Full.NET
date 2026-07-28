# 测试数量门槛核对记录

- 日期：2026-07-19
- 类型：门槛核对与新鲜验证
- 状态：已完成
- 代码基线：`7894c8d`（`docs: converge agents baselines to rules pointers`）
- 范围：四套 .NET 测试程序集的 `--minimum-expected-tests` 门槛与 README、CI、`getting-started.md`、`delivery-map.md` 声明是否一致，并在真实双库环境执行 Integration 全套
- 方法：Release 构建后按 Microsoft Testing Platform 直接运行测试 DLL；Integration 使用 Testcontainers 拉起 SQL Server 2022 与 MySQL 8.0

## 声明门槛与文档来源

| 套件 | 声明门槛 | 权威来源 |
| --- | ---: | --- |
| `Full.NET.UnitTests` | 291 | `README.md`、`docs/development/getting-started.md`、`.github/workflows/ci.yml`、`.agents/skills/fullnet-module-delivery/references/delivery-map.md` |
| `Full.NET.CompatibilityTests` | 5 | 同上 |
| `Full.NET.ArchitectureTests` | 24 | 同上 |
| `Full.NET.IntegrationTests` | 58 | 同上 |

核对结论：四处 canonical 门槛完全一致，无文档内部漂移。历史计划/评审快照（如 `.superpowers/sdd/*` 中的 `277/287/20/22`）属于过程留档，不作为当前门槛。

## 新鲜自动验证

环境：Windows 10、.NET SDK `10.0.400-preview.0.26322.102`、Docker Desktop（Linux containers）、Testcontainers `ryuk:0.14.0`。

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Release 构建 | `dotnet build Full.NET.slnx -c Release` | 0 警告、0 错误 |
| Unit Tests | `--minimum-expected-tests 291` | **291/291** 通过，约 7s |
| Compatibility Tests | `--minimum-expected-tests 5` | **5/5** 通过，约 3s |
| Architecture Tests | `--minimum-expected-tests 24` | **24/24** 通过，约 9s |
| Integration Tests | `--minimum-expected-tests 58 --timeout 45m` | **58/58** 通过，25m 54s；Workers=2，真实 SQL Server + MySQL |

Integration 执行前曾终止一次孤儿宿主进程并干净重跑；完整摘要见仓库根目录临时日志 `integration-run.log`（未纳入版本控制）。

## 结论

- 声明门槛 **291/5/24/58** 与当前代码基线实测数量 **完全一致**。
- 四套测试在声明门槛下 **全部通过**，无需调整 `--minimum-expected-tests` 数字。
- 本记录 **不能** 将任意能力矩阵项整体提升为 `Verified`；各能力仍须满足自身规格中的跨端、人工或生产验收条件。

## 未验证项

- 客户端工作区：`pnpm test:clients`、`pnpm test:e2e`、`pnpm test:e2e:uniapp` 未在本核对中重跑。
- 治理校验：`pnpm test:governance` 在基线 `7894c8d` 已落地，但不在本次四套 .NET 核对范围内。
- 基准测试：`benchmarks/Full.NET.Benchmarks` 序列化基线未重跑。
- 本记录绑定提交 `7894c8d`；后续未提交工作区变更不在本证据范围内。

## 增补（2026-07-19，基线 `84ab8f5` 之后）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **58 → 60** | 新增 `Session_refresh_and_context_switch_races_are_linearized`（SQL Server + MySQL 各 1） |
| UnitTests 门槛 **293 → 294** | 新增 `E2eHostViewerSeedContributorTests` |
| 新鲜验证 | `pnpm test:e2e:real` **16/16**（SQL Server）；`pnpm test:e2e:real:mysql` **16/16**（MySQL）；新增 `permission-denied`、`session-cross-tab`；CI `real-stack-e2e-mysql` |
| client-contracts | `session-refresh-coordinator` 无 Web Locks 时 `sessionStorage` 互斥回退，单测 **27** 项 |

四处 canonical 门槛已同步为 **296/6/26/66**。

## 增补（2026-07-19，基线 `9760590` 之后）

| 变更 | 说明 |
| --- | --- |
| UnitTests 门槛 **295 → 296** | `FullNetJsonOptionsTests` 验证 Guid 小写连字符序列化 |
| Compatibility 门槛 **5 → 6** | `AdminNetApiResultMapperTests` 验证 Guid 包络 JSON |
| Integration 门槛 **64 → 66** | 新增 `UuidExternalContractIntegrationTests`（SQL Server + MySQL 各 1） |

## 增补（2026-07-19，基线 `b7ff745` 之后，P0 UUID Task 5 Step 5）

环境：Windows 10、.NET SDK `10.0.400-preview.0.26322.102`、Docker Desktop（Linux containers）、Testcontainers。

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Unit 聚焦 | `--filter "FullyQualifiedName~Guid\|FullyQualifiedName~FullNetJson"`，`--minimum-expected-tests 6` | **9/9** 通过，约 0.4s |
| Integration 聚焦 | `--filter "FullyQualifiedName~Guid\|FullyQualifiedName~IdentityApi\|FullyQualifiedName~TenancyApi\|FullyQualifiedName~Outbox\|FullyQualifiedName~MultiResult"`，`--minimum-expected-tests 20` | **26/26** 通过，约 8m 32s |
| Integration 全量 | `--minimum-expected-tests 66 --timeout 45m` | **66/66** 通过，27m 59s；Workers=2，真实 SQL Server + MySQL |
| Architecture 全量 | `--minimum-expected-tests 26` | **26/26** 通过 |
| Compatibility 全量 | `--minimum-expected-tests 6` | **6/6** 通过 |

结论：声明门槛 **296/6/26/66** 与实测一致；UUID v7 应用持久化、读取路径与外部 JSON 契约相关新增测试在双库聚焦与 Integration 全量下均通过。本记录不将 UUID 能力整体提升为 `Verified`（生产维护窗口与 Runbook 实跑仍缺）。

## 增补（2026-07-19，基线 `f855e86` 之后，P0 UUID Task 6）

| 变更 | 说明 |
| --- | --- |
| UnitTests 门槛 **296 → 304** | `PrimaryKeyTypeMappingTests`（UUID v7 / Snowflake 四端映射与互斥） |
| Node 治理 | `validate-uuid-storage-sql.mjs` + `uuid-storage-sql-governance.test.mjs`（FNUUID001–003）；`pnpm test:naming` **20/20** |
| Skill 合同 | `fullnet-module-delivery` 新增 UUID 主键场景与 `PrimaryKeyTypeMapping`/`BINARY(16)`/`CLUSTERED` 术语 |

四处 canonical 门槛已同步为 **304/6/26/66**。

## 增补（2026-07-19，基线 `55e82d4` 之后，P0 UUID Task 7 Step 1）

| 验证 | 结果 |
| --- | --- |
| Release 构建 | 0 警告、0 错误 |
| 四套 .NET 测试 | **304/6/26/66** 全部通过（Integration 26m 06s） |
| Node/客户端 | `test:naming` 20、`test:governance` 6、`test:skills` 44、`test:workspace`、`test:clients`、`audit:clients` 通过 |
| NuGet 漏洞扫描 | 无已知漏洞 |
| `git diff --check` | 通过 |

详情见 [UUID v7 验证记录](uuid-v7-primary-key-storage-2026-07-19.md)。真实栈 E2E 未在本轮重跑。

## 增补（2026-07-19，命名规范化 Task 2 Step 5）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **66 → 74** | 新增 `NamingExpandMigrationTests`（4）与 `NamingPartialRecoveryTests`（4），双库 Expand 与半完成恢复 |

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Integration 聚焦 | `--filter "NamingExpand\|NamingPartialRecovery"`，`--minimum-expected-tests 8` | **8/8** 通过，约 9m 54s |

四处 canonical 门槛已同步为 **308/6/26/74**（Integration 全量 74 项待下一轮 CI/本地全量重跑确认）。

## 增补（2026-07-19，命名规范化 Task 3 Step 5）

| 变更 | 说明 |
| --- | --- |
| UnitTests 门槛 **304 → 308** | `IntegrationEventHandlerMatcherTests`（2）与 `OutboxProcessorTests` 别名/未知类型（2） |
| 应用切换 | Tenancy 写入 `fn_tenancy_tenant` + `CreatedAtUtc`；Outbox 写入 `MessageType`/`OccurredAtUtc`；Worker 兼容 legacy MessageType |

| 验证 | 结果 |
| --- | --- |
| Unit 聚焦（Outbox/Tenancy/Messaging） | **8/8** 通过 |
| Integration `TenantProvisioning` | **2/2** 通过（双库） |
| `pnpm test:naming` | **22/22** 通过 |
| Architecture | **26/26** 通过 |

## 增补（2026-07-21，声明门槛对齐；外部分析吸收）

来源：[`external-review-2026-07-21.md`](external-review-2026-07-21.md) 指出本文件曾停在 **66/74**，与当前 CI/README 漂移。

| 套件 | 当前声明门槛 | 权威来源 |
| --- | ---: | --- |
| `Full.NET.UnitTests` | **314** | `README.md`、`docs/development/getting-started.md`、`.github/workflows/ci.yml`、`.agents/skills/fullnet-module-delivery/references/delivery-map.md` |
| `Full.NET.CompatibilityTests` | **7** | 同上 |
| `Full.NET.ArchitectureTests` | **26** | 同上 |
| `Full.NET.IntegrationTests` | **87**（全量 / `push main`）；PR 冒烟 **2** | 同上；PR filter=`migration_is_idempotent_and_creates_binary_outbox_schema` |

核对结论：四处 canonical 声明门槛现为 **314/7/26/87**，彼此一致。自 2026-07-19 增补以来的增长主要来自命名 Expand/Contract/Recovery、UUID、会话竞态与相关 Unit/Compatibility 用例（中间过程见上文增补段；部分增补未逐次把 Integration 全量数字写回本表，造成本次漂移）。

| 未在本增补完成的验证 | 说明 |
| --- | --- |
| Integration **85/85** 新鲜全量重跑 | 本轮为文档吸收，未附 90m 级全量日志；下次发布候选或合入敏感数据变更时必须补新鲜证据 |
| PR 冒烟加宽 | 已记入[硬化计划](../superpowers/plans/2026-07-18-architecture-hardening.md) Task 13；实现前 PR 仍只保证迁移冒烟 2 项 |

能力矩阵中个别单元格若仍写历史 **304/6/26/66**，以本节声明门槛与 CI 为准，并在对应能力下次更新时改写证据列。

## 增补（2026-07-21，Host 用户管理 Task 1 RED 夹具）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **85 → 87** | 新增 `Host_user_management_follows_contract_with_sql_server` 与 `Host_user_management_follows_contract_with_mysql`（RED，待 Task 2–3 实现端点后转绿） |

四处 canonical 门槛已同步为 **314/7/26/87**。

## 增补（2026-07-21，Host 用户管理 Task 4–5 客户端与真实栈）

| 变更 | 说明 |
| --- | --- |
| 客户端单测 | 管理端与共享包 **122 → 138**（`host-users` 契约、Vue/Layui 用户 API/控制器各 +1） |
| Mock parity E2E | **30 → 32**（新增 Host 用户列表/创建/禁用场景 × 双端） |
| Real-stack E2E | **16 → 20**（新增 `host-users.spec.mjs` 两项 × 双端）；验证记录见 [identity-user-management-2026-07-21.md](identity-user-management-2026-07-21.md) |
| OpenAPI 夹具 | 新增 `contracts/openapi/identity-host-users-v1.json` 与 `pnpm test:openapi`（CI 已接入） |

## 增补（2026-07-21，用户-角色分配纵向切片）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **97 → 99** | 新增 `Host_user_roles_management_follows_contract_with_sql_server` 与 `Host_user_roles_management_follows_contract_with_mysql` |
| OpenAPI | **12 → 14**（`identity-host-user-roles-v1.json`） |
| 客户端单测 | 管理端与共享包 **157 → 158**（Vue 用户 API +1） |
| 四处 canonical 门槛 | **322/7/26/99**；验证记录见 [identity-user-roles-assignment-2026-07-21.md](identity-user-roles-assignment-2026-07-21.md) |

## 增补（2026-07-21，运行时数据范围并集纵向切片）

| 变更 | 说明 |
| --- | --- |
| Unit 门槛 **319 → 322** | 多角色并集 SQL 投影 +3 |
| Integration 门槛 **99 → 101** | 租户机构 custom 范围过滤 SQL Server/MySQL +2 |
| 四处 canonical 门槛 | **322/7/26/101**；验证记录见 [identity-runtime-data-scope-2026-07-21.md](identity-runtime-data-scope-2026-07-21.md) |

## 增补（2026-07-21，Seed 双库契约 Task 6）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **101 → 103** | `SqlServer_development_seed_contract` / `MySql_development_seed_contract` +2 |
| 四处 canonical 门槛 | **322/7/26/103**；验证记录见 [seed-dual-database-contract-2026-07-21.md](seed-dual-database-contract-2026-07-21.md) |

## 增补（2026-07-21，Production Seed Secret + 超管禁用保护）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **103 → 105** | Production 缺 Bootstrap Secret SQL Server/MySQL +2 |
| 四处 canonical 门槛 | **322/7/26/105**；见 [seed-production-secret-and-super-admin-disable-2026-07-21.md](seed-production-secret-and-super-admin-disable-2026-07-21.md) |



## 增补（2026-07-21，TOTP 强认证 Provider）

| 变更 | 说明 |
| --- | --- |
| Unit 门槛 **322 → 331** | Validator/Management/TOTP 算法相关 +9 |
| Integration 门槛 **105 → 107** | TotpStrongReauth SQL Server/MySQL +2 |
| 四处 canonical 门槛 | **331/7/26/107**；见 [identity-totp-strong-reauth-2026-07-21.md](identity-totp-strong-reauth-2026-07-21.md) |

## 增补（2026-07-21，租户上下文 Host 目录 SQL 作用域）

| 变更 | 说明 |
| --- | --- |
| Unit 门槛 **331 → 333** | `HostCatalogSqlScopeTests`（`ListActiveHostMenus` / `FindHostUserById` 在租户上下文可校验）+2 |
| 四处 canonical 门槛 | **333/7/26/107**；见 [identity-tenant-navigation-host-sql-scope-2026-07-21.md](identity-tenant-navigation-host-sql-scope-2026-07-21.md) |

## 增补（2026-07-22，Pre-v1 发布候选逻辑克隆升级演练）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **107 → 109** | `NamingReleaseCandidateUpgradeDrillTests` SQL Server/MySQL +2 |
| 四处 canonical 门槛 | **333/7/26/109**；见 [pre-v1-naming-normalization.md](pre-v1-naming-normalization.md) |
| 债务计数文档 | 权威 `naming-debt.json` **85**（含 015 双库 dynamic_sql）；能力矩阵/治理记录已对齐 |

## 增补（2026-07-22，架构硬化 Task 3A E2E 场景数据发布物隔离）

| 变更 | 说明 |
| --- | --- |
| Unit 门槛 **333 → 332** | 删除仅覆盖生产程序集内 `E2eHostViewerSeedContributor` 的 1 项单测；真实栈查看者改由测试目录脚本创建 |
| Architecture 门槛 **26 → 28** | 基线实际已有 27 项；新增 1 项生产发布程序集测试场景类型、Contributor 名和配置节门禁后，新鲜运行 **28/28** |
| Integration 门槛 | 保持 **109**；`DevelopmentSeedTests` 仍为 SQL Server/MySQL 各 1 项，但 Contributor 预期改为生产 Baseline + Development Overlay |
| 四处 canonical 门槛 | **332/7/28/109**，已同步 README、getting-started、CI 与 Skill delivery-map |

本轮 Unit **332/332**、Architecture **28/28**、三宿主 Release 发布物扫描均通过。SQL Server/MySQL Integration 与真实栈因当前机器没有可用容器运行时而未执行，不能据此声明双库新路径已通过；详见 [Seed 双库契约验证记录](seed-dual-database-contract-2026-07-21.md)。

## 增补（2026-07-22，架构硬化 Task 4A 跨模块实现依赖）

| 变更 | 说明 |
| --- | --- |
| Unit 门槛 **332 → 341** | 稳定字符串依赖契约与 Registry 的确定顺序、注册快照、空/重复模块键、null/空白/未知依赖及单节点/多节点循环共 +9；新鲜运行 **341/341** |
| Architecture 门槛 **28 → 30** | 生产模块不得跨逻辑模块引用非 Contracts 项目或开放生产友元的门禁 +1，嵌套源文件与常见 IVT 写法的负向夹具 +1；新鲜运行 **30/30** |
| Compatibility / Integration 门槛 | 保持 **7/109**；Task 4A 未增删这两套测试发现项 |
| 四处 canonical 门槛 | **341/7/30/109**，已同步 README、getting-started、CI 与 Skill delivery-map |

Task 4A 首轮聚焦 Integration 中，使用 Organization.Contracts 空目录替身保持最小依赖闭包的 TenantProvisioning SQL Server/MySQL **2/2** 通过；Identity 登录与 Organization 机构管理曾暴露可重复失败。后续复核与闭环结果见下方增补，首轮结果不再作为当前阻塞项。

## 增补（2026-07-22，Task 4A 聚焦 API 失败闭环）

| 变更 | 说明 |
| --- | --- |
| Unit 门槛 **341 → 342** | 新增 `AssignedGuidTypeHandlerTests`，锁定自定义 Guid Handler 必须声明 `DbType.Guid`；新鲜运行 **342/342** |
| Compatibility / Architecture / Integration 门槛 | 保持 **7/30/109**；未增删对应测试发现项 |
| 四处 canonical 门槛 | **342/7/30/109**，已同步 README、getting-started、CI 与 Skill delivery-map |

Identity 登录、机构管理、用户-机构隶属 SQL Server/MySQL 聚焦 Integration **6/6** 通过；原失败已归因为测试契约/权限夹具漂移、Host/租户作用域误用、Guid 参数数据库类型和 Organization 成功响应 OpenAPI 元数据缺失。完整 Integration 门槛仍为 **109**，本增补不把聚焦通过表述为全量通过。

## 增补（2026-07-22，架构硬化 Task 4B API 迁移能力隔离）

| 变更 | 说明 |
| --- | --- |
| Architecture 门槛 **30 → 31** | 新增迁移组件消费者与 API 源码负向门禁；新鲜运行 **31/31** |
| Unit / Compatibility / Integration 门槛 | 保持 **342/7/109**；Task 4B 未增删这三套测试发现项 |
| 四处 canonical 门槛 | **342/7/31/109**，已同步 README、getting-started、CI 与 Skill delivery-map |

SQL Server/MySQL API 聚焦 **2/2**、migration idempotence **2/2** 通过；API Release 发布物 `.deps.json` 与迁移 DLL 扫描均为零命中。完整 Integration 门槛仍为 **109**，本增补不把聚焦通过表述为全量通过。

## 增补（2026-07-22，Task 4B review 跨平台与传递闭包修复）

| 变更 | 说明 |
| --- | --- |
| Architecture 门槛 **31 → 33** | 正/反斜杠 `ProjectReference` Include 夹具 +1，API→Bridge→DbUp 传递闭包夹具 +1；新鲜运行 **33/33** |
| Unit / Compatibility / Integration 门槛 | 保持 **342/7/109**；review 修复未增删这三套测试发现项 |
| 四处 canonical 门槛 | **342/7/33/109**，已同步 README、getting-started、CI 与 Skill delivery-map |

RED 为 **32/33**：闭包仅返回 API 与 Bridge，未发现第二跳 DbUp；GREEN 改为统一解析两种分隔符并用已访问集合递归遍历项目图，真实 API 闭包与两类夹具全部通过。首次 Task 4B RED 未在实现前实际运行的历史事实保持原记录，不以后补夹具伪装为首次 RED。

## 增补（2026-07-23，Task 4F Tenancy Core/Http 拓扑合并）

| 变更 | 说明 |
| --- | --- |
| Unit 门槛 **342 → 343** | 新增 Tenancy 合并后的宿主/模块装配回归断言 1 项；新鲜运行 **343/343** |
| Architecture 门槛 **33 → 36** | 新增 Tenancy 单主项目拓扑、防止 Composition 回退引用 `.Http` 以及发布/导出边界相关门禁共 +3；新鲜运行 **36/36** |
| Compatibility / Integration 门槛 | 保持 **7/109**；Task 4F 未增删这两套测试发现项 |
| 四处 canonical 门槛 | **343/7/36/109**，已同步 README、getting-started、CI 与 Skill delivery-map |

Tenancy API + TenantProvisioning SQL Server/MySQL 聚焦 Integration **4/4**、Development/Production Seed SQL Server/MySQL 聚焦 Integration **4/4**、`pnpm test:openapi` **14/14** 通过；三宿主 Release 发布物对 `Full.NET.Modules.Tenancy.Http` 的文本扫描为 **0** 命中。当前记录只声明 4F 相关聚焦验证与门槛同步已完成，不把上述结果表述为全量 Integration **109** 项已重跑。

## 增补（2026-07-23，Task 13 PR 集成冒烟加宽）

| 变更 | 说明 |
| --- | --- |
| Unit / Compatibility / Architecture / Integration 全量门槛 | 保持 **343/7/36/109**；Task 13 只调整 PR 快门禁 filter，不增删测试发现项 |
| PR Integration smoke | **2 → 8**；由仅双库迁移冒烟升级为 Identity/Tenancy/Outbox 核心双库组合 |
| PR smoke 稳定 filter | `SqlServer_migration_is_idempotent_and_creates_binary_outbox_schema`、`MySql_migration_is_idempotent_and_creates_binary_outbox_schema`、`Login_and_current_user_follow_secure_http_contract`、`Anonymous_current_tenant_endpoint_returns_minimal_standard_http_contract`、`SqlServer_provisioning_is_atomic_and_writes_binary_outbox`、`MySql_provisioning_is_atomic_and_writes_binary_outbox` |
| 四处 canonical 来源 | README、getting-started、CI 与 Skill delivery-map 已同步；`push main` 继续保持全量 **109** 项 |

新鲜验证：按 PR filter 直接运行 `Full.NET.IntegrationTests.dll --minimum-expected-tests 8 --timeout 15m`，结果 **8/8** 通过，墙钟 **3m 42s**。本记录只证明 PR 快门禁组合稳定且满足 15 分钟目标，不把该结果表述为完整双库回归。

## 增补（2026-07-23，Task 6 Outbox 最大重试 / 死信 / 版本共存）

| 变更 | 说明 |
| --- | --- |
| Unit 门槛 **343 → 348** | 新增 `OutboxProcessorTests` 4 项（未知类型死信、坏载荷死信、最大尝试死信、Options 化领取）与 `IntegrationEventHandlerMatcherTests` 1 项（并行版本精确路由） |
| Integration 门槛 **109 → 124** | 新增 `OutboxRecoveryTests` 6 项（双库未知版本死信、坏载荷死信、租约过期回收）与死信迁移/恢复相关 9 项；`--list-tests` 新鲜发现 **124** 项 |
| 四处 canonical 门槛 | README、getting-started、CI 与 Skill delivery-map 已同步为 **348/7/36/124** |
| 运维文档 | 新增 [Outbox Worker 运维说明](../operations/outbox-worker-topology.md)，记录默认数据库租约多副本模型、死信原因码与受控人工重放边界 |

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Unit 聚焦 | `OutboxProcessorTests|IntegrationEventHandlerMatcherTests`，`--minimum-expected-tests 11` | **11/11** 通过 |
| Integration 聚焦 | `OutboxRecoveryTests|SqlServer_outbox_dead_letter_migration_recovers_partial_state|MySql_outbox_dead_letter_migration_recovers_partial_state|migration_is_idempotent_and_creates_binary_outbox_schema`，`--minimum-expected-tests 10` | **10/10** 通过，约 3m 09s |
| Unit 全量 | `--minimum-expected-tests 348` | **348/348** 通过 |
| Integration 发现 | `--list-tests` | **124** 项 |

本增补只证明 Task 6 的代码、双库迁移、聚焦运行时恢复与门槛同步已完成；不把该结果表述为完整 Integration **124** 项或更高层能力整体 `Verified`。真实多副本压力基准、相邻版本升级链/版本退役扫描与受控人工重放自动化仍待后续任务补齐。

## 增补（2026-07-23，Task 7 缓存一致性最小闭环）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **124 → 126** | 新增 `SqlServer_provisioning_clears_negative_domain_cache_before_outbox_processing` 与 `MySql_provisioning_clears_negative_domain_cache_before_outbox_processing` 2 项，锁定“先负缓存、再创建租户、主节点立即可见、第二节点在 Outbox 处理前仍陈旧”的双节点窗口 |
| 四处 canonical 门槛 | README、getting-started、CI 与 Skill delivery-map 已同步为 **348/7/36/126** |
| 能力状态 | `FusionCache + .AsHybridCache()` 已从 `Implemented` 提升为 `Build-verified`，但 Redis/Backplane 故障注入、延迟 Worker 与指标仍待补齐 |

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Integration 聚焦 | `FullyQualifiedName~CacheConsistencyTests`，`--minimum-expected-tests 2` | **2/2** 通过，约 1m 37s |
| Integration 组合回归 | `FullyQualifiedName~TenantProvisioningTests|FullyQualifiedName~CacheConsistencyTests`，`--minimum-expected-tests 4` | **4/4** 通过，约 2m 25s |
| Integration 发现 | `--list-tests` | **126** 项 |

本增补只证明 Task 7 已完成“提交后本机同步失效 + Outbox 跨节点修复”的最小缓存一致性闭环与双库双节点聚焦验证；Redis/Backplane 中断、Redis 不可用/恢复、延迟 Worker、多实例指标和完整 S0/S1/S2 分级仍属后续任务，不把本记录表述为缓存能力整体 `Verified`。

## 增补（2026-07-26，缓存一致性闭环复核）

| 验证 | 结果 |
| --- | --- |
| `CacheConsistencyTests` | **6/6** 通过；覆盖 SQL Server/MySQL 本机负缓存修复、双 API 节点 + Redis + Worker Backplane 精确失效，以及 Redis 不可达时主节点写后可见 |
| Outbox/Backplane 确认边界 | Worker 对安全关键租户失效同步等待 Backplane 发布完成后再确认 Outbox；请求节点本机修复不替代可靠消费者 |
| 测试宿主启动配置 | Redis 连接在 Minimal Hosting 模块注册前注入，避免只在晚期 `ConfigureAppConfiguration` 生效而产生“假多节点”测试 |

本次复核不改变 canonical 测试总数；Redis 中断后恢复、长时间延迟 Worker、积压指标与真实编排环境证据仍保持开放。

## 增补（2026-07-23，Tenancy Host 租户管理纵向切片）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **126 → 128** | 新增 `Host_tenant_management_returns_standard_contract`（SQL Server + MySQL），含 OpenAPI 运行时断言 |
| 四处 canonical 门槛 | README、getting-started、CI 与 Skill delivery-map 已同步为 **348/7/36/128** |
| OpenAPI 静态夹具 | **14 → 16**（`tenancy-host-tenants-v1.json` + node 合同测试 2 项） |
| Mock parity E2E | **40 → 42**（新增「租户列表、开通与禁用」× 双端） |
| 真实栈 E2E | **38 → 42**（新增 `host-tenants.spec.mjs` 2 场景 × 双端） |
| 客户端单测 | 管理端与共享包 **158 → 165**（`host-tenants` 契约 +1、Vue `tenants` +3、Layui `tenants` +1） |

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Integration 聚焦 | `FullyQualifiedName~Host_tenant_management`，`--minimum-expected-tests 2` | **2/2** 通过，约 2m 10s |
| Parity E2E 聚焦 | `playwright test -g '租户列表、开通与禁用'` | **2/2** 通过 |
| `pnpm test:openapi` | 全量 | **16/16** 通过 |
| Unit 全量 | `--minimum-expected-tests 348` | **348/348** 通过 |

本增补只证明 Tenancy Host 租户管理切片已完成；不把该结果表述为完整 Integration **128** 项或租户能力整体 `Verified`。

## 增补（2026-07-23，Migrator 租户上下文 DI 修复）

| 变更 | 说明 |
| --- | --- |
| Unit 门槛 **348 → 349** | 新增 `Migrator_profile_registers_tenant_context_for_seed_and_outbox`，锁定 Tenancy `AddMigrationServices` 必须注册 `ICurrentTenant`，否则 Migrator Seed/Outbox 在 `ValidateOnBuild` 阶段失败 |
| 四处 canonical 门槛 | README、getting-started、CI 与 Skill delivery-map 已同步为 **349/7/36/128** |

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Unit 聚焦 | `FullyQualifiedName~FullNetModuleCatalogTests`，`--minimum-expected-tests 4` | **4/4** 通过 |
| Architecture 全量 | `--minimum-expected-tests 36` | **36/36** 通过 |
| Integration 聚焦 | `FullyQualifiedName~Host_tenant_management`，`--minimum-expected-tests 2` | **2/2** 通过 |
| 真实栈 SQL Server 全量 | `pnpm test:e2e:real` | **42/42** 通过 |

## 增补（2026-07-23，Host 租户目录权限三段式命名）

| 变更 | 说明 |
| --- | --- |
| 权限码 | `tenancy.tenants.manage.read`（四段，违规）→ **`tenancy.host_tenants.read`**（符合 `{module}.{plural_resource}.{action}`） |
| 语义 | `tenancy.tenants.read` 保留给 `/available` 与租户上下文；`tenancy.host_tenants.read` 专用于 Host 目录 API 与「租户管理」导航 |

## 增补（2026-07-23，Tenancy Host 租户套餐目录 API）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **128 → 130** | 新增 `Host_tenant_package_management_returns_standard_contract`（SQL Server + MySQL） |
| 四处 canonical 门槛 | README、getting-started、CI 与 Skill delivery-map 已同步为 **349/7/36/130** |
| 迁移 | `018_TenancyTenantPackage.sql` 新增 `fn_tenancy_tenant_package` |

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Integration 聚焦 | `FullyQualifiedName~Host_tenant_package`，`--minimum-expected-tests 2` | **2/2** 通过 |
| Architecture 全量 | `--minimum-expected-tests 36` | **36/36** 通过 |
| Release 构建 | `dotnet build -c Release` | 0 警告 / 0 错误 |

本增补只证明 Host 租户套餐目录 API 与双库迁移已完成；双端 UI、OpenAPI 夹具与 E2E 仍属同一纵向切片后续 Task，不把 C2.1 套餐能力标为 `Verified`。

## 增补（2026-07-23，Tenancy Host 租户套餐双端 UI）

| 变更 | 说明 |
| --- | --- |
| OpenAPI 夹具 | **16 → 18**（`tenancy-host-tenant-packages-v1.json` + node 合同测试 2 项） |
| Mock parity E2E | **42 → 44**（新增「套餐列表、创建与禁用」× 双端） |
| 真实栈 E2E | **42 → 44**（新增 `host-tenant-packages.spec.mjs` 2 场景 × 双端） |
| 客户端单测 | 管理端与共享包 **165 → 170**（`host-tenant-packages` 契约 +1、Vue `tenant-packages` +3、Layui `tenant-packages` +1） |

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| `pnpm test:openapi` | 全量 | **18/18** 通过 |
| `pnpm test:clients` | 全量 | 通过（管理端 **170**） |
| Parity E2E 聚焦 | `playwright test -g '套餐列表、创建与禁用'` | **2/2** 通过 |
| Integration 聚焦 | `FullyQualifiedName~Host_tenant_package` | **2/2** 通过（含 OpenAPI 运行时断言） |

验证记录：[`tenancy-host-tenant-package-management-2026-07-23.md`](tenancy-host-tenant-package-management-2026-07-23.md)

## 增补（2026-07-24，Tenancy 套餐 Mock Parity E2E）

| 变更 | 说明 |
| --- | --- |
| Mock parity E2E | **44 → 48**（新增「租户开通可选套餐」「套餐仍被引用时禁用失败」× 双端） |
| 既有套餐 parity 夹具 | 补全 `assignedTenantCount` |
| Parity E2E 聚焦 | `playwright test -g '租户开通可选套餐|套餐仍被引用时禁用失败'` | **4/4** 通过 |

验证记录：[`2026-07-24-tenancy-package-parity-e2e-vertical-slice.md`](../superpowers/plans/2026-07-24-tenancy-package-parity-e2e-vertical-slice.md)

## 增补（2026-07-24，Tenancy 租户列表分配套餐 Mock Parity E2E）

| 变更 | 说明 |
| --- | --- |
| Mock parity E2E | **48 → 50**（新增「租户列表内分配套餐」× 双端） |

验证记录：[`tenancy-host-tenant-package-assignment-2026-07-23.md`](tenancy-host-tenant-package-assignment-2026-07-23.md)

## 增补（2026-07-24，Tenancy 租户分配套餐真实栈 E2E）

| 变更 | 说明 |
| --- | --- |
| 真实栈 E2E | **44 → 46**（新增 `host-tenants.spec.mjs`「Host 管理员可为种子租户分配套餐」× 双端） |
| 辅助函数 | `createTenantPackageViaApi`、`findSeedTenantViaApi` |

验证记录：[`2026-07-24-tenancy-tenant-package-assignment-real-stack-e2e-vertical-slice.md`](../superpowers/plans/2026-07-24-tenancy-tenant-package-assignment-real-stack-e2e-vertical-slice.md)

## 增补（2026-07-25，Settings Host 数据字典纵向切片）

| 变更 | 说明 |
| --- | --- |
| Architecture | **36 → 37**（Settings 显式声明 Identity 模块依赖） |
| Integration | **134 → 136**（`SettingsApiSqlServerTests` / `SettingsApiMySqlTests` 各 1 项，含 403、重复编码、乐观锁、禁用含启用项、字典项生命周期与 OpenAPI 运行时断言） |
| OpenAPI 夹具 | **18 → 20**（`settings-dict-types-v1.json` + node 合同测试 2 项） |
| Mock parity E2E | `shell-parity.spec.mjs` **34 → 36**（新增「字典类型列表、创建与禁用」× 双端） |
| 客户端单测 | contracts **37 → 39**、Vue **135 → 138**、Layui **79 → 80**（admin-i18n 保持 **8**） |
| 四处 canonical 门槛 | **349/7/37/136**，已同步 README、getting-started、CI 与 Skill delivery-map |

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Release 构建 | `dotnet build Full.NET.slnx -c Release` | 0 警告 / 0 错误 |
| Unit 全量 | `--minimum-expected-tests 349` | **349/349** 通过（先暴露 1 项既有失败并修复） |
| Architecture 全量 | `--minimum-expected-tests 37` | **37/37** 通过 |
| Compatibility 全量 | `--minimum-expected-tests 7` | **7/7** 通过 |
| `pnpm test:openapi` | 全量 | **20/20** 通过 |
| Parity E2E 全量 | `pnpm test:e2e` | **76** 项：**71** 通过 / **5** 按客户端专属跳过 |
| `pnpm test:naming` | 全量 | **23/23** 通过（先暴露 8 处未登记 `dynamic_sql`，补登债务 **85 → 87** 后转绿） |
| `pnpm test:governance` / `pnpm test:skills` | 全量 | **7/7** / **52** 项契约检查通过 |
| Integration 双库 | — | **未执行**：本机缺容器运行时，由 CI 矩阵覆盖 |

Unit 全量首次运行暴露 `AuthorizationCatalogTests.Built_in_contributors_publish_the_initial_permission_set` 失败：2026-07-23 套餐切片新增的 `tenancy.tenant_packages.read` / `.write` 未登记到内置权限断言。已补齐期望列表。**门槛纪律**：filter 聚焦运行被 `--minimum-expected-tests` 拒绝时必须补跑一次全量，不得将聚焦结果表述为全量通过。

验证记录：[`settings-dictionary-2026-07-25.md`](settings-dictionary-2026-07-25.md)

## 增补（2026-07-25，Settings 字典项双端 UI）

| 变更 | 说明 |
| --- | --- |
| Mock parity E2E | `shell-parity.spec.mjs` **36 → 38**（新增「字典项列表、创建与禁用」× 双端） |
| 客户端单测 | contracts **39 → 40**、Vue **138 → 140**、Layui **80 → 81** |

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| contracts / i18n / Vue dict-types / Layui dict-types | 聚焦 | **40/40**、**8/8**、**7/7**、**2/2** |
| Parity E2E 聚焦 | `playwright test -g '字典项列表、创建与禁用'` | **2/2** 通过 |
| `vue-tsc --noEmit` | Vue 管理端 | 通过 |

验证记录增补见 [`settings-dictionary-2026-07-25.md`](settings-dictionary-2026-07-25.md)。

## 增补（2026-07-25，Settings 数据字典真实栈 E2E）

| 变更 | 说明 |
| --- | --- |
| 真实栈 E2E | **46 → 50**（新增 `host-dict-types.spec.mjs`：管理员加载/创建类型与项 + 受限账号 403 × 双端） |
| 辅助函数 | `createSettingsDictTypeViaApi`、`createSettingsDictItemViaApi` |

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| 真实栈聚焦（SqlServer） | `playwright test host-dict-types` | **4/4** 通过 |
| 真实栈聚焦（MySql） | `FULLNET_E2E_DATABASE_PROVIDER=MySql` + 同上 | **4/4** 通过 |
| Integration 聚焦 | `FullyQualifiedName~SettingsApi`，`--minimum-expected-tests 2` | **2/2** 通过 |

本机已实跑双库 Integration 与双库真实栈聚焦；全量真实栈矩阵仍由 CI `real-stack-e2e` / `real-stack-e2e-mysql` 覆盖。

计划：[`2026-07-25-settings-dict-real-stack-e2e-vertical-slice.md`](../superpowers/plans/2026-07-25-settings-dict-real-stack-e2e-vertical-slice.md)

验证记录增补见 [`settings-dictionary-2026-07-25.md`](settings-dictionary-2026-07-25.md)。

## 增补（2026-07-25，Settings Host 系统配置 Task 1）

| 变更 | 说明 |
| --- | --- |
| Integration | **136 → 138**（`Host_config_entry_management` SQL Server/MySQL 各 1 项，列表 403 RED） |
| 迁移 | `021_SettingsConfigEntry.sql` 双库 |
| 四处 canonical 门槛 | **349/7/37/138** |

计划：[`2026-07-25-settings-system-config-vertical-slice.md`](../superpowers/plans/2026-07-25-settings-system-config-vertical-slice.md)

## 增补（2026-07-25，Settings Host 系统配置 Task 4–5）

| 变更 | 说明 |
| --- | --- |
| Mock parity | `shell-parity` **38 → 40**（系统配置创建/禁用 × 双端） |
| 真实栈 E2E | **50 → 54**（`host-config-entries.spec.mjs` × 双端） |
| 验证记录 | [`settings-system-config-2026-07-25.md`](settings-system-config-2026-07-25.md) |

## 增补（2026-07-25，Settings Host 枚举/常量元数据）

| 变更 | 说明 |
| --- | --- |
| Integration | **138 → 140**（`Host_enum_catalog_query` SQL Server/MySQL） |
| Mock parity | `shell-parity` **40 → 42** |
| 真实栈 E2E | **54 → 58** |
| 四处 canonical 门槛 | **349/7/37/140** |
| 验证记录 | [`settings-enum-catalog-2026-07-25.md`](settings-enum-catalog-2026-07-25.md) |

## 增补（2026-07-25，Auditing Host 访问日志）

| 变更 | 说明 |
| --- | --- |
| Architecture | **37 → 38**（`Auditing_declares_identity_as_an_explicit_module_dependency`） |
| Integration | **140 → 142**（`Host_access_log_query` SQL Server/MySQL） |
| Mock parity | `shell-parity` **42 → 44** |
| 真实栈 E2E | **58 → 62** |
| 四处 canonical 门槛 | **349/7/38/142** |
| 验证记录 | [`auditing-access-log-2026-07-25.md`](auditing-access-log-2026-07-25.md) |

## 增补（2026-07-25，Auditing Host 操作日志）

| 变更 | 说明 |
| --- | --- |
| Integration | **142 → 144**（`Host_operation_log_query` SQL Server/MySQL） |
| Mock parity | `shell-parity` **44 → 46** |
| 真实栈 E2E | **62 → 66** |
| 四处 canonical 门槛 | **349/7/38/144** |
| 验证记录 | [`auditing-operation-log-2026-07-25.md`](auditing-operation-log-2026-07-25.md) |

## 增补（2026-07-25，Auditing Host 异常日志）

| 变更 | 说明 |
| --- | --- |
| Integration | **144 → 146**（`Host_exception_log_query` SQL Server/MySQL） |
| Mock parity | `shell-parity` **46 → 48** |
| 真实栈 E2E | **66 → 70** |
| 四处 canonical 门槛 | **349/7/38/146** |
| 验证记录 | [`auditing-exception-log-2026-07-25.md`](auditing-exception-log-2026-07-25.md) |

## 增补（2026-07-25，Organization 职位管理）

| 变更 | 说明 |
| --- | --- |
| Integration | **146 → 148**（`Tenant_position_management` SQL Server/MySQL） |
| Mock parity | `shell-parity` **48 → 50** |
| 真实栈 E2E | **70 → 74** |
| 四处 canonical 门槛 | **349/7/38/148** |
| 验证记录 | [`organization-position-2026-07-25.md`](organization-position-2026-07-25.md) |

## 增补（2026-07-25，Organization 用户-职位隶属）

| 变更 | 说明 |
| --- | --- |
| Integration | **148 → 150**（`Tenant_user_position_management` SQL Server/MySQL） |
| Mock parity | `shell-parity` **50 → 52** |
| 真实栈 E2E | **74 → 76** |
| 四处 canonical 门槛 | **349/7/38/150** |
| 验证记录 | [`organization-user-position-assignment-2026-07-25.md`](organization-user-position-assignment-2026-07-25.md) |

## 增补（2026-07-25，Identity Host 用户重置密码）

| 变更 | 说明 |
| --- | --- |
| Integration | **150 → 152**（`VerifyResetPasswordInvalidatesOldCredentialsAsync` SQL Server/MySQL） |
| Mock parity | `shell-parity` **52** 不变（扩展现有用户管理场景） |
| 四处 canonical 门槛 | **349/7/38/152** |
| 验证记录 | [`identity-host-user-reset-password-2026-07-25.md`](identity-host-user-reset-password-2026-07-25.md) |

## 增补（2026-07-25，Identity Host 用户启用）

| 变更 | 说明 |
| --- | --- |
| Integration | 门槛 **152** 不变（`VerifyEnableUserRestoresLoginAsync` 纳入既有 `Host_user_management`） |
| Mock parity | `shell-parity` **52** 不变（扩展现有用户管理场景） |
| 验证记录 | [`identity-host-user-enable-2026-07-25.md`](identity-host-user-enable-2026-07-25.md) |

## 增补（2026-07-26，Identity Host 在线用户与强制下线）

| 变更 | 说明 |
| --- | --- |
| Integration | **152 → 154**（`Host_online_sessions` SQL Server/MySQL） |
| Mock parity | `shell-parity` **52 → 54**（「在线用户列表与强制下线」× 双端） |
| 四处 canonical 门槛 | **349/7/38/154** |
| 验证记录 | [`identity-host-online-sessions-2026-07-26.md`](identity-host-online-sessions-2026-07-26.md) |

## 增补（2026-07-26，Files Host 文件元数据）

| 变更 | 说明 |
| --- | --- |
| Integration | **154 → 156**（`Host_file_management` SQL Server/MySQL） |
| Mock parity | `shell-parity` **54 → 56**（「Host 文件列表与上传删除」× 双端） |
| 四处 canonical 门槛 | **349/7/38/156** |
| 验证记录 | [`files-host-file-metadata-2026-07-26.md`](files-host-file-metadata-2026-07-26.md) |

## 增补（2026-07-26，Files Host 文件真实栈 E2E）

| 变更 | 说明 |
| --- | --- |
| 真实栈 E2E | **76 → 80**（`host-files.spec.mjs` 2 场景 × 双端） |
| 验证记录 | [`files-host-file-metadata-2026-07-26.md`](files-host-file-metadata-2026-07-26.md) 增补节 |

## 增补（2026-07-26，Identity Host 在线用户真实栈 E2E）

| 变更 | 说明 |
| --- | --- |
| 真实栈 E2E | **80 → 84**（`host-online-sessions.spec.mjs` 2 场景 × 双端） |
| 验证记录 | [`identity-host-online-sessions-2026-07-26.md`](identity-host-online-sessions-2026-07-26.md) 增补节 |

## 增补（2026-07-26，Realtime SignalR 基础）

| 变更 | 说明 |
| --- | --- |
| Unit | **349 → 351**（`RealtimeGroupsTests`） |
| Architecture | **38 → 40**（`BusinessModules_DoNotDependOnSignalRHubContext`） |
| Integration | **156 → 158**（`Realtime_hub_and_probe` SQL Server/MySQL） |
| 四处 canonical 门槛 | **351/7/40/158** |

## 增补（2026-07-26，Notifications Host 公告）

| 变更 | 说明 |
| --- | --- |
| Integration | **158 → 160**（`Host_announcement_management` SQL Server/MySQL） |
| Mock parity | `shell-parity` **56 → 58**（「Host 公告列表与创建发布」× 双端） |
| 四处 canonical 门槛 | **351/7/40/160** |
| 验证记录 | [`notifications-host-announcement-2026-07-26.md`](notifications-host-announcement-2026-07-26.md) |

## 增补（2026-07-26，Notifications 站内信收件箱）

| 变更 | 说明 |
| --- | --- |
| Integration | 扩展现有双库 Notifications 用例（门槛 **160** 不变） |
| Mock parity | `shell-parity` **58 → 60**（「消息中心列表与发信」× 双端） |
| 验证记录 | [`notifications-inbox-message-2026-07-26.md`](notifications-inbox-message-2026-07-26.md) |
| 验证记录 | [`realtime-signalr-foundation-2026-07-26.md`](realtime-signalr-foundation-2026-07-26.md) |

## 增补（2026-07-26，Jobs Host 任务定义）

| 变更 | 说明 |
| --- | --- |
| Integration | **160 → 162**（`Host_job_definition_and_trigger` SQL Server/MySQL 各 1） |
| Mock parity | `shell-parity` **60 → 62**（「任务调度列表与触发」× 双端） |
| 四处 canonical 门槛 | **351/7/40/162** |
| 验证记录 | [`jobs-host-definitions-2026-07-26.md`](jobs-host-definitions-2026-07-26.md) |

## 增补（2026-07-26，Platform Host 工作台汇总）

| 变更 | 说明 |
| --- | --- |
| Integration | **162 → 164**（`Host_dashboard_summary` SQL Server/MySQL 各 1） |
| Mock parity | 扩展现有 Overview 场景 mock（门槛 **62** 不变） |
| 四处 canonical 门槛 | **351/7/40/164** |
| 验证记录 | [`platform-host-dashboard-2026-07-26.md`](platform-host-dashboard-2026-07-26.md) |

## 增补（2026-07-26，OpenAPI 与 Scalar 接口文档）

| 变更 | 说明 |
| --- | --- |
| Integration | **164 → 166**（`OpenApi_documentation` SQL Server/MySQL 各 1） |
| 四处 canonical 门槛 | **351/7/40/166** |
| 验证记录 | [`platform-openapi-documentation-2026-07-26.md`](platform-openapi-documentation-2026-07-26.md) |

## 增补（2026-07-26，Host 全局限流）

| 变更 | 说明 |
| --- | --- |
| Integration | **166 → 168**（`Global_api_rate_limit` SQL Server/MySQL 各 1） |
| Unit | **351 → 352**（`RateLimitPolicyErrorCodes`） |
| 四处 canonical 门槛 | **352/7/40/168** |
| 验证记录 | [`hosting-global-api-rate-limit-2026-07-26.md`](hosting-global-api-rate-limit-2026-07-26.md) |

## 增补（2026-07-26，角色与数据授权对标收口）

| 变更 | 说明 |
| --- | --- |
| 对标矩阵 | `角色与数据授权` **Mapped → Build-verified**（汇总既有切片，无新增测试） |
| 四处 canonical 门槛 | **352/7/40/168**（不变） |
| 验证记录 | [`identity-role-data-authorization-2026-07-26.md`](identity-role-data-authorization-2026-07-26.md) |

## 增补（2026-07-26，API Key 认证）

| 变更 | 说明 |
| --- | --- |
| Integration | **168 → 170**（`Host_api_keys_follow_contract` SQL Server/MySQL 各 1） |
| client-contracts Vitest | **68 → 69**（`host-api-keys.test.ts`） |
| 四处 canonical 门槛 | **352/7/40/170** |
| 验证记录 | [`identity-api-key-2026-07-26.md`](identity-api-key-2026-07-26.md) |

## 增补（2026-07-26，主干基线正确性恢复）

| 变更 | 说明 |
| --- | --- |
| Unit | **352 → 356**（Host 目录 SQL 作用域、Files 启动配置校验、通知提交后发布失败隔离、租户提交后取消隔离） |
| Integration | 重新发现当前程序集为 **172**，修正此前 canonical **170** 的漂移 |
| Compatibility / Architecture | **7 / 40**（不变） |
| 四处 canonical 门槛 | **356/7/40/172** |

## 增补（2026-07-26，审计跨上下文写入）

| 项目 | 结果 |
|---|---|
| Unit 门槛 **356 → 358** | 新增 `AuditingSqlScopeTests` 2 项，锁定审计写入允许 Host、租户与匿名请求上下文，同时查询继续保持 Host-only |
| 四处 canonical 门槛 | **358/7/40/172** |
| 验证记录 | [`main-baseline-correctness-recovery-2026-07-26.md`](main-baseline-correctness-recovery-2026-07-26.md) |

## 增补（2026-07-26，租户数据范围 SQL 作用域）

| 项目 | 结果 |
|---|---|
| Unit 门槛 **358 → 359** | 新增 Host 角色数据范围 SQL 作用域回归，锁定租户业务查询可在显式 Host 行过滤下解析角色范围 |
| 四处 canonical 门槛 | **359/7/40/172** |
| 验证记录 | [`main-baseline-correctness-recovery-2026-07-26.md`](main-baseline-correctness-recovery-2026-07-26.md) |

## 2026-07-26 Integration 反馈分层与 CI 分片增补

| 项目 | 结果 |
| --- | --- |
| 四处 canonical 门槛 | **359/7/40/172**，测试发现数量不变 |
| main Integration 分片 | API SQL Server **34** + API MySQL **34** + Migrations **62** + Infrastructure **42** = **172** |
| 新鲜运行 | 四分片分别 **34/34、34/34、62/62、42/42**，合计 **172/172**，失败 **0**、跳过 **0** |
| PR smoke | **8/8**，墙钟 **2分13秒**，按需启动路径未启动 Redis |

本增补只改变验证反馈路径和 CI 调度，不增删 .NET 测试发现项。详细耗时、按需容器证据与仍待解决的 MySQL/Factory 成本见 [`integration-test-feedback-2026-07-26.md`](integration-test-feedback-2026-07-26.md)。

## 增补（2026-07-26，强化模块化单体硬化）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **359 → 363** | 新增租户事务提交后缓存失效、Backplane 失败传播与 API Key 五分钟写入窗口 4 项 |
| Architecture 门槛 **40 → 43** | 新增跨模块表所有权主门禁、负向夹具与 Identity→Organization.Contracts 反向依赖门禁 3 项 |
| 四处 canonical 门槛 | **363/7/43/172** |
| 验证记录 | [`strengthened-modular-monolith-hardening-2026-07-26.md`](strengthened-modular-monolith-hardening-2026-07-26.md) |

## 增补（2026-07-26，Identity–Organization 数据范围 Port）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **363 → 364** | 新增 Organization 自有数据范围 SQL 投影回归，覆盖 `self`、`organization`、`organization_subtree` 与非法范围拒绝 |
| Architecture 门槛 | **43** 不变；Identity→Organization 跨模块表访问债务 **7 → 5** |
| 双库焦点验证 | Organization Data Scope **2/2**，TenantProvisioning 精简宿主 **2/2** |
| Integration 全量 | **172/172**，失败 **0**、跳过 **0**，**26m 32s** |
| 四处 canonical 门槛 | **364/7/43/172** |
| 验证记录 | [`identity-organization-data-scope-port-2026-07-26.md`](identity-organization-data-scope-port-2026-07-26.md) |

## 增补（2026-07-26，Organization–Identity 用户目录边界）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **364 → 365** | 新增 Identity Host 用户批量目录回归，锁定 ID 去重、单次查询和显示投影 |
| Architecture 门槛 | **43** 不变；Organization→Identity 跨模块表访问债务 **5 → 4** |
| 双库焦点验证 | 用户-机构与用户-职位 SQL Server/MySQL **4/4** |
| Integration 全量 | **172/172**，失败 **0**、跳过 **0**，**26m 05s** |
| 四处 canonical 门槛 | **365/7/43/172** |
| 验证记录 | [`organization-identity-user-directory-boundary-2026-07-26.md`](organization-identity-user-directory-boundary-2026-07-26.md) |

## 增补（2026-07-26，Host 工作台指标边界）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **365 → 366** | 新增 Host 工作台聚合回归，锁定所有者指标消费与精简模块配置的零值/空活动退化行为 |
| Architecture 门槛 | **43** 不变；精确跨模块表访问债务 **3 → 0** |
| 四处 canonical 门槛 | **366/7/43/172** |
| 验证记录 | [`host-dashboard-metrics-boundary-2026-07-26.md`](host-dashboard-metrics-boundary-2026-07-26.md) |

## 增补（2026-07-26，租户 SQL 绑定元数据）

| 项目 | 结果 |
| --- | --- |
| Architecture 门槛 **43 → 44** | 新增全模块 `SqlDataScope`/`SqlTenantBinding` 一致性门禁，覆盖 BuildingBlocks、宿主及全部官方业务模块 |
| Unit / Compatibility / Integration 门槛 | **366 / 7 / 172**（不变） |
| 双库焦点验证 | Organization 与 Tenancy 租户 SQL SQL Server/MySQL **12/12** |
| Integration 全量 | **172/172**，失败 **0**、跳过 **0**，**27m 32s** |
| 四处 canonical 门槛 | **366/7/44/172** |
| 验证记录 | [`tenant-sql-binding-metadata-2026-07-26.md`](tenant-sql-binding-metadata-2026-07-26.md) |

## 增补（2026-07-26，Global SQL Statement 精确目录）

| 项目 | 结果 |
| --- | --- |
| Architecture 门槛 **44 → 46** | 新增生产 Global SQL 精确目录主门禁及分析器负向夹具；锁定 Statement Name、声明、文件、安全分类、理由和必需 SQL 片段 |
| Global 目录 | **23/23** 条生产声明精确登记；禁止新增未登记项、过期项、重复项与通配符 |
| Unit / Compatibility / Integration 门槛 | **366 / 7 / 172**（不变） |
| 四处 canonical 门槛 | **366/7/46/172** |
| 验证记录 | [`global-sql-statement-catalog-2026-07-26.md`](global-sql-statement-catalog-2026-07-26.md) |

## 增补（2026-07-26，可信代理客户端地址边界）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **366 → 378** | 新增默认关闭、显式 Header、代理 IP/CIDR、IPv4/IPv6、无效配置、全网 CIDR 与覆盖完整 IPv4-mapped 空间的 IPv6 CIDR 启动拒绝回归 |
| Architecture 门槛 **46 → 48** | 禁止生产模块直接解析 `X-Forwarded-*`，并锁定转发中间件先于日志、限流、认证和授权 |
| Integration 门槛 **172 → 184** | 新增伪造/未知代理、单/多层链、CIDR、IPv4/IPv6、IPv4-mapped 精确代理与网段、无效地址及限流分区 **10** 项，以及 Identity Origin/审计双库 **2** 项 |
| Integration 四分片 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **52** = **184** |
| 四处 canonical 门槛 | **378/7/48/184** |
| 验证记录 | [`trusted-proxy-forwarding-2026-07-26.md`](trusted-proxy-forwarding-2026-07-26.md) |

## 增补（2026-07-26，Architecture 扫描工作树隔离）

| 项目 | 结果 |
| --- | --- |
| Architecture 门槛 **48 → 49** | 新增仓库扫描回归，锁定 `.git`、`.worktrees`、`bin`、`obj` 不进入当前工作树的项目与源码边界检查 |
| RED | 创建 `codex/uniapp-uni-ui-adoption` 工作树后，MySQL 消费者、迁移所有权与 UUID 转换扫描稳定出现 **3** 项跨工作树假阳性 |
| GREEN | 聚焦回归 **4/4**、Architecture **49/49**，失败 **0**、跳过 **0** |
| Unit / Compatibility / Integration 门槛 | **378 / 7 / 184**（不变） |
| 四处 canonical 门槛 | **378/7/49/184** |

## 增补（2026-07-26，缓存可靠性指标与延迟 Worker 确认）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **378 → 380** | 新增失效时延/失败固定标签合同，以及陈旧命中、Backplane 熔断转换/恢复事件桥接合同 |
| RED / GREEN | 指标类型缺失时聚焦编译失败；Tenancy 未接入时本机成功/分布式失败指标断言 **2/2** 失败；实现后指标与 Tenancy 聚焦 **4/4** 通过 |
| 双库故障注入 | `CacheConsistencyTests` SQL Server/MySQL **6/6**；锁定共享 L2 可提前收敛，但延迟 Worker 前事件不得确认、恢复后必须正式确认 |
| Compatibility / Architecture / Integration 门槛 | **7 / 49 / 184**（不变） |
| 四处 canonical 门槛 | **380/7/49/184** |
| 验证记录 | [`cache-reliability-telemetry-2026-07-26.md`](cache-reliability-telemetry-2026-07-26.md) |

## 增补（2026-07-26，日志高优先级独立通道）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **380 → 386** | 新增普通队列过载隔离、高优先级固定非阻塞、固定通道指标、健康降级、非法容量与 Service Defaults 注册合同 |
| RED / GREEN | 双通道类型缺失时聚焦编译失败；实现后高优先级日志聚焦 **6/6**、连同既有 Monitor 回归 **7/7** 通过 |
| 故障注入 | 阻塞普通 Sink 并填满队列时 Error 仍独立交付；阻塞高优先级 Sink 并填满队列时调用方仍在有界时间返回且累计丢弃 |
| Compatibility / Architecture / Integration 门槛 | **7 / 49 / 184**（不变） |
| 四处 canonical 门槛 | **386/7/49/184** |
| 验证记录 | [`high-priority-logging-channel-2026-07-26.md`](high-priority-logging-channel-2026-07-26.md) |

## 增补（2026-07-26，OpenAPI 离线夹具覆盖收口）

| 项目 | 结果 |
| --- | --- |
| OpenAPI 离线门槛 **41 → 50** | API Key、Jobs、平台工作台、平台接口文档各新增结构与源码对齐 2 项，并增加全夹具覆盖守卫 1 项 |
| RED | 覆盖守卫准确列出 4 个已有夹具未进入 `pnpm test:openapi` |
| GREEN | 聚焦 **9/9**、OpenAPI 全量 **50/50** |
| .NET canonical 门槛 | 不变；本次未修改 C# 运行时或 Integration 测试 |
| 验证记录 | [`openapi-offline-fixture-coverage-2026-07-26.md`](openapi-offline-fixture-coverage-2026-07-26.md) |

## 增补（2026-07-26，Outbox 积压指标）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **386 → 390** | 新增待处理数量/最老年龄 Gauge、采样失败不阻断、采样周期去重与配置下界合同 |
| Integration 门槛 **184 → 186** | SQL Server/MySQL 各新增一项 backlog 快照测试，锁定死信与已处理排除、数量收敛和 UTC 最老时间 |
| 双库聚焦 | `OutboxRecoveryTests` **8/8**，失败 **0**、跳过 **0**，**2m50s** |
| Integration 四分片 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **54** = **186** |
| 完整门禁 | **186/186**，失败 **0**、跳过 **0**，**33m39s**，stderr 为 0 |
| 四处 canonical 门槛 | **390/7/49/186** |
| 验证记录 | [`outbox-backlog-telemetry-2026-07-26.md`](outbox-backlog-telemetry-2026-07-26.md) |

## 增补（2026-07-27，Notifications 双管理端实时客户端）

| 项目 | 结果 |
| --- | --- |
| client-contracts Vitest | **69 → 72**（稳定消息守卫、认证连接/切上下文重连、连接失败降级） |
| Vue Vitest | **191 → 197**（实时状态、旧会话查询隔离、显式禁用及 App 快照订阅回归） |
| Layui Vitest | **91 → 95**（实时状态、显式禁用、应用装配/卸载；通知面板既有测试扩展动态未读徽标） |
| Mock parity | **99/99** 通过，按双项目矩阵跳过 **5**；Mock web server 显式关闭 Realtime，真实环境默认启用 |
| 客户端生产构建 | `@fullnet/client-contracts`、Vue、Layui 全部退出 0 |
| .NET canonical 门槛 | 不变；本次未修改 C# 运行时或 Integration 测试 |
| 验证记录 | [`realtime-signalr-foundation-2026-07-26.md`](realtime-signalr-foundation-2026-07-26.md) |

## 增补（2026-07-27，OpenAPI 破坏性变更门禁）

| 项目 | 结果 |
| --- | --- |
| OpenAPI 离线门槛 **50 → 58** | 新增兼容/破坏变化目录样例 **5** 项、Git ref/错误退出 **2** 项及 package/PR/main push CI wiring **1** 项 |
| RED | 比较 CLI 缺失时聚焦 **0/7**；package script 与 PR/main push 基线 wiring 缺失时 **0/1** |
| GREEN | 聚焦比较器 **7/7**、CI wiring **1/1**；`HEAD` 基线比较 **25/25** 个夹具兼容 |
| CI 边界 | PR 使用 `github.event.pull_request.base.sha`，`main` 推送使用 `github.event.before`，并跳过全零 before SHA；checkout 保留完整 Git 历史，不启动后端、不访问网络、不占用 Docker |
| .NET canonical 门槛 | **390/7/49/186**（不变；本次未修改 C#、数据库或 Integration 测试） |
| 验证记录 | [`openapi-breaking-change-gate-2026-07-27.md`](openapi-breaking-change-gate-2026-07-27.md) |

## 增补（2026-07-26，Realtime Redis Backplane 故障恢复）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **390 → 392** | 新增运行连接重连/Channel Prefix 与专用 ready 注册合同 2 项 |
| Integration 门槛 **186 → 189** | 新增专用 Backplane 不可达健康合同 1 项，以及 SQL Server/MySQL 双 API 节点 Redis stop/start 恢复各 1 项 |
| Integration 四分片 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** = **189** |
| 完整门禁 | **189/189**，失败 **0**、跳过 **0**，**50m49s**，stderr 为 0 |
| 四处 canonical 门槛 | **392/7/49/189** |
| 验证记录 | [`realtime-redis-backplane-recovery-2026-07-26.md`](realtime-redis-backplane-recovery-2026-07-26.md) |

## 增补（2026-07-27，Notifications 首次连接失败恢复）

| 项目 | 结果 |
| --- | --- |
| client-contracts Vitest **72 → 75** | 新增首次 `start()` 失败退避恢复、切上下文取消旧重试、匿名化/销毁取消重试 3 项 |
| RED / GREEN | 聚焦测试由 **3/6** 失败转为 **6/6**；共享契约全量 **75/75**，TypeScript 构建通过 |
| Vue / Layui / Mock parity | **200/200** / **95/95** / **99/99**（Vue 聚合门槛随 workspace 链接中的共享测试增加 3 项；两端继续消费共享控制器） |
| .NET canonical 门槛 | **395/7/49/189**（不变；本次未修改 C#、数据库或 Integration 测试） |
| 验证记录 | [`realtime-signalr-foundation-2026-07-26.md`](realtime-signalr-foundation-2026-07-26.md) |

## 增补（2026-07-27，会话刷新存储回退）

| 项目 | 结果 |
| --- | --- |
| client-contracts Vitest **75 → 76** | 无 Web Locks 时使用 `localStorage` 跨 Tab 短租约；存储策略抛出 `SecurityError` 时降级执行刷新 |
| RED / GREEN | 存储拒绝场景由聚焦 **1/4** 失败转为 **4/4**；共享契约全量 **76/76**，TypeScript 构建通过 |
| Vue / Layui | **201/201** / **95/95**（Vue 聚合门槛随 workspace 链接中的共享测试增加 1 项） |
| .NET canonical 门槛 | **395/7/49/189**（不变；本次未修改 C#、数据库或 Integration 测试） |
| 验证记录 | [`session-refresh-localstorage-fallback-2026-07-27.md`](session-refresh-localstorage-fallback-2026-07-27.md) |

## 增补（2026-07-26，日志退出共享预算）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **392 → 395** | 新增双阻塞 Sink 共享总预算、正常退出双通道完整排空、退出预算范围校验 |
| RED / GREEN | 配置契约缺失时聚焦编译失败；补齐契约后旧 Async 实现的双阻塞退出测试准确超时；自有双通道调度器实现后日志聚焦与 Monitor 回归 **10/10** 通过 |
| 故障注入 | 普通与高优先级 Sink 同时阻塞、总预算 100ms 时 Logger 释放在单一预算边界后返回；可用 Sink 在预算内完整接收两条通道事件 |
| Compatibility / Architecture / Integration 门槛 | **7 / 49 / 189**（不变） |
| 四处 canonical 门槛 | **395/7/49/189** |
| 验证记录 | [`bounded-logging-shutdown-2026-07-26.md`](bounded-logging-shutdown-2026-07-26.md) |

## 增补（2026-07-27，Jobs 取消传播与批次故障隔离）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **395 → 396** | 新增宿主取消向上传播且不写业务失败状态的 Runner 回归测试 |
| RED / GREEN | 生产实现未修改时取消测试因“未抛异常”失败；最小修复后聚焦 **1/1** 通过 |
| Jobs SQL Server/MySQL 聚焦 | 既有两项用例内嵌同批缺失 Handler/健康任务隔离场景，**2/2** 通过，失败 0、跳过 0，约 **60s** |
| Integration 门槛与分片 | 保持 **189**；API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** |
| 最终静态门禁 | Release **0 warning / 0 error**；Unit/Compatibility/Architecture **396/7/49**；Governance **11/11**；Skill **52**；workspace 通过 |
| 四处 canonical 门槛 | **396/7/49/189** |
| 验证记录 | [`jobs-cancellation-batch-failure-isolation-2026-07-27.md`](jobs-cancellation-batch-failure-isolation-2026-07-27.md) |

## 增补（2026-07-27，Identity 组合根职责拆分）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **396 → 398** | 新增组合根委托等价与重复注册关键契约 2 项；冻结 Identity 自有描述符顺序、实现类型与生命周期 |
| RED / GREEN | 四个内部注册扩展缺失时编译失败；重复 Scheme、授权有效描述符顺序等中间回归被测试捕获，最小修复后 Identity 聚焦 **122/122** |
| Release / canonical | Release **0 warning / 0 error**；Unit/Compatibility/Architecture **398/7/49**，失败 0、跳过 0 |
| 静态门禁 | OpenAPI **58/58**、breaking **25/25**、Governance **11/11**、Skill **52**、workspace 通过 |
| 客户端事实 | client-contracts **76**、Vue **201**、Layui **95**；本次未修改客户端，保留 `localStorage` 跨 Tab 短租约及 `SecurityError` 降级事实 |
| Integration 门槛 | 保持 **189**；SQL Server/MySQL Identity 登录、刷新、权限与 Seed 聚焦 **8/8**，Vue/Layui 真实栈登录 **2/2** |
| 四处 canonical 门槛 | **398/7/49/189** |
| 验证记录 | [`identity-module-registration-split-2026-07-27.md`](identity-module-registration-split-2026-07-27.md) |

## 增补（2026-07-27，Jobs Worker 有界配置）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **398 → 400** | 新增 Worker Options 绑定/启动校验，以及 Processor 消费批大小与轮询间隔 2 项回归 |
| RED / GREEN | 缺少 Options 类型时编译失败；移除 DI 绑定后解析 `IOptions<JobsWorkerOptions>` 失败；Processor 未消费配置时因缺少构造参数与测试边界编译失败；最小实现后聚焦 **2/2** 通过 |
| 配置边界 | `BatchSize` 默认 `10`、范围 `1..50`；`PollMilliseconds` 默认 `2000`、范围 `100..60000`；越界配置在宿主启动期失败 |
| Release / canonical | Release **0 warning / 0 error**；Unit/Compatibility/Architecture **400/7/49**，失败 0、跳过 0 |
| Integration 门槛与分片 | 保持 **189**；API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** |
| Jobs 双库 / Integration 全量 | SQL Server/MySQL 聚焦 **2/2**；完整 **189/189**，失败 0、跳过 0，**27m16.8s**，stderr 0 |
| 并发夹具校正 | 首轮完整运行暴露 SQL Server `READPAST` 单轮只处理 24/32；夹具改为按批大小 8 有界重复轮询后通过，生产 Runner 与领取 SQL 未修改 |
| 四处 canonical 门槛 | **400/7/49/189** |
| 验证记录 | [`jobs-worker-bounded-options-2026-07-27.md`](jobs-worker-bounded-options-2026-07-27.md) |

## 增补（2026-07-27，Jobs 长任务主动续租）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **400 → 404** | 新增租约配置边界、主动续租、所有权丢失取消与终态零行续租竞态 4 项回归 |
| RED / GREEN | 配置与续租接口缺失先编译失败；移除零行所有权判断后超时；终态竞态修正前误抛所有权丢失，最小修复后 Jobs 聚焦 **7/7** |
| 配置边界 | `LeaseSeconds` 默认 `300`、范围 `30..3600`；`LeaseRenewalSeconds` 默认 `60`、范围 `5..1200` 且不超过租约一半；越界配置在 Worker 启动期失败 |
| Jobs SQL Server/MySQL 聚焦 | 既有两项用例加入主动续租场景，**2/2** 通过，失败 0、跳过 0，**1m48s** |
| Integration 门槛与分片 | 保持 **189**；API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** |
| 四处 canonical 门槛 | **404/7/49/189** |
| 规则 / Skills 复盘 | 不新增强制规则；`fullnet-dual-database-change` 候选观察次数 **10 → 11**，尚未达到独立 Skill 触发条件 |
| 验证记录 | [`jobs-active-lease-renewal-2026-07-27.md`](jobs-active-lease-renewal-2026-07-27.md) |

## 增补（2026-07-27，Logging Sink 单事件故障隔离）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **404 → 406** | 普通与高优先级通道各新增 1 项 Sink 首次失败后继续消费的回归测试 |
| RED / GREEN | 普通 `WriteTo` 子 Logger 吞掉内部 Sink 异常时 dropped 假绿为 0；仅传播异常后 Worker 又会终止；改为内部 `AuditTo` 传播并把 catch 收紧到单条事件后聚焦 **11/11** |
| 安全与监控边界 | 失败事件计入既有 `fullnet.logging.events.dropped{channel}`；SelfLog 只记录异常 CLR 类型，不包含异常消息、日志正文或事件属性 |
| Integration 门槛与分片 | 保持 **189**；API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** |
| 四处 canonical 门槛 | **406/7/49/189** |
| 规则 / Skills 复盘 | 未发现需要升级的新规则或重复稳定工作流，本次无规则和 Skill 变化 |
| 验证记录 | [`logging-sink-failure-isolation-2026-07-27.md`](logging-sink-failure-isolation-2026-07-27.md) |

## 增补（2026-07-27，Realtime HubPath 启动校验）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **406 → 407** | 新增启用 Realtime 时拒绝空值、相对路径、空白、查询字符串与片段的 1 项注册期回归测试 |
| RED / GREEN | 旧实现对非法 HubPath 不抛异常；最小启动校验后 `RealtimeBackplaneRegistrationTests` **3/3** 通过，并保留合法自定义路径原值 |
| 停用边界 | `Realtime:Enabled=false` 时跳过 HubPath 校验，使应急关闭配置仍可启动；停用状态不映射 Hub |
| Integration 门槛与分片 | 保持 **189**；API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** |
| 四处 canonical 门槛 | **407/7/49/189** |
| 规则 / Skills 复盘 | 未发现需要升级的新规则或重复稳定工作流，本次无规则和 Skill 变化 |
| 验证记录 | [`realtime-hub-path-validation-2026-07-27.md`](realtime-hub-path-validation-2026-07-27.md) |

## 增补（2026-07-27，Tenancy HostDomains 启动校验）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **407 → 408** | 新增 1 项真实宿主启动回归，覆盖空集合引用、空白、协议、端口、路径、通配符、大小写重复及合法主机/IP |
| RED / GREEN | 旧实现对非法配置不执行启动校验，且初版校验器对 `null` 集合抛空引用；显式 validator、`ValidateOnStart` 与空值守卫后聚焦 **7/7** |
| 兼容边界 | 合法 DNS 主机名、`localhost`、IPv4、无方括号 IPv6 与合法空集合保持原语义；不自动修剪或改写配置 |
| Integration 门槛与分片 | 保持 **189**；API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** |
| 四处 canonical 门槛 | **408/7/49/189** |
| 规则 / Skills 复盘 | 既有模块交付 Skill 已覆盖配置启动校验，本次无规则和 Skill 变化 |
| 验证记录 | [`tenancy-host-domain-startup-validation-2026-07-27.md`](tenancy-host-domain-startup-validation-2026-07-27.md) |

## 增补（2026-07-27，限流策略错误码冲突注册）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **408 → 409** | 新增 1 项 Hosting 回归，拒绝同一限流策略被注册为不同稳定错误码，并保留首次映射 |
| RED / GREEN | 旧实现静默覆盖首次映射；冲突保护后聚焦 **2/2**，同值重复注册保持幂等 |
| Compatibility / Architecture / Integration | 保持 **7/49/189** |
| 四处 canonical 门槛 | **409/7/49/189** |
| 规则 / Skills 复盘 | 既有稳定机器码与测试先行规则已覆盖，本次无规则和 Skill 变化 |
| 验证记录 | [`rate-limit-policy-code-conflict-2026-07-27.md`](rate-limit-policy-code-conflict-2026-07-27.md) |

## 增补（2026-07-27，Seeding DefaultLocale 启动校验）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **409 → 410** | 新增 1 项 Seeding 启动回归，拒绝非空但无法由 `CultureInfo` 解析的默认语言标签 |
| RED / GREEN | 旧实现只拒绝空白值，`not a locale!` 未触发启动异常；最小校验后新增契约 **1/1**、Seeding Unit 聚焦 **46/46** |
| 稳定边界 | 非法配置由 `IStartupValidator` 以 `OptionsValidationException` 快速失败，并保留 `seed.options.invalid`；默认 `zh-CN` 与数据库租约语义不变 |
| Compatibility / Architecture / Integration | 保持 **7/49/189**；Seeding SQL Server/MySQL 聚焦 **6/6**，紧邻 Tenancy 完整全量 **189/189** |
| 四处 canonical 门槛 | **410/7/49/189** |
| 规则 / Skills 复盘 | 既有 BCP 47、启动校验和测试先行规则已覆盖，本次无规则和 Skill 变化 |
| 验证记录 | [`seeding-default-locale-validation-2026-07-27.md`](seeding-default-locale-validation-2026-07-27.md) |

## 关联文档

- [当前能力状态矩阵](../roadmap/capability-status.md)
- [本地开发与运行指南](../development/getting-started.md)
- [外部全面分析复核与吸收记录（2026-07-21）](external-review-2026-07-21.md)

## 增补：2026-07-27，Cache Redis 连接串启动校验

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **410 → 411** | 新增 1 项 Caching 注册回归，畸形 Redis 连接串在服务注册阶段以脱敏配置异常失败 |
| RED / GREEN | 旧实现未抛出异常；接入 StackExchange.Redis 官方解析器后聚焦 **2/2** |
| Compatibility / Architecture / Integration | 保持 **7/49/189**；紧邻 DatabaseOptions 完整 Integration **189/189** |
| 四处 canonical 门槛 | **411/7/49/189** |
| 规则 / Skills 复盘 | 单次局部配置遗漏已由自动回归阻断，本次无规则和 Skill 变化 |
| 验证记录 | [`cache-redis-connection-validation-2026-07-27.md`](cache-redis-connection-validation-2026-07-27.md) |

## 增补：2026-07-27，Outbox 旧版本退役扫描

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **411 → 416** | 新增命令解析 2 项与 Scanner 3 项，覆盖参数隔离、非法输入、canonical/legacy 路由、safe/blocked 与缺失 Handler |
| Integration 门槛 **189 → 191** | SQL Server/MySQL 各新增 1 项只读快照，覆盖目标 pending、dead-letter、已处理、其他消息类型和其他版本 |
| Integration 四分片 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **59** = **191** |
| 稳定边界 | 退出码 `0/1/2` 分别表示安全、命令或路由错误、仍有阻塞；扫描不领取、修改、重放或输出敏感消息字段 |
| 双库与完整门禁 | 退役快照 SQL Server/MySQL **2/2**；完整 **191/191**，失败 0、跳过 0，**34m26s**，TRX Completed 且 stderr 0 |
| 四处 canonical 门槛 | **416/7/49/191** |
| 规则 / Skills 复盘 | 现有双库、敏感信息、公共契约与完成门禁规则已覆盖；首次单一能力切片不足以形成新 Skill，本次无变化 |
| 验证记录 | [`outbox-version-retirement-scan-2026-07-27.md`](outbox-version-retirement-scan-2026-07-27.md) |

## 增补：2026-07-27，性能硬化基础

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **416 → 421** | 新增 Dapper 低基数指标 2 项、Outbox/Jobs 满批次排空 2 项、Jobs 同批 Definition 合并查询 1 项 |
| RED / GREEN | Dapper 指标缺失、Worker 固定空等、Jobs Definition 逐条查询与 Vue 静态路由均先出现预期失败；最小实现后 Dapper 聚焦 **2/2**、Outbox/Jobs 聚焦 **19/19**、Vue 路由聚焦 **1/1** |
| Compatibility / Architecture / Integration | 门槛保持 **7/49/191**；完整验证结果记录于性能硬化验证文档 |
| 四处 canonical 门槛 | **421/7/49/191** |
| 规则 / Skills 复盘 | 根据项目所有者的长期治理决策，新增 `rules/performance-engineering.md` 与 `fullnet-performance-hardening`，并以 Skill 契约测试和官方校验器固化 |
| 验证记录 | [`performance-hardening-foundation-2026-07-27.md`](performance-hardening-foundation-2026-07-27.md) |

## 增补：2026-07-27，审计分页数据库往返合并

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **421 → 427** | 访问、操作、异常日志在 SQL Server/MySQL 下各新增 1 个单次多结果往返场景 |
| RED / GREEN | 三个查询服务缺少多结果执行器依赖时编译失败；实现后聚焦 **8/8**、Unit 全量 **427/427** |
| 双库聚焦 | SQL Server/MySQL 审计 API 各 **3/3**，失败 0、跳过 0 |
| Integration 门槛 | 保持 **191**；本次未新增 Integration 测试发现项 |
| 四处 canonical 门槛 | **427/7/49/191** |
| 性能边界 | 可确认每个审计列表数据库命令往返 **2 → 1**；未执行生产等价 P50/P95/P99，不声明固定延迟收益 |
| 验证记录 | [`performance-hardening-foundation-2026-07-27.md`](performance-hardening-foundation-2026-07-27.md) |

## 增补（2026-07-27，审计大表双库基准契约）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **427 → 431** | 新增基准默认规模、最近秩百分位、四场景矩阵和生产 Access Log SQL 防漂移共 4 项 |
| RED / GREEN | 基准类型尚不存在时编译失败；实现后聚焦 **4/4**、Unit 全量 **431/431** |
| Compatibility / Architecture / Integration | 门槛保持 **7/49/191**；Architecture 先以 **47/49** 识别 benchmark 的新基础设施引用，精确登记非运行时基准消费者后 **49/49**；基准使用独立 Testcontainers，不增加 Integration 发现项 |
| 四处 canonical 门槛 | **431/7/49/191** |
| 性能证据 | SQL Server/MySQL 100,000 行、预热 5、采样 30 的 P50/P95/P99 与双库计划已记录 |
| 验证记录 | [`performance-hardening-foundation-2026-07-27.md`](performance-hardening-foundation-2026-07-27.md) |

## 增补（2026-07-27，SQL Server 审计计划稳定性 A/B）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **431 → 435** | 新增 A/B mode Provider 约束、混合请求顺序、三类查询策略和 ShowPlan 编译/运行指标解析共 4 项 |
| RED / GREEN | A/B mode、查询工厂、顺序和指标类型缺失时编译失败；实现后聚焦 **8/8** |
| Compatibility / Architecture / Integration | 门槛保持 **7/49/191**；本 Task 仅新增隔离 benchmark，不修改生产 SQL 或 Integration 发现项 |
| 四处 canonical 门槛 | **435/7/49/191** |
| 性能证据 | SQL Server 100,000 行、两种首次编译顺序、三策略、预热 5、采样 30 的 P50/P95/P99、逻辑读、实际读行与编译成本已记录 |
| 验证记录 | [`performance-hardening-foundation-2026-07-27.md`](performance-hardening-foundation-2026-07-27.md) 第 10 节 |

## 增补（2026-07-27，SQL Server 审计固定谓词生产落地）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **435 → 436** | 新增三类 SQL Server 分页形状有界、唯一、参数化与稳定 Scope/Statement Name 契约 1 项 |
| RED / GREEN | 三类生产 shape 接口缺失时编译失败；实现后审计 SQL/服务/benchmark 聚焦 **15/15**、Unit 全量 **436/436** |
| Compatibility / Architecture / Integration | **7/49/191**；Architecture 首次 **48/49** 阻止普通方法构造 `SqlStatement`，改用精确白名单的安全原型 clone 后 **49/49**；SQL Server/MySQL 审计 API 各 **3/3** |
| 四处 canonical 门槛 | **436/7/49/191** |
| 性能证据 | SQL Server 100,000 行、两种顺序、三策略、预热 5、采样 30；生产等价分支四组 P95 相对当前下降 **41.62%–56.13%**，count 逻辑读保持 **606/81** |
| 验证记录 | [`performance-hardening-foundation-2026-07-27.md`](performance-hardening-foundation-2026-07-27.md) 第 11 节 |

## 增补（2026-07-27，MySQL 深 OFFSET 索引 Hint A/B）

| 项目 | 结果 |
| --- | --- |
| Unit 门槛 **436 → 439** | 新增 MySQL A/B mode Provider 约束、固定索引 Hint SQL 和成对反转采样顺序共 3 项 |
| RED / GREEN | mode、MySQL 策略工厂和采样顺序缺失时编译失败；实现后基准聚焦 **11/11**、Unit 全量 **439/439** |
| Compatibility / Architecture / Integration | 门槛保持 **7/49/191**；本 Task 仅增加隔离 benchmark，不修改生产 SQL、迁移、公共 API 或 Integration 发现项 |
| 四处 canonical 门槛 | **439/7/49/191** |
| 性能证据 | MySQL 100,000 行、四场景、两策略、预热 5、成对交替采样 30；Hint 深页 P95/P99 改善但 P50 与其他场景尾延迟退化，拒绝生产落地 |
| 验证记录 | [`performance-hardening-foundation-2026-07-27.md`](performance-hardening-foundation-2026-07-27.md) 第 12 节 |

## 增补（2026-07-27，MySQL 延迟物化 A/B）

| 审计项 | 结果 |
| --- | --- |
| Unit 门槛 **439 → 442** | 新增延迟物化 mode Provider 约束、固定 SQL/独立策略矩阵和有序 ID 等价性共 3 项 |
| RED / GREEN | mode、延迟物化策略和页面有序 ID 签名缺失时编译失败；实现后基准聚焦 **14/14**、Unit 全量 **442/442** |
| 行为边界 | 仅新增隔离 benchmark；生产 SQL、迁移、公共 API 和 MySQL 运行时行为未修改 |
| Compatibility / Architecture / Integration | 门槛保持 **7/49/191**；本 Task 仅增加隔离 benchmark，不修改生产 SQL、迁移、公共 API 或 Integration 发现项 |
| 四处 canonical 门槛 | **442/7/49/191** |
| 性能证据 | MySQL 100,000 行、四场景、两策略、两次独立容器、预热 5、成对交替采样 30；深页全部百分位稳定改善，但 contains 尾延迟方向不稳定，拒绝通用生产落地 |
| 验证记录 | [`performance-hardening-foundation-2026-07-27.md`](performance-hardening-foundation-2026-07-27.md) 第 13 节 |

## 增补（2026-07-27，访问日志显式游标分页）

| 审计项 | 结果 |
| --- | --- |
| Unit 门槛 **442 → 454** | 游标/服务/双库 SQL 8 项，cursor A/B mode、生产 SQL 防漂移和 keyset 契约 4 项 |
| RED / GREEN | C# 类型缺失、客户端守卫/API/页面和 Layui OFFSET 请求分别失败；实现后游标 **8/8**、基准 **18/18**、客户端契约 **4/4**、Vue **3/3**、Layui **2/2** |
| 公共兼容 | 旧 OFFSET/PagedResult 路由与客户端函数保留；新增 `/cursor`、游标页 schema 和稳定 400 错误码 |
| 双库 API | SQL Server/MySQL 各 **1/1**，权限、首批、下一批无重复、三条相同时间戳记录跨页后 ID 完整唯一、非法游标、详情与运行时 OpenAPI 通过 |
| 四处 canonical 门槛 | **454/7/49/191** |
| 性能证据 | 双库 100,000 行、页面 50、预热 5、成对采样 30；cursor P50/P95/P99 降幅 SQL Server **92.65%/91.96%/88.39%**，MySQL **97.05%/96.67%/97.12%** |
| 验证记录 | [`performance-hardening-foundation-2026-07-27.md`](performance-hardening-foundation-2026-07-27.md) 第 14 节 |

## 增补（2026-07-28，Outbox 主动续租与批尾保护）

| 审计项 | 结果 |
| --- | --- |
| Unit 门槛 **454 → 461** | 新增长 Handler 周期续租、独立 Scoped Store、续租失败传播、最终终态竞争、终态前失败、异常优先级与 MySQL matched-row 连接策略共 7 项 |
| Integration 门槛 **191 → 193** | SQL Server/MySQL 各新增 1 项批尾保护，Infrastructure 分片 **59 → 61** |
| RED / GREEN | 缺少配置/Store 契约时 7 个预期编译错误；补契约但未接入 Processor 时 **12/14**；最终 Processor 回归 **18/18**、MySQL 连接策略 **15/15** |
| 双库聚焦 | SQL Server/MySQL 批尾续租 **2/2**；MySQL 独立连续复跑 **3/3** |
| 四处 canonical 门槛 | **461/7/49/193** |
| 完整验证 | Release **0 warning / 0 error**；**461/7/49/193** 全部通过，失败 0、跳过 0；最终完整 Integration `32m44.276s` |
| 验证记录 | [`outbox-active-lease-renewal-2026-07-28.md`](outbox-active-lease-renewal-2026-07-28.md) |

## 增补（2026-07-28，生产等价混合负载基线）

| 审计项 | 结果 |
| --- | --- |
| Unit 门槛 **461 → 474** | 新增默认矩阵、workload 覆盖、稳定权重、固定种子选择、CLI 校验、必需指标、百分位算法、Provider 预算、逐档检查点、完整响应体消费、MySQL 连接池遥测、证据失败门禁与容器 CPU 归一化共 13 项 |
| RED / GREEN | `MixedLoad` 类型与命名空间缺失时基准和契约测试编译失败；检查点与响应体消费 API 缺失时新增测试编译失败；实现后聚焦 **13/13** |
| 行为边界 | 只新增隔离 benchmark、测试和文档；不修改生产 API、数据库结构、认证语义、Audit 可靠性或 Outbox Worker 默认并发 |
| 双库短矩阵 | SQL Server/MySQL 各一档冒烟通过；完整消费响应体，连接池/Dapper/数据库证据完整，原始 NDJSON 存在，证据门禁退出码 0 |
| 四处 canonical 门槛 | **474/7/49/193** |
| 性能证据 | V2 的 8 档长窗因 `ResponseHeadersRead` 未完整消费多数读取响应体而降级为诊断数据；正式 V3 待从已提交源码重跑，不以 V2 冻结 QPS 或饱和结论 |
| 验证记录 | [`production-equivalent-mixed-load-2026-07-28.md`](production-equivalent-mixed-load-2026-07-28.md) |
