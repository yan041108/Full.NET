# uni-app H5 Workflow Session Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `fullnet-module-delivery` and `superpowers:test-driven-development` to implement this plan task-by-task.

**Goal:** 为 uni-app H5 建立与 Full.NET 浏览器身份边界一致的内存会话，并让后续 Workflow 页面安全复用；微信、支付宝小程序在完成平台凭据交换和账号绑定设计前保持失败关闭。

**Architecture:** H5 继续使用服务端现有的 HttpOnly Refresh Cookie 与 CSRF 双提交协议，Access Token 只保存在进程内存。uni.request 客户端通过可替换认证桥读取 Access Token，并对普通请求的首个 401 执行单航班刷新和一次重试。登录、刷新和退出请求显式禁止递归重试。小程序不得复用浏览器 Cookie 会话，也不得持久化 Refresh Token；后续应通过 `uni.login` 一次性平台凭据、服务端供应商交换和外部身份绑定形成独立设计。

**Tech Stack:** uni-app Vue 3、TypeScript、`uni.request`、`@fullnet/client-contracts` 契约守卫、Vitest。

## Global Constraints

- Access Token、Refresh Token、权限快照不得写入 `uni.setStorageSync` 或其他持久化存储。
- H5 请求必须携带同源 Cookie；Refresh/Logout 必须携带 CSRF 请求头。
- 只有普通受保护请求可以在 401 后重试一次，认证端点不得递归刷新。
- 并发 401 只允许一个刷新请求；刷新失败时所有等待请求都返回原始 401 语义。
- 微信/支付宝小程序未配置平台交换时必须保持匿名，不得回退到 H5 密码与 Cookie 流程。
- `ui/admin-layui` 保持零 diff；现有无关工作区改动不得纳入提交。

## Task 1: uni.request 认证桥与单航班刷新

**Files:**

- Modify: `clients/uniapp/src/api/http.ts`
- Modify: `clients/uniapp/tests/http-locale.test.ts`

1. 先增加失败测试，覆盖认证桥动态读取 token、401 后只重试一次、并发 401 共享刷新、刷新失败、认证端点禁用重试和 H5 Cookie 凭据选项。
2. 扩展 `HttpRequestOptions` 与 `HttpClient`，实现可清除的认证桥。
3. 将单次发送与重试协调分离，确保重试读取刷新后的 token，且 ProblemDetails 不被吞掉。
4. 运行 uni-app 定向测试与类型检查。

## Task 2: H5 内存身份会话

**Files:**

- Add: `clients/uniapp/src/features/identity/h5-identity-session.ts`
- Add: `clients/uniapp/tests/h5-identity-session.test.ts`
- Modify: `clients/uniapp/src/main.ts`

1. 先增加失败测试，覆盖登录、启动恢复、权限判断、退出本地先清理、畸形契约和刷新失败。
2. 使用生成契约操作和手写契约守卫建立最小状态机，仅保存 Access Token 与当前用户内存快照。
3. 通过依赖注入读取 CSRF Header；仅在 H5 编译分支装配浏览器实现。
4. 在 App 启动时恢复会话，恢复失败保持匿名且不阻塞应用启动。

## Task 3: Workflow 移动页面

**Files:**

- Add: `clients/uniapp/src/pages/identity/login.vue`
- Add: `clients/uniapp/src/pages/workflow/todos.vue`
- Add: `clients/uniapp/src/pages/workflow/todo-detail.vue`
- Modify: `clients/uniapp/src/pages.json`
- Add/Modify: matching Vitest tests

1. 登录页只在 H5 启用密码登录；小程序展示稳定的“平台登录未配置”状态。
2. 待办列表要求 `workflow.todos.read`，详情按运行时 Schema Hash 读取并缓存表单。
3. 审批与驳回分别要求稳定操作权限，按钮无权限时不创建；服务端仍做精确 Endpoint 授权。
4. 使用安全 UUID 作为幂等键，失败重试复用同一键。

## Task 4: Verification and Closeout

1. 运行 `pnpm --filter @fullnet/uniapp test`、typecheck 及 H5/微信/支付宝三端构建。
2. 使用 `pnpm test:inner -- --snapshot workflow-uniapp-auth-runtime-20260830` 验证影响集。
3. 检查 `git diff --check`、任务影响集与工作区状态，只提交本计划影响的文件。
4. 在平台 Exchange、账号绑定、密钥配置、审计和双库迁移设计批准前，不把微信/支付宝身份能力标记为完成。
