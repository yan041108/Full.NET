# Organization–Identity 用户目录边界验证记录

- 日期：2026-07-26
- 分支：`main`
- 状态：Build-verified；完整 Integration 门禁通过
- 范围：用户-机构、用户-职位只读投影与 Identity Host 用户目录

## 已实现边界

1. Organization 的六条隶属查询只读取自有 assignment、unit 和 position 表，
   不再直接联接或引用 `fn_identity_user`。
2. Identity Contracts 新增独立 `IHostUserDisplayDirectory`，不改变既有
   `IHostUserDirectory`，避免破坏外部活动用户校验实现。
3. Identity 通过 `identity.list_host_users_by_ids` 一次读取当前页面的去重用户集合；
   SQL 显式限制 `ScopeKey = 'host'` 与 `TenantId IS NULL`，并允许租户上下文调用。
4. 批量显示目录包含禁用用户，保持历史隶属关系的显示语义；写入路径仍通过原有
   active-only 目录拒绝不存在或禁用的用户。
5. Organization 列表每页只增加一次 Identity 查询，不产生 N+1；缺失用户保持旧
   `INNER JOIN` 行为：列表省略该行，详情返回原有 not-found。
6. 跨模块表访问债务由 **5 条降至 4 条**，Organization 生产源码中的
   `fn_identity_user` 引用降为 **0**。

## 测试先行与审查闭环

1. 从精确债务登记删除 Organization→Identity 条目后，Architecture 门禁按预期报告
   `OrganizationSql.cs` 对 `fn_identity_user` 的未登记访问。
2. 批量目录测试先因 `HostUserDirectoryRecord` 和批量方法不存在而编译失败；
   最小实现后焦点 Unit **5/5**、Architecture **43/43**。
3. 初次测试替身使用 NSubstitute 时，内部泛型行类型触发 Castle 代理可见性异常；
   根因是测试框架代理限制，改为最小手写 `IQueryExecutor`，没有扩大生产类型可见性。
4. 架构自审发现“给既有公共接口增加方法”会破坏外部实现者，最终改为独立
   `IHostUserDisplayDirectory`，并重新通过焦点门禁和双库 API。

## 验证证据

| 门禁 | 结果 |
| --- | --- |
| Release Build | **0 warnings / 0 errors** |
| Unit 全量 | **365/365** |
| Compatibility | **7/7** |
| Identity batch directory + module graph Unit | **5/5** |
| Architecture | **43/43** |
| Naming | **23/23** |
| 项目 Skill 契约 | **52** 项通过 |
| User-unit / User-position SQL Server/MySQL | **4/4**，失败 **0** |
| Integration 全量 | **172/172**，失败 **0**、跳过 **0**，**26m 05s** |

## 状态与范围边界

- 本轮没有新增数据库对象、迁移、服务、传输协议、缓存或项目。
- HTTP/JSON、权限、租户过滤、分页、排序和写入事务语义不变。
- 剩余 4 条跨模块 SQL 仍是精确登记的迁移债务，不代表允许新增同类访问。

## 治理复盘

- Rules：现有模块边界、公共契约兼容、Dapper、租户 SQL Scope 与双库规则覆盖本次结论；
  不新增近义规则。
- Skills：`fullnet-module-delivery` 已覆盖批量目录 Port、Dapper 和双库验证；
  本轮只同步 Unit 数量，不新增或演进项目 Skill。
