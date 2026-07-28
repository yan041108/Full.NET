# Auditing Contains 显式时间边界验证

- 日期：2026-07-28
- 工作分支：`codex/performance-hardening`
- 基线提交：`1833c3ce7a2bdc63c42b449702e26f869efbd3c0`
- 范围：Access/Operation/Exception 查询契约、OpenAPI、共享客户端、Vue/Layui、
  SQL Server/MySQL 真实 API 与 100,000 行受控基准

## 1. 结果

Task 21 已把审计 contains 从“可无界调用”改为显式服务端契约：

- 规范化后存在 `pathContains` 或 `exceptionTypeContains` 时，必须同时提供
  `fromUtc`/`toUtc`；
- 反向时间返回 `auditing.query.time_range_invalid`；
- 超过 `Auditing:Query:MaximumContainsWindowDays` 返回
  `auditing.query.contains_time_range_exceeded`；
- 缺少边界返回 `auditing.query.contains_time_range_required`；
- 普通列表与空白 contains 保持兼容；
- 默认生产上限为 1 天，配置允许范围 `1..31` 且启动期校验；放宽前必须复跑当前数据规模
  和 Provider；
- Vue/Layui 首次启用 contains 时显示最近 24 小时范围，游标加载更多保持同一筛选；
  清空由客户端自动生成的范围时不会把普通列表静默变成时间筛选。

服务端在规范化后、数据库调用前复用同一策略。现有参数化 SQL、Host-only Scope、
SQL Server 查询形状和 MySQL 固定 Statement 保持不变；没有迁移、索引、动态 SQL、
缓存或 OFFSET 调整。

## 2. RED/GREEN

| 阶段 | 证据 |
| --- | --- |
| 服务端 RED | 新策略测试先因 `AuditingContainsTimeRangePolicy` 不存在而编译失败 |
| 服务端 GREEN | 策略、Options 启动校验、三类查询接入及双语 ProblemDetails 资源完成后，Auditing 聚焦 Unit **23/23** |
| 共享客户端 RED | 24 小时默认函数不存在，新增用例 **2** 项按预期失败 |
| 共享客户端 GREEN | 访问日志契约聚焦 **6/6** |
| Vue RED/GREEN | 新筛选控件与游标参数保持先失败；实现后 API + View 聚焦 **5/5** |
| Layui RED/GREEN | 新筛选控件与清空自动范围先失败；实现后聚焦 **3/3** |
| 双库 API | SQL Server/MySQL 的三类 Auditing API 共 **6/6**，失败 0、跳过 0 |

双库 API 覆盖普通列表、缺少范围、超窗、合法 contains、Access cursor 下一批、游标筛选
不匹配及三类 OpenAPI 查询参数/400 响应。

## 3. 100,000 行候选选择

固定条件：

- 100,000 行、30 天确定性时间分布、10% 路径命中；
- 页面 50、并发 1、每场景预热 5 次、采样 30 次；
- SQL Server 2022 CU14、MySQL 8.0.46；
- 本机 Windows 10、20 逻辑核、Docker Desktop Testcontainers；
- 完整原始报告与执行计划位于未提交本地工件
  `.tmp/auditing-query-task21-v1/` 和 `.tmp/auditing-query-task21-v2/`。

运行命令：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release --no-build -- audit-query --mode baseline `
  --providers sqlserver,mysql --rows 100000 --warmup 5 --iterations 30 `
  --output .tmp/auditing-query-task21-v2
```

### 3.1 被拒绝的 31 天候选

31 天覆盖整个 30 天数据集，不能形成有效默认边界。

| Provider | 场景 | P50 ms | P95 ms | P99 ms | 命中总数 |
| --- | --- | ---: | ---: | ---: | ---: |
| SQL Server | 无界 | 274.414 | 350.821 | 358.030 | 10,000 |
| SQL Server | 31 天 | 270.062 | 351.110 | 400.270 | 10,000 |
| MySQL | 无界 | 61.965 | 101.826 | 102.532 | 10,000 |
| MySQL | 31 天 | 64.561 | 82.805 | 86.325 | 10,000 |

SQL Server P95 基本不变且 P99 更差，MySQL 虽有波动改善但仍扫描同一业务集合。因此拒绝
31 天作为生产默认值，不能仅以“存在时间参数”宣称 contains 已硬化。

### 3.2 保留的 1 天默认

| Provider | 场景 | P50 ms | P95 ms | P99 ms | 命中总数 |
| --- | --- | ---: | ---: | ---: | ---: |
| SQL Server | 无界 | 314.649 | 418.816 | 426.864 | 10,000 |
| SQL Server | 1 天 | 24.824 | 37.180 | 45.491 | 333 |
| MySQL | 无界 | 71.631 | 100.142 | 115.793 | 10,000 |
| MySQL | 1 天 | 13.205 | 16.484 | 19.210 | 333 |

相对同轮无界 contains：

- SQL Server P95 改善 **91.12%**，P99 改善 **89.34%**；
- MySQL P95 改善 **83.54%**，P99 改善 **83.41%**。

这些值只证明本机受控实验中的相对差异，不是生产 SLA，也不允许跨 Provider 用绝对值排名。

## 4. 执行计划

- SQL Server 两个 COUNT 计划仍为聚集索引扫描，`ActualRowsRead = 100000`、
  `ActualLogicalReads = 2348`；1 天谓词把 contains 命中输出从 10,000 降至 333，
  但没有把该模式变成 Seek。该证据禁止继续宣称普通 B-tree 已解决前后通配搜索。
- MySQL 无界 COUNT 为 `access_type = ALL`，每轮估计检查 99,216 行；1 天范围使用
  `IX_fn_auditing_access_log_OccurredAtUtc_Id` 的 `range`，估计检查 3,334 行，
  `LOCATE` 仍作为附加条件。
- 若数据量、保留期或业务所需窗口使 1 天仍越预算，下一步是专用搜索设施 Decision Gate，
  不是盲加 B-tree、深 OFFSET 微调或静默截断。

## 5. 结论与后续

Task 21 关闭了无界 contains 的公共入口，并用否定/肯定两轮证据选择 1 天安全默认。
`MaximumContainsWindowDays` 仍允许显式运维调整到 31 天，但放宽不等于已验证；生产变更前
必须用本任务命令重跑目标数据规模与两种 Provider。

下一任务按计划进入 Audit 同步写入尾延迟与可靠性分层。不得直接 fire-and-forget，
也不得从本任务查询收益推导出访问审计可以丢弃。

## 6. 最终回归矩阵

| 门禁 | 结果 |
| --- | --- |
| Release build | `Full.NET.slnx` 成功，0 警告、0 错误 |
| 后端静态测试 | Unit **484/484**、Compatibility **7/7**、Architecture **49/49** |
| Auditing 双库聚焦 | SQL Server/MySQL 三类 API **6/6** |
| Integration 全量 | 首轮 **192/193**，唯一失败为未修改 Identity MySQL 用例的瞬时 Broken 连接；相同用例隔离重跑 **1/1**，无代码改动的第二轮全量 **193/193**（36m08s） |
| 前端测试 | client-contracts **82/82**、Vue **213/213**、Layui **103/103**、admin-i18n **8/8** |
| 前端构建 | client-contracts、Vue typecheck/build、Layui build、admin-i18n build 全部成功 |
| 契约与治理 | OpenAPI **58/58**、兼容检查 25→25；Governance **11/11**、Naming **23/23**、SQL safety **5/5**、Localization **7/7**、Performance governance **3/3** |
| Integration 分片 | api-sqlserver **35**、api-mysql **35**、migrations **62**、infrastructure **61**，合计 **193**，无遗漏或重复 |
| Skills 与包体 | module-delivery **52**、performance-hardening **33**；Vue/Layui 各包体预算通过 |

首轮全量失败的调用链显示业务请求先使 MySQL 连接进入 `Broken`，随后既有事务回滚异常遮蔽
原始异常；本任务未修改 Dapper 事务或 Identity 登录。隔离复现与第二轮全量均通过，因此本
任务不引入猜测性的重试、超时或异常吞噬修补。该现象保留为后续 Integration 稳定性与事务
异常保真度的独立调查输入。

规则与 Skills 遗漏复盘未发现新模式：现有性能证据、双库、公共契约、双管理端与完成门禁
已覆盖本任务。除随 canonical Unit 门槛从 475 更新为 484 外，不新增或演进规则与项目
Skill。
