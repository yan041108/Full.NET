# Vue 操作权限交付审查记录（2026-08-03）

## 范围与结论

本次审查覆盖 Cursor 已交付的操作权限目录、Host 角色精确授权、客户端授权树、Vue 角色授权页、治理门禁与测试矩阵。审查结论为：已完成切片可以保留，但必须先合入本记录列出的失败关闭修复；剩余粗粒度权限继续按 [W4–W5 实施计划](../superpowers/plans/2026-08-03-vue-action-authorization-w4-w5.md) 执行。

Layui 不再作为交付目标或验证证据。审查期间发现的 `ui/admin-layui/**` 变更已恢复到 2026-08-02 冻结基线，并增加可执行治理门禁，防止后续任务再次写入。

## 已纠正问题

1. **Layui 冻结仅有文档约束，缺少机器门禁。** 已恢复冻结后的全部 Layui 改动，并新增基于完整冻结提交的已跟踪差异与未跟踪新文件检查。
2. **角色授权弹窗在打开后撤销会话权限，保存按钮仍留在 DOM。** 权限授权与数据范围保存按钮现均使用精确 `PermissionGate`，提交处理器同时执行命令式权限复核。
3. **Authorization Catalog 的动作排序依赖 Contributor 原始插入顺序。** 现先排序页面导航，再按排序后的导航生成动作秩；动作权限作用域必须是父页面作用域的子集，不能只存在部分交集。
4. **Host 角色权限写入会静默清理空白项和重复项。** 现对空白、修剪后重复和未知权限统一失败关闭；保留 Host 角色承载 Tenant-only 权限的既有跨上下文模型，不静默切断 Organization/Settings 授权。
5. **客户端授权树接受页面读取权限伪装为动作，并接受跨页面重复动作码。** 严格解析器现拒绝两类非法目录；重复权限测试使用独立动作 ID，避免被重复 ID 断言掩盖。
6. **注册、模块目录与测试矩阵快照落后于生产代码。** 已补齐 Authorization Tree 投影器、Document 模块以及 fresh Unit/Architecture 门槛。
7. **迁移恢复测试选择器硬编码到 Migration054。** 工具链现从迁移测试目录自动发现恢复测试；新增恢复测试若未登记到矩阵将由契约测试立即拦截。
8. **角色撤权或停用只更新角色数据，旧 access token 与并发晚插入的旧权限会话仍可能继续使用。** 角色权限替换与角色停用现于同一事务轮换全部角色成员的 `SecurityStamp` 并撤销现有会话；双库场景同时验证旧 token 返回 401，重新登录后的 token 按新权限返回精确 403。即使登录流程已读取旧权限后才晚插入会话，访问校验也会因安全戳不匹配而失败关闭。

审查还确认角色授权页目前只有“页面/操作”两级，没有真实模块节点，并且 Host 角色树尚不能选择既有 Tenant-only 跨上下文权限。该缺口未伪装成已修复，已作为后续计划 Task 0 的首要任务；完成前不得宣称“模块/页面/操作”三级授权已验收。

## 新鲜验证证据

以下命令均在任务快照 `admin-action-permission-review-20260803` 上重新执行并通过：

```powershell
pnpm test:dotnet:unit
pnpm test:dotnet:architecture
pnpm --filter @fullnet/client-contracts test
pnpm --filter @fullnet/admin test
pnpm test:integration:tooling
pnpm test:governance
pnpm test:integration:affected -- --snapshot admin-action-permission-review-20260803 --phase slice
```

affected slice 命中 Identity、Integration 工具链与测试矩阵，SQL Server/MySQL 聚焦测试通过，Release build 为零警告零错误；测试结束后 shared runner、SQL Server、MySQL 与 Ryuk 残留均为零。

## 后续执行边界

- 只开发 `ui/admin`，任何任务不得修改 `ui/admin-layui/**`。
- 一个资源一个任务快照、一个可审查提交；迁移号只在任务启动时重新确认，不把计划中的候选号视为永久占位。
- 每个动作同时交付：稳定权限码、页面动作目录、精确 Endpoint 策略、角色/API Key 存量迁移、Vue DOM 失败关闭、直接 API 失败关闭与双库恢复测试。
- W4–W5 已完成；不得把“所有后台按钮均可独立授权”标记为未完成。剩余粗粒度权限继续按库存与 Architecture 门禁治理。
