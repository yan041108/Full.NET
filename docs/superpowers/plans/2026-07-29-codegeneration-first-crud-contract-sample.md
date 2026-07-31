# CodeGeneration 首个 CRUD 契约样例实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立一个由统一 `FullNetCrudSchema` 驱动、跨进程和文化环境确定性输出后端契约、双库 Dapper SQL、Vue API、Layui API 与命名报告的首个 CRUD 生成样例。

**Architecture:** 本切片只扩展无 Web 依赖的 `Full.NET.Data.CodeGeneration`，不创建后台管理模块、CLI、数据库反向工程或模板存储。Schema 保存所有已确认的逻辑名和物理名；生成器只消费 Schema 与嵌入的 Naming Profile，不在模板中猜测单复数、大小写或表前缀。生成结果先作为内存产物清单返回，文件覆盖策略留给后续 CLI 切片。

**Tech Stack:** .NET 10、C#、MSTest、System.Text.Json、TypeScript、ES Modules、SQL Server、MySQL。

## Global Constraints

- OwnerKey、ModuleKey、EntityKey、数据库对象名和稳定协议码必须通过共享 Naming Profile。
- Schema 必须显式保存表名、CLR 类型名、API 资源名、权限资源名及列的数据库/CLR/JSON 三种名称。
- SQL Server/MySQL 必须生成同名表访问、参数化 SQL、租户过滤、稳定排序与乐观并发条件。
- `IsActive` 是当前软禁用生成器的必需非空 Boolean 字段；版本化禁用必须从客户端提交 Version。
- C# 契约必须显式绑定 JSON 名；Int64/Decimal 的跨 JavaScript 线格式统一使用字符串，避免精度丢失。
- nullable 字段按“属性存在且值可为 null”生成，C# 使用可空类型，TypeScript 使用 `T | null`，不得误写成可省略属性。
- 生成文件只使用 `.g.cs`、`.generated.ts`、`.generated.js` 等已登记后缀，统一 LF 和 UTF-8 文本语义。
- 相同 Schema 重复生成必须字节级一致；生成器不得读取当前文化、当前时间、随机数或机器路径。
- 本切片不写目标工作区，不实现 `Scaffold`/`RefreshGenerated` 覆盖策略，不宣称完整代码生成能力已完成。

---

### Task 1: 统一 CRUD Schema 与 Naming Profile 校验

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Naming/NamingProfile.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Naming/ContractNameValidator.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetScalarType.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetColumn.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudSchema.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/FullNetCrudSchemaTests.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/ContractNameValidatorTests.cs`

**Interfaces:**
- Produces: `FullNetColumn(string DatabaseName, string ClrPropertyName, string JsonPropertyName, FullNetScalarType ScalarType, bool IsNullable = false, int? MaxLength = null)`.
- Produces: `FullNetCrudSchema.CreateProject(...)`.
- Produces: `ContractNameValidator.IsValidDotNetType(...)` 与 `IsValidHttpPathSegment(...)`.

- [x] **Step 1: 写入 Schema RED**

新增测试，使用显式名称创建租户级 `acme_catalog_product` Schema，并断言表名、权限码和列顺序保持不变；同时断言重复列名、错误 `TenantId` 类型、缺失 `Version`、不规范 CLR/JSON/API 名称会抛出 `ArgumentException`。

```csharp
var schema = FullNetCrudSchema.CreateProject(
    ownerKey: "acme",
    moduleKey: "catalog",
    entityKey: "product",
    databaseTableName: "acme_catalog_product",
    rootNamespace: "Acme.Modules.Catalog",
    clrTypeName: "Product",
    apiResourceName: "products",
    permissionResourceName: "products",
    isTenantScoped: true,
    hasVersion: true,
    columns:
    [
        new("Id", "Id", "id", FullNetScalarType.Uuid),
        new("TenantId", "TenantId", "tenantId", FullNetScalarType.Uuid),
        new("Name", "Name", "name", FullNetScalarType.String, MaxLength: 200),
        new("IsActive", "IsActive", "isActive", FullNetScalarType.Boolean),
        new("Version", "Version", "version", FullNetScalarType.Int64),
        new("CreatedAtUtc", "CreatedAtUtc", "createdAtUtc", FullNetScalarType.DateTimeUtc),
    ]);
```

- [x] **Step 2: 运行 RED**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
pnpm test:dotnet:unit -- --no-build
```

Expected: 因 Schema 类型与新校验入口尚不存在而编译失败。

- [x] **Step 3: 实现最小 Schema**

扩展 Naming Profile 的 `.NET` 类型正则与 HTTP path segment 正则映射；实现不可变 Schema 工厂。工厂必须验证：

```text
表名 == SchemaName.CreateProject(ownerKey, moduleKey, entityKey).Value
权限 read/write == {moduleKey}.{permissionResourceName}.read/write
列的三种名称分别唯一且符合 Naming Profile
Id == 非空 Uuid
IsActive == 非空 Boolean
isTenantScoped => TenantId == 非空 Uuid
hasVersion => Version == 非空 Int64
String.MaxLength > 0
DateTimeUtc 的 CLR 属性名以 Utc 结尾
```

- [x] **Step 4: 运行 GREEN**

Run: 与 Step 2 相同。

Expected: Schema 与 Naming 测试全部通过。

### Task 2: 确定性 CRUD 产物生成器

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/GeneratedArtifact.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudArtifactGenerator.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`

**Interfaces:**
- Consumes: `FullNetCrudSchema`.
- Produces: `GeneratedArtifact(string RelativePath, GeneratedArtifactKind Kind, string Content)`.
- Produces: `IReadOnlyList<GeneratedArtifact> CrudArtifactGenerator.Generate(FullNetCrudSchema schema)`.

- [x] **Step 1: 写入生成器 RED**

测试要求同一个 Product Schema 精确生成以下五个按路径排序且路径唯一的产物：

```text
backend/ProductContracts.g.cs
backend/ProductSql.g.cs
clients/layui/products.generated.js
clients/vue/products.generated.ts
reports/products.generation.json
```

并断言：

```text
C# 契约包含 ProductResponse/CreateProductRequest/UpdateProductRequest
SQL 同时包含 SQL Server OFFSET/FETCH 与 MySQL LIMIT/OFFSET
所有写 SQL 包含 TenantId，更新包含 Version
Vue/Layui API 均使用 /api/v1/catalog/products 与 catalog.products.read/write
报告保存 acme_catalog_product 和列的三种显式名称
```

- [x] **Step 2: 运行 RED**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
pnpm test:dotnet:unit -- --no-build
```

Expected: 因 `CrudArtifactGenerator` 与产物类型尚不存在而编译失败。

- [x] **Step 3: 实现最小生成器**

实现纯函数生成器。所有模板使用 `StringBuilder` 与 `InvariantCulture`，输出统一以单个 LF 结尾。相同 Schema 的第二次生成必须与第一次逐路径、逐内容相等；禁止把时间戳、绝对路径或随机标识写入产物。

- [x] **Step 4: 补充边界测试并运行 GREEN**

增加土耳其文化重复生成、路径唯一性、相同数据库/CLR/JSON 名称映射和 SQL 参数化断言。

Run: 与 Step 2 相同。

Expected: 生成器测试全部通过。

### Task 3: 编译快照与切片验证

**Files:**
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/backend/ProductContracts.g.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/backend/ProductSql.g.cs`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/clients/vue/products.generated.ts`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/clients/layui/products.generated.js`
- Create: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/reports/products.generation.json`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/capability-status.md`

**Interfaces:**
- Consumes: Task 2 的五项产物。
- Produces: 编译期 C# 快照、TypeScript/JavaScript 语法门禁和字节级重复生成证据。

- [x] **Step 1: 写入快照 RED**

把 Product Schema 的生成结果与五个固定夹具逐字节比较。首次运行必须因夹具不存在或内容不匹配而失败。

- [x] **Step 2: 写入固定夹具并运行 GREEN**

将生成器输出保存为固定夹具；`.g.cs` 由 UnitTests 项目直接编译，TypeScript 使用独立 `tsc --noEmit` 命令，Layui 产物使用 `node --check`。

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore
pnpm test:dotnet:unit -- --no-build
pnpm --dir ui/admin exec tsc --noEmit --ignoreConfig --target ES2024 --module NodeNext --moduleResolution NodeNext ../../tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/clients/vue/products.generated.ts
node --check tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/clients/layui/products.generated.js
```

Expected: 构建、CodeGeneration 单测和双客户端语法检查全部通过。

- [x] **Step 3: 更新真实状态**

仅把路线图更新为“统一 CRUD Schema 与首个确定性契约/SQL/API 产物样例已完成”；继续保留后台生成页面、CLI、数据库元数据导入、Vue/Layui 页面模板与写盘覆盖策略为开放项。

- [x] **Step 4: 执行切片收口**

Run:

```powershell
pnpm test:naming
pnpm test:integration:affected:plan -- --snapshot codegeneration-first-crud-sample-20260729 --phase inner
pnpm test:integration:affected -- --snapshot codegeneration-first-crud-sample-20260729 --phase slice
git diff --check
git status --short
```

Expected: Naming、影响集与 diff 检查通过；完整集合仍只由 `main` CI 执行。

Execution note（2026-07-29）：Release 构建、CodeGeneration **37/37**、Unit **537/537**、Naming **23/23**、TypeScript/JavaScript 语法和 Integration 工具/治理契约均通过。受影响 Smoke **6/8**；仅两条迁移首次执行数量断言因共享脏工作区同时存在其他任务尚未收口的 033～036 四组迁移而从预期 1 变为 5，本切片未新增或修改迁移，须在迁移任务收口后重跑，不能记为通过。
