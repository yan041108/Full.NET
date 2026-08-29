# Identity Excel and Observability Log Control Plane Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付 Identity 用户安全 `.xlsx` 模板/导入导出，以及 Observability Admin 固定日志根目录、稳定文件 ID、有界尾读和下载控制面。

**Architecture:** Identity 在现有用户管理切片内增加固定列 Open XML 边界，继续复用现有批量导入服务作为唯一权威写入口，不引入通用 Excel 框架。Observability Admin 使用独立官方模块，只读取配置根目录下顶层 `.log` 文件；客户端永远只提交服务端生成的 SHA-256 文件 ID，不提交路径。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、System.IO.Compression、System.Xml.Linq、System.Text.Json source generation、Vue 3、TypeScript、Vitest、MSTest。

## Global Constraints

- 不新增第三方 Excel 包；`.xlsx` 只支持本切片固定工作表和固定列。
- Identity 文件上传上限 1 MiB、数据行上限 1,000；拒绝公式单元格、外部关系和未知表头。
- Excel 导出文本必须防止 `= + - @` 公式注入；导入不得接受公式计算结果。
- Observability Admin 只读配置根目录顶层 `.log` 文件；禁止客户端路径、递归枚举和符号链接越界。
- 日志目录最多返回 100 项；尾读默认 200 行，最多 5,000 行和 1 MiB；读取必须支持取消及活动文件 `FileShare.ReadWrite | FileShare.Delete`。
- 两项能力使用独立稳定权限；无权限时 Vue 不创建入口，服务端失败关闭。
- Host.Api 可达 JSON 类型必须进入源生成上下文；文件流与纯文本响应不进入 JSON 包络。
- 不修改数据库结构，不创建迁移，不修改 Layui 冻结客户端。

---

### Task 1: Identity fixed-schema workbook boundary

**Files:**
- Create: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/HostUserWorkbookCodec.cs`
- Create: `tests/Full.NET.UnitTests/Identity/HostUserWorkbookCodecTests.cs`

**Interfaces:**
- Produces: `HostUserWorkbookCodec.CreateImportTemplate()`, `Export(IReadOnlyList<HostUserResponse>)`, and `ParseImport(Stream, long, CancellationToken)` returning fixed `CreateHostUserRequest` rows.

- [x] **Step 1: Write failing codec tests.**

Cover template sheet/header values, export formula escaping, shared-string and inline-string imports, formula rejection, unknown/missing header rejection, ZIP/XML size limits, and 1,000-row limit.

- [x] **Step 2: Run RED.**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter HostUserWorkbookCodecTests
```

Expected: compilation fails because `HostUserWorkbookCodec` does not exist.

- [x] **Step 3: Implement the minimal fixed-schema codec.**

Use `ZipArchive`, deterministic Open XML parts and `XDocument`; reject formula cells and archive entries beyond declared limits. Do not expose a generic spreadsheet abstraction.

- [x] **Step 4: Run GREEN.**

Run the same filtered command; expected all codec tests pass.

### Task 2: Identity file endpoints and Vue workflow

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Serialization/IdentityJsonSerializerContext.cs`
- Modify: `ui/admin/src/api/users.ts`
- Modify: `ui/admin/src/api/users.test.ts`
- Modify: `ui/admin/src/views/UsersView.vue`
- Modify: `ui/admin/src/views/UsersView.test.ts`
- Modify: relevant `ui/admin/src/i18n/locales/*.ts`
- Modify: OpenAPI/client manifest fixtures selected by repository governance.

**Interfaces:**
- Produces: `GET /api/v1/identity/users/import-template`, `GET /api/v1/identity/users/export-file`, `POST /api/v1/identity/users/import-file`.
- Reuses: `identity.users.export`, `identity.users.import`, `HostUserQueryService.ExportAsync`, `HostUserManagementService.ImportAsync`.

- [x] **Step 1: Write failing Endpoint/API/Vue tests.**

Assert MIME/file names, multipart upload, permissions, template/export downloads, file picker flow, row result display and cancellation-safe cleanup.

- [x] **Step 2: Run RED.**

Run focused .NET and Vitest filters; expected failures are missing endpoints/functions/UI controls.

- [x] **Step 3: Implement minimal endpoints and Vue controls.**

Keep existing JSON endpoints for compatibility. The file import endpoint parses the workbook then delegates to the existing batch service.

- [x] **Step 4: Run GREEN.**

Run focused tests and OpenAPI/client contract checks; expected all selected tests pass.

### Task 3: Observability Admin bounded log service

**Files:**
- Create: `src/Modules/Full.NET.Modules.ObservabilityAdmin/Full.NET.Modules.ObservabilityAdmin.csproj`
- Create: `src/Modules/Full.NET.Modules.ObservabilityAdmin/ObservabilityAdminModule.cs`
- Create: `src/Modules/Full.NET.Modules.ObservabilityAdmin/ObservabilityAdminOptions.cs`
- Create: `src/Modules/Full.NET.Modules.ObservabilityAdmin/ObservabilityAdminAuthorizationContributor.cs`
- Create: `src/Modules/Full.NET.Modules.ObservabilityAdmin/Contracts/ObservabilityAdminContracts.cs`
- Create: `src/Modules/Full.NET.Modules.ObservabilityAdmin/Features/ManageLogFiles/LogFileControlPlane.cs`
- Create: `src/Modules/Full.NET.Modules.ObservabilityAdmin/Features/ManageLogFiles/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.ObservabilityAdmin/Serialization/ObservabilityAdminJsonSerializerContext.cs`
- Create: `tests/Full.NET.UnitTests/ObservabilityAdmin/LogFileControlPlaneTests.cs`

**Interfaces:**
- Produces: list, bounded tail and stable-ID open operations; API paths under `/api/v1/observability/log-files`.
- Permissions: `observability.log_files.read`, `observability.log_files.download`.

- [x] **Step 1: Write failing security and bounded-read tests.**

Cover stable IDs, sort/100 cap, missing root, nested/symlink exclusion, ID not found, active file sharing, line/byte caps, UTF-8 boundary, cancellation and download filename.

- [x] **Step 2: Run RED.**

Run the focused unit filter; expected compilation failure because the module does not exist.

- [x] **Step 3: Implement the module core and endpoints.**

Resolve relative root against `IHostEnvironment.ContentRootPath`, enumerate top-level files only, reject reparse points, re-resolve and verify containment before every open, and stream files without buffering the full file.

- [x] **Step 4: Run GREEN.**

Run focused unit and endpoint tests; expected all selected tests pass.

### Task 4: Composition, Vue page and static closure

**Files:**
- Modify: `src/Composition/Full.NET.Composition/Full.NET.Composition.csproj`
- Modify: `src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs`
- Modify: `src/Composition/Full.NET.Composition/FullNetModuleSelection.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/appsettings.json`
- Modify: `ui/admin/src/router/index.ts`
- Create: `ui/admin/src/api/observability-log-files.ts`
- Create: `ui/admin/src/api/observability-log-files.test.ts`
- Create: `ui/admin/src/views/ObservabilityLogFilesView.vue`
- Create: `ui/admin/src/views/ObservabilityLogFilesView.test.ts`
- Modify: relevant localization, OpenAPI fixture, client coverage and module graph files.

**Interfaces:**
- Registers: official module key `ObservabilityAdmin`, navigation component `observability-log-files` and generated/static JSON metadata.

- [x] **Step 1: Write failing composition, contract and Vue tests.**

Assert official catalog inclusion, dependency closure, navigation/action permissions, route lazy loading, list/tail/download UI and no unauthorized action creation.

- [x] **Step 2: Run RED.**

Expected failures are missing module registration, route and API client coverage.

- [x] **Step 3: Implement composition and Vue page.**

Use a text dialog for bounded tail, explicit download permission gate, and no arbitrary path input.

- [x] **Step 4: Run GREEN.**

Run focused Architecture/Unit/Vitest/OpenAPI tests and Release builds.

### Task 5: Closeout evidence and documentation

**Files:**
- Create: `docs/verification/2026-08-30-identity-excel-observability-log-control-plane.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/superpowers/plans/2026-08-30-identity-excel-observability-log-control-plane.md`

**Interfaces:**
- Consumes: fresh test/build outputs.
- Produces: exact `Build-verified` or partial status with unverified boundaries.

- [x] **Step 1: Run snapshot affected plan and inner/slice selectors.**

```powershell
pnpm test:integration:affected:plan -- --snapshot identity-excel-observability-logs-20260830 --phase inner
pnpm test:inner -- --snapshot identity-excel-observability-logs-20260830
pnpm test:slice -- --snapshot identity-excel-observability-logs-20260830
```

- [x] **Step 2: Run Native AOT and governance gates selected by the changed closure.**

Run `pnpm test:aot:analyzers`, relevant JSON/static-contract tests, `pnpm test:governance`, Release builds, Vue tests/build and `git diff --check`.

- [x] **Step 3: Record evidence without promotion beyond output.**

Document exact commands, counts, skipped environment-dependent checks, and remaining real-stack/production limits.

## Self-review

- Spec coverage: both requested slices include backend, permissions, Vue, AOT metadata, security limits and verification.
- Placeholder scan: every task names concrete files, interfaces and commands; no unspecified runtime behavior remains.
- Type consistency: workbook endpoints reuse existing `CreateHostUserRequest`/`ImportHostUsersResponse`; log endpoints use concrete response DTOs and non-JSON stream responses.
