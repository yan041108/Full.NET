# OpenAPI 客户端迁移：Identity Host API Keys（Identity remaining 第 3 slice）

> **For agentic workers:** 按本计划逐步执行；每 Task 独立 snapshot；行为变更必须 RED→GREEN。勾选或提交存在不能替代新鲜 Verification。

**Goal:** 将 `ui/admin/src/api/api-keys.ts` 从手写 HTTP/守卫收缩为消费仓库内 OpenAPI 生成 Operation 的薄适配层（`identity-host-api-keys`）。

**Architecture:** 延续 [`ADR-0007`](../../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md) 与已通过的 Host Roles / Host Menus slice 模式。

**Tech Stack:** .NET 10 OpenAPI、仓库内 Node 生成器、`@fullnet/client-contracts`、Vue admin Vitest、双库 Integration。

**Approved basis:** ADR-0007；试点 [`Pilot-passed`](../../verification/openapi-client-generation-pilot-2026-08-21.md)；Menus [`Slice-passed`](../../verification/openapi-client-identity-host-menus-2026-08-21.md)。

**Baseline:** 计划编写基线为 `0745b25008abd43fec45aa9ebe5db5c75de2494a`。执行者必须在每个 Task 开始时重新记录 `git rev-parse HEAD`。

## 执行状态（2026-08-22）

- 计划已创建；Task 1 已完成（WithName / IdentityHostApiKeys / Produces；inner Identity 15/15）。
- Task 2–4 尚未开始。

- [x] Step 1–4：WithName / Tag / Produces / ProblemDetails → Architecture + OpenAPI 夹具 → `pnpm test:inner -- --snapshot openapi-client-identity-host-api-keys-metadata-20260822`
- [x] Step 5：提交 `feat: stabilize Identity Host API Keys OpenAPI identities`

- 每个 slice 只迁移一个 `generatedGroup`：`identity-host-api-keys`。
- `ui/admin-layui/**` 零修改。
- 禁止改路径、HTTP 方法、成功状态码语义、序列化形状；只允许补齐 `WithName`、主 Tag、`.Produces` / ProblemDetails 与夹具 `requestSchema`。
- JSON 必须 `unknown → generated guard → DTO`；禁止 `request<T>`；禁止页面直连生成 Class。
- 页面导出函数名与签名保持稳定（含可选查询参数与一次性 `secret` 校验语义）。
- 禁止并行迁移其他未批准资源组；本 slice 不得改 `menus.ts` / `roles.ts`。
- 工作区已脏时：`pnpm test:task:start -- <Snapshot>`。
- 新依赖禁止；禁止 skip / 降断言 / `audit ignore`。

## 范围

| 项 | 值 |
| --- | --- |
| Vue 适配 | `ui/admin/src/api/api-keys.ts`（及 `api-keys.test.ts`） |
| 轻量夹具 | `contracts/openapi/identity-host-api-keys-v1.json` |
| C# Endpoint | `ManageHostApiKeys/Endpoint.cs` |
| generatedGroup | `identity-host-api-keys` |
| 语义 | 纯 JSON（分页查询、201 Created、禁用、轮换返回一次性 secret）；无 multipart/Blob/`204` |

### 排除

- `online-sessions`、`superAdministrators`、`totpEnrollment`、`module-catalog`、`me`、auth/session
- 已完成的 users / roles / menus

## 目标 Operation 清单

主 Tag：`IdentityHostApiKeys`。

| Method | Path | operationId | Vue 导出 |
| --- | --- | --- | --- |
| GET | `/api/v1/identity/api-keys` | `identityListHostApiKeys` | `listHostApiKeys` |
| POST | `/api/v1/identity/api-keys` | `identityCreateHostApiKey` | `createHostApiKey` |
| POST | `/api/v1/identity/api-keys/{apiKeyId}/disable` | `identityDisableHostApiKey` | `disableHostApiKey` |
| POST | `/api/v1/identity/api-keys/{apiKeyId}/rotate` | `identityRotateHostApiKey` | `rotateHostApiKey` |

清单初始 `pilot`；Verification `Slice-passed` 后改 `generated`。

---

### Task 1: 固定 operationId、主 Tag 与 Produces

**Snapshot:** `openapi-client-identity-host-api-keys-metadata-20260822`

**Files:**
- Modify: `ManageHostApiKeys/Endpoint.cs`
- Modify: `contracts/openapi/identity-host-api-keys-v1.json`（补 POST `requestSchema`）
- Modify: `OpenApiOperationIdentityRulesTests.cs`
- Modify as needed: `tests/openapi/identity-host-api-keys-contract.test.mjs`

- [ ] Step 1–4：WithName / Tag / Produces / ProblemDetails → Architecture + OpenAPI 夹具 → `pnpm test:inner -- --snapshot openapi-client-identity-host-api-keys-metadata-20260822`
- [ ] Step 5：提交 `feat: stabilize Identity Host API Keys OpenAPI identities`

---

### Task 2: 导出标准快照并登记生成清单（pilot）

**Snapshot:** `openapi-client-identity-host-api-keys-snapshot-20260822`

- 追加 4 条 `pilot`；更新规范化测试计数（50→54）；`pnpm openapi:client:snapshot -- --update`；`pnpm openapi:client:generate`；`pnpm test:openapi`

- [ ] 提交 `feat: freeze Identity Host API Keys client OpenAPI snapshot`

---

### Task 3: 生成客户端并收缩 `api-keys.ts`

**Snapshot:** `openapi-client-identity-host-api-keys-generate-20260822`

- 模式对齐 `menus.ts`（`http` mock、手写守卫保留对一次性 `secret` 与页面类型）

- [ ] 提交 `feat: generate Identity Host API Keys client operations`

---

### Task 4: 完整门禁与 Verification；通过后升 `generated`

**Snapshot:** `openapi-client-identity-host-api-keys-verify-20260822`

门禁同 Menus Task 4。verify snapshot 无代码 diff 时允许 `--base <Task1 前提交>`。

- Create: `docs/verification/openapi-client-identity-host-api-keys-2026-08-22.md`
- 4 条 `pilot` → `generated`

- [ ] 提交 `docs: verify Identity Host API Keys OpenAPI client slice`

## 停止条件

继承 ADR-0007；任一门禁失败保持 `pilot`，禁止开始 online-sessions 或其他资源组。

## 规则与 Skill 复盘预期

完成后若无新冲突类别，Verification 写一行“未新增规则/Skill 候选”。
