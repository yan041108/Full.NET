# OpenAPI 驱动客户端生成实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立以运行时标准 OpenAPI 为最终权威、生成低层 TypeScript 模型/守卫/Operation、复用 Full.NET `createHttpClient` 且由 Vue 薄业务适配层消费的确定性客户端生成链路。

**Architecture:** C# Endpoint 显式声明稳定 `operationId`、主 Tag 与完整响应元数据；Integration Host 导出并规范化标准 OpenAPI 快照；生成器只产出模型、运行时守卫、参数编码和低层 Operation。`packages/client-contracts` 保留唯一 HTTP Runtime，`ui/admin/src/api` 保持页面稳定门面；先以 JSON CRUD、Blob/multipart、`204` 三类试点验证，未通过停止门禁时不迁移其余模块。

**Tech Stack:** .NET 10 / ASP.NET Core OpenAPI、System.Text.Json、Node.js 24、pnpm 10.26.0、TypeScript 7、Vitest 4、OpenAPI 3.1 JSON、候选 OpenAPI Generator 7.24.0 + `@openapitools/openapi-generator-cli@2.40.1`、Microsoft Testing Platform。

**Approved basis:** [`ADR-0007`](../../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md) 与[总体架构 Spec](../specs/2026-07-17-fullnet-architecture-design.md#14-api-与错误模型)。

**Baseline:** 计划编写基线为 `268f992448620261084166f35a5fb305e8fa9e8f`。执行者必须在每个 Task 开始时重新记录 `git rev-parse HEAD`；不得把本文基线当成任务快照或合并基线。

## 执行状态（2026-08-21）

- Task 1—6 已在 `codex/openapi-client-pilots` 完成并形成独立提交；第一阶段严格限制为 Identity Host Users、Files Host Files、Settings Host Config Entries 三类试点。
- Task 7 已形成 [`openapi-client-generation-pilot-2026-08-21.md`](../../verification/openapi-client-generation-pilot-2026-08-21.md)，判定为 `Pilot-stopped`。
- 三类试点保持 `pilot`，不得改为 `generated`；非试点模块保持手写实现，不创建或执行批量迁移计划。
- 重新解除停止门禁前，必须修复并验证 Verification 中记录的 Vue Unit/Build 与依赖审计阻断项，然后完整重跑 ADR-0007 第 5 节门禁。

## Global Constraints

- Vue 主管理端 `ui/admin` 是唯一后台产品交付线；`ui/admin-layui` 必须零修改。
- OpenAPI 是客户端线协议的最终生成输入；数据库 Schema 和 Vue 模板不得独立猜测 HTTP 契约。
- `createHttpClient` 继续唯一负责 Access Token、并发 Refresh、Cookie、语言、ProblemDetails、401 单次重试、Blob、`204` 和取消。
- 所有生成 JSON 响应必须走 `unknown → runtime guard → DTO`；禁止生成 `request<T>` 直接断言。
- `operationId` 使用 `{module}{Verb}{Resource}[Qualifier]` lowerCamelCase；主 Tag 使用 `{Module}{Resource}` PascalCase。
- 生成器、配置和模板必须固定版本并进入版本控制；禁止全局安装、`latest`、CI 在线读取 Swagger URL 和人工编辑 `.generated.ts`。
- 现有 `contracts/openapi/*-v1.json` 继续承担轻量兼容门禁，不转换成伪标准 OpenAPI，也不在本计划中批量删除。
- 生成物删除、替换和陈旧清理必须遵守 R-20260730 的所有权清单、claim、复验、墓碑和 recovery 规则。
- 行为变更必须 RED→GREEN；工作区已脏时先使用当前 Task 的 `Snapshot` 值执行 `pnpm test:task:start -- SNAPSHOT_ID`，并在该 Task 的所有 inner/slice 命令使用同一值。
- 不在文档复制测试数量；新增测试最低发现数只修改 `eng/testing/test-matrix.json`。
- 新依赖必须通过许可证、漏洞、维护状态、传递依赖、包体与 CI 环境审查，必要时同步 `THIRD-PARTY-NOTICES`。

---

## 文件职责与目标结构

```text
contracts/openapi/
  fullnet-client-v1.openapi.json              # 规范化标准 OpenAPI 生成输入
  client-generation-manifest-v1.json          # 迁移所有权、Operation 与适配层精确映射

scripts/openapi/
  normalize-client-openapi.mjs                # 规范排序与非语义字段移除
  validate-client-generation-readiness.mjs    # operationId/Tag/Schema/响应门禁
  generate-fullnet-client.mjs                 # 唯一生成入口与零漂移模式

eng/openapi-generator/
  openapi-generator-config.json               # 候选工具固定配置
  templates/                                  # 仅 Full.NET Operation/guard/exports 模板

packages/client-contracts/src/generated/
  models.generated.ts                         # 线协议类型
  guards.generated.ts                         # unknown 运行时守卫
  operations.generated.ts                     # 注入 HttpClient 的低层调用
  index.generated.ts                          # 确定性导出

ui/admin/src/api/*.ts                         # 稳定薄业务适配层；不生成
```

`fullnet-client-v1.openapi.json` 只允许由真实 Integration Host 导出后经规范化脚本更新。`client-generation-manifest-v1.json` 是生成所有权和渐进迁移清单，不复制 Schema；每个条目只记录 `operationId`、生成文件、适配模块和状态 `pilot|generated`。

---

### Task 1: 建立 OpenAPI 生成就绪失败关闭门禁

**Snapshot:** `openapi-client-readiness-20260821`

**Files:**
- Create: `scripts/openapi/validate-client-generation-readiness.mjs`
- Create: `tests/openapi/client-generation-readiness.test.mjs`
- Create: `tests/openapi/fixtures/client-generation/valid.openapi.json`
- Create: `tests/openapi/fixtures/client-generation/duplicate-operation-id.openapi.json`
- Create: `tests/openapi/fixtures/client-generation/missing-runtime-schema.openapi.json`
- Modify: `package.json`
- Modify only if discovery count changes: `eng/testing/test-matrix.json`

**Interfaces:**
- Produces: `validateClientGenerationReadiness(document, options?) => string[]`
- Produces: CLI `node scripts/openapi/validate-client-generation-readiness.mjs contracts/openapi/fullnet-client-v1.openapi.json` with exit `0` only when violations are empty.
- Consumes later: Task 3 snapshot export and Task 4/5 generation entry.

- [ ] **Step 1: 写失败测试，锁定稳定 Operation 身份**

测试必须断言以下输入失败：缺少或重复 `operationId`、非 lowerCamelCase ID、零个或多个主 Tag、没有 `2xx` 响应、JSON 成功响应没有 Schema、Schema 缺少明确 `type`/`$ref`、数组缺少 `items`、受保护 API 没有安全定义、`204` 声明 JSON content、文件下载误标 JSON。

```js
test('重复 operationId 与多主 Tag 失败关闭', async () => {
  const document = await readFixture('duplicate-operation-id.openapi.json');
  assert.deepEqual(validateClientGenerationReadiness(document), [
    'POST /api/v1/identity/users: duplicate operationId identityListHostUsers',
    'POST /api/v1/identity/users: expected exactly one primary tag'
  ]);
});
```

- [ ] **Step 2: 运行 RED**

Run: `node --test tests/openapi/client-generation-readiness.test.mjs`

Expected: FAIL，原因是 `validate-client-generation-readiness.mjs` 不存在或未导出目标函数。

- [ ] **Step 3: 实现最小验证器**

验证器必须稳定排序 violations，拒绝通配豁免，并使用以下正则：

```js
const operationIdPattern = /^[a-z][A-Za-z0-9]*$/u;
const primaryTagPattern = /^[A-Z][A-Za-z0-9]*$/u;

export function validateClientGenerationReadiness(document) {
  const violations = [];
  const operationIds = new Map();
  // 枚举 paths + get/post/put/patch/delete，执行身份、Tag、响应和 Schema 检查。
  return violations.sort((left, right) => left.localeCompare(right, 'en'));
}
```

禁止在验证器中维护业务路径 allowlist。Pilot 范围由 Task 3 的精确 manifest 决定，单个进入 manifest 的 Operation 必须满足全部规则。

- [ ] **Step 4: 接入稳定命令**

在 `package.json` 增加：

```json
"test:openapi:client-generation": "node --test tests/openapi/client-generation-readiness.test.mjs"
```

现有 `test:openapi` glob 必须继续自动发现该测试；不得创建绕过聚合的第二套默认测试入口。

- [ ] **Step 5: 运行 GREEN 与治理检查**

Run:

```powershell
node --test tests/openapi/client-generation-readiness.test.mjs
pnpm test:openapi
pnpm test:governance
```

Expected: 全部 PASS；无效 fixtures 被测试按预期拒绝。

- [ ] **Step 6: 提交聚焦变更**

```powershell
git add scripts/openapi/validate-client-generation-readiness.mjs tests/openapi package.json eng/testing/test-matrix.json
git commit -m "test: define OpenAPI client generation readiness"
```

---

### Task 2: 为三个试点补齐稳定 OpenAPI 元数据

**Snapshot:** `openapi-client-pilot-metadata-20260821`

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostUsers/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Features/ManageHostFiles/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Settings/Features/ManageHostConfigEntries/Endpoint.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/OpenApiHostUsersContractAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/OpenApiFilesHostFilesContractAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/OpenApiSettingsConfigEntriesContractAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/IdentityApiSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/IdentityApiMySqlTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/FilesApiSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/FilesApiMySqlTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/SettingsApiSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/SettingsApiMySqlTests.cs`
- Create: `tests/Full.NET.ArchitectureTests/OpenApiOperationIdentityRulesTests.cs`

**Interfaces:**
- Produces Operation IDs:
  - `identityListHostUsers`, `identityExportHostUsers`, `identityImportHostUsers`, `identityBatchDisableHostUsers`, `identityBatchEnableHostUsers`, `identityGetHostUser`, `identityCreateHostUser`, `identityUpdateHostUser`, `identityDisableHostUser`, `identityEnableHostUser`, `identityResetHostUserPassword`, `identityGetHostUserRoles`, `identityReplaceHostUserRoles`.
  - `filesListHostFiles`, `filesGetHostFile`, `filesUploadHostFile`, `filesDownloadHostFileContent`, `filesDeleteHostFile`.
  - `settingsListHostConfigEntries`, `settingsGetHostConfigEntryByKey`, `settingsGetHostConfigEntry`, `settingsCreateHostConfigEntry`, `settingsUpdateHostConfigEntry`, `settingsDisableHostConfigEntry`, `settingsDeleteHostConfigEntry`, `settingsBatchDeleteHostConfigEntries`, `settingsBatchUpdateHostConfigEntryValues`, `settingsListAllHostConfigEntries`, `settingsListHostConfigEntryGroups`.
- Produces primary tags: `IdentityHostUsers`, `FilesHostFiles`, `SettingsHostConfigEntries`.

- [ ] **Step 1: 写运行时 OpenAPI RED 断言**

Integration 测试从 `/openapi/v1.json` 按 Path/Method 读取 Operation，并精确断言 ID、唯一 Tag、请求/成功响应和 ProblemDetails。Blob 下载必须断言二进制 content；Config Entry 删除/批量删除必须断言 `204` 无 content。

```csharp
OpenApiPilotContractAssertions.AssertOperation(
    document,
    "/api/v1/files/host-files/{fileId}/content",
    HttpMethod.Get,
    "filesDownloadHostFileContent",
    "FilesHostFiles",
    StatusCodes.Status200OK,
    expectedMediaType: "application/octet-stream");
```

- [ ] **Step 2: 运行 RED**

Run: `pnpm test:integration:affected -- --snapshot openapi-client-pilot-metadata-20260821 --phase inner`

Expected: FAIL，运行时 Operation 缺少规定 `operationId`/Tag 或 Blob/`204` 元数据不完整。

- [ ] **Step 3: 添加显式 `.WithName(...)` 与试点主 Tag**

每个 `Map*` 链显式追加：

```csharp
.WithName("identityListHostUsers")
.WithTags("IdentityHostUsers")
```

将组级宽 Tag 替换为试点资源 Tag，避免同一 Operation 同时继承多个主 Tag。不得用反射、路由字符串或方法名自动生成 `operationId`。

- [ ] **Step 4: 补齐 Blob、multipart、204 与 ProblemDetails 元数据**

使用 ASP.NET Core `Produces`/`Accepts`/ProblemDetails 元数据表达真实行为。不得改变现有 HTTP 行为来迎合文档；如果运行时返回与声明不一致，先以 Integration 测试证明并修正 Endpoint 元数据或真实缺陷。

- [ ] **Step 5: 添加 Architecture 唯一性门禁**

门禁枚举生产 Endpoint 元数据并断言进入生成 manifest 的 Operation 名称全局唯一、符合 lowerCamelCase、恰有一个主 Tag。非试点 Endpoint 暂不要求加入 manifest，但任何新增 `.WithName` 都不得冲突。

- [ ] **Step 6: 运行 GREEN**

```powershell
pnpm test:dotnet:architecture
pnpm test:integration:affected -- --snapshot openapi-client-pilot-metadata-20260821 --phase slice
pnpm test:openapi
```

Expected: 三个试点的 SQL Server/MySQL API 契约和 Architecture 门禁 PASS；业务响应不变。

- [ ] **Step 7: 提交**

```powershell
git add src/Modules tests/Full.NET.ArchitectureTests tests/Full.NET.IntegrationTests eng/testing/test-matrix.json
git commit -m "feat: stabilize pilot OpenAPI operation identities"
```

---

### Task 3: 导出规范化标准 OpenAPI 快照与迁移清单

**Snapshot:** `openapi-client-canonical-snapshot-20260821`

**Files:**
- Create: `contracts/openapi/fullnet-client-v1.openapi.json`
- Create: `contracts/openapi/client-generation-manifest-v1.json`
- Create: `scripts/openapi/normalize-client-openapi.mjs`
- Create: `tests/openapi/client-openapi-normalization.test.mjs`
- Create: `tests/Full.NET.IntegrationTests/Api/OpenApiClientSnapshotContractAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/OpenApiDocumentationApiSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/OpenApiDocumentationApiMySqlTests.cs`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Produces: `normalizeClientOpenApi(document, operationIds) => OpenApiDocument`.
- Manifest schema:

```json
{
  "schemaVersion": 1,
  "entries": [
    {
      "operationId": "identityListHostUsers",
      "apiModule": "ui/admin/src/api/users.ts",
      "generatedGroup": "identity-host-users",
      "status": "pilot"
    }
  ]
}
```

- [ ] **Step 1: 写规范化 RED 测试**

测试必须证明：输入 Path/Schema 顺序不同仍产生逐字节相同 JSON；移除 `servers`、时间戳和开发机 URL；保留 OpenAPI version、security schemes、Path/Method、Operation ID、Tag、参数、requestBody、responses、components/schemas；只保留 manifest 精确列出的 Operation 及其传递 `$ref`；循环 `$ref` 不死循环。

- [ ] **Step 2: 运行 RED**

Run: `node --test tests/openapi/client-openapi-normalization.test.mjs`

Expected: FAIL，因为规范化实现或快照不存在。

- [ ] **Step 3: 实现确定性规范化**

输出使用 UTF-8、两个空格、LF、文件末尾换行；对象 Key 按协议固定顺序和英文序排序，数组只在语义无序时排序。禁止重排 `allOf`/`oneOf`/`anyOf`、security requirement 或参数顺序来改变语义。

- [ ] **Step 4: 从真实 Integration Host 导出**

导出流程必须启动测试 Host、读取 `/openapi/v1.json`、调用规范化脚本并写入临时文件，再由显式 `--update` 参数更新仓库快照。普通测试默认只比较，不写工作区。

```powershell
pnpm openapi:client:snapshot -- --update
pnpm openapi:client:snapshot -- --check
```

`--check` 发现差异必须输出首个 Operation/Schema 差异并退出非零。

- [ ] **Step 5: 将三个试点写入 manifest**

只加入 Task 2 的精确 Operation；不得用路径前缀或 Tag 通配。每个 Operation 恰有一个 API 适配模块和生成组。

- [ ] **Step 6: 运行生成就绪与双库快照检查**

SQL Server/MySQL Test Host 对规范化后的客户端子集必须逐字节一致；Provider 差异不得进入线协议。

```powershell
pnpm openapi:client:snapshot -- --check --provider SqlServer
pnpm openapi:client:snapshot -- --check --provider MySql
node scripts/openapi/validate-client-generation-readiness.mjs contracts/openapi/fullnet-client-v1.openapi.json
```

- [ ] **Step 7: 接入 CI 后提交**

CI 在 `test:openapi` 后执行 snapshot `--check`，不更新文件、不访问外部环境。

```powershell
git add contracts/openapi scripts/openapi tests/openapi tests/Full.NET.IntegrationTests .github/workflows/ci.yml package.json eng/testing/test-matrix.json
git commit -m "feat: freeze canonical client OpenAPI snapshot"
```

---

### Task 4: 评估并固定生成器实现

**Snapshot:** `openapi-client-generator-evaluation-20260821`

**Files:**
- Modify: `package.json`, `pnpm-lock.yaml`
- Create when candidate path is active: `openapitools.json`
- Create: `eng/openapi-generator/openapi-generator-config.json`
- Create: `eng/openapi-generator/templates/`
- Create: `tests/openapi/client-generator-evaluation.test.mjs`
- Create: `scripts/openapi/generate-fullnet-client.mjs`
- Modify: `THIRD-PARTY-NOTICES` only when required by license audit

**Interfaces:**
- Produces CLI:

```text
pnpm openapi:client:generate
pnpm openapi:client:generate -- --check
```

- Generated Operation signature:

```ts
export type GeneratedJsonOperation<T> = (
  http: HttpClient,
  parameters: Readonly<Record<string, unknown>>,
  signal?: AbortSignal
) => Promise<T>;
```

- [ ] **Step 1: 写工具无关验收 RED**

测试对生成目录执行以下断言：无 `Configuration`/`BaseAPI`/直接 `fetch`/`axios`/`localStorage`；JSON 调用必须包含 `request<unknown>` 与 guard；Blob 调用只用 `requestBlob`；Void 调用不解析 JSON；导出只来自 `index.generated.ts`；同输入两次输出摘要相同。

- [ ] **Step 2: 运行 RED**

Run: `node --test tests/openapi/client-generator-evaluation.test.mjs`

Expected: FAIL，因为生成入口和产物尚不存在。

- [ ] **Step 3: 固定候选版本并审计**

添加精确开发依赖 `@openapitools/openapi-generator-cli@2.40.1`，`openapitools.json` 固定 Generator `7.24.0`，禁止 semver 范围和自动 latest。执行：

```powershell
pnpm install --save-dev --save-exact @openapitools/openapi-generator-cli@2.40.1
pnpm audit:clients
pnpm licenses list --prod --json
```

记录 Java/JAR 下载、离线缓存、许可证、Critical/High 漏洞和 CI 可用性。默认模板输出只作为临时目录评估证据，不提交默认 SDK。

- [ ] **Step 4: 实现最小 Full.NET 模板**

模板只能生成 models、guards、operations 和 exports。Operation 必须注入 `HttpClient`：

```ts
export async function identityGetHostUser(
  http: HttpClient,
  parameters: { readonly userId: string },
  signal?: AbortSignal
): Promise<HostUser> {
  const value = await http.request<unknown>(
    `/api/v1/identity/users/${encodeURIComponent(parameters.userId)}`,
    undefined,
    signal
  );
  return readHostUser(value);
}
```

- [ ] **Step 5: 执行候选停止门禁**

候选工具只有在 ADR-0007 §4.5 六项全部通过时保留。若通过，`generate-fullnet-client.mjs` 调用固定 CLI 和模板；若任一项失败，在同一 Task 中删除候选 npm 依赖与 `openapitools.json`，保留验收测试，并让 `generate-fullnet-client.mjs` 使用 Node.js 读取标准 JSON，按 `operationId`、Schema 与 media type 生成相同四类文件。两条路径的公开输出和测试完全相同，不允许双生成器长期并存。

- [ ] **Step 6: 运行 GREEN、依赖与零漂移门禁**

```powershell
pnpm openapi:client:generate
pnpm openapi:client:generate -- --check
node --test tests/openapi/client-generator-evaluation.test.mjs
pnpm audit:clients
pnpm test:governance
```

Expected: 全部 PASS；第二次生成工作区零差异；最终只保留一个生成实现。

- [ ] **Step 7: 提交工具选择**

```powershell
git add package.json pnpm-lock.yaml openapitools.json eng/openapi-generator scripts/openapi tests/openapi THIRD-PARTY-NOTICES
git commit -m "feat: add deterministic OpenAPI client generator"
```

---

### Task 5: 生成三个试点并保持 Vue 稳定适配层

**Snapshot:** `openapi-client-three-pilots-20260821`

**Files:**
- Create: `packages/client-contracts/src/generated/*.generated.ts`
- Modify: `packages/client-contracts/src/index.ts`
- Modify: `packages/client-contracts/src/host-users.ts`
- Modify: `packages/client-contracts/src/host-files.ts`
- Modify: `packages/client-contracts/src/settings-config-entries.ts`
- Modify: `ui/admin/src/api/users.ts`
- Modify: `ui/admin/src/api/host-files.ts`
- Modify: `ui/admin/src/api/config-entries.ts`
- Modify: `packages/client-contracts/tests/host-users.test.ts`
- Modify: `packages/client-contracts/tests/host-files.test.ts`
- Modify: `packages/client-contracts/tests/settings-config-entries.test.ts`
- Modify: `ui/admin/src/api/users.test.ts`
- Modify: `ui/admin/src/api/host-files.test.ts`
- Modify: `ui/admin/src/api/config-entries.test.ts`

**Interfaces:**
- Existing page-facing function names and signatures remain unchanged.
- Generated JSON Operation accepts `HttpClient`; generated Blob/Void Operation uses the same `HttpClient` object.
- Generated guards throw stable `client.invalid_{schema_key}` errors or return type predicates according to one repository-wide convention；不得在同一层混用两种失败语义。

- [ ] **Step 1: 为畸形成功响应写 RED**

每个试点至少覆盖：缺失 required、错误 primitive、`null`/optional 边界、未知 enum、数组 item 错误；Files 覆盖 multipart 字段和 Blob；Config Entry 覆盖 `204` 不调用 JSON parser；HTTP 测试继续覆盖 Refresh、ProblemDetails 和取消。

- [ ] **Step 2: 运行 RED**

```powershell
pnpm --filter @fullnet/client-contracts test
pnpm --filter @fullnet/admin test -- src/api/users.test.ts src/api/host-files.test.ts src/api/config-entries.test.ts
```

Expected: 新测试因生成入口不存在或旧适配层仍手写协议而 FAIL。

- [ ] **Step 3: 生成并导出低层产物**

运行 `pnpm openapi:client:generate`。`packages/client-contracts/src/index.ts` 只从 `./generated/index.generated.js` 导出批准的模型/Operation；禁止导出第三方 Runtime 类型。

- [ ] **Step 4: 将手写 API 文件收缩为薄适配层**

页面公共函数名不变，只负责参数默认值、业务命名和调用生成 Operation：

```ts
export async function listHostUsers(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostUserPage> {
  return identityListHostUsers(http, { page, pageSize }, signal);
}
```

禁止在页面适配层重新拼接路径、声明后端 DTO 或重复 JSON 守卫。

- [ ] **Step 5: 验证共享 HTTP 语义不变**

现有 `packages/client-contracts/src/http.ts` 除非 RED 证明真实缺陷，否则保持零行为修改。测试必须证明生成 Operation 沿用同一 token、refresh、credentials、locale、ProblemDetails、Blob 与 Void 行为。

- [ ] **Step 6: 运行 slice**

```powershell
pnpm openapi:client:generate -- --check
pnpm --filter @fullnet/client-contracts test
pnpm --filter @fullnet/client-contracts build
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin build
pnpm test:openapi
pnpm test:integration:affected -- --snapshot openapi-client-three-pilots-20260821 --phase slice
```

Expected: 三类试点全部 PASS；Vue 页面调用无需批量重命名；生成两次零漂移。

- [ ] **Step 7: 提交三个试点**

```powershell
git add packages/client-contracts ui/admin/src/api contracts/openapi tests eng/testing/test-matrix.json
git commit -m "feat: pilot OpenAPI generated client operations"
```

---

### Task 6: 将 CRUD 代码生成器收敛到同一 OpenAPI 客户端链路

**Snapshot:** `openapi-client-crud-convergence-20260821`

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudArtifactGenerator.cs`
- Create or modify focused generator for OpenAPI fragment under `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/GenerationWritePlannerTests.cs`
- Modify: `tests/Full.NET.UnitTests/CodeGeneration/GenerationWorkspaceStoreTests.cs`

**Interfaces:**
- `FullNetCrudSchema` continues generating server code, SQL, permissions and Vue page model.
- It additionally produces a standard OpenAPI fragment containing stable Operation IDs/Tags and complete request/response Schema.
- It no longer treats its direct TypeScript `request<T>` template as authoritative client output.

- [ ] **Step 1: 写 RED Golden File**

断言 Catalog Product 生成结果包含：

```text
catalogListProducts
catalogCreateProduct
catalogUpdateProduct
catalogDisableProduct
CatalogProducts
```

并断言任何生成 TypeScript 不含 `request<ProductResponse>`、直接路径拼接或独立 DTO 真相。

- [ ] **Step 2: 运行 RED**

Run: `pnpm test:dotnet:unit -- --filter FullyQualifiedName~CodeGeneration`

Expected: FAIL，旧 `products.generated.ts` 仍从数据库 Schema 直接生成强制断言式客户端。

- [ ] **Step 3: 生成标准 OpenAPI fragment**

Fragment 必须由与 Endpoint 相同的路径、DTO、权限和能力元数据产生，并通过 Task 1 readiness validator。生成 Vue 页面继续依赖稳定 API 门面，不直接依赖 fragment 或第三方模板。

- [ ] **Step 4: 用统一生成入口生成客户端 Golden File**

测试将 fragment 交给 `pnpm openapi:client:generate` 的库入口或固定 CLI，比较 models/guards/operations。C# 单元测试不得在线启动 Swagger 服务；fixture 是完整标准 OpenAPI JSON。

- [ ] **Step 5: 验证生成所有权与恢复**

运行现有 `GenerationWritePlannerTests`、`GenerationWorkspaceStoreTests`，覆盖旧客户端产物被人工修改、陈旧文件 claim、摘要复验、墓碑和 recovery。禁止简单 `File.Delete`。

- [ ] **Step 6: 运行 GREEN**

```powershell
pnpm test:dotnet:unit -- --filter FullyQualifiedName~CodeGeneration
pnpm test:naming
pnpm test:openapi
pnpm test:governance
```

- [ ] **Step 7: 提交**

```powershell
git add src/BuildingBlocks/Full.NET.Data.CodeGeneration tests/Full.NET.UnitTests/CodeGeneration contracts/openapi
git commit -m "refactor: converge CRUD clients on OpenAPI generation"
```

---

### Task 7: 形成试点 Verification 并决定是否解除批量迁移停止门禁

**Snapshot:** `openapi-client-pilot-verification-20260821`

**Files:**
- Create: `docs/verification/openapi-client-generation-pilot-2026-08-21.md`
- Modify only if gate passes: `contracts/openapi/client-generation-manifest-v1.json`
- Modify only if gate passes: `docs/roadmap/capability-status.md`
- Modify only if evidence changes current wording: `docs/roadmap/client-delivery-roadmap.md`

**Interfaces:**
- Produces decision: `Pilot-passed` or `Pilot-stopped`.
- `Pilot-passed` allows one module per subsequent slice; `Pilot-stopped` forbids Task 8 and preserves current non-pilot API modules.

- [ ] **Step 1: 收集新鲜证据**

必须记录实际提交、OS/Node/.NET/pnpm、最终生成器及版本、模板文件/行数、生成耗时、产物数量和字节、第二次生成 diff、依赖/许可/漏洞、JSON/Blob/multipart/`204`、Refresh/ProblemDetails/Locale/取消、Vue Unit/Build、双库 API 测试和未验证项。

- [ ] **Step 2: 执行 merge 阶段门禁**

```powershell
pnpm openapi:client:generate -- --check
pnpm test:openapi
pnpm --filter @fullnet/client-contracts test
pnpm --filter @fullnet/client-contracts build
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin build
pnpm audit:clients
pnpm test:naming
pnpm test:governance
pnpm test:integration:affected -- --snapshot openapi-client-pilot-verification-20260821 --phase merge
dotnet build Full.NET.slnx -c Release
git diff --check
```

- [ ] **Step 3: 应用停止判定**

ADR-0007 §5 任一项失败、跳过或缺证据时写 `Pilot-stopped`，不得更新非试点 manifest 状态或声称生成链路 Verified。全部通过才写 `Pilot-passed`，并将三个试点条目从 `pilot` 改为 `generated`。

- [ ] **Step 4: 提交 Verification**

```powershell
git add docs/verification contracts/openapi/client-generation-manifest-v1.json docs/roadmap
git commit -m "docs: record OpenAPI client generation pilot evidence"
```

---

## Pilot 之后的独立迁移计划边界

本计划只交付标准、生成链路、CRUD 收敛和三个代表试点。只有 Task 7 记录 `Pilot-passed` 后，才允许依据当时真实生成产物、模板规模和测试耗时新建一份批量迁移计划；迁移顺序固定为 Identity remaining → Tenancy/Organization → Settings/Auditing → Files remaining/Notifications/Jobs/CodeGeneration → Document，每个 slice 只迁移一个模块，不并行修改共享快照和生成清单。

后续计划必须为每个模块列出精确 Endpoint、OpenAPI Integration、`packages/client-contracts`、`ui/admin/src/api` 和测试路径，使用独立 snapshot，并重复执行 runtime guard、共享 HTTP、OpenAPI、Vue、双库 slice 与零漂移门禁。Task 7 为 `Pilot-stopped` 时禁止创建该迁移计划，现有非试点 45 个 Vue API 模块继续保持手写实现。

最终只有在全部生产 Vue API 模块迁移、`vue-client-coverage-v1.json` 与生成 manifest 一致、旧数据库 Schema TypeScript 客户端模板退出且完整 merge 门禁通过后，才可把生成客户端能力更新为 `Build-verified`；这不等于公开 npm SDK 或 `Production-verified`。

---

## 最终完成定义

- 标准 OpenAPI 快照可由 SQL Server/MySQL Integration Host 确定性导出且逐字节一致。
- 所有生成范围 Operation 具有稳定唯一 `operationId`、单主 Tag、完整 JSON/ProblemDetails/Blob/multipart/`204` 元数据。
- 生成入口只有一个，版本固定，同输入连续生成零差异。
- JSON Operation 全部执行 `unknown → generated guard → DTO`。
- `createHttpClient` 仍是认证、刷新、Cookie、语言、ProblemDetails、Blob、Void 和取消的唯一 Runtime。
- Vue 页面只通过稳定 `ui/admin/src/api` 业务函数调用，不依赖生成 Class/Configuration/Runtime。
- CRUD 生成器不再以数据库 Schema 独立生成 HTTP 客户端真相。
- OpenAPI、client-contracts、Vue、Architecture、governance、naming、依赖审计、Release build 和受影响双库 Integration 使用新鲜输出通过。
- Verification 如实记录工具选择、生成成本、迁移状态和未验证项；计划存在或产物生成不自动提升能力状态。
- `git diff --check`、`git status --short` 和当前分支状态已检查，无无关变更。
