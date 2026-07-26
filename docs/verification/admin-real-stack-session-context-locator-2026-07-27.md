# Admin 真实栈会话上下文定位器验证

- 日期：2026-07-27
- 开发基线：`main@191b536`
- 范围：`session-cross-tab.spec.mjs`、`session-restore.spec.mjs`
- 数据库：SQL Server Testcontainer

## 问题与边界

Vue 管理端把租户上下文移入用户菜单后，页面仍包含租户选择器的隐藏选项文本。两个会话用例继续使用全页 `getByText(...).first()`，因此优先命中隐藏的 `Full.NET Host`，并在真实登录已经成功后误报超时。Layui 当前不会触发同一症状，但直接依赖页面文本顺序会让双端断言语义分叉。

本次仅让两个用例复用既有 `expectVisibleCurrentContext` 双端辅助函数；不修改会话、租户、鉴权、API、数据库或客户端生产代码，也不增加测试方法和门槛数量。

## RED 证据

```powershell
pnpm --dir tests/e2e/admin-real-stack exec playwright test `
  tests/session-cross-tab.spec.mjs `
  tests/session-restore.spec.mjs `
  --project=vue-admin
```

结果：`0/2`。两项均解析到隐藏 `<span>Full.NET Host</span>`，分别在 `session-cross-tab.spec.mjs:12` 与 `session-restore.spec.mjs:12` 报告 `Expected: visible; Received: hidden`。

## GREEN 证据

```powershell
pnpm --dir tests/e2e/admin-real-stack exec playwright test `
  tests/session-cross-tab.spec.mjs `
  tests/session-restore.spec.mjs
```

结果：`4/4`，耗时 `1.2m`：

- Vue：跨 Tab Refresh Cookie `1/1`，刷新恢复会话 `1/1`
- Layui：跨 Tab Refresh Cookie `1/1`，刷新恢复会话 `1/1`
- SQL Server 迁移执行 `32` 个脚本并完成 Development Seed
- teardown 后 SQL Server、Ryuk、API、Playwright 和 Vite 进程均释放

运行期间浏览器控制台仍记录既有 Notifications SignalR negotiate `404` 噪声；该连接不属于本切片断言范围，四项用例均正常完成。

## 结论与未验证项

两个会话用例现在按实际可见的当前上下文断言，不再依赖隐藏选项或 DOM 文本顺序，并保持 Vue/Layui 同一测试语义。

未重跑 MySQL 和真实栈全量：本次不改数据库、API 或会话协调逻辑，SQL Server 双端聚焦已覆盖定位器的客户端分支。合入前仍需同步最新 `main`，重新执行同一聚焦命令及仓库静态门禁。
