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

## 关联文档

- [当前能力状态矩阵](../roadmap/capability-status.md)
- [本地开发与运行指南](../development/getting-started.md)
- [外部全面分析复核与吸收记录（2026-07-21）](external-review-2026-07-21.md)
