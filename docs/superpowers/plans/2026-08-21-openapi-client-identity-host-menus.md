# OpenAPI 客户端迁移：Identity Host Menus（Identity remaining 第 2 slice）

> **For agentic workers:** 按本计划逐步执行；每 Task 独立 snapshot；行为变更必须 RED→GREEN。勾选或提交存在不能替代新鲜 Verification。

**Goal:** 将 `ui/admin/src/api/menus.ts` 从手写 HTTP/守卫收缩为消费仓库内 OpenAPI 生成 Operation 的薄适配层（`identity-host-menus`）。

**Architecture:** 延续 [`ADR-0007`](../../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md) 与已通过的 Host Roles slice 模式：稳定 `operationId` + 主 Tag → 标准快照 → 仓库内生成器 → Vue 薄适配。

**Tech Stack:** .NET 10 OpenAPI、仓库内 Node 生成器、`@fullnet/client-contracts`、Vue admin Vitest、双库 Integration。

**Approved basis:** ADR-0007；试点 [`Pilot-passed`](../../verification/openapi-client-generation-pilot-2026-08-21.md)；Host Roles [`Slice-passed`](../../verification/openapi-client-identity-host-roles-2026-08-21.md)；母计划 post-pilot 边界。

**Baseline:** 计划编写基线为 `500d7da83a5e3152113f0bef69d82d2f0bdba348`。执行者必须在每个 Task 开始时重新记录 `git rev-parse HEAD`。

## 执行状态（2026-08-21）

- 计划已创建；Task 1 已完成（`WithName` / `IdentityHostMenus` / 夹具补齐 `sync-catalog`；inner Identity 15/15；Architecture + OpenAPI 夹具绿）。
- Task 2–4 尚未开始。

## Global Constraints

- 每个 slice 只迁移一个 `generatedGroup`；本计划唯一目标组为 `identity-host-menus`。
- `ui/admin-layui/**` 零修改。
- 禁止改路径、HTTP 方法、成功状态码语义、序列化形状；只允许补齐 `WithName`、主 Tag、OpenAPI 元数据，以及夹具补齐已存在但未登记的 `sync-catalog`。
- JSON 必须 `unknown → generated guard → DTO`；禁止 `request<T>` 断言；禁止页面直连生成 Class。
- 页面导出函数名与签名保持稳定。
- 禁止并行迁移 Roles 以外的其他未批准资源组；本 slice 不得改 `roles.ts`。
- 工作区已脏时：`pnpm test:task:start -- <Snapshot>`。
- 新依赖禁止；禁止 skip / 降断言 / `audit ignore`。

## 范围

### 纳入

| 项 | 值 |
| --- | --- |
| Vue 适配 | `ui/admin/src/api/menus.ts`（及 `menus.test.ts`） |
| 轻量夹具 | `contracts/openapi/identity-host-menus-v1.json`（须补齐 `POST .../sync-catalog`） |
| C# Endpoint | `ManageHostMenus/Endpoint.cs` |
| generatedGroup | `identity-host-menus` |
| 语义 | 纯 JSON（分页、列表、201 Created、sync 结果）；无 multipart/Blob/`204` |

### 排除

- 其他 Identity remaining：`api-keys`、`online-sessions`、`superAdministrators`、`totpEnrollment`、`module-catalog`、`me`、auth/session
- 已完成的 `users.ts` / `roles.ts`

## 目标 Operation 清单

主 Tag：`IdentityHostMenus`。

| Method | Path | operationId | Vue 导出 |
| --- | --- | --- | --- |
| GET | `/api/v1/identity/menus` | `identityListHostMenus` | `listHostMenus` |
| GET | `/api/v1/identity/menus/all` | `identityListAllHostMenus` | `listHostMenusAll` |
| GET | `/api/v1/identity/menus/permission-options` | `identityListHostMenuPermissionOptions` | `listHostMenuPermissionOptions` |
| POST | `/api/v1/identity/menus/sync-catalog` | `identitySyncHostMenuCatalog` | `syncHostMenuCatalog` |
| GET | `/api/v1/identity/menus/{menuId}` | `identityGetHostMenu` | （无 Vue 导出） |
| POST | `/api/v1/identity/menus` | `identityCreateHostMenu` | `createHostMenu` |
| PUT | `/api/v1/identity/menus/{menuId}` | `identityUpdateHostMenu` | `updateHostMenu` |
| POST | `/api/v1/identity/menus/{menuId}/disable` | `identityDisableHostMenu` | `disableHostMenu` |
| POST | `/api/v1/identity/menus/{menuId}/enable` | `identityEnableHostMenu` | `enableHostMenu` |

清单初始 `pilot`；Verification `Slice-passed` 后改 `generated`。

---

### Task 1: 固定 Host Menus 的 operationId、主 Tag 与夹具对齐

**Snapshot:** `openapi-client-identity-host-menus-metadata-20260821`

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostMenus/Endpoint.cs`
- Modify: `contracts/openapi/identity-host-menus-v1.json`（补 `sync-catalog`）
- Modify: `tests/Full.NET.ArchitectureTests/OpenApiOperationIdentityRulesTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/OpenApiHostMenusContractAssertions.cs`
- Modify: `tests/openapi/identity-host-menus-contract.test.mjs`

- [x] **Step 1:** `.WithName` + Tag `IdentityHostMenus` + ProblemDetails 401/403
- [x] **Step 2:** 夹具补齐 `sync-catalog` 与 `HostNavigationCatalogSyncResponse`
- [x] **Step 3:** Architecture / OpenAPI 夹具 / Integration 断言 RED→GREEN
- [x] **Step 4:** `pnpm test:inner -- --snapshot openapi-client-identity-host-menus-metadata-20260821`
- [x] **Step 5:** 提交 `feat: stabilize Identity Host Menus OpenAPI identities`

---

### Task 2: 导出标准快照并登记生成清单（pilot）

**Snapshot:** `openapi-client-identity-host-menus-snapshot-20260821`

- Modify: `contracts/openapi/fullnet-client-v1.openapi.json`
- Modify: `contracts/openapi/client-generation-manifest-v1.json`（追加 9 条 `pilot`）
- Modify: 规范化契约测试计数/分组
- 同步 `pnpm openapi:client:generate` 避免 `--check` 漂移

- [ ] 提交 `feat: freeze Identity Host Menus client OpenAPI snapshot`

---

### Task 3: 生成客户端并收缩 `menus.ts`

**Snapshot:** `openapi-client-identity-host-menus-generate-20260821`

- Modify: `ui/admin/src/api/menus.ts`、`menus.test.ts`
- 模式对齐 `users.ts` / `roles.ts`（mock `http`、合法 UUID）

- [ ] 提交 `feat: generate Identity Host Menus client operations`

---

### Task 4: 完整门禁与 Verification；通过后升 `generated`

**Snapshot:** `openapi-client-identity-host-menus-verify-20260821`

门禁同 Host Roles Task 4（含双库 slice）。若 verify snapshot 相对工作区无 diff，允许 `--base <Task1 前提交>`。

- Create: `docs/verification/openapi-client-identity-host-menus-2026-08-21.md`
- 通过后 9 条 `pilot` → `generated`

- [ ] 提交 `docs: verify Identity Host Menus OpenAPI client slice`

## 停止条件

继承 ADR-0007；任一门禁失败保持 `pilot`，禁止开始 api-keys 或其他资源组。

## 规则与 Skill 复盘预期

完成后若无新冲突类别，Verification 写一行“未新增规则/Skill 候选”。
