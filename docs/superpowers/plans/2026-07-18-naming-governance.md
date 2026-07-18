# Naming Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把已批准的 Full.NET 命名规范转化为机器可读 Profile、SQL/C#／协议门禁和代码生成器共用的确定性命名内核。

**Architecture:** `contracts/naming/fullnet-naming-profile.json` 是跨工具命名事实源；Node 校验器负责仓库与 SQL 静态检查，C# Architecture Tests 负责程序集和稳定协议目录，`Full.NET.Data.CodeGeneration` 将相同 Profile 嵌入发布物并提供纯函数命名服务。现有不合规名称只能通过精确债务清单暂时放行，数据库和公共契约修复由独立的 1.0 前规范化计划执行。

**Tech Stack:** .NET 10、MSTest/Microsoft Testing Platform、Node.js 24、原生 ESM、System.Text.Json、SHA-256、DbUp、SQL Server、MySQL。

**实施状态（2026-07-18）：** Tasks 1-5 已完成，验证证据见 [`docs/verification/naming-governance.md`](../../verification/naming-governance.md)。为避免项目生成器误占 `fn`，实际 API 使用 `SchemaName.CreateFramework(...)` 与 `SchemaName.CreateProject(...)` 两个显式入口，替代计划示例中语义不明确的单一 `Create(ownerKey, ...)`；存量规范化仍由独立计划负责。

## Global Constraints

- Full.NET 官方表使用 `fn` OwnerKey；项目 OwnerKey 在脚手架生成时冻结，禁止运行时动态表前缀。
- 表名使用 `{owner}_{module}_{entity}` 小写 snake_case；数据库列使用 PascalCase并直接映射 C# 属性。
- 数据库标识符共同上限为 64 个 ASCII 字符；只有索引/约束允许统一稳定摘要压缩。
- JSON 保持 camelCase；权限、错误、消息、缓存和配置分别遵循批准规格，禁止用一种 casing 覆盖所有平台。
- 现有债务必须按精确名称、范围和最晚移除里程碑登记；禁止通配符、目录级或永久豁免。
- 本计划不改数据库数据或已发布协议值；相关迁移只能执行 `2026-07-18-pre-v1-naming-normalization.md`。

---

### Task 1: 建立机器可读 Naming Profile 与债务清单

**Files:**
- Create: `contracts/naming/fullnet-naming-profile.json`
- Create: `contracts/naming/naming-debt.json`
- Create: `tests/naming/naming-profile.test.mjs`
- Modify: `package.json`

**Interfaces:**
- Consumes: `rules/naming-conventions.md`
- Produces: `NamingProfileV1` JSON、`NamingDebtV1` JSON 和根命令 `pnpm test:naming`

- [x] **Step 1: 先写失败的 Profile 结构与值测试**

测试必须读取两个 JSON，断言：`schemaVersion=1`、框架 OwnerKey 为 `fn`、禁止 OwnerKey 含 `sys`、数据库标识符上限为 64、约束摘要为 SHA-256/8 hex、表/列/权限/错误/消息正则与规则文档一致。债务记录必须包含精确 `kind`、`value`、`reason`、`removalMilestone`，并拒绝 `*`、正则和空到期里程碑。

```js
assert.equal(profile.database.frameworkOwnerKey, 'fn');
assert.equal(profile.database.maxIdentifierLength, 64);
assert.equal(profile.database.columnCase, 'pascal');
assert.deepEqual(profile.database.reservedOwnerKeys.sort(), [
  'dbo', 'fn', 'information_schema', 'mysql', 'performance_schema', 'sys'
].sort());
assert.ok(debt.items.every((item) => !item.value.includes('*')));
```

- [x] **Step 2: 运行并确认 Profile 尚不存在而失败**

Run: `node --test tests/naming/naming-profile.test.mjs`

Expected: FAIL，指出 `contracts/naming/fullnet-naming-profile.json` 不存在。

- [x] **Step 3: 创建最小 Profile 与精确债务记录**

Profile 必须显式保存：Owner/Module/Entity 正则、表模板、PascalCase 列、标准时间后缀、约束格式、64 字符限制、摘要算法、缩写词典、API/JSON/权限/错误/消息/配置/缓存格式。初始债务至少逐项登记 `fn_tenant_tenant`、Foundation Tenancy/Outbox 时间列、Outbox `Type`、已知连字符错误码/事件类型/Statement 名以及未显式命名的主键。

- [x] **Step 4: 增加根命令并验证 JSON 无重复或漂移**

在 `package.json` 增加：

```json
"test:naming": "node --test tests/naming/*.test.mjs"
```

Run: `pnpm test:naming`

Expected: PASS；Profile/债务 Schema 和规则关键常量完全一致。

- [x] **Step 5: 提交 Profile 基线**

```bash
git add contracts/naming package.json tests/naming/naming-profile.test.mjs
git commit -m "test: define naming governance profile"
```

### Task 2: 建立 SQL、迁移和数据库对象命名 Lint

**Files:**
- Create: `scripts/naming/load-naming-profile.mjs`
- Create: `scripts/naming/database-object-name.mjs`
- Create: `scripts/naming/validate-sql-names.mjs`
- Create: `tests/naming/database-object-name.test.mjs`
- Create: `tests/naming/sql-naming.test.mjs`
- Create: `tests/fixtures/naming/valid-schema.sql`
- Create: `tests/fixtures/naming/invalid-schema.sql`
- Modify: `package.json`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: `NamingProfileV1`、`NamingDebtV1`、两库 DbUp SQL
- Produces: `buildDatabaseObjectName(fullName): string` 与 `validateSqlNaming(paths): NamingViolation[]`

- [x] **Step 1: 写失败的对象名和 SQL 规则测试**

覆盖：小写表名、三段所有权、PascalCase 列、显式 PK/FK/UX/IX/CK/DF、64 字符、保留字、`SELECT *`、迁移文件配对、表名大小写不一致、精确债务放行和通配豁免拒绝。长名称预期值必须固定，不能只断言长度。

```js
assert.equal(
  buildDatabaseObjectName('IX_fn_notifications_delivery_attempt_SubscriptionId_RequestedAtUtc_ChannelProvider'),
  'IX_fn_notifications_delivery_attempt_SubscriptionId_Req_5b137a8d'
);
```

该期望值来自对完整 UTF-8 名称计算 SHA-256 后取前 8 位小写 hex；实现不得修改算法迎合其他结果。

- [x] **Step 2: 运行并确认校验器缺失而失败**

Run: `pnpm test:naming`

Expected: FAIL，指出 `database-object-name.mjs` 或 `validate-sql-names.mjs` 不存在。

- [x] **Step 3: 实现稳定摘要与 SQL 标识符提取**

`buildDatabaseObjectName` 对 ASCII 名称执行：长度不超过 64 原样返回；否则使用前 55 字符、`_`、SHA-256 UTF-8 首 8 位小写 hex。SQL 校验器仅解析 DDL/静态 SQL 的受控子集；不能可靠解析的语句报告“需人工审查”，禁止假装安全通过。

- [x] **Step 4: 只用精确债务清单放行存量**

每个违反项以 `{kind, value, file}` 精确匹配债务；文件移动、值变化或新违规必须失败。输出包含规则 ID、文件、行号、实际值和推荐规范值，不自动改写 SQL。

- [x] **Step 5: 接入根命令与 CI**

根命令顺序执行 Profile、对象名和 SQL 测试，再扫描 `src/**/*.sql` 及包含静态 SQL 的已登记 `.cs` 文件。CI 在客户端审计后、.NET 构建前执行 `pnpm test:naming`。

Run: `pnpm test:naming && pnpm test:workspace`

Expected: PASS；删除任一债务条目会因对应存量违规失败，新建 `sys_identity_user` 夹具会失败。

- [x] **Step 6: 提交 SQL 命名门禁**

```bash
git add scripts/naming tests/naming tests/fixtures/naming package.json .github/workflows/ci.yml
git commit -m "test: enforce database naming conventions"
```

### Task 3: 建立 C# 与稳定协议命名门禁

**Files:**
- Modify: `.editorconfig`
- Create: `tests/Full.NET.ArchitectureTests/NamingConventionTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: 已加载的 Full.NET 程序集、ErrorCodes、PermissionDefinition、Outbox Handler 和 SQL Statement Catalog
- Produces: C# 标识符、权限码、错误码、消息类型和 Statement ID 的 Architecture Tests

- [x] **Step 1: 写失败的程序集与协议目录测试**

新增测试分别断言：接口 `I` 前缀、异步方法 `Async`、项目缩写词典、数据库 Row 属性 PascalCase；权限符合 `module.plural_resource.action`；新错误码每段为 lower_snake；新消息类型为 `owner.module.entity.event` 且版本不在字符串中；Statement ID 使用 lower_snake 点分层。精确债务清单中的既有值暂时放行。

- [x] **Step 2: 运行并确认现有混合协议值触发失败**

Run:

```powershell
dotnet build Full.NET.slnx -c Release
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 10
```

Expected: FAIL，至少报告一个未被债务清单覆盖的连字符协议值或测试尚未实现；失败不能来自测试发现数量不足以外的环境问题。

- [x] **Step 3: 配置 `.editorconfig` C# 命名**

增加 types/public members PascalCase、interfaces `I`＋PascalCase、parameters/local/private fields camelCase/`_camelCase` 的 `dotnet_naming_*` 规则；通过 `dotnet_diagnostic.IDE1006.severity = warning` 进入构建。异步后缀和项目缩写由 Architecture Tests 检查，不用无法表达语义的 EditorConfig 规则硬凑。

- [x] **Step 4: 实现协议扫描并读取同一债务 JSON**

测试从仓库根定位 `contracts/naming/naming-debt.json`，只接受精确值；常量目录和 Contributor 必须通过公开/内部可验证入口枚举，不通过脆弱的源文本正则假装覆盖运行时值。

- [x] **Step 5: 更新测试数量门槛并运行验证**

若新增 4 项 Architecture Tests，则把 README、getting-started 和 CI 的最小数量从 9 更新为 13；实际数量以测试运行器新鲜输出为准，不能机械采用示例值。

Run:

```powershell
dotnet build Full.NET.slnx -c Release
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 13
```

Expected: PASS，执行数不少于更新后的门槛，失败数 0。

- [x] **Step 6: 提交 C#／协议门禁**

```bash
git add .editorconfig tests/Full.NET.ArchitectureTests README.md docs/development/getting-started.md .github/workflows/ci.yml
git commit -m "test: enforce code and contract naming"
```

### Task 4: 建立 CodeGeneration 共用命名内核

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Full.NET.Data.CodeGeneration.csproj`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Naming/NamingProfile.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Naming/SchemaName.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Naming/DatabaseObjectNameBuilder.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Naming/ContractNameValidator.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/DatabaseObjectNameBuilderTests.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/ContractNameValidatorTests.cs`
- Modify: `Full.NET.slnx`
- Modify: `tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj`
- Modify: `README.md`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: 嵌入发布物的 `fullnet-naming-profile.json`
- Produces: `SchemaName.Create(ownerKey, moduleKey, entityKey)`、`DatabaseObjectNameBuilder.Build(string)`、`ContractNameValidator.Validate*`

- [x] **Step 1: 写失败的纯函数单元测试**

覆盖合法/非法 OwnerKey、项目占用 `fn`、禁止 `sys`、表名 64 字符、PascalCase 列、固定缩写、长约束摘要、不同文化区一致性、10 万个确定性样例无碰撞、权限/错误/消息格式。测试明确区分“表名超长直接失败”和“约束名超长允许摘要”。

- [x] **Step 2: 运行聚焦测试并确认项目不存在而失败**

Run:

```powershell
dotnet build Full.NET.slnx -c Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter CodeGeneration --minimum-expected-tests 1
```

Expected: FAIL，指出 CodeGeneration 项目/类型尚不存在。

- [x] **Step 3: 嵌入 Profile 并实现最小命名服务**

项目文件将 `contracts/naming/fullnet-naming-profile.json` 作为 `EmbeddedResource` 链接进入程序集；`NamingProfile.LoadDefault()` 使用 System.Text.Json 读取并在加载时验证 `schemaVersion=1`。所有 Builder 为无 IO、无当前 Culture、无全局可变状态的纯函数。

- [x] **Step 4: 建立 JSON 与 C# 行为一致性测试**

Node 与 C# 使用同一组 `contracts/naming/examples.json` 输入/期望输出，至少覆盖表、列、约束、权限、错误和消息。任何一端结果不同都失败，禁止在两个实现中维护不同示例。

- [x] **Step 5: 运行 Unit、Architecture 和重复生成验证**

Run:

```powershell
dotnet build Full.NET.slnx -c Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter CodeGeneration --minimum-expected-tests 12
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 13
pnpm test:naming
```

Expected: 至少新增并执行 12 项 CodeGeneration Unit Tests，Architecture Tests 不少于 13 项，全部 PASS；第二次执行不产生 Git diff。随后把 Unit 总门槛按运行器新鲜输出同步到 README、getting-started 和 CI。

- [x] **Step 6: 提交命名内核**

```bash
git add src/BuildingBlocks/Full.NET.Data.CodeGeneration tests/Full.NET.UnitTests Full.NET.slnx contracts/naming/examples.json README.md .github/workflows/ci.yml
git commit -m "feat: add code generation naming kernel"
```

### Task 5: 接入模块交付、模板和状态治理

**Files:**
- Modify: `.agents/skills/fullnet-module-delivery/SKILL.md`
- Modify: `.agents/skills/fullnet-module-delivery/references/module-delivery-checklist.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md`
- Create: `docs/verification/naming-governance.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: Tasks 1-4 的 Profile、Lint、Architecture Tests 和命名内核
- Produces: 新模块/生成器默认执行的命名门禁及准确状态记录

- [x] **Step 1: 先为项目 Skill 增加失败契约**

修改 `tests/skills/validate_project_skills.py`，要求模块交付 Skill 引用 `rules/naming-conventions.md`、执行 `pnpm test:naming`，并在数据库/API/消息命名变化时检查债务清单。先运行确认当前 Skill 因缺少引用而失败。

- [x] **Step 2: 更新 Skill 和模板入口**

Skill 只说明何时调用命名 Profile 与门禁，不复制整份规则。后端、SQL、Vue/Layui 模板必须通过 `Full.NET.Data.CodeGeneration` 命名内核取名，禁止模板自行实现 snake/Pascal 或单复数转换。

- [x] **Step 3: 运行 Skills、命名和完整相关测试**

Run:

```powershell
python -X utf8 tests/skills/validate_project_skills.py
pnpm test:naming
dotnet build Full.NET.slnx -c Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter CodeGeneration --minimum-expected-tests 12
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 13
```

Expected: 全部 PASS，无新增通配债务；再按 README 的最新完整测试命令运行 Unit/Compatibility/Integration 等受影响套件，并以新鲜发现数量更新全量门槛。

- [x] **Step 4: 更新状态但不误报存量已规范化**

命名 Profile、Lint、C#／协议门禁和代码生成内核全部通过后，命名治理最多标记为 `Implemented`；只有独立规范化计划完成双库和兼容验收后才可标记为 `Verified`。

- [x] **Step 5: 提交治理闭环**

```bash
git add .agents/skills tests/skills docs README.md
git commit -m "docs: close naming governance loop"
```
