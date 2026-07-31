# 租户上下文导航 HostOnly 越界修复验证记录

- 日期：2026-07-21
- 缺陷：进入租户后 `/api/v1/navigation` 返回 `common.unexpected`（500），客户端清空会话回到登录页；机构/租户真实栈 E2E 失败

## 根因

`IdentitySql.ListActiveHostMenus` 标注 `SqlDataScope.HostOnly`，但 `GetNavigation` 在租户请求上下文中也会加载 Host 菜单目录以合并代码导航。`SqlScopeGuard` 因此抛出 `HostContextRequiredException`。

次要：`FindHostUserById` 同为 `HostOnly`，会阻断租户上下文中 `IHostUserDirectory` 校验；用户机构隶属页在租户上下文调用 `/api/v1/identity/users` 时因缺 `identity.users.read` 得 403，Layui 未降级导致空列表文案不可见。

## 修复

| 项 | 说明 |
|---|---|
| SQL 作用域 | `ListActiveHostMenus`、`FindHostUserById` → `SqlDataScope.Global`（SQL 仍限制 Host 行） |
| 单测 | `HostCatalogSqlScopeTests`（+2 → Unit **333**） |
| 客户端 | 租户上下文中用户机构隶属页对 Host 用户列表 403 降级，不阻断空列表渲染 |
| 2026-07-29 增补 | 用户机构/职位隶属改用各自精确写权限保护的可分配 Host 用户候选 API；双端分页按需加载，403 降级为空候选且不依赖 `/api/v1/me`；未放宽 Host 用户管理目录权限 |
| E2E | `enterDevelopmentTenant` / `expectVisibleCurrentContext`；机构与租户规格等待侧栏上下文 |

## 本地验证

| 命令 | 结果 |
|---|---|
| Unit filter `HostCatalog`（Release DLL） | **2/2** 通过 |
| Unit 全量 `--minimum-expected-tests 333` | **333/333** 通过 |
| `CI=1` real-stack：`tenant-context` + `host-org-units` + `host-org-user-units` | **10/10** 通过（约 56s） |

## 仍开放

- 可分配 Host 用户候选目录已由用户机构/职位专用 API 关闭；后续若候选规模需要服务端搜索，须以真实数据规模和交互证据另行立项。
