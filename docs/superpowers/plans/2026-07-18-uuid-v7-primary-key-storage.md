# UUID v7 Primary Key Storage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:test-driven-development` for each behavior change and `fullnet-module-delivery` for every Dapper/migration slice. Execute tasks in order; the database switch is a maintenance-window change and must not be parallelized with naming or Seed migrations.

**Goal:** 在不改变 C# `Guid` 与公共 UUID 字符串契约的前提下，把 Full.NET 的 MySQL UUID 主键、外键和租约标识从 `char(36)` 迁移为 RFC 9562 字节序 `BINARY(16)`，并使 SQL Server 的主键/聚集索引选择显式、可验证、可恢复。

**Architecture:** 应用继续通过 `IIdGenerator` 在写库前生成 UUID v7。新增 `Full.NET.Data.MySql` Provider 边界统一构造 MySqlConnector 连接串并强制最终模式 `GuidFormat=Binary16`；业务模块只处理 `Guid`。MySQL 使用 008 Expand 添加/回填二进制影子列并以触发器保持旧应用写入同步，维护窗口内执行 009 Contract 原子化切换列名、约束和连接模式。SQL Server 不改变 `uniqueidentifier`，但在 009 中显式治理主键与高写入表聚集索引。001-007 历史迁移保持不变。

**Tech Stack:** .NET 10、Dapper、MySqlConnector、Microsoft.Data.SqlClient、DbUp、SQL Server 2022、MySQL 8.4、Testcontainers、MSTest/Microsoft Testing Platform、Node SQL governance tests。

**Approved Decision:** [ADR-0003](../../architecture/adr/ADR-0003-uuid-v7-primary-key-storage.md)

## Global Constraints

- `007_SeedExecutionAudit` 已真实实现并进入合并基线。本计划拥有迁移编号 `008_UuidBinaryExpand` 与 `009_UuidBinaryContract`；命名规范化顺延为 010/011。
- 008 和 009 必须在 Naming Expand 以及任何新的 UUID 持久化列之前完成；已存在的 Seed 执行表必须纳入本次存量转换。执行期间禁止其他分支新增迁移。
- SQL Server/MySQL 必须提交同名配对迁移。SQL Server 不做无意义的数据类型转换，但必须验证 UUID v7 写入、显式主键名称和聚集策略。
- `GuidFormat=Binary16` 的最终连接策略必须覆盖 API、Worker、Migrator、集成测试和导入适配器；业务模块不得直接引用 MySqlConnector 或执行 UUID 字节转换。
- MySQL 只接受 `UUID_TO_BIN(value, 0)`/`BIN_TO_UUID(value, 0)` 语义；明确禁止 `TimeSwapBinary16`、第二参数 `1` 和 `Guid.ToByteArray()`。
- 008 Expand 不删除或改名旧列；009 Contract 只有在停止写入、备份、数据核对、回退演练和批准的破坏性 DDL 豁免全部成立后才能执行。
- 009 后回退旧应用需要恢复数据库备份，不能只回滚二进制；Runbook 必须把这一事实写入 Go/No-Go 门禁。
- 完成前必须验证空库 001→009、带真实关系数据的 001-007→009、008 半完成重跑、冲突数据拒绝、双库应用 API/Worker/Outbox/Seed 路径。

---

### Task 1: 冻结 UUID 列清单、字节序契约和维护窗口

**Files:**
- Create: `contracts/database/uuid-storage-v1.json`
- Create: `tests/database/uuid-storage-contract.test.mjs`
- Create: `docs/development/uuid-binary-migration-runbook.md`
- Modify: `package.json`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Consumes: 001-007 SQL Server/MySQL 迁移、ADR-0003、当前 `DatabaseOptions`
- Produces: 机器可读 `UuidStorageContractV1`、完整列关系图、迁移 Go/No-Go 与恢复步骤

- [ ] **Step 1: 先写会失败的清单完整性测试**

扫描两库迁移中的 `uniqueidentifier`、`char(36)` 以及 `{Id|*Id}` 列，要求每个 UUID 列都登记 `table`、`column`、`nullable`、`role`（primary/foreign/reference/lease/family）、`referencedTable/Column`、`sqlServerType`、`mySqlLegacyType` 与 `mySqlTargetType`。未登记列、重复登记、非 RFC 目标或孤立外键必须失败。

- [ ] **Step 2: 建立现有 UUID 图**

清单至少覆盖：

```text
fn_tenant_tenant: Id
fn_outbox_message: Id, TenantId, LockId
fn_identity_user: Id, TenantId
fn_identity_refresh_session: Id, UserId, FamilyId, ReplacedById, ActiveTenantId
fn_identity_auth_audit: Id, UserId, SessionId, ContextTenantId, ActorUserId
fn_identity_role: Id, TenantId
fn_identity_user_role: UserId, RoleId
fn_identity_role_permission: RoleId
fn_seed_run: Id
fn_seed_run_item: RunId
```

`FamilyId`、`SessionId`、`TenantId` 等即使没有数据库外键也属于 UUID 引用，不能只扫描约束。清单还必须声明 010/011 命名变化，避免后续把旧表名当成新模板。

- [ ] **Step 3: 固定字节序向量**

在合同中保存至少三个固定 UUID v7（含首尾字节易辨识向量）、期望 16 字节小写 Hex 和规范文本。测试断言期望 Hex 与 RFC 网络字节序一致，并断言 time-swap Hex 不等于目标。

- [ ] **Step 4: 编写维护窗口 Runbook**

Runbook 按顺序规定：冻结迁移；备份并验证可恢复；停止 API/Worker；等待事务与 Outbox lease 到期；记录行数/外键/摘要；运行 008 或确认已运行；执行 delta backfill；执行 009；部署 Binary16 应用；运行双库冒烟；开启流量。任一步失败均停止；009 前可删除影子对象并继续旧应用，009 后必须恢复备份和旧应用。

- [ ] **Step 5: 接入合同检查**

`package.json` 新增 `test:uuid-storage` 并纳入 `test:naming` 或发布门禁。运行后必须因当前清单/实现不存在而 RED，随后仅补合同与扫描器使合同层 GREEN；不得因此修改迁移或代码。

- [ ] **Step 6: 提交合同和 Runbook**

```bash
git add contracts/database tests/database docs/development/uuid-binary-migration-runbook.md package.json docs/roadmap/capability-status.md
git commit -m "docs: freeze uuid storage migration contract"
```

### Task 2: 建立统一 MySQL 连接策略与固定向量测试

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.MySql/Full.NET.Data.MySql.csproj`
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/MySqlGuidStorageMode.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.MySql/MySqlConnectionStringPolicy.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Abstractions/DatabaseOptions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Full.NET.Data.Dapper.csproj`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DbConnectionFactory.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Full.NET.Migrations.DbUp.csproj`
- Modify: `src/BuildingBlocks/Full.NET.Migrations.DbUp/DbUpMigrationRunner.cs`
- Modify: `Full.NET.slnx`
- Create: `tests/Full.NET.UnitTests/Data/MySqlConnectionStringPolicyTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Data/GuidBinaryRoundTripTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`

**Interfaces:**
- Consumes: `DatabaseOptions.Provider/ConnectionString` 与 Task 1 固定向量
- Produces: `MySqlConnectionStringPolicy.Create(connectionString, mode, allowUserVariables)`；Legacy/Target 两种显式部署模式

- [ ] **Step 1: 写连接策略 RED 测试**

单元测试要求：`LegacyChar36` 只用于 001-008 过渡；`Binary16` 总是输出 `GuidFormat=Binary16`；输入含 `Char36`、`TimeSwapBinary16` 或冲突 GuidFormat 时拒绝而不是静默覆盖；迁移连接只额外启用 `AllowUserVariables=true`；错误不回显连接串或 Secret。

- [ ] **Step 2: 写真实 MySQL 往返 RED 测试**

在临时表中以 `BINARY(16)` 写入固定 `Guid`，断言：Dapper 读取回同一 `Guid`；`HEX(Id)` 等于合同 Hex；`BIN_TO_UUID(Id, 0)` 等于规范文本；`UUID_TO_BIN(text, 0)` 等于驱动写入；主键和外键连接成功；空 Guid 按应用门禁拒绝。另建反例断言 `UUID_TO_BIN(text, 1)` 不被接受为目标字节。

- [ ] **Step 3: 实现最小共享 Provider 边界**

`Full.NET.Data.MySql` 只负责 MySqlConnector 连接策略，不依赖 Dapper、业务模块或 Hosts。`DatabaseOptions` 增加封闭 `MySqlGuidStorageMode` 配置，默认在迁移完成前保持 `LegacyChar36`；Production 若未显式配置必须启动失败。Dapper Factory 与 DbUp Runner 都调用同一 Policy，禁止复制连接串修改逻辑。

- [ ] **Step 4: 添加依赖与敏感信息门禁**

Architecture Tests 断言业务模块不引用 `Full.NET.Data.MySql`/MySqlConnector，只有 Dapper、Migrator 与测试基础设施可引用；代码扫描禁止 `Guid.ToByteArray`、`UUID_TO_BIN(*, 1)`、`TimeSwapBinary16` 出现在允许的反例夹具之外。

- [ ] **Step 5: 运行聚焦验证**

```powershell
dotnet build Full.NET.slnx --configuration Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter "MySqlConnectionStringPolicy" --minimum-expected-tests 6
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --filter "GuidBinaryRoundTrip" --minimum-expected-tests 5 --timeout 10m
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --filter "Guid|MySql" --minimum-expected-tests 2
```

Expected: 全部 PASS；当前应用仍以 `LegacyChar36` 运行，不能提前把能力状态提升为可用。

- [ ] **Step 6: 提交 Provider 边界**

```bash
git add Full.NET.slnx src/BuildingBlocks/Full.NET.Data.* src/BuildingBlocks/Full.NET.Migrations.DbUp tests
git commit -m "feat: centralize mysql guid storage policy"
```

### Task 3: 实现 008 MySQL UUID Binary Expand

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/008_UuidBinaryExpand.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/008_UuidBinaryExpand.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/UuidBinaryExpandMigrationTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Migrations/UuidBinaryPartialRecoveryTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/MySqlMigrationTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Migrations/SqlServerMigrationTests.cs`

**Interfaces:**
- Consumes: Task 1 UUID 图、001-007 数据库、Task 2 `LegacyChar36`
- Produces: 每个 MySQL UUID 列的 `BINARY(16)` 影子列、完整回填和 legacy-write 同步触发器；旧列保持可用

- [ ] **Step 1: 先写存量数据与半完成恢复 RED 测试**

测试写入多租户用户、角色关系、刷新令牌 family/replacement、审计 Actor/Session、Pending/Locked Outbox 和 Seed run/item，再执行 008。分别模拟影子列仅创建一部分、回填一部分、触发器缺失和 DbUp 未记账重跑。SQL Server 配对脚本必须安全通过且不伪造类型转换。

- [ ] **Step 2: 实现可重入影子列**

每个登记 UUID 列添加同可空性的 `{Column}Binary BINARY(16)` 影子列；先以可空形式创建，再按主键确定顺序分批执行 `UUID_TO_BIN({Column}, 0)`。非法 UUID、重复二进制值、非空源产生空目标或引用缺失必须中止并只报告表/列/计数，不输出业务数据。

- [ ] **Step 3: 建立 legacy-write 同步**

为仍以 `char(36)` 写入的表建立命名稳定的 `BEFORE INSERT/UPDATE` 触发器，只执行文本到 Binary16 的 RFC 转换。触发器发现源/影子同时存在但不一致时必须 `SIGNAL` 失败，不得覆盖冲突。008 后旧应用继续运行时，新写入和更新必须保持影子列一致。

- [ ] **Step 4: 增加核对存储过程/查询**

Runbook 使用只读核对查询验证：每表源/目标非空计数、Distinct 计数、`BIN_TO_UUID(Binary, 0) = LOWER(Text)`、主外键/引用关系、固定抽样 SHA-256。核对 SQL 进入版本控制，不靠人工临时拼接。

- [ ] **Step 5: 运行 Expand 矩阵**

```powershell
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --filter "UuidBinaryExpand|UuidBinaryPartialRecovery" --minimum-expected-tests 10 --timeout 15m
```

Expected: MySQL 完整/四类半完成恢复、冲突拒绝和 legacy 新写入同步全部 PASS；SQL Server 空库/升级配对通过。

- [ ] **Step 6: 提交 Expand**

```bash
git add src/BuildingBlocks/Full.NET.Migrations.DbUp tests/Full.NET.IntegrationTests docs/development/uuid-binary-migration-runbook.md
git commit -m "feat: expand mysql binary uuid storage"
```

### Task 4: 冻结写入并执行 009 Contract 切换

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/009_UuidBinaryContract.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/009_UuidBinaryContract.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/UuidBinaryContractMigrationTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Migrations/UuidBinaryContractRecoveryTests.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/appsettings.json`
- Modify: `src/Hosts/Full.NET.Host.Worker/appsettings.json`
- Modify: `src/Hosts/Full.NET.Host.Migrator/appsettings.json`
- Modify: `src/Hosts/Full.NET.AppHost/Program.cs`
- Modify: `docs/development/uuid-binary-migration-runbook.md`

**Interfaces:**
- Consumes: 已核对的 008、停止 API/Worker 写入的维护窗口、已验证备份
- Produces: MySQL canonical UUID 列全部为 `BINARY(16)`；Host 全部使用 `Binary16` 模式；legacy 触发器/文本列退出

- [ ] **Step 1: 写 Contract 前置条件 RED 测试**

009 必须拒绝：008 未完成、任一影子 NULL/冲突、引用不一致、同步触发器缺失、数据库仍标记为允许 legacy writer、维护窗口令牌缺失或备份确认未登记。测试覆盖 009 在删除部分约束、重命名部分列后未记账的恢复路径。

- [ ] **Step 2: 编写 MySQL 约束切换顺序**

按依赖图先删除外键和关系表主键，再将文本列重命名为 `{Column}Legacy`、二进制影子列重命名为 canonical `{Column}`，重建显式 `PK_/FK_/UX_/IX_`。每一步前检查真实列类型、名称和数据一致性；如果 canonical 名同时存在但类型不符合预期立即失败。

- [ ] **Step 3: 收紧类型并移除过渡对象**

所有原非空 UUID 列收紧为 `BINARY(16) NOT NULL`，可空列保持可空。完成主外键和查询索引验证后删除同步触发器与 `{Column}Legacy`。破坏性步骤必须引用 Runbook 的批准豁免 ID；不得用 `SET FOREIGN_KEY_CHECKS=0` 掩盖坏关系。

- [ ] **Step 4: 显式治理 SQL Server 聚集索引**

SQL Server 保持 `uniqueidentifier`。009 为现有主键补齐稳定名称和显式 `CLUSTERED/NONCLUSTERED`：Outbox、Auth Audit 使用 UUID 非聚集主键，并按当前时间列＋`Id` 建立显式聚集索引；关系表按主导连接顺序显式聚集；低写入实体保留/重建显式 UUID 聚集主键。变更前后执行计划、页分裂和碎片基准保存在验证记录，未达到门禁时 009 不得发布。

- [ ] **Step 5: 切换所有 Host 配置**

API、Worker、Migrator、AppHost 和集成测试默认目标模式改为 `Binary16`。`LegacyChar36` 只允许受控恢复工具显式使用，Production 启动必须拒绝 legacy。对数据库 Schema 版本做启动检查：应用处于 Binary16 而 009 未完成，或数据库已 009 而应用处于 legacy，均在接收流量前失败。

- [ ] **Step 6: 运行 Contract/恢复矩阵**

```powershell
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --filter "UuidBinaryContract|UuidBinaryContractRecovery" --minimum-expected-tests 12 --timeout 20m
```

Expected: 空库、001-007 升级、008 在线增量、五类 009 半完成、冲突拒绝和 SQL Server 聚集策略全部 PASS；测试必须确认旧应用配置在 009 数据库上启动失败。

- [ ] **Step 7: 提交 Contract**

```bash
git add src tests docs/development/uuid-binary-migration-runbook.md
git commit -m "feat: switch uuid storage to binary16"
```

### Task 5: 验证应用生成、Dapper、事务与公共契约

**Files:**
- Create: `tests/Full.NET.UnitTests/Ids/GuidV7IdGeneratorTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Data/GuidPrimaryKeyApplicationTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/IdentityApiMySqlTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/TenancyApiMySqlTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Data/MultiResultQueryTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Tenancy/TenantProvisioningTests.cs`
- Modify: `tests/Full.NET.UnitTests/Hosting/FullNetJsonOptionsTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/DataAccessRulesTests.cs`

**Interfaces:**
- Consumes: 009 数据库与 Binary16 连接策略
- Produces: 业务层只用 `Guid`、写库前有 Id、父子/审计/Outbox 同事务可引用的跨库证据

- [x] **Step 1: 补 UUID v7 行为测试**

验证 `IIdGenerator` 产生非空 Version 7 UUID，版本位与变体位正确；任何持久化 Command 在执行首条 SQL 前已得到 Id。测试不把“连续生成值严格单调”作为 UUID 标准保证。

- [x] **Step 2: 验证同事务引用**

SQL Server/MySQL 分别创建父记录、子/关系记录、审计和 Outbox，全部在同一 `ICommandTransaction` 使用预生成 `Guid`；提交后引用完整，故障注入回滚后全部不存在。证明应用侧生成能力没有因 `BINARY(16)` 丢失。

- [x] **Step 3: 验证读取路径**

覆盖单行、列表、动态筛选、`QueryMultiple`、Nullable Guid、跨表 Join、Outbox lease/LockId、刷新 Session family/replacement 和超级管理员 Actor；业务 Row/DTO 仍为 `Guid`，禁止出现 `byte[]`。

- [x] **Step 4: 验证外部契约**

标准 API、ProblemDetails extension、Admin.NET 兼容包络、Vue/Layui/uni-app 契约夹具继续使用规范 UUID 字符串。OpenAPI 格式为 `uuid`；小写输出为规范化要求，输入可按 System.Text.Json 的标准 UUID 解析并在输出时规范化。

- [x] **Step 5: 运行双库全路径聚焦测试**

运行 Unit、Architecture、Integration 的 Release 全量命令前，先以 `--filter "Guid|IdentityApi|TenancyApi|Outbox|MultiResult"` 执行聚焦测试。所有新增测试通过后，以新鲜发现数量更新 README 门槛，不得降低现有门槛。

- [x] **Step 6: 提交应用验证**（分步提交：`f9bac5c`–`b7ff745`；验证记录见 `docs/verification/test-threshold-audit-2026-07-19.md`）

```bash
git add tests src docs
git commit -m "test: verify uuid v7 persistence boundaries"
```

### Task 6: 更新代码生成器与数据库治理门禁

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/`
- Modify: `contracts/naming/naming-profile.json`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/`
- Modify: `tests/naming/sql-naming.test.mjs`
- Modify: `tests/database/uuid-storage-contract.test.mjs`
- Modify: `.github/workflows/ci.yml`
- Modify: `.agents/skills/fullnet-module-delivery/SKILL.md`
- Modify: `tests/skills/fullnet-module-delivery.contract.json`

**Interfaces:**
- Consumes: ADR-0003、UuidStorageContractV1、目标数据库状态
- Produces: 新模块/CRUD 默认生成正确物理类型、显式聚集属性和跨端契约

- [ ] **Step 1: 先写生成器快照 RED 测试**

同一 UUID Schema 必须生成 C# `Guid`、SQL Server `uniqueidentifier`、MySQL `BINARY(16)` 和 JSON `string/uuid`；关系外键/复合主键沿用相同类型。Snowflake Profile 生成 `long/bigint` 和 JSON 字符串，并与 UUID Profile 互斥。

- [ ] **Step 2: 增加 SQL 硬门禁**

扫描新增/修改 MySQL 迁移，禁止 UUID 列使用 `char(36)`；扫描业务代码禁止手工 UUID 字节转换；扫描 SQL Server 新主键必须显式 `CLUSTERED`/`NONCLUSTERED`，并要求高写入表登记聚集索引用途。历史 001-007 只允许精确路径豁免，009 完成后删除该豁免。

- [ ] **Step 3: 更新项目 Skill（测试先行）**

先扩展 Skill 合同，要求任何新模块交付检查 UUID logical/physical mapping、MySQL Binary16、SQL Server clustering 和 API UUID string；确认现有 Skill 合同 RED 后再更新 `SKILL.md`，运行项目 Skill 验证与官方 `quick_validate.py`。

- [ ] **Step 4: 运行生成器与治理验证**

```powershell
pnpm test:uuid-storage
pnpm test:naming
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter "CodeGeneration|Guid" --minimum-expected-tests 10
python tests/skills/validate_project_skills.py
```

Expected: 全部 PASS；修改一份夹具为 `char(36)`、time-swap 或隐式 SQL Server 主键时测试可确定性失败。

- [ ] **Step 5: 提交生成治理**

```bash
git add src/BuildingBlocks/Full.NET.Data.CodeGeneration contracts tests .github .agents/skills/fullnet-module-delivery
git commit -m "feat: govern uuid primary key generation"
```

### Task 7: 完成发布验证与文档收口

**Files:**
- Modify: `README.md`
- Modify: `docs/getting-started.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md`
- Modify: `docs/superpowers/specs/2026-07-18-fullnet-naming-conventions-design.md`
- Modify: `rules/naming-conventions.md`
- Create: `docs/verification/uuid-v7-primary-key-storage-2026-07-18.md`
- Modify: `docs/superpowers/plans/2026-07-18-pre-v1-naming-normalization.md`
- Modify: `docs/superpowers/plans/2026-07-17-seed-data-module.md`

**Interfaces:**
- Consumes: Tasks 1-6 的新鲜输出、维护窗口记录和恢复演练
- Produces: 可复制配置、真实能力状态、后续 010/011 命名迁移解锁

- [ ] **Step 1: 运行完整门禁**

执行 README 当前全部 Release 构建、Unit、Compatibility、Architecture、SQL Server/MySQL Integration、Node/前端/客户端、许可证与漏洞检查；执行 `git diff --check`、链接、占位符和迁移配对检查。任何失败都不得把状态提升为 `Verified`。

- [ ] **Step 2: 执行恢复演练**

使用含真实关系数据、Pending Outbox 和 Seed 审计的备份演练两条路径：008 前/后回退旧应用；009 后恢复数据库备份并回退旧应用。记录耗时、数据摘要、RPO/RTO 和无法仅靠应用回滚的事实。

自动化等价路径（Testcontainers 31 项 + Runbook 映射）已记录于 [`docs/verification/uuid-v7-primary-key-storage-2026-07-19.md`](../../verification/uuid-v7-primary-key-storage-2026-07-19.md)。真实生产整库备份恢复与 RPO/RTO 计时仍待完成。

- [ ] **Step 3: 发布验证记录**

验证文档记录提交、镜像版本、数据库版本、连接模式、测试发现/通过数量、迁移耗时、行数/摘要、聚集索引基准与已知限制。不得保存连接串、用户数据或 Secret。

[`docs/verification/uuid-v7-primary-key-storage-2026-07-19.md`](../../verification/uuid-v7-primary-key-storage-2026-07-19.md) 已覆盖自动化恢复与 SQL Server 聚集索引 **结构** 证据；性能基准与生产窗口记录仍缺。

- [ ] **Step 4: 更新真实状态**

只有 Tasks 1-3 完成时标记 `Implemented`；009、全路径双库和恢复验证完成后可标记 `Build-verified`；只有生产等价维护窗口与 SQL Server 索引证据全部完成才标记 `Verified`。更新 Getting Started，说明 MySQL 必须使用 Binary16 模式和人工排障时使用 `BIN_TO_UUID(..., 0)`。

- [ ] **Step 5: 解锁后续迁移**

确认 008/009 已合并且任何未发布分支没有占号后，才执行 010/011 命名规范化。Seed 执行审计已经在 007 落地，不得重建或复用其编号；若实际后续编号改变，相关计划、合同和所有测试必须在同一提交同步，禁止仅改文件名。

- [ ] **Step 6: 规则与 Skills 复盘**

执行 `rules/rule-evolution.md` 与 `rules/skill-evolution.md`。ADR-0003 已在本次文档任务升级为强制规则；实现阶段若连接模式漂移、迁移恢复或生成器暴露新的重复问题，优先增强现有规则/`fullnet-module-delivery`，不得创建近义 Skill。

- [ ] **Step 7: 提交文档与证据**

```bash
git add README.md docs rules AGENTS.md
git commit -m "docs: verify uuid primary key storage"
```

## Completion Gate

以下条件全部满足前，本计划不得标记完成：

1. 业务层仍只使用 UUID v7/C# `Guid`，并在写库前获得非空主键；
2. SQL Server UUID 列为 `uniqueidentifier`，主键/聚集属性显式且高写入表有基准证据；
3. MySQL 当前 Schema 中所有登记 UUID 列均为 RFC 字节序 `BINARY(16)`，不存在未登记 `char(36)` UUID 债务；
4. API、Worker、Migrator、测试和导入边界统一使用 Binary16 连接策略；
5. 主键、外键、审计、Outbox、QueryMultiple、Seed 前置路径和公共 UUID 文本双库通过；
6. 001-007 升级、008/009 半完成恢复、冲突拒绝和备份恢复演练有可定位证据；
7. 010/011 后续命名计划编号无冲突，能力矩阵没有把未验证目标写成现状；
8. 完整发布门禁、`git diff --check`、规则复盘和 Skills 复盘完成。
