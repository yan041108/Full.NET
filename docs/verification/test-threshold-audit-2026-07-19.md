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

## 关联文档

- [当前能力状态矩阵](../roadmap/capability-status.md)
- [本地开发与运行指南](../development/getting-started.md)
- [外部全面分析复核与吸收记录（2026-07-21）](external-review-2026-07-21.md)
