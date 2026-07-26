# Admin 真实栈会话上下文定位器契约验证

- 日期：2026-07-27
- 开发基线：`main@8d80c62`
- 范围：`tests/e2e/admin-real-stack/scripts`、真实栈 provisioner 静态门槛
- 数据库与容器：不涉及

## 问题与根因

Vue 管理端同时包含可见的当前租户上下文和隐藏的下拉选项文本。真实栈用例若直接使用
`getByText('Full.NET Host').first()`，会优先命中隐藏副本并产生伪超时。此前已在登录冒烟、
跨 Tab 刷新和刷新恢复三个用例中重复出现，说明只修具体 spec 不能阻止回归。

真实栈已经提供 `expectVisibleCurrentContext`，它分别按 Vue 用户菜单和 Layui 当前上下文
元素断言可见状态。本切片把“Host 上下文必须通过该辅助函数断言”固化为可执行静态契约。

## RED

先新增契约扫描器测试并执行：

```powershell
node --test scripts/spec-contracts.test.mjs
```

结果：失败，`ERR_MODULE_NOT_FOUND`，因为 `spec-contracts.mjs` 尚不存在。该失败证明新门槛
确实依赖待实现的扫描能力。

## GREEN

新增最小扫描器，识别真实栈 spec 中直接调用
`getByText('Full.NET Host')` 的位置，并把所有 `scripts/*.test.mjs` 接入既有
`test:e2e:provisioner` 命令。

```powershell
pnpm --dir tests/e2e/admin-real-stack test:provisioner
```

结果：`6/6`，失败 `0`，跳过 `0`。其中包括：

- 直接 Host 文本定位的正向识别；
- `expectVisibleCurrentContext` 的允许路径；
- 当前全部真实栈 spec 的仓库级契约扫描；
- 原有 provisioner 三项回归。

完成前静态门槛：

- Governance：`11/11`
- 项目 Skill：`52` 项契约检查
- client workspace：通过
- `git diff --check`：通过

## 边界与结论

本切片不改变真实栈场景数量、客户端生产代码、API、数据库或 canonical .NET 门槛，也不
启动 Docker。新增守卫只约束 `tests/e2e/admin-real-stack/tests/*.spec.mjs`；Mock parity
使用独立页面夹具和定位策略，不在本次契约范围。

后续若真实栈新增 Host 上下文断言，应继续复用 `expectVisibleCurrentContext`。门槛失败会
报告具体 spec 与行号，避免再次通过扩大超时掩盖隐藏元素定位错误。

规则复盘：同类遗漏已在登录冒烟、跨 Tab 和刷新恢复三个真实栈用例中出现，达到重复性
升级门槛，因此在 `rules/client-frontend.md` 固化可见上下文定位要求，并由本契约自动验证。
Skill 复盘无变化：该防护是确定性单命令扫描，不包含需要沉淀为项目 Skill 的复杂判断流程。
