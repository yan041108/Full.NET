# OpenAPI 客户端迁移：Identity Host Roles（Identity remaining 第 1 slice）

> **For agentic workers:** 按本计划逐步执行；每 Task 独立 snapshot；行为变更必须 RED→GREEN。勾选或提交存在不能替代新鲜 Verification。

**Goal:** 将 `ui/admin/src/api/roles.ts` 从手写 HTTP/守卫收缩为消费仓库内 OpenAPI 生成 Operation 的薄适配层，作为 `Pilot-passed` 后的第四个资源组（`identity-host-roles`）。

**Architecture:** 延续 [`ADR-0007`](../../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)：C# Endpoint 声明稳定 `operationId` 与单主 Tag → Integration Host 导出并规范化 `fullnet-client-v1.openapi.json` → `generate-fullnet-client.mjs` 生成 models/guards/operations → Vue `roles.ts` 只做业务命名与默认参数。不替换 `createHttpClient`，不暴露第三方 Runtime。

**Tech Stack:** .NET 10 OpenAPI、仓库内 Node 生成器、`@fullnet/client-contracts`、Vue admin Vitest、双库 Integration。

**Approved basis:** ADR-0007；试点 Verification [`openapi-client-generation-pilot-2026-08-21.md`](../../verification/openapi-client-generation-pilot-2026-08-21.md)（`Pilot-passed`）；母计划 [`2026-08-21-openapi-driven-client-generation.md`](./2026-08-21-openapi-driven-client-generation.md) 的 post-pilot 边界。

**Baseline:** 计划编写基线为 `80ec35693e0bf977f22632b58ef923767960fb0c`。执行者必须在每个 Task 开始时重新记录 `git rev-parse HEAD`。

## 执行状态（2026-08-21）

- 计划已创建。
- Task 1–4 已完成；Verification 判定 `Slice-passed`，见 [`openapi-client-identity-host-roles-2026-08-21.md`](../../verification/openapi-client-identity-host-roles-2026-08-21.md)。
- `identity-host-roles` 共 12 个 Operation 已由 `pilot` 提升为 `generated`。
- 允许创建下一个 Identity remaining 计划（默认 Menus）；禁止并行迁移其他资源组。
- Menus 计划已创建：[`2026-08-21-openapi-client-identity-host-menus.md`](./2026-08-21-openapi-client-identity-host-menus.md)。

## Global Constraints

- 每个 slice 只迁移一个 `generatedGroup`；本计划唯一目标组为 `identity-host-roles`。
- `ui/admin-layui/**` 零修改。
- 禁止改路径、HTTP 方法、成功状态码语义、序列化形状；只允许补齐 `WithName`、主 Tag 与 OpenAPI 元数据。
- JSON 必须 `unknown → generated guard → DTO`；禁止 `request<T>` 断言；禁止页面直连生成 Class。
- 页面导出函数名与签名保持稳定（`getAuthorizationTree`、`listHostRoles` 等）。
- 不删除运行时守卫；手写 `packages/client-contracts/src/host-roles.ts` / `authorization-tree.ts` 等可在薄适配落地后按覆盖门禁收敛，但不得在未生成前删守卫。
- 工作区已脏时：`pnpm test:task:start -- <Snapshot>`，后续 inner/slice 使用同一 snapshot。
- 新依赖禁止；生成器保持零新增运行时依赖。
- 禁止 skip / 降断言 / `audit ignore` 宣称通过。

## 范围

### 纳入（本 slice）

| 项 | 值 |
| --- | --- |
| Vue 适配 | `ui/admin/src/api/roles.ts`（及 `roles.test.ts`） |
| 轻量夹具 | `contracts/openapi/identity-host-roles-v1.json`（及已对齐的 authorization-tree / data-scope / field-grants 相关夹具一致性） |
| C# Endpoint | `ManageHostRoles/Endpoint.cs`、`GetAuthorizationTree/Endpoint.cs`、`ManageHostRoleFieldGrants/Endpoint.cs` |
| generatedGroup | `identity-host-roles` |
| 语义 | 纯 JSON（分页、Path/Query/Body、201 Created）；无 multipart/Blob/`204` |

### 排除（必须另开计划）

- `menus.ts`、`api-keys.ts`、`online-sessions.ts`、`superAdministrators.ts`、`totpEnrollment.ts`、`module-catalog.ts`、`me.ts`
- `packages/client-contracts/src/identity-session.ts` / `ui/admin/src/auth/**`（login/refresh/logout、locale、navigation）
- Organization `host-user-organization-reference.ts`
- 已完成的 `users.ts` / `identity-host-users`

### “一个模块”定义

与 ADR-0007 试点粒度一致：**一个资源组 / 一个 `generatedGroup`**（本 slice = Host Roles），不是整个 C# Identity 模块，也不是一次改多个 Vue API 文件。

## 目标 Operation 清单

主 Tag：`IdentityHostRoles`（组级 `.WithTags`，与 Host Users 试点一致）。

| Method | Path | operationId | Vue 导出 | 备注 |
| --- | --- | --- | --- | --- |
| GET | `/api/v1/identity/authorization-tree` | `identityGetAuthorizationTree` | `getAuthorizationTree` | |
| GET | `/api/v1/identity/field-projections/catalog` | `identityListFieldProjectionCatalog` | `getFieldProjectionCatalog` | |
| GET | `/api/v1/identity/roles` | `identityListHostRoles` | `listHostRoles` | Query: page, pageSize |
| POST | `/api/v1/identity/roles` | `identityCreateHostRole` | `createHostRole` | 201 |
| GET | `/api/v1/identity/roles/{roleId}` | `identityGetHostRole` | （无 Vue 导出） | 进清单与生成；适配层可不暴露 |
| PUT | `/api/v1/identity/roles/{roleId}` | `identityUpdateHostRole` | `updateHostRole` | |
| PUT | `/api/v1/identity/roles/{roleId}/permissions` | `identityReplaceHostRolePermissions` | `replaceHostRolePermissions` | |
| POST | `/api/v1/identity/roles/{roleId}/disable` | `identityDisableHostRole` | `disableHostRole` | |
| GET | `/api/v1/identity/roles/{roleId}/data-scope` | `identityGetHostRoleDataScope` | `getHostRoleDataScope` | |
| PUT | `/api/v1/identity/roles/{roleId}/data-scope` | `identityUpdateHostRoleDataScope` | `updateHostRoleDataScope` | |
| GET | `/api/v1/identity/roles/{roleId}/field-grants` | `identityGetHostRoleFieldGrants` | `getHostRoleFieldGrants` | Query: resourceKey |
| PUT | `/api/v1/identity/roles/{roleId}/field-grants` | `identityReplaceHostRoleFieldGrants` | `replaceHostRoleFieldGrants` | |

清单初始状态：`pilot`；本 slice Verification `Slice-passed` 后改为 `generated`（兼容门禁已允许 `pilot→generated`，禁止降级）。

---

### Task 1: 固定 Host Roles 的 operationId、主 Tag 与 OpenAPI 元数据

**Snapshot:** `openapi-client-identity-host-roles-metadata-20260821`

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoles/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/GetAuthorizationTree/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoleFieldGrants/Endpoint.cs`
- Modify as needed: `contracts/openapi/identity-host-roles-v1.json`（及相关轻量夹具，若权限码/路径断言漂移）
- Modify as needed: OpenAPI Integration 测试断言 `operationId` / Tag

**Steps:**

- [x] **Step 1:** 为上表每个 Endpoint 增加 `.WithName(...)`；组 Tag 从 `"Identity"` 改为 `"IdentityHostRoles"`。
- [x] **Step 2:** 确认 `.Produces` / ProblemDetails / 请求体 Schema 满足客户端生成就绪门禁（参考 Host Users）。
- [x] **Step 3:** RED→GREEN：聚焦 Identity Roles / OpenAPI readiness 相关测试。
- [x] **Step 4:** `pnpm test:integration:affected -- --snapshot openapi-client-identity-host-roles-metadata-20260821 --phase inner`
- [x] **Step 5:** 提交 `feat: stabilize Identity Host Roles OpenAPI identities`

---

### Task 2: 导出标准快照并登记生成清单（pilot）

**Snapshot:** `openapi-client-identity-host-roles-snapshot-20260821`

**Files:**
- Modify: `contracts/openapi/fullnet-client-v1.openapi.json`
- Modify: `contracts/openapi/client-generation-manifest-v1.json`（追加本 slice 全部条目，`status: pilot`）
- Modify as needed: `contracts/openapi/vue-client-coverage-v1.json`

**Steps:**

- [x] **Step 1:** 按仓库既有流程导出并规范化标准快照（SQL Server/MySQL 导出一致）。
- [x] **Step 2:** 确认快照含上表全部 `operationId`，且不夹带其他未批准资源组。
- [x] **Step 3:** 清单追加 `identity-host-roles` → `ui/admin/src/api/roles.ts`。
- [x] **Step 4:** `pnpm openapi:client:snapshot -- --check`（或项目现行等价命令）、`pnpm test:openapi`
- [x] **Step 5:** 提交 `feat: freeze Identity Host Roles client OpenAPI snapshot`

---

### Task 3: 生成客户端并收缩 `roles.ts` 为薄适配层

**Snapshot:** `openapi-client-identity-host-roles-generate-20260821`

**Files:**
- Modify: `packages/client-contracts/src/generated/*.generated.ts`（仅经生成器）
- Modify: `ui/admin/src/api/roles.ts`
- Modify: `ui/admin/src/api/roles.test.ts`
- Modify as needed: `packages/client-contracts` 手写 host-roles / authorization-tree 导出与测试（避免双真相；页面不再重复路径与守卫）

**Steps:**

- [x] **Step 1:** 为畸形 JSON 守卫写/扩 RED（缺失 required、错误类型、数组 item）。
- [x] **Step 2:** `pnpm openapi:client:generate`；`--check` 零漂移。
- [x] **Step 3:** `roles.ts` 改为调用生成 Operation（模式对齐 `users.ts`），保留导出签名。
- [x] **Step 4:** `pnpm --filter @fullnet/client-contracts test`；`pnpm --filter @fullnet/admin exec vitest run src/api/roles.test.ts`
- [x] **Step 5:** 提交 `feat: generate Identity Host Roles client operations`

---

### Task 4: 完整门禁与 Verification；通过后清单升 `generated`

**Snapshot:** `openapi-client-identity-host-roles-verify-20260821`

**Files:**
- Create: `docs/verification/openapi-client-identity-host-roles-2026-08-21.md`
- Modify only if gates pass: `contracts/openapi/client-generation-manifest-v1.json`（本 slice `pilot` → `generated`）
- Modify only if evidence supports: `docs/roadmap/client-delivery-roadmap.md`（一句进度即可）
- Modify: 本计划执行状态勾选

**Gates（全部必须绿）：**

```text
pnpm openapi:client:generate -- --check
pnpm test:openapi
pnpm --filter @fullnet/client-contracts test
pnpm --filter @fullnet/client-contracts build
pnpm --filter @fullnet/admin exec vitest run src/api/roles.test.ts
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin build
pnpm audit:clients
pnpm test:naming
pnpm test:governance
dotnet build Full.NET.slnx -c Release
git diff --check
pnpm test:integration:affected -- --snapshot openapi-client-identity-host-roles-verify-20260821 --phase slice
```

**Decision（已完成）：**

- 全部通过 → Verification 写 `Slice-passed`；本 slice 清单改 `generated`；允许创建**下一个** Identity remaining 计划（默认 Menus）。
- 任一项失败 → `Slice-stopped`；清单保持 `pilot`；禁止开始 Menus 或其他资源组。

本轮结果：`Slice-passed`（smoke 8 + Identity 30 双库；其余门禁见 Verification）。

**提交建议：**

1. `fix: ...`（若门禁发现缺陷）
2. `docs: verify Identity Host Roles OpenAPI client slice`（含清单晋升） — 本轮执行

---

## Identity remaining 后续队列（本计划不执行）

| 顺序 | Vue API | 建议 generatedGroup | 备注 |
| --- | --- | --- | --- |
| 2 | `menus.ts` | `identity-host-menus` | 含 `sync-catalog`；夹具需对齐 |
| 3 | `api-keys.ts` | `identity-host-api-keys` | |
| 4 | `online-sessions.ts` | `identity-host-online-sessions` | |
| 5 | `module-catalog.ts` | `identity-host-modules` | 无独立 Vue test 时先补 RED |
| 6 | `me.ts` | `identity-me` | 夹具 `operationId` 需收敛命名 |
| 7 | `totpEnrollment.ts` | `identity-totp-enrollment` | |
| 8 | `superAdministrators.ts` | `identity-super-administrators` | 敏感动作，单独 Verification |
| 后置 | auth/session | （独立计划） | logout `204`；不在 `src/api` |

---

## 停止条件（继承 ADR-0007）

- 生成两次有漂移；或 JSON 守卫可被畸形响应穿透。
- Vue 页面批量改名或直连生成物。
- `createHttpClient` 行为回归（Refresh / Cookie / 语言 / ProblemDetails / 取消）。
- 双库 slice 失败或仅单库证据。
- 试图在同一 PR/窗口并行修改 Menus 或其他未批准资源组。

## 规则与 Skill 复盘预期

完成后若无新冲突类别，Verification 写一行“未新增规则/Skill 候选”。禁止机械累计 Skill 次数。
