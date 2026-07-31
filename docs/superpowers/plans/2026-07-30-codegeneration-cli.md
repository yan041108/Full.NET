# CodeGeneration CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 提供一个安全默认预览、显式授权写盘的 CRUD CodeGeneration 命令行入口。

**Architecture:** 新建一个真实 Console Tool 项目作为 `Full.NET.Data.CodeGeneration` 的薄适配器。CLI 只负责严格解析 JSON 输入、参数与退出码，生成、冲突检测、Manifest 和原子写盘全部复用 `CrudGenerationWorkspace`；本切片不包含数据库元数据导入、模板扩展或物理清理 committed tombstone。

**Tech Stack:** .NET 10、System.Text.Json、MSTest、现有 `Full.NET.Data.CodeGeneration`

## Global Constraints

- 默认只预览计划，只有显式 `--apply` 才允许写盘。
- 工作区必须已经存在，且继续服从链接、大小写别名、所有权和 recovery fail-closed 门禁。
- JSON 使用 camelCase、字符串枚举、严格 UTF-8，并拒绝未知字段。
- 退出码固定为：成功 `0`、工作区冲突 `2`、用法或输入无效 `64`、未预期运行失败 `1`。
- 不引入第三方 CLI 或序列化依赖，不复制生成、Manifest 或写盘逻辑。
- 所有手写注释使用中文；代码标识符使用英文。

---

### Task 1: 严格 Schema 文档适配器与命令编排

**Files:**
- Create: `src/Tools/Full.NET.CodeGeneration.Cli/Full.NET.CodeGeneration.Cli.csproj`
- Create: `src/Tools/Full.NET.CodeGeneration.Cli/CrudSchemaDocument.cs`
- Create: `src/Tools/Full.NET.CodeGeneration.Cli/CodeGenerationCli.cs`
- Create: `src/Tools/Full.NET.CodeGeneration.Cli/Program.cs`
- Create: `src/Tools/Full.NET.CodeGeneration.Cli/Properties/AssemblyInfo.cs`
- Create: `samples/codegeneration/catalog-product.schema.json`
- Modify: `tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`

**Interfaces:**
- Consumes: `FullNetCrudSchema.CreateProject(...)`、`CrudGenerationWorkspace.PlanAsync(...)`、`CrudGenerationWorkspace.ApplyAsync(...)`
- Produces: `CodeGenerationCli.RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)` 与可执行命令 `Full.NET.CodeGeneration.Cli`

- [x] **Step 1: 写入 CLI RED**

覆盖以下真实行为：

```csharp
[TestMethod]
public async Task Preview_valid_schema_reports_creates_without_writing()
{
    var exitCode = await CodeGenerationCli.RunAsync(
        ["--schema", schemaPath, "--workspace", workspaceRoot],
        output,
        error);

    Assert.AreEqual(0, exitCode);
    StringAssert.Contains(output.ToString(), "Create backend/ProductContracts.g.cs");
    Assert.AreEqual(0, Directory.GetFiles(
        workspaceRoot,
        "*",
        SearchOption.AllDirectories).Length);
}
```

同一测试类还必须覆盖：`--apply` 写入并可重复变为 `Unchanged`；手写文件冲突返回 `2` 且不写其他产物；缺参数、未知参数、未知 JSON 字段或非法 Schema 返回 `64`。

- [x] **Step 2: 运行 RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGenerationCliTests"
```

Expected: 编译失败，因为 CLI 项目和 `CodeGenerationCli` 尚不存在。

- [x] **Step 3: 实现严格 JSON 输入**

`CrudSchemaDocument` 必须只负责把 camelCase JSON 转换为已验证领域 Schema：

```csharp
var document = JsonSerializer.Deserialize(
    bytes,
    CodeGenerationCliJson.Options.GetTypeInfo(typeof(CrudSchemaDocument)))
    ?? throw new JsonException("CRUD Schema 文档不能为空。");

return FullNetCrudSchema.CreateProject(
    document.OwnerKey,
    document.ModuleKey,
    document.EntityKey,
    document.DatabaseTableName,
    document.RootNamespace,
    document.ClrTypeName,
    document.ApiResourceName,
    document.PermissionResourceName,
    document.IsTenantScoped,
    document.HasVersion,
    document.Columns.Select(column => column.ToColumn()).ToArray());
```

序列化选项必须使用 `JsonNamingPolicy.CamelCase`、`JsonStringEnumConverter`、`JsonUnmappedMemberHandling.Disallow`，并通过严格 UTF-8 解码拒绝 BOM 和无效字节。

- [x] **Step 4: 实现最小命令编排**

参数只接受：

```text
--schema <json-file> --workspace <existing-directory> [--apply]
--help
```

预览调用 `PlanAsync`，写盘调用 `ApplyAsync`。动作按计划稳定顺序输出：

```text
Create backend/ProductContracts.g.cs
Unchanged clients/vue/products.generated.ts
Conflict backend/ProductContracts.g.cs
```

冲突返回 `2`；可预期的参数、JSON、Schema 和路径输入错误返回 `64`；其他异常写入 stderr 并返回 `1`。不得输出输入文件内容、绝对工作区路径或堆栈。

- [x] **Step 5: 运行 GREEN**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CodeGenerationCliTests"
```

Expected: CLI 测试全部通过。

### Task 2: 解决方案接入、命令冒烟与分层验证

**Files:**
- Modify: `Full.NET.slnx`
- Modify: `eng/testing/test-matrix.json`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`

**Interfaces:**
- Consumes: Task 1 的 Console 项目和稳定退出码。
- Produces: 解决方案内可构建的 CLI、唯一 Unit 门槛和真实能力状态。

- [x] **Step 1: 接入解决方案与 Unit 引用**

在 `Full.NET.slnx` 的 `/src/Tools/` 下登记：

```xml
<Project Path="src/Tools/Full.NET.CodeGeneration.Cli/Full.NET.CodeGeneration.Cli.csproj" />
```

UnitTests 只为测试命令编排引用该项目；生产模块不得引用 Tool。

- [x] **Step 2: 执行真实进程冒烟**

在临时目录写入固定 Product Schema，依次运行：

```powershell
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -c Release -- --schema <schema.json> --workspace <workspace>
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -c Release -- --schema <schema.json> --workspace <workspace> --apply
dotnet run --project src/Tools/Full.NET.CodeGeneration.Cli -c Release -- --schema <schema.json> --workspace <workspace>
```

Expected: 首次预览输出五个 `Create` 且不写盘；显式应用写入五个产物与 Manifest；再次预览全部为 `Unchanged`。

- [x] **Step 3: 更新唯一测试门槛与真实状态**

只在 `eng/testing/test-matrix.json` 增加本次新增测试数。路线图将“写盘 CLI”从开放缺口移动到当前证据，但数据库元数据导入、模块/页面模板仍保持未交付。

- [x] **Step 4: 执行完成验证**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release
dotnet build Full.NET.slnx -c Release --no-restore
pnpm test:governance
pnpm test:integration:affected:plan -- --snapshot codegeneration-cli-20260730 --phase slice
git diff --check
```

Expected: Unit、Release 构建、治理和静态检查通过；affected 计划只报告任务真实影响集，完整 Integration 仍留给 `main` CI。

## Self-Review

- Spec coverage: 安全预览、显式写盘、严格输入、稳定退出码、冲突不写盘、真实进程冒烟、解决方案接入和状态同步均已分配。
- Placeholder scan: 无 TBD、TODO、模糊错误处理或“同上”步骤。
- Type consistency: CLI 只接受 `FullNetCrudSchema` 并返回现有 `GenerationWritePlan` 的可观察动作；Tool 不进入业务模块依赖方向。
