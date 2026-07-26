# Identity API Key 双管理端 UI 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为既有 Host API Key 管理 API 补齐 Vue/Layui 双管理端列表、创建、一次性明文提示、复制与禁用闭环。

**Architecture:** 服务端只新增受 `identity.api_keys.read` 保护的可信导航项，不改变 HTTP、数据库或序列化契约。双端复用 `@fullnet/client-contracts` 的响应守卫，各自保留独立视图实现，并通过同一组双端 Playwright 场景锁定权限、一次性密钥和禁用语义。

**Tech Stack:** .NET 10、Vue 3、Element Plus、Layui 2、TypeScript/JavaScript、Vitest、Playwright。

## Global Constraints

- 仅支持 Host 作用域，不增加租户 API Key、轮换或使用审计。
- 明文 `secret` 只保存在当前页面内存，刷新或下一次创建时覆盖，不进入 Web Storage、日志或列表响应。
- 读权限控制导航与列表；写权限单独控制创建、复制后的确认流程和禁用按钮。
- Layui 使用 DOM API 写入服务端字段，禁止把用户数据拼接进 `innerHTML`。
- 所有用户可见文本进入 `@fullnet/admin-i18n` 的 `zh-CN/en-US` 对称资源。
- 完成状态保持 `Build-verified`，不把 Mock 浏览器场景描述成真实后端验证。

---

### Task 1: 发布可信导航与共享目录

**Files:**
- Modify: `tests/Full.NET.UnitTests/Identity/AuthorizationCatalogTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityAuthorizationContributor.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Authorization/AdminNavigationWhitelist.cs`
- Modify: `packages/client-contracts/tests/navigation-catalog.test.ts`
- Modify: `packages/client-contracts/src/navigation-catalog.ts`
- Modify: `ui/admin/src/navigation/catalog.test.ts`
- Modify: `ui/admin/src/navigation/catalog.ts`
- Modify: `ui/admin-layui/tests/navigation.test.js`
- Modify: `ui/admin-layui/js/core/navigation.js`

**Interfaces:**
- Produces: `componentKey/routeName = "api-keys"`，`path = "/identity/api-keys"`。
- Consumes: `IdentityApiKeyManagementPermissions.Read`。

- [ ] **Step 1: 写服务端与客户端目录 RED 测试**

  断言授权目录包含 `api-keys`，并且共享、Vue、Layui 目录只接受精确的 route/path 组合。

- [ ] **Step 2: 运行 RED**

  Run:
  `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --filter "FullyQualifiedName~AuthorizationCatalogTests"`
  `pnpm --filter @fullnet/client-contracts test -- navigation-catalog.test.ts`
  Expected: 因 `api-keys` 尚未发布而失败。

- [ ] **Step 3: 实现最小可信导航**

  服务端发布：

  ```csharp
  new NavigationDefinition(
      "api-keys", null, "api-keys", "/identity/api-keys", "api-keys",
      "API Key", "API Keys", "key", 36,
      IdentityApiKeyManagementPermissions.Read)
  ```

  三份客户端目录使用同一精确映射；未知组件继续拒绝。

- [ ] **Step 4: 运行 GREEN**

  重跑 Task 1 的 .NET 与三份前端目录测试，预期全部通过。

### Task 2: 建立 Vue API 适配器与页面

**Files:**
- Create: `ui/admin/src/api/api-keys.ts`
- Create: `ui/admin/src/api/api-keys.test.ts`
- Create: `ui/admin/src/views/ApiKeysView.vue`
- Create: `ui/admin/src/views/ApiKeysView.test.ts`
- Modify: `ui/admin/src/router/index.ts`
- Modify: `packages/admin-i18n/src/messages.ts`

**Interfaces:**
- Produces:
  - `listHostApiKeys(page?, pageSize?, userId?, displayNameContains?)`
  - `createHostApiKey({ userId, displayName, permissions, expiresAtUtc })`
  - `disableHostApiKey(id)`
- Consumes: `HostApiKeyPage`、`CreateHostApiKeyResult`、`HostApiKey` 守卫。

- [ ] **Step 1: 写 Vue RED 测试**

  API 测试锁定 GET 查询、POST JSON、disable 路径和非法响应拒绝；视图测试锁定写权限、trim/去重后的权限数组、一次性明文、复制、禁用确认和 ProblemDetails。

- [ ] **Step 2: 运行 RED**

  Run:
  `pnpm --filter @fullnet/admin test -- api-keys.test.ts ApiKeysView.test.ts`
  Expected: 因适配器和页面不存在而失败。

- [ ] **Step 3: 实现最小 Vue 闭环**

  创建请求体固定为：

  ```ts
  {
    userId: string;
    displayName: string;
    permissions: string[];
    expiresAtUtc: string | null;
  }
  ```

  页面把权限输入按换行或逗号拆分、trim、去重；成功后仅在 `secret` ref 中展示，复制使用 `navigator.clipboard.writeText`，下一次创建前清空旧明文。

- [ ] **Step 4: 运行 GREEN**

  重跑 Vue 聚焦测试、`pnpm --filter @fullnet/admin typecheck`。

### Task 3: 建立 Layui 独立页面与控制器

**Files:**
- Create: `ui/admin-layui/js/core/api-keys.js`
- Create: `ui/admin-layui/tests/api-keys.test.js`
- Modify: `ui/admin-layui/index.html`
- Modify: `ui/admin-layui/js/app.js`

**Interfaces:**
- Produces: `createApiKeysController(root, { request, translation, clipboard })`。
- Consumes: 与 Vue 相同的 HTTP 路径和请求体。

- [ ] **Step 1: 写 Layui RED 测试**

  测试加载列表、创建请求、明文只显示一次、复制、禁用确认、重复提交合并以及服务端字段按文本写入 DOM。

- [ ] **Step 2: 运行 RED**

  Run:
  `pnpm --filter @fullnet/admin-layui test -- api-keys.test.js`
  Expected: 因控制器不存在而失败。

- [ ] **Step 3: 实现最小 Layui 闭环**

  使用 `createElement`、`textContent` 和 `replaceChildren` 渲染列表；事件代理只读取固定 `data-api-key-*` 属性；`dispose()` 移除表单和目录监听器。

- [ ] **Step 4: 运行 GREEN**

  重跑 Layui 聚焦测试和生产构建。

### Task 4: 锁定双端权限与浏览器同场景

**Files:**
- Modify: `tests/e2e/admin-parity/tests/shell-parity.spec.mjs`
- Modify: `packages/admin-i18n/src/messages.ts`
- Modify: `packages/admin-i18n/tests/resources.test.ts`

**Interfaces:**
- Consumes: `api-keys` 导航和两端相同可访问名称。
- Produces: Mock parity 场景，覆盖创建、复制提示、禁用和只读账号无写按钮。

- [ ] **Step 1: 写 E2E RED 场景**

  Mock GET/POST/disable，导航到 `/identity/api-keys`；创建后断言 `fnk_*` 明文和“一次性”提示，禁用后断言状态变化；只读权限快照下不出现创建表单和禁用按钮。

- [ ] **Step 2: 运行 RED**

  Run:
  `pnpm test:e2e -- --grep "API Key"`
  Expected: 因本地路由/视图未完整接入而失败。

- [ ] **Step 3: 完成双端装配与资源**

  补齐中英文消息键、Vue Router、Layui `knownLocalPaths`、控制器创建/load/dispose 与页面标记。

- [ ] **Step 4: 运行 GREEN**

  重跑 API Key E2E，预期 Vue/Layui 两项目全部通过。

### Task 5: 文档、全量验证、提交与合并

**Files:**
- Modify: `docs/superpowers/plans/2026-07-26-identity-api-key-vertical-slice.md`
- Modify: `docs/verification/identity-api-key-2026-07-26.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify: `docs/superpowers/plans/2026-07-26-identity-api-key-admin-ui.md`

**Interfaces:**
- Produces: 可审计的 `Build-verified` 证据。

- [ ] **Step 1: 同步状态但保留真实限制**

  记录双端 UI、测试数量和 Mock parity；继续声明轮换、使用审计及真实后端浏览器链路未完成。

- [ ] **Step 2: 执行最终验证**

  Run:

  ```powershell
  pnpm test:clients
  pnpm build:clients
  pnpm test:e2e -- --grep "API Key"
  pnpm test:governance
  pnpm test:skills
  pnpm test:workspace
  pnpm audit:clients
  dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --filter "FullyQualifiedName~AuthorizationCatalogTests"
  git diff --check
  ```

  Expected: 全部返回 0，测试摘要无失败。

- [ ] **Step 3: 规则与 Skills 复盘**

  仅当 `rules/rule-evolution.md` 或 `rules/skill-evolution.md` 的证据门槛命中时更新治理文件；否则在交付说明中记录“无变化”。

- [ ] **Step 4: 提交、合并并清理**

  在 `codex/identity-api-key-admin-ui` 提交聚焦变更；将最新本地 `main` 合并进分支复验，再合并回本地 `main`。成功后删除分支和 `.worktrees/identity-api-key-admin-ui`，不推送远端。

## 执行记录

- [x] Task 1：服务端与三份客户端目录完成 RED→GREEN。
- [x] Task 2：Vue API 适配器、管理页、路由、权限与一次性明文测试完成。
- [x] Task 3：Layui DOM 安全控制器、页面、应用装配与只读权限测试完成。
- [x] Task 4：中英文资源与 Vue/Layui Mock API Playwright 场景 2/2 完成。
- [ ] Task 5：全量验证、提交、合并与分支清理进行中。
