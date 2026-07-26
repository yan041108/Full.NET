# Host 工作台指标边界验证记录

- 日期：2026-07-26
- 分支：`main`
- 状态：Build-verified；Host 工作台双库场景通过
- 范围：Identity 聚合 Host 工作台，Tenancy/Auditing 所有者指标 Port

## 已实现边界

1. Identity 的 Host 工作台 SQL 只保留 Identity 自有用户与刷新会话表统计。
2. Identity.Contracts 新增租户指标与审计指标两个消费者定义的只读 Port；未改变既有 HTTP/JSON 响应。
3. Tenancy 通过 `IHostDashboardTenantMetricsReader` 使用自有 `CountActiveTenants` SQL。
4. Auditing 通过 `IHostDashboardAuditMetricsReader` 使用自有访问日志与操作日志 SQL；当日请求数和错误率合并为一次查询，最近活动仍按 `OccurredAtUtc DESC, Id DESC` 读取五条。
5. 两个所有者实现采用枚举注册，未产生 Identity→Tenancy/Auditing 项目依赖或模块依赖环；精简配置缺少贡献者时返回零值与空活动。
6. 精确跨模块表访问债务由 **3 条降至 0**；Identity 对三张外部表的生产源码引用为 **0**。

## 测试先行与调试证据

删除最后三条债务登记、尚未迁移 SQL 时，所有权门禁按预期同时报告：

```text
identity -> fn_auditing_access_log @ HostDashboardSql.cs
identity -> fn_auditing_operation_log @ HostDashboardSql.cs
identity -> fn_tenancy_tenant @ HostDashboardSql.cs
```

迁移到所有者 Port 后，Architecture 恢复 **43/43**，债务登记为空。

新增 Unit 回归同时覆盖完整贡献者聚合与缺少贡献者的零值/空活动退化。双库 API 验收又增加七次操作，锁定最近活动恰为五条且按时间倒序。

排序断言首次运行在 SQL Server/MySQL 均失败。失败输出显示实际返回顺序正确，根因是 MSTest `IsGreaterThanOrEqualTo` 的参数顺序为“下限、实际值”，测试表达式写反；只调整断言参数后双库 **2/2** 通过，生产 SQL 未修改。

## 验证证据

| 门禁 | 结果 |
| --- | --- |
| Release solution build | **0 warnings / 0 errors** |
| Unit 全量 | **366/366** |
| Compatibility 全量 | **7/7** |
| Architecture 全量 | **43/43** |
| Naming | **23/23** |
| 项目 Skill 契约 | **52** 项通过 |
| Host 工作台 SQL Server/MySQL API 场景 | **2/2**，失败 **0**，耗时 **50.021s** |
| 精确跨模块表访问债务 | **0** |
| Identity 外部表引用扫描 | 三张目标表均为 **0** |

此次变更局限于 Host 工作台聚合路径，公共响应、鉴权、迁移和共享基础设施均未改变；使用新鲜的双库聚焦场景替代重复执行约 26 分钟的全量 Integration，仓库全量门槛保持 **172**。

## 架构审查

独立只读审查未发现 Critical 问题，并确认：

- 消费者定义 Port、所有者 SQL、模块/项目依赖方向与精简配置退化设计成立。
- SQL Server/MySQL 分页语法、UTC 日界线、五条限制与稳定排序保持。
- 审查提出的两个 Important 项均已关闭：增加精简配置 Unit 回归，并补齐本验证记录与新鲜规范门禁。
- 审查提出的活动上限/排序覆盖已并入双库场景。

## 治理复盘

- Rules：现有跨模块表所有权、消费者 Port、项目拆分门禁、Dapper、Host 数据范围、双库验证和公共契约兼容规则完整覆盖本次演进；没有新的重复遗漏需要升级为规则。
- Skills：`fullnet-module-delivery` 已覆盖本次模块边界、SQL 所有权、双库和文档闭环；只同步 Unit 门槛，不新增或演进项目 Skill。
- 文档分层：实施步骤位于 `docs/superpowers/plans/`，本文件记录新鲜事实；没有新增架构决策，强化模块化单体基线不变。
