# Admin Parity Client Contract Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 收口 Cursor 对 Jobs 与 Identity 的 Admin.NET 字段扩展，使共享 TypeScript runtime guards、Vue 组件、测试 fixture、OpenAPI 和生产 API 调用重新一致。

**Architecture:** C# public contract 与已冻结 OpenAPI 是字段事实源，共享 `@fullnet/client-contracts` 提供唯一 DTO/runtime guard；Vue API 只接收 `unknown` 并经 guard 验证，组件 fixture 必须构造完整真实 DTO。Jobs 与 Identity 分任务串行修改，最后统一跑客户端和 OpenAPI 门禁。

**Tech Stack:** Vue 3、TypeScript 6/7、Element Plus 2.14.3、Vitest、Vue Test Utils、OpenAPI frozen fixtures、pnpm。

## Global Constraints

- 不修改已冻结的 `ui/admin-layui`。
- 不用可选字段或 `as unknown as` 掩盖后端必填字段；fixture 必须显式提供真实值。
- 不在 Vue API 文件声明后端 DTO；全部 DTO/guards 来自 `@fullnet/client-contracts`。
- 不静默修改 v1 OpenAPI 稳定字段；需要加法升级时新增版本文件并保留旧版本。
- 每个 Task 独立 RED/GREEN/commit，禁止与 Document 收口计划并行修改 `packages/client-contracts/src/index.ts`。

---

### Task 1: 收口 Identity 用户扩展字段与编辑器 props

**Files:**
- Modify: `packages/client-contracts/tests/host-users.test.ts`
- Modify: `ui/admin/src/views/UsersView.test.ts`
- Modify: `ui/admin/src/views/components/UserEditorDialog.test.ts`
- Modify: `ui/admin/src/views/UsersView.vue` only if a typed table callback still widens rows to `DefaultRow`
- Test: `ui/admin/src/views/components/UserEditorDialog.test.ts`

**Interfaces:**
- Consumes: `HostUser.accountType`、`HostUserProfile.emergencyContactRelation` and the current `UserEditorDialog` required props.
- Produces: complete user fixtures and a typed row callback accepting `HostUser`.

- [ ] **Step 1: 保留当前失败作 RED 证据**

Run: `pnpm --filter @fullnet/client-contracts test -- host-users.test.ts`

Run: `pnpm --filter @fullnet/admin typecheck`

Expected: host user guard 返回 false；dialog fixture 缺 `accountType`、unit/position visibility/options/callback props；Users fixture 缺 `emergencyContactRelation`。

- [ ] **Step 2: 补齐共享 fixture**

在所有 `HostUser` fixture 增加与 C# 枚举契约一致的 `accountType`；在所有 `HostUserProfile` fixture 增加 `emergencyContactRelation: null` 或明确测试值。至少增加一个非法 account type 和错误 emergency contact 类型会被 guard 拒绝的负向断言。

- [ ] **Step 3: 建立完整 dialog props factory**

在测试文件创建 `createProps()`，返回组件当前声明的全部 required props，包括：

```ts
accountType: 'normal_user',
canViewUserUnits: true,
canViewUserPositions: true,
accountTypeOptions: [],
idCardTypeOptions: [],
ethnicityOptions: [],
educationLevelOptions: [],
emergencyContactRelationOptions: []
```

保留现有 `orgUnitTreeOptions`、`positionOptions`、`canManageUserUnits` 和 `canManageUserPositions`；禁止用 `Record<string, unknown>` 绕过类型。

- [ ] **Step 4: 修正表格行类型并运行 GREEN**

显式把相关 `TableColumnCtx`/formatter callback 的 row 泛型绑定为 `HostUser`，不要在回调内强制转换。

Run: `pnpm --filter @fullnet/client-contracts test -- host-users.test.ts`

Run: `pnpm --filter @fullnet/admin test -- UsersView.test.ts UserEditorDialog.test.ts`

Run: `pnpm --filter @fullnet/admin typecheck`

Expected: 本 Task 涉及的 Identity 错误消失；其他模块错误允许继续存在并在对应 Task 关闭。

- [ ] **Step 5: 提交**

```bash
git add packages/client-contracts/tests/host-users.test.ts ui/admin/src/views/UsersView.vue ui/admin/src/views/UsersView.test.ts ui/admin/src/views/components/UserEditorDialog.test.ts
git commit -m "fix(identity-ui): align user parity contracts"
```

### Task 2: 收口 Jobs definition/schedule 契约与组件类型

**Files:**
- Modify: `packages/client-contracts/tests/host-jobs.test.ts`
- Modify: `ui/admin/src/views/HostJobsView.vue`
- Modify: `ui/admin/src/views/HostJobsView.test.ts`
- Modify: `ui/admin/src/views/HostJobSchedulesView.test.ts`

**Interfaces:**
- Consumes: `HostJobDefinition.groupName`; `HostJobSchedule.numberOfRuns, numberOfErrors, startTime, endTime, args`; `DrawerProps` from Element Plus.
- Produces: valid Jobs fixtures and literal-safe well-known job key handling.

- [ ] **Step 1: 写/保留 RED**

Run: `pnpm --filter @fullnet/client-contracts test -- host-jobs.test.ts`

Expected: definition/schedule valid fixture 被新 guard 拒绝。

- [ ] **Step 2: 补完整 fixture 与负向测试**

Definition fixture 增加 `groupName`；Schedule fixture 增加 `numberOfRuns: 0`、`numberOfErrors: 0`、`startTime: null`、`endTime: null`、`args: null`。Vue 两个 view tests 使用同一字段形状，不复制另一套接口。

- [ ] **Step 3: 修正组件类型**

把不存在的 `ElDrawerProps` 改为 Element Plus 2.14.3 实际导出的 `DrawerProps`。`jobs.ping` 参数必须保持 `JOBS_WELL_KNOWN_KEYS.Ping` 的字面量类型；不要把它先扩宽成任意 `string`。

- [ ] **Step 4: 运行 GREEN**

Run: `pnpm --filter @fullnet/client-contracts test -- host-jobs.test.ts`

Run: `pnpm --filter @fullnet/admin test -- HostJobsView.test.ts HostJobSchedulesView.test.ts`

Run: `pnpm --filter @fullnet/admin typecheck`

Expected: Jobs 相关 guard、fixture、Element Plus 类型和 literal 错误全部消失。

- [ ] **Step 5: 提交**

```bash
git add packages/client-contracts/tests/host-jobs.test.ts ui/admin/src/views/HostJobsView.vue ui/admin/src/views/HostJobsView.test.ts ui/admin/src/views/HostJobSchedulesView.test.ts
git commit -m "fix(jobs-ui): align admin parity contracts"
```

### Task 3: 完整客户端与 OpenAPI 合并门禁

**Files:**
- Modify: `docs/verification/cursor-delivery-review-2026-08-09.md`
- Modify: `docs/roadmap/capability-status.md` only if all relevant gates pass

**Interfaces:**
- Consumes: Tasks 1–2 and completed Document parity client Task 5.
- Produces: fresh client verification evidence without copied test thresholds.

- [ ] **Step 1: 运行共享契约和 Vue 门禁**

Run: `pnpm --filter @fullnet/client-contracts test`

Run: `pnpm --filter @fullnet/client-contracts build`

Run: `pnpm --filter @fullnet/admin test`

Run: `pnpm --filter @fullnet/admin typecheck`

Run: `pnpm --filter @fullnet/admin build`

Expected: 全部 exit 0；不得用排除测试文件获得通过。

- [ ] **Step 2: 运行平台契约门禁**

Run: `pnpm test:openapi`

Run: `pnpm test:governance`

Run: `git diff --check`

Expected: 全部 exit 0，Vue production API module 数量来自实际枚举并与 manifest 一一对应。

- [ ] **Step 3: 更新证据并提交**

只记录新鲜命令、结果和仍未运行的真实浏览器项；测试数量只维护 `eng/testing/test-matrix.json`。

```bash
git add packages/client-contracts ui/admin docs
git commit -m "test(admin): close parity client contract gates"
```
