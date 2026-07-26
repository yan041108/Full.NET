# Identity–Organization 数据范围 Port 验证记录

- 日期：2026-07-26
- 分支：`main`
- 状态：Build-verified；双数据库完整 Integration 门禁通过
- 范围：Identity 角色数据范围组合、Organization 自有表 SQL 投影、精简宿主装配

## 已实现边界

1. Identity Contracts 定义消费方 Port
   `IIdentityOrganizationDataScopeSqlProjection`；Organization 在同一进程内实现
   `self`、`organization` 和 `organization_subtree` 三类机构范围，不引入网络边界。
2. Identity 继续拥有角色范围解析、多角色并集、拒绝全部及
   `fn_identity_role_data_scope_unit` 自定义范围；其生产代码不再出现
   `fn_organization_unit` 或 `fn_organization_user_unit`。
3. Organization 适配器拥有两张机构表的参数化 SQL，保留
   `@TenantId`、`@DataScopeUserId`、活动状态与主机构约束。
4. Identity 通过可选的唯一 Port 集合装配，使不包含 Organization 的精简宿主仍可启动；
   只有实际请求 Organization 范围而未装配适配器时才显式失败。
5. 跨模块表访问债务由 **7 条降至 5 条**，删除的两条债务均已由架构测试证明不再存在。

## 测试先行与缺陷闭环

1. 先从精确债务登记删除两条 Identity→Organization 访问，架构测试按预期报告
   `RoleDataScopeProjection.cs` 中两处未登记表访问。
2. 先把投影测试改为期望消费方 Port，编译按预期因接口不存在、静态投影不可注入而失败；
   随后以最小实现恢复焦点 Unit 与 43 项 Architecture 门禁。
3. 首轮真实 SQL Server/MySQL 机构范围测试发现 `Self` 查询中的未限定 `Id`
   在子查询内绑定为 `assignment.Id`，导致条件退化为
   `assignment.UnitId = assignment.Id`。改为外层列
   `IN (SELECT assignment.UnitId ...)` 后，双库焦点测试 **2/2** 通过。
4. 首轮完整 Integration 发现 Identity/Tenancy 精简宿主未装配 Organization Port；
   改为可选集合发现后，原失败的 SQL Server/MySQL TenantProvisioning 焦点测试
   **2/2** 通过。

## 验证证据

| 门禁 | 结果 |
| --- | --- |
| Release Build | **0 warnings / 0 errors** |
| Unit | **364/364** |
| Architecture | **43/43** |
| Compatibility | **7/7** |
| Naming | **23/23** |
| 项目 Skill 契约 | **52** 项通过 |
| Organization Data Scope SQL Server/MySQL | **2/2** |
| TenantProvisioning SQL Server/MySQL | **2/2** |
| Integration 全量 | **172/172**，失败 **0**、跳过 **0**，**26m 32s** |

## 状态与范围边界

- 本轮没有新增数据库对象、迁移、项目、服务或传输协议。
- SQL Server 与 MySQL 共用相同运行时查询语义，并通过两种提供程序的真实 API 场景。
- 剩余 5 条跨模块表访问仍是精确登记的迁移债务，不代表允许新增同类访问。
- 当前只提升模块边界的 Build-verified 证据，不改变尚未完成的生产部署与运行演练状态。

## 治理复盘

- Rules：现有强化模块化单体、Dapper 边界、双数据库和测试先行规则已完整覆盖本次发现；
  两个问题均由现有门禁暴露并已建立回归证据，不新增近义规则。
- Skills：`fullnet-module-delivery` 已覆盖 Port/Adapter、Dapper、双库和验证流程；
  本轮只同步其 Unit 发现门槛，不新增或演进项目 Skill。
