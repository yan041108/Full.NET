# Vue 单一后台与页面/操作精确授权设计

**状态：** Approved

**决策日期：** 2026-08-02

**适用范围：** Authorization Catalog、Identity 角色授权、所有后台管理 Endpoint、`packages/client-contracts`、`ui/admin`、代码生成器；`ui/admin-layui` 仅适用冻结边界

## 1. 背景与目标

Full.NET 已具备稳定权限码、动态权限策略、角色权限持久化、服务端导航投影、Vue `session.can(...)` 和 Endpoint 授权基础，但多数业务操作仍共用 `*.write`。这会导致“能编辑”同时获得创建、启禁用、授权角色、重置密码等不同风险动作，角色授权无法最小化；部分客户端按钮隐藏也可能与后端 Endpoint 权限漂移。

Admin.NET.Pro 的目录/页面/按钮树、登录返回按钮权限、前端 `v-auth` 和后端二次校验证明了完整用户流程的价值。本设计只吸收该流程，不复制其 `SysMenu` 复用、URL 权限码、Furion 动态路由、SqlSugar、七天缓存、超级管理员短路和“未知接口不在黑名单则放行”的实现。

本设计同时执行项目所有者的新客户端决策：Vue 是后台管理唯一持续交付线；Layui 停止新增功能，不再参与新能力 `Verified` 门槛。

目标：

1. 每个受保护页面可独立授权；
2. 每个调用受保护 API、读取敏感数据、导出数据或产生业务副作用的页面操作可独立授权；
3. Vue 无权限时不创建相应页面入口或操作按钮；
4. 绕过 Vue 直接请求时，Endpoint 以同一权限码返回标准 `403`；
5. 角色授权页能按模块、页面和操作分层查看、勾选和撤销；
6. 未登记权限、孤立操作和未声明授权意图的生产 Endpoint 失败关闭；
7. 存量角色和 API Key 在权限拆分时保持等价行为，并允许管理员随后收紧。

## 2. 非目标

- 不把取消、关闭、折叠、分页、纯本地排序和布局切换做成权限；
- 不根据 DOM、按钮文本或 URL 推导后端权限；
- 不创建通用 Repository、动态权限表达式或数据库驱动的任意客户端组件；
- 不在本计划中继续建设、追平或删除 Layui；
- 不一次性批量改写全部模块。先建立目录和门禁，再按可验证纵向切片迁移存量模块。

## 3. 权限模型

### 3.1 稳定权限码

权限码采用 `<module>.<resource>.<action>` 语义，不绑定 HTTP 路径。页面读取通常使用 `read`，操作使用明确动词：

```text
identity.users.read
identity.users.create
identity.users.update
identity.users.assign_roles
identity.users.reset_password
identity.users.disable
identity.users.enable
identity.users.export
```

禁止新增以 `write` 同时代表多个语义动作的权限。确实是单一原子动作的既有 `write` 可在该资源迁移切片前保留，但不得继续承载新按钮。

### 3.2 页面与操作定义

保持 `PermissionDefinition` 为稳定权限事实，并新增代码拥有的 `AuthorizationActionDefinition`：

```csharp
public sealed record AuthorizationActionDefinition(
    string Id,
    string NavigationId,
    string PermissionCode,
    string Name,
    string ClientActionKey,
    int Order);
```

- `Id`：全局唯一、稳定的目录项标识，例如 `identity.users.reset-password`；
- `NavigationId`：已登记页面导航 ID；
- `PermissionCode`：已登记权限码；
- `Name`：角色授权页显示名称，后续可迁移为资源键；
- `ClientActionKey`：Vue 本地操作白名单键，不是组件路径；
- `Order`：页面内稳定排序。

`IAuthorizationCatalogContributor` 新增带空集合默认实现的 `Actions`，尚未迁移的模块无需机械修改；只有拥有页面业务操作的 Contributor 显式覆盖。目录创建时必须验证：ID 唯一、`NavigationId` 已知、权限码已知、同一权限不得被两个不同操作歧义占用、页面读取权限不能被注册为页面内副作用动作、操作不可形成孤立目录项。

### 3.3 Endpoint 绑定

每个管理 Endpoint 显式调用：

```csharp
.RequireFullNetPermission(IdentityUserManagementPermissions.ResetPassword)
```

权限策略只接受 Authorization Catalog 中已登记代码。Architecture Tests 必须枚举生产 Endpoint：

- 显式匿名：允许；
- 显式普通认证且具备批准豁免元数据：允许；
- 显式 Full.NET 权限且代码存在：允许；
- 缺少授权意图、引用未知权限或使用未批准通用策略：失败。

Endpoint 权限只回答“能否执行该动作”；Handler/Domain 继续保护 Host/租户、账号状态、并发版本、最后一名管理员和其他业务不变量。

## 4. 角色授权目录与持久化

### 4.1 API

新增只读目录 Endpoint：

```text
GET /api/v1/identity/authorization-tree
```

响应按模块/页面组织，页面包含：

```json
{
  "id": "identity.users",
  "title": "用户管理",
  "permissionCode": "identity.users.read",
  "actions": [
    {
      "id": "identity.users.reset-password",
      "name": "重置密码",
      "permissionCode": "identity.users.reset_password",
      "order": 50
    }
  ],
  "children": []
}
```

客户端只消费结构化目录，不接受服务端组件路径、HTML、脚本或任意 URL。

### 4.2 授权不变量

现有 `PUT /api/v1/identity/roles/{roleId}/permissions` 继续提交精确权限码集合和角色版本。服务端在写入前：

1. 规范化并按 ordinal 排序；
2. 拒绝空白、重复、未知、不可分配和作用域不匹配的权限；
3. 每个操作权限必须同时包含其页面 `RequiredPermission`；
4. 系统角色、超级管理员和乐观并发保护保持不变；
5. 提交后撤销受影响用户会话/权限快照，不能依赖长 TTL 等待生效。

数据库继续使用 `fn_identity_role_permission(RoleId, PermissionCode)`，不新增角色按钮表。页面和操作是代码目录投影，权限码是持久化事实。

## 5. Vue 行为

### 5.1 统一权限门

Vue 提供响应式 `PermissionGate` 组件和 `usePermission` 组合函数：

```vue
<PermissionGate code="identity.users.reset_password">
  <el-button @click="resetPassword(user)">重置密码</el-button>
</PermissionGate>
```

无权限时默认 slot 不渲染。权限数据缺失、会话未恢复、权限码未知时都返回 false。不得默认放行，不得只用 `disabled` 或 CSS 隐藏。

页面路由继续由服务端导航和本地组件白名单控制；页面内部业务操作必须使用目录中的权限码。权限变更后，Session 快照更新会使权限门响应式收敛。

### 5.2 角色授权页

`RolesView.vue` 从授权树 API 加载目录，使用树形复选框展示页面和操作：

- 勾选操作自动勾选页面；
- 取消页面清除所有后代操作；
- 半选只用于显示，不作为隐式授权；
- 提交精确叶子与页面权限集合；
- 未知已存权限显示受控错误并阻止覆盖，避免静默丢失未来权限；
- 保存成功后刷新角色版本和当前会话权限。

## 6. 存量权限迁移

第一个迁移切片以 Identity Users 为样板。实施时必须重新检查迁移目录；截至本设计基线，`053_DocumentHostFoundation` 已存在，候选号为成对 `054_IdentityUserActionPermissions`，若任一 Provider 已占用 054 必须停止并重新协调，禁止改写已发布迁移。

迁移将每个持有 `identity.users.write` 的角色展开为：

```text
identity.users.create
identity.users.update
identity.users.assign_roles
identity.users.reset_password
identity.users.disable
identity.users.enable
```

迁移同时规范化 `fn_identity_api_key.PermissionsJson` 中的旧权限，保持现有 API Key 等价能力。完成展开后移除旧行；应用目录不再把 `identity.users.write` 视为可授予权限。迁移必须可从半完成状态重跑，SQL Server/MySQL 分别验证重复、混合新旧权限、空集合和无关权限保持。

后续模块按相同策略独立迁移；不得在单个迁移中猜测并扩展全部模块权限。

## 7. Layui 冻结决策

`ui/admin-layui`、Layui E2E 和生成器 Layui 模板停止新增。历史成果与测试可暂时保留为冻结基线，但：

- 新授权树 API 不要求 Layui 消费；
- 新操作权限不要求补写 Layui 按钮；
- Layui 不参与本能力 `Verified`；
- 公共契约演进造成的 Layui 不兼容进入退役债务，不反向限制 Vue/服务端正确设计；
- 只有明确授权的安全、许可、迁移辅助或退役切片可以修改 Layui。

## 8. 代码生成与全模块推广

代码生成器只为 Vue 业务操作生成权限绑定。每个生成的写 Endpoint 必须使用独立动作权限；CRUD 默认动作集为 `read/create/update/delete`，启禁用、导入、导出、审批、发布、回滚等只在 Schema 明确声明时生成。

存量推广顺序：

1. Identity Users 样板；
2. Identity Roles/Menus/API Keys/Sessions/Super Administrators；
3. Tenancy 与 Organization；
4. Settings、Auditing、Files、Notifications、Jobs、CodeGeneration；
5. Document 及后续 Admin.NET 吸收模块；
6. 架构门禁切换为禁止新增多动作 `*.write`，并逐步清零旧权限。

每个资源是独立纵向切片，必须同时完成权限目录、Endpoint、存量角色/API Key 迁移（如需）、Vue 按钮、OpenAPI、双库 Integration 和 Vue E2E。

## 9. 验收场景

1. 仅有页面读取权限：用户能进入页面，看不到任何未授权业务按钮；
2. 单独授予一个动作：只出现该按钮，其他按钮不进入 DOM；
3. 手工调用未授权动作 API：返回 `403` 和稳定 `authorization.permission_denied`；
4. 撤销权限：会话/快照失效后按钮消失，直接 API 同步拒绝；
5. 角色授权树能分别选择页面和每个业务动作，父子不变量由前后端同时保护；
6. 未知权限、未知操作、孤立页面和未声明授权 Endpoint 在启动或架构测试阶段失败；
7. SQL Server/MySQL 对角色和 API Key 的存量权限展开结果等价且可恢复；
8. 超级管理员拥有全部已知动作但仍受会话、作用域、审计和最后一名保护；
9. Vue 通过权限、租户、错误处理、可访问性和真实栈 E2E 后即可进入 `Verified`，不等待 Layui。

## 10. 关联文档

- [客户端与前端规则](../../../rules/client-frontend.md)
- [开发质量规则 R-20260802-admin-action-authorization](../../../rules/development-quality.md#r-20260802-admin-action-authorization后台页面与业务操作必须端到端精确授权)
- [客户端交付路线图](../../roadmap/client-delivery-roadmap.md)
- [Admin.NET 设计吸收计划](../plans/2026-07-30-adminnet-design-absorption-program.md)
- [实施计划](../plans/2026-08-02-vue-action-authorization.md)
