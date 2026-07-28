# Performance Hardening Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 建立可重复的性能治理门禁，并消除已确认的 SQL 可观测性、Worker 满批次空等和 Vue 路由首包问题。

**Architecture:** 本阶段不改变认证撤销时效、审计持久化语义或数据库结构。Dapper 只按稳定 Statement 名称暴露低基数指标；Outbox/Jobs 保持现有租约和逐条可靠语义，仅在批次取满时立即继续领取；Vue 页面使用路由级动态导入。需要改变安全或双库数据语义的优化进入后续 RED/压测门禁。

**Tech Stack:** .NET 10、Dapper、OpenTelemetry Metrics、Microsoft Testing Platform、Vue 3、Vue Router、Vite、Vitest、Codex project skills。

## Global Constraints

- 保持 SQL Server/MySQL、租户隔离、事务 Outbox、审计可靠性和 Vue/Layui 双端基线。
- 指标只使用稳定 Statement 名称、Provider、操作类型和结果；禁止原始 SQL、路径、用户或租户高基数标签。
- 未取得双库执行计划和真实压测证据前，不修改认证缓存、审计存储、索引或分页公共契约。
- 所有行为变更先运行会失败的验证；完成前运行 Release 构建、门槛测试和 `git diff --check`。
- 本轮不自动提交、推送或发布。

---

### Task 1: 性能治理规则与项目 Skill

**Files:**
- Create: `rules/performance-engineering.md`
- Modify: `rules/README.md`
- Modify: `AGENTS.md`
- Modify: `docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md`
- Create: `tests/skills/fullnet-performance-hardening.contract.json`
- Modify: `tests/skills/validate_project_skills.py`
- Create: `.agents/skills/fullnet-performance-hardening/SKILL.md`
- Create: `.agents/skills/fullnet-performance-hardening/agents/openai.yaml`
- Create: `.agents/skills/fullnet-performance-hardening/references/performance-map.md`

**Interfaces:**
- Consumes: 现有规则演进与 Skill 契约校验入口。
- Produces: `$fullnet-performance-hardening` 项目 Skill 和性能优化的强制门禁。

- [x] **Step 1: 扩展 Skill 契约测试并加入新 Skill 契约**

使 `validate_project_skills.py` 枚举 `tests/skills/*.contract.json`，校验每个 Skill 的 frontmatter、元数据、直接引用和场景术语。

- [x] **Step 2: 运行 RED**

Run: `pnpm test:skills`

Expected: FAIL，明确报告缺少 `.agents/skills/fullnet-performance-hardening`。

- [x] **Step 3: 使用 `init_skill.py` 初始化项目 Skill**

Run:

```powershell
python -X utf8 C:\Users\Administrator\.codex\skills\.system\skill-creator\scripts\init_skill.py fullnet-performance-hardening --path .agents/skills --resources references --interface display_name="Full.NET Performance Hardening" --interface short_description="Measure and harden Full.NET performance safely" --interface default_prompt="Use $fullnet-performance-hardening to measure and improve a Full.NET hot path."
```

- [x] **Step 4: 写入最小 Skill、规则和性能地图**

Skill 必须覆盖基线、请求往返、Dapper 指标、分页、Worker、审计、安全缓存、双库执行计划、前端包体和停止条件。规则负责强制约束，Skill 负责重复执行方法。

- [x] **Step 5: 运行 GREEN 与官方校验**

Run:

```powershell
pnpm test:skills
python -X utf8 C:\Users\Administrator\.codex\skills\.system\skill-creator\scripts\quick_validate.py .agents/skills/fullnet-performance-hardening
```

Expected: 两项均 PASS。

### Task 2: Dapper 低基数性能指标

**Files:**
- Create: `tests/Full.NET.UnitTests/Data/DapperTelemetryTests.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperTelemetry.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperSqlExecutor.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Full.NET.Data.Dapper.csproj`

**Interfaces:**
- Consumes: `SqlStatement.Name`、`DatabaseProvider`、OpenTelemetry MeterProvider。
- Produces: Meter `fullnet.data.dapper`，包含执行次数、失败次数和毫秒耗时。

- [x] **Step 1: 编写指标 RED**

测试通过 `MeterListener` 断言成功和失败执行分别产生稳定名称、Provider、操作与结果标签，且没有 SQL 文本标签。

- [x] **Step 2: 运行 RED**

Run: `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~DapperTelemetryTests"`

Expected: FAIL，因为 `DapperTelemetry` 尚不存在。

- [x] **Step 3: 实现最小指标并接入执行器**

所有四种执行路径在 `finally` 记录耗时；异常路径增加失败计数。将伪异步 `CreateCommandAsync` 收敛为同步 `CreateCommand`，不改变取消和事务语义。

- [x] **Step 4: 运行 GREEN**

Run: `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~DapperTelemetryTests"`

Expected: PASS。

### Task 3: Outbox 与 Jobs 满批次立即排空

**Files:**
- Modify: `tests/Full.NET.UnitTests/Outbox/OutboxProcessorTests.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobExecutionHostedProcessorTests.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobExecutionRunnerTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionHostedProcessor.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionRunner.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobSql.cs`

**Interfaces:**
- Consumes: 现有 BatchSize、PollMilliseconds、租约与 Handler Registry。
- Produces: 满批次零等待、未满批次按配置等待；Jobs 每批一次加载 Definition。

- [x] **Step 1: 编写 RED**

断言处理数量等于 BatchSize 时下一轮延迟为零，未满时仍使用 PollMilliseconds；两个执行记录只触发一次 Definition 批量查询。

- [x] **Step 2: 运行 RED**

Run:

```powershell
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~OutboxProcessorTests|FullyQualifiedName~JobExecutionHostedProcessorTests|FullyQualifiedName~JobExecutionRunnerTests"
```

Expected: FAIL，因为处理方法不返回批次数量且 Jobs 仍逐条查 Definition。

- [x] **Step 3: 实现最小排空与批量 Definition 查询**

保持同一批次逐条执行以维护现有顺序和租约语义；只有领取循环取消固定空等。Definition 使用参数化 `IN @Ids` 一次读取并建立只读字典。

- [x] **Step 4: 运行 GREEN**

重复 Step 2 命令，Expected: PASS。

### Task 4: Vue 路由级代码拆分

**Files:**
- Create: `ui/admin/src/router/index.performance.test.ts`
- Modify: `ui/admin/src/router/index.ts`
- Create: `tests/performance/frontend-bundle-budget.test.mjs`
- Create: `tests/performance/frontend-bundle-budgets.json`
- Create: `scripts/testing/check-frontend-bundle-budget.mjs`
- Modify: `package.json`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: Vue Router 的 lazy route component 契约。
- Produces: 除首屏 Overview 外的管理页面均通过动态 `import()` 加载。

- [x] **Step 1: 编写 RED**

测试断言 `/identity/users`、`/auditing/access-logs` 等非首屏路由的默认组件是异步加载函数。

- [x] **Step 2: 运行 RED**

Run: `pnpm --filter @fullnet/admin test -- src/router/index.performance.test.ts`

Expected: FAIL，因为页面当前静态导入。

- [x] **Step 3: 改为路由级动态导入**

保留 Overview 首屏静态加载；Status 和其余业务页面使用显式动态 import，保持 route name/path/权限守卫不变。

- [x] **Step 4: 运行 GREEN 与生产构建**

Run:

```powershell
pnpm --filter @fullnet/admin test -- src/router/index.performance.test.ts
pnpm --filter @fullnet/admin build
```

Expected: 测试和构建通过，业务页面产生独立 chunk，并记录主 chunk 的 gzip 大小变化。

- [x] **Step 5: 固化 Vue/Layui 首屏静态 JavaScript 相对预算**

先用 Node 契约测试锁定“只递归同步 import、不把动态 import 计入首屏”的依赖图算法，再以当前同环境构建产物为基线，允许最多 5% 相对退化；CI 在客户端构建后运行预算门禁。

### Task 5: 验证、记录与后续停止条件

**Files:**
- Create: `docs/verification/performance-hardening-foundation-2026-07-27.md`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`

**Interfaces:**
- Consumes: 新鲜构建、测试、包体预算和双库 Integration 输出。
- Produces: 可审查验证记录和同步后的 canonical 测试门槛。

- [x] **Step 1: 更新测试数量门槛**

新增 Unit 测试后同步四个 canonical 来源和最新门槛审计，不降低任何门槛。

- [x] **Step 2: 执行全量验证**

Run:

```powershell
pnpm test:skills
pnpm test:governance
pnpm test:naming
pnpm test:performance-governance
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin-layui test
pnpm --filter @fullnet/admin build
pnpm --filter @fullnet/admin-layui build
pnpm test:bundle-budgets
dotnet build Full.NET.slnx -c Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 454
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 7
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 49
pnpm test:integration:full
```

Expected: 全部 PASS。

- [x] **Step 3: 保留后续性能变更停止条件**

认证缓存、审计异步化/留存、分页契约和双库索引只有在 SQL Server/MySQL 真实数据规模、P50/P95/P99、执行计划与失败恢复基线齐全后进入独立计划；当前阶段不得据静态推断宣称已提升生产 QPS。

- [x] **Step 4: 最终仓库检查**

Run:

```powershell
git diff --check
git status --short --branch
```

Expected: 无空白错误，差异仅属于本计划和用户原有未跟踪目录。

### Task 6: Layui 路由控制器代码拆分

**Files:**
- Create: `ui/admin-layui/js/core/route-controllers.js`
- Create: `ui/admin-layui/tests/route-controllers.test.js`
- Modify: `ui/admin-layui/js/app.js`
- Modify: `tests/performance/frontend-bundle-budgets.json`
- Modify: `docs/verification/performance-hardening-foundation-2026-07-27.md`

**Interfaces:**
- Consumes: Layui 本地路由、现有业务控制器工厂和首屏静态依赖图预算。
- Produces: 业务控制器按首次进入路由动态导入；同一路由并发触发共享一次加载；路由过期和应用卸载竞态不启动数据请求。

- [x] **Step 1: 编写并运行注册表 RED**

Run: `pnpm --filter @fullnet/admin-layui test -- tests/route-controllers.test.js`

Observed: FAIL，测试无法解析尚不存在的 `core/route-controllers.js`。

- [x] **Step 2: 实现最小注册表并替换静态控制器**

23 个业务控制器改为显式动态 import；Shell、Session、导航、Realtime 等首屏基础能力保持静态。注册表负责导入/实例化去重、路由有效性检查与统一释放。

- [x] **Step 3: 运行 GREEN、双端回归和生产构建**

Run:

```powershell
pnpm --filter @fullnet/admin-layui test
pnpm --filter @fullnet/admin-layui build
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin build
```

Observed: Layui **98/98**、Vue **205/205**；两端生产构建通过。Layui 生成 23 个业务控制器路由 chunk。

- [x] **Step 4: 下调并验证包体预算**

Layui 首屏静态 JavaScript 从 `691484/192765` bytes 降至 `592147/180170` bytes（minified/gzip），分别下降约 **14.37%/6.53%**。将新值设为预算基线后运行 `pnpm test:bundle-budgets`，Vue/Layui 均为 `+0.00%`。

### Task 7: Vue ECharts 延迟 chunk 精简

**Files:**
- Modify: `ui/admin/src/framework/art-design/charts/echarts.ts`
- Modify: `ui/admin/src/framework/art-design/charts/FullNetChart.test.ts`
- Modify: `scripts/testing/check-frontend-bundle-budget.mjs`
- Modify: `tests/performance/frontend-bundle-budget.test.mjs`
- Modify: `tests/performance/frontend-bundle-budgets.json`

**Interfaces:**
- Consumes: 工作台当前折线图 Option 与 ECharts 模块化注册入口。
- Produces: 只注册 Line/Grid/Tooltip/Canvas；对延迟加载的 `FullNetChart-*` 资产执行独立相对预算。

- [x] **Step 1: 建立并运行双 RED**

`pnpm test:performance-governance` 因缺少延迟资产测量函数失败；图表聚焦测试明确观测到额外的 Bar/Pie 注册。

- [x] **Step 2: 精简注册集合并扩展预算脚本**

删除当前没有真实 Option 消费者的 Bar、Pie、Title、Legend 与 Dataset 注册；预算脚本按稳定资产前缀匹配单个延迟 chunk，并继续使用相同 Node gzip 口径。

- [x] **Step 3: 生产构建并下调预算**

ECharts 延迟 chunk 从 `559016/187747` bytes 降至 `496501/166669` bytes（minified/gzip），分别下降约 **11.18%/11.23%**，Vite 不再产生 Vue 大 chunk 告警。新值设为 5% 相对退化基线。

### Task 8: Layui 运行库延迟到会话恢复和首个绘制之后

**Files:**
- Create: `ui/admin-layui/js/core/layui-runtime.js`
- Create: `ui/admin-layui/tests/layui-runtime.test.js`
- Create: `ui/admin-layui/tests/main-performance.test.js`
- Modify: `ui/admin-layui/js/main.js`
- Modify: `ui/admin-layui/js/app.js`
- Modify: `ui/admin-layui/tests/app.test.js`
- Modify: `tests/performance/frontend-bundle-budgets.json`
- Modify: `rules/performance-engineering.md`
- Modify: `.agents/skills/fullnet-performance-hardening/SKILL.md`

**Interfaces:**
- Consumes: 会话恢复 Promise、浏览器下一绘制调度、现有 Layui 语言同步与组件刷新入口。
- Produces: 不阻塞会话恢复和首个可见界面的 Layui 渐进增强；首屏静态图和延迟运行库分别受预算约束。

- [x] **Step 1: 建立并运行 RED**

聚焦测试锁定运行库调度顺序、失败隔离、`app.enhanceLayui()` 契约和 `main.js` 禁止静态导入。初次运行因运行时调度模块不存在、应用入口未暴露增强能力而失败。

- [x] **Step 2: 实现会话恢复后的下一绘制延迟加载**

移除 `main.js` 对完整 Layui JavaScript 的静态导入；会话恢复无论成功或失败，都先让首个界面绘制，再动态导入运行库并同步语言、刷新组件。CSS 仍静态加载，运行库失败只影响渐进增强。

- [x] **Step 3: 运行回归、构建与预算**

Layui 全量 **101/101**，Vue 全量 **206/206**，两端生产构建通过且无大 chunk 告警。Layui 首屏静态图为 `198567/54392` bytes，延迟运行库为 `394758/126525` bytes，两者分别建立 5% 相对预算。

- [x] **Step 4: 执行受控浏览器 A/B**

本地 Chromium、冷缓存、4 倍 CPU 降速、固定 API `401`，基线和候选交替各 10 次。候选 FCP 中位数 `238.0 ms`，相对基线 `228.0 ms` 未改善；DOMContentLoaded、Load 和主脚本 resource duration 中位数分别下降 `41.03%`、`30.69%`、`40.68%`。运行库中位开始时间 `943.05 ms`，晚于 FCP。该证据只支持本地加载顺序和相对变化，不外推生产。

- [x] **Step 5: 演进规则与 Skill**

ECharts 和 Layui 连续暴露出“只约束首屏会掩盖延迟资产回涨”的重复风险，因此通过 RED 契约将“首屏静态依赖图和大体积延迟 chunk 分别预算”升级到性能规则和项目 Skill。

### Task 9: 审计分页合并数据库往返

**Files:**
- Create: `tests/Full.NET.UnitTests/Auditing/AuditingPagedQueryRoundTripTests.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostAccessLogs/HostAccessLogQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostOperationLogs/HostOperationLogQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostExceptionLogs/HostExceptionLogQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Persistence/AccessLogSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Persistence/OperationLogSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Persistence/ExceptionLogSql.cs`

**Interfaces:**
- Consumes: 现有筛选、稳定排序、OFFSET 分页与 `IMultiResultQueryExecutor`。
- Produces: 三类审计列表在一次数据库命令中顺序返回总数和当前页；详情查询、公共 API 和分页响应保持不变。

- [x] **Step 1: 建立并运行 RED**

SQL Server/MySQL 各覆盖访问、操作、异常日志，断言列表只调用一次多结果执行器，并保持总数、页码、页大小和行映射。RED 因三个服务没有三参数构造函数而编译失败。

- [x] **Step 2: 合并 COUNT 与列表 SQL**

保留原有 Provider SQL、参数、过滤和 `(OccurredAtUtc DESC, Id DESC)` 排序，通过单个 `SqlStatement` 顺序执行两个结果集；详情仍使用普通查询执行器。

- [x] **Step 3: 运行单元与双库验证**

单元聚焦 **8/8**、Unit 全量 **427/427**；SQL Server/MySQL 审计 API 各 **3/3**，失败 0、跳过 0。可确认数据库命令往返由每个列表请求 2 次降为 1 次；没有生产等价延迟样本，不声明 P95/P99 提升。

- [x] **Step 4: 保留后续停止条件**

contains 搜索、深 OFFSET、游标分页和索引调整仍需代表性数据量、双库执行计划、写放大和 API 兼容设计，不在本阶段猜测性修改。

### Task 10: 审计大表双库基准与执行计划证据

**Files:**
- Modify: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingQueryBenchmarkOptions.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingQueryScenarios.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingQuerySql.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingQueryStatistics.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingQueryBenchmarkRunner.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingBenchmarkDatabase.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingQueryReportWriter.cs`
- Create: `tests/Full.NET.UnitTests/Auditing/AuditingQueryBenchmarkTests.cs`
- Modify: `tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj`
- Modify: `docs/verification/performance-hardening-foundation-2026-07-27.md`
- Modify: Unit canonical threshold sources

**Interfaces:**
- Consumes: 已发布迁移、SQL Server/MySQL Testcontainers、当前 Access Log `COUNT + list` SQL。
- Produces: `audit-query` 显式基准子命令、确定性数据集、P50/P95/P99 摘要、双库执行计划和可审查验证结论。

- [x] **Step 1: 建立基准契约 RED**

新增测试，断言默认 `100000` 行、预热 `5` 次、采样 `30` 次、并发 `1`；百分位采用最近秩；四个场景固定为 `first_page`、`deep_offset`、`contains_unbounded`、`contains_bounded`；基准 SQL 规范化跨平台换行后必须与 `AccessLogSql.PageFilteredSqlServer/MySql` 完全相同。

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~AuditingQueryBenchmarkTests"
```

Expected: FAIL，因为基准参数、场景、统计和 SQL 类型尚不存在。

- [x] **Step 2: 实现显式命令、双库准备与采样**

`Program.cs` 仅在首参数为 `audit-query` 时调用基准 Runner，其余参数继续交给 BenchmarkDotNet。Runner 使用官方迁移器创建真实表，以应用端 UUID v7 和固定分布批量写入；每个样本顺序执行并完整消费总数与页面结果集。

Run:

```powershell
dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~AuditingQueryBenchmarkTests"
```

Expected: Build PASS，聚焦测试 PASS。

- [x] **Step 3: 采集双库执行计划和统计工件**

SQL Server 对 count/list 分别采集 `SET STATISTICS XML ON` 实际执行计划；MySQL 对相同语句采集 `EXPLAIN FORMAT=JSON`。输出目录包含 `summary.json`、`README.md` 及每个 Provider/场景的计划文本。

Run:

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --rows 100000 --warmup 5 --iterations 30
```

Expected: 两个 Provider 各得到四个场景的 P50/P95/P99，所有样本成功且计划文件非空。

- [x] **Step 4: 基于证据决定是否继续改 SQL**

只有双库计划和统计共同证明深 OFFSET 或 contains 是主要成本，且候选方案能保留 API/租户/排序语义并量化写放大时，才新增独立索引或游标分页计划。本 Task 默认停止于证据，不直接变更迁移或公共分页契约。

- [x] **Step 5: 同步验证记录与完成门禁**

记录环境、数据分布、样本参数、P50/P95/P99、计划摘要、限制和下一步决策；同步 Unit 门槛，运行 Release 构建、全量 Unit/Compatibility/Architecture、性能治理、Skills、双库审计聚焦、`git diff --check` 与规则/Skill 复盘。

### Task 11: SQL Server 可选谓词计划稳定性 A/B

**Files:**
- Modify: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingQueryBenchmarkOptions.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingSqlServerQueryFactory.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingSqlServerPlanMetrics.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingSqlServerAbBenchmarkRunner.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingSqlServerAbReportWriter.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingBenchmarkDatabase.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Modify: `tests/Full.NET.UnitTests/Auditing/AuditingQueryBenchmarkTests.cs`
- Modify: `docs/verification/performance-hardening-foundation-2026-07-27.md`
- Modify: Unit canonical threshold sources

**Interfaces:**
- Consumes: Task 10 的 100,000 行确定性数据、`first_page`/`contains_bounded` 场景、SQL Server `STATISTICS XML`。
- Produces: 显式 `--mode sqlserver-plan-ab` 基准；`current_optional`、`branch_specific`、`recompile` 三种策略在 `broad_first`/`bounded_first` 两种混合顺序下的 P50/P95/P99、编译 CPU/时间、逻辑读和实际读行证据。

- [x] **Step 1: 建立查询策略、顺序和计划指标 RED**

新增单元测试，断言 A/B 模式只接受 SQL Server；两个顺序互换首次编译场景；分支 SQL 只保留实际筛选条件；`OPTION (RECOMPILE)` 同时应用于 count/list；ShowPlan XML 能提取 `CompileTime`、`CompileCPU`、`ActualLogicalReads`、`ActualRowsRead` 和缓存命中。

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~AuditingQueryBenchmarkTests"
```

Expected: FAIL，因为 A/B mode、查询工厂、混合顺序与计划指标类型尚不存在。

- [x] **Step 2: 实现最小 A/B 查询与隔离计划缓存**

`current_optional` 复用生产 SQL；`branch_specific` 按非空参数生成参数化谓词；`recompile` 在两个 SELECT 尾部追加 `OPTION (RECOMPILE)`。每个策略/顺序组合在隔离 SQL Server 容器中执行 `DBCC FREEPROCCACHE`，按固定顺序完成预热和采样，完整消费 count/list。

- [x] **Step 3: 输出统计、实际计划和编译成本**

每个组合输出原始样本、最近秩 P50/P95/P99、总数/返回行数、count/list `STATISTICS XML` 与解析后的编译/运行指标；报告明确区分缓存策略的一次性编译与 `RECOMPILE` 每次编译，禁止把单个计划的 CompileCPU 当作总吞吐成本。

- [x] **Step 4: 运行 100,000 行正式矩阵并作决策**

Run:

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --mode sqlserver-plan-ab --providers sqlserver --rows 100000 --warmup 5 --iterations 30
```

只有候选在两种请求顺序下改善筛选场景 P95/P99，且首屏、编译 CPU、逻辑读和错误率没有不可接受退化时，才形成生产 SQL 变更 Task；否则保留现状或收窄为显式时间上界设计。

- [x] **Step 5: 同步门槛、验证记录与演进复盘**

同步 Unit canonical 门槛；运行 benchmark 聚焦、全量 Unit/Compatibility/Architecture、Release 构建、性能治理、Skills、`git diff --check`、规则复盘与 Skill 复盘。该 Task 不修改生产 SQL、迁移或公共 API。

### Task 12: 审计 SQL Server 固定谓词分支生产落地

**Files:**
- Create: `src/Modules/Full.NET.Modules.Auditing/Persistence/AuditingSqlServerPageStatementBuilder.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Persistence/AccessLogSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Persistence/OperationLogSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Persistence/ExceptionLogSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostAccessLogs/HostAccessLogQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostOperationLogs/HostOperationLogQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostExceptionLogs/HostExceptionLogQueryService.cs`
- Modify: `tests/Full.NET.UnitTests/Auditing/AuditingPagedQueryRoundTripTests.cs`
- Modify: `tests/Full.NET.UnitTests/Auditing/AuditingQueryBenchmarkTests.cs`
- Modify: `docs/verification/performance-hardening-foundation-2026-07-27.md`
- Modify: Unit canonical threshold sources

**Interfaces:**
- Consumes: Task 11 的 `branch_specific` 胜出证据、现有三类 Audit 过滤参数与单次 QueryMultiple 往返。
- Produces: SQL Server 按非空过滤参数选择缓存的固定参数化 SQL 形状；Access/Operation 最多 32 个、Exception 最多 16 个；Statement Name、HostOnly Scope、参数对象、MySQL SQL、API 和分页响应保持不变。

- [x] **Step 1: 建立 32/32/16 SQL 形状与服务接线 RED**

新增单元测试，枚举三类 SQL Server 全部形状并断言数量有界、文本唯一、只包含固定参数谓词、无 `@Value IS NULL OR`；现有三类 QueryMultiple 测试使用非空筛选，断言 SQL Server 选择直接谓词而 MySQL 仍选择原可选谓词。

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~AuditingPagedQueryRoundTripTests|FullyQualifiedName~AuditingQueryBenchmarkTests"
```

Expected: FAIL，因为三类 SQL 仍只有单个可选谓词 Statement，不存在按形状选择的生产接口。

- [x] **Step 2: 实现缓存的固定白名单 SQL 形状**

Builder 只接受模块内固定 SELECT 前缀、固定谓词数组和分页尾部，在模块初始化时预生成 `2^N` 个 `SqlStatement`；运行时只按布尔 mask 取缓存项。所有值继续通过 Dapper 参数传递，Statement Name 不包含 shape，禁止把用户输入拼入 SQL。

- [x] **Step 3: 接入三类 Query Service 并保持 MySQL**

SQL Server 根据已规范化 filter 的 null 状态选择缓存形状；MySQL 继续使用现有 `PageFilteredMySql`。单次 QueryMultiple、精确总数、稳定 `(OccurredAtUtc DESC, Id DESC)` 排序、分页参数与详情查询不变。

- [x] **Step 4: 运行双库 API 与同环境性能复测**

运行单元聚焦与全量、SQL Server/MySQL 审计 API 各 3 项；再以 100,000 行相同参数重跑 SQL Server A/B 中的生产等价分支场景，确认 SQL 文本防漂移、逻辑读和延迟没有退化。该 Task 不增加索引、迁移、游标或搜索设施。

- [x] **Step 5: 同步验证与演进复盘**

同步 Unit canonical 门槛，运行 Release 构建、Compatibility、Architecture、治理、Naming、Skills、`git diff --check`、规则复盘和 Skill 复盘；只声明受控样本支持的改进。

### Task 13: MySQL 深 OFFSET 时间索引 Hint A/B

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingMySqlQueryFactory.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingMySqlIndexAbBenchmarkRunner.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingMySqlIndexAbReportWriter.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingQueryBenchmarkOptions.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingBenchmarkDatabase.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Modify: `tests/Full.NET.UnitTests/Auditing/AuditingQueryBenchmarkTests.cs`
- Modify: `.agents/skills/fullnet-performance-hardening/references/performance-map.md`
- Modify: `docs/verification/performance-hardening-foundation-2026-07-27.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`

**Interfaces:**
- Consumes: 100,000 行确定性 Audit 数据、四个既有场景、MySQL `EXPLAIN FORMAT=JSON` 和正式索引 `IX_fn_auditing_access_log_OccurredAtUtc_Id`。
- Produces: 显式 `--mode mysql-index-ab`；`current_optimizer` 与 `force_occurred_at_index` 两种策略的 8 个 workload、16 份计划、成对交替采样和生产决策证据。

- [x] **Step 1: 建立 mode、固定 Hint 和交替采样 RED**

新增测试，要求 `mysql-index-ab` 只接受 `--providers mysql`；候选 list SQL 必须包含固定
`FORCE INDEX (IX_fn_auditing_access_log_OccurredAtUtc_Id)`，count 与当前 SQL 完全一致，
SQL 不接受索引名或用户输入；每个场景 30 次采样按偶数
`current → force`、奇数 `force → current` 交替，避免固定执行顺序偏差。

- [x] **Step 2: 运行 RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~AuditingQueryBenchmarkTests" --nologo
```

若项目级 MTP 入口匹配 0 项，则先构建并直接执行程序集：

```powershell
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~AuditingQueryBenchmarkTests" --minimum-expected-tests 11
```

Expected: FAIL，因为 mode、MySQL 策略工厂与交替顺序尚不存在。

- [x] **Step 3: 实现隔离 MySQL A/B**

`AuditingMySqlQueryFactory.Create(strategy)` 只返回两个封闭策略：当前 SQL，或仅在 list
的固定表名后加入固定索引 Hint；两者复用同一 count、参数和稳定排序。Runner 在一个
MySQL 8.0 容器中迁移并准备数据，对四场景分别预热两策略，再按交替顺序采样；
`MySqlAuditingBenchmarkDatabase` 增加按策略执行和捕获计划的内部入口，现有 baseline
入口继续使用 `current_optimizer`。

- [x] **Step 4: 输出 8 workload、16 计划与原始样本**

报告记录环境、镜像、数据准备时间、策略、场景、P50/P95/P99、总数、返回行数和
计划文件。每个场景的两策略必须得到相同总数与行数；任一结果、样本数或计划为空时
立即失败，禁止输出部分成功报告。

- [x] **Step 5: 运行正式矩阵并决定生产候选**

Run:

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build -- audit-query --mode mysql-index-ab --providers mysql --rows 100000 --warmup 5 --iterations 30 --output BenchmarkDotNet.Artifacts/auditing-query-mysql-index-ab/formal-100k-v1-20260727
```

只有候选同时满足以下条件才新增生产 Task：深 OFFSET 的 P95/P99 改善；计划使用目标
索引且不再 filesort；首屏和两个 contains 场景没有不可接受的 P95/P99、扫描行或错误
退化。否则保留优化器选择，转向显式 cursor/API 契约设计，不通过猜测阈值落地 Hint。

- [x] **Step 6: 同步验证、门槛与演进复盘**

同步 Unit canonical 门槛和性能地图；运行 Release 构建、全量
Unit/Compatibility/Architecture、Governance、Naming、SQL Safety、Performance
Governance、Skills、`git diff --check`、规则复盘和 Skill 复盘。本 Task 只增加隔离
benchmark，不修改生产 SQL、迁移、公共 API 或 MySQL 运行时语义。

### Task 14: MySQL 深 OFFSET 延迟物化 A/B

**Files:**
- Modify: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingMySqlQueryFactory.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingMySqlIndexAbBenchmarkRunner.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingMySqlIndexAbReportWriter.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingQueryBenchmarkOptions.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Auditing/AuditingBenchmarkDatabase.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Modify: `tests/Full.NET.UnitTests/Auditing/AuditingQueryBenchmarkTests.cs`
- Modify: `.agents/skills/fullnet-performance-hardening/references/performance-map.md`
- Modify: `docs/verification/performance-hardening-foundation-2026-07-27.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`

**Interfaces:**
- Consumes: Task 13 的 MySQL 配对采样、100,000 行确定性数据、四场景、执行计划和当前生产等价查询。
- Produces: 显式 `--mode mysql-late-materialization-ab`；`current_optimizer` 与 `late_materialization` 的配对 P50/P95/P99、顺序 ID 等价性和执行计划证据。

- [x] **Step 1: 建立 mode、SQL 形状、策略矩阵和顺序等价性 RED**

测试要求新模式只接受 `--providers mysql`；延迟物化内层只投影 `Id,
OccurredAtUtc` 并保留全部过滤、稳定排序、LIMIT/OFFSET，外层按主键回表取得完整字段
并再次稳定排序；索引 Hint 实验仍只有原来的两个策略，新实验也只有当前查询和延迟物化
两个策略。页面结果必须比较总数、返回行数和有序 ID 签名，禁止不同结果集仅因行数相同而
通过。

- [x] **Step 2: 运行 RED**

Run:

```powershell
dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --nologo
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~AuditingQueryBenchmarkTests"
```

Expected: FAIL，因为 `mysql-late-materialization-ab` mode、延迟物化策略、策略矩阵和有序
ID 签名尚不存在。

- [x] **Step 3: 实现最小隔离 A/B**

扩展现有 MySQL A/B Runner 按 mode 选择固定策略矩阵；延迟物化 SQL 不接受表名、索引名
或用户 SQL 输入，不添加 `FORCE INDEX`。内层派生表在 OFFSET 前只携带覆盖排序所需的
`Id, OccurredAtUtc`，外层仅回表当前页主键并按相同二元组排序。每次执行完整消费
count/list，并将有序 ID 规范化为稳定签名用于两策略等价性门禁。

- [x] **Step 4: 输出独立工件并运行 smoke**

新模式使用独立 `auditing-query-mysql-late-materialization-ab` 工件组，报告明确实验类型、
环境、原始样本、P50/P95/P99、总数、返回行数和 count/list 计划路径。先以 1,000 行、
预热 1 次、采样 5 次验证 8 个 workload、16 个非空计划和策略等价性。

- [x] **Step 5: 运行 100,000 行正式矩阵并作生产决策**

Run:

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build -- audit-query --mode mysql-late-materialization-ab --providers mysql --rows 100000 --warmup 5 --iterations 30 --output BenchmarkDotNet.Artifacts/auditing-query-mysql-late-materialization-ab/formal-100k-v1-20260727
```

只有深 OFFSET P50/P95/P99 共同改善、有序 ID 完全等价，并且首页与 contains 场景的
P95/P99、计划和错误率没有不可接受退化时，才形成生产 SQL Task。否则停止继续修补
OFFSET，转入显式 cursor API 规格，不凭单一百分位或单一执行计划落地。

- [x] **Step 6: 同步验证、门槛与演进复盘**

同步 Unit canonical 门槛和性能地图；运行 Release 构建、全量
Unit/Compatibility/Architecture、Governance、Naming、SQL Safety、Performance
Governance、Skills、`git diff --check`、规则复盘和 Skill 复盘。本 Task 只增加隔离
benchmark，不修改生产 SQL、迁移、公共 API 或 MySQL 运行时语义。

### Task 15: Host 访问日志游标分页纵向切片

**Files:**
- Create: `docs/superpowers/specs/2026-07-27-auditing-access-log-cursor-pagination.md`
- Create: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostAccessLogs/AccessLogCursor.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Contracts/AccessLogContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Contracts/AuditingErrorCodes.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Resources/AuditingErrors.resx`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Resources/AuditingErrors.en-US.resx`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostAccessLogs/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostAccessLogs/HostAccessLogQueryService.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Persistence/AccessLogSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Persistence/AuditingSqlServerPageStatementBuilder.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Serialization/AuditingJsonSerializerContext.cs`
- Modify: `contracts/openapi/auditing-access-logs-v1.json`
- Modify: `packages/client-contracts/src/auditing-access-logs.ts`
- Modify: `packages/client-contracts/src/index.ts`
- Modify: `packages/admin-i18n/src/messages.ts`
- Modify: `ui/admin/src/api/access-logs.ts`
- Modify: `ui/admin/src/views/AccessLogsView.vue`
- Modify: `ui/admin-layui/index.html`
- Modify: `ui/admin-layui/js/core/access-logs.js`
- Test: `tests/Full.NET.UnitTests/Auditing/AuditingAccessLogCursorTests.cs`
- Test: `tests/Full.NET.UnitTests/Auditing/AuditingPagedQueryRoundTripTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Auditing/AuditingAccessLogAssertions.cs`
- Test: `tests/openapi/auditing-access-logs-contract.test.mjs`
- Test: `packages/client-contracts/tests/auditing-access-logs.test.ts`
- Test: `ui/admin/src/api/access-logs.test.ts`
- Test: `ui/admin/src/views/AccessLogsView.test.ts`
- Test: `ui/admin-layui/tests/access-logs.test.js`

**Interfaces:**
- Consumes: `auditing.access.read`、HostOnly SQL Scope、现有访问日志筛选与
  `(OccurredAtUtc DESC, Id DESC)` 排序。
- Produces: 向后兼容的 `GET /api/v1/auditing/access-logs/cursor`、
  `AccessLogCursorPageResponse`、双库 keyset SQL 与 Vue/Layui “加载更多”流程。

- [x] **Step 1: 建立游标、SQL、服务与公开契约 RED**

先新增单元和 OpenAPI/客户端契约测试，断言版本化 Base64Url 游标可往返且筛选不匹配
失败；SQL Server/MySQL 均无 COUNT/OFFSET、使用固定二元 keyset 和 HostOnly；
服务读取 `limit + 1`、生成下一游标且无游标时不传边界；新 Endpoint、JSON schema、
错误码和旧 API 兼容均被冻结。

- [x] **Step 2: 实现最小后端 GREEN**

实现固定二进制游标和筛选 SHA-256 摘要；在 AccessLog SQL 增加 Provider 专用第一批/
下一批 Statement；服务通过 `IQueryExecutor.QueryAsync` 单次读取并映射
`AccessLogCursorPageResponse`；无效游标返回
`auditing.access_log.cursor_invalid` Validation Error；Endpoint 保留旧路由并新增
`/cursor`。

- [x] **Step 3: 实现共享契约与双管理端 GREEN**

共享客户端保留旧分页守卫并新增游标页守卫；Vue/Layui 首屏调用
`/cursor?limit=20`，`nextCursor` 使用 URL 编码，加载更多只追加服务端有序记录；
两端同步 ProblemDetails、加载状态、按钮隐藏与 `accessLogs.loadMore` 中英文。

- [x] **Step 4: 运行双库真实 API 和 OpenAPI 验收**

SQL Server/MySQL 各验证无权限 403、第一批、下一批无重复、筛选绑定、畸形游标 400 和
详情兼容；运行 `pnpm test:openapi`、共享客户端、Vue/Layui 聚焦测试。数据库结构不变，
不新增迁移。

- [x] **Step 5: 建立并运行游标性能证据**

在 100,000 行相同数据、页面 50、预热 5、采样 30、并发 1 下，对 SQL Server/MySQL
分别比较现有深 OFFSET 响应与等价 keyset 页，记录 P50/P95/P99、原始样本、执行计划、
返回 ID 等价性和 COUNT 往返差异；只报告受控样本，不承诺生产固定 QPS。

- [x] **Step 6: 完成全量验证与演进复盘**

同步 Unit/OpenAPI/客户端门槛、路线图和 Verification；运行 Release 构建、全量
Unit/Compatibility/Architecture、双库审计聚焦、Governance、Naming、SQL Safety、
OpenAPI、双客户端、Skills、`git diff --check`、规则复盘与 Skill 复盘。

### Task 16: MySQL Jobs 多 Worker 领取死锁硬化

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionRunner.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobSql.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobExecutionRunnerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Jobs/JobsActiveLeaseRenewalAssertions.cs`
- Modify: `rules/performance-engineering.md`
- Modify: `.agents/skills/fullnet-performance-hardening/SKILL.md`

- [x] **Step 1: 保留真实并发 RED 与锁图证据**

全量 Integration 的 MySQL Jobs 多 Worker 场景在 32 个并发任务中产生一个错误终态。
`SHOW ENGINE INNODB STATUS` 证明领取 `UPDATE ... ORDER BY ... LIMIT` 持有状态/租约
二级索引后等待主键，而成功终态更新持有主键后等待同一二级索引，形成锁顺序反转。

- [x] **Step 2: 实现事务内分阶段领取**

MySQL 8 在 `ICommandTransaction` 短事务内以
`SELECT Id ... FOR UPDATE SKIP LOCKED` 取得候选，再按主键集合更新租约并读取本次
Lease；SQL Server 的 `UPDLOCK`/`READPAST` 路径保持不变。Handler 仍在领取事务提交后
执行，禁止把业务执行放进数据库事务。

- [x] **Step 3: 聚焦回归与 Skill 演进**

Jobs Unit 聚焦 **7/7**，MySQL 真实多 Worker 用例连续 **3/3**，SQL Server 对称用例
**1/1** 通过。性能 Skill 契约先因缺少“锁顺序”和 `SKIP LOCKED` 失败，再补充规则与
Skill 后以 **33** 项契约通过。

- [x] **Step 4: 修复后全量验证**

运行 Release 构建、全量 Unit/Compatibility/Architecture/Integration、治理门禁、
`git diff --check` 与工作区检查；新鲜结果为 Release **0/0**、
Unit **454/454**、Compatibility **7/7**、Architecture **49/49**、
Integration **191/191**，治理与静态门禁全部通过。

### Task 17: Outbox 多 Worker 领取锁顺序硬化

**Files:**
- Modify: `tests/Full.NET.IntegrationTests/Messaging/OutboxRecoveryTests.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxStore.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxSql.cs`
- Modify: `docs/verification/performance-hardening-foundation-2026-07-27.md`

**Interfaces:**
- Consumes: `IOutboxStore.AcquireAsync`、`ICommandTransaction`、现有
  `UPDLOCK/READPAST` SQL Server 领取语义与 MySQL 8。
- Produces: 与终态更新保持一致锁顺序的 MySQL 非阻塞领取；不改变
  `OutboxEnvelope`、Lease、Attempts、重试或 Dead Letter 契约。

- [x] **Step 1: 建立双库终态锁竞争 RED**

在现有 SQL Server/MySQL 租约恢复测试中，各插入并领取一条消息；将时钟推进到租约
过期后，用独立 `ReadCommitted` 事务执行生产等价的 `MarkProcessed` 更新并保持事务
未提交，同时从另一 Scope 调用 `AcquireAsync`。领取必须在 3 秒内返回空集合；旧
MySQL `UPDATE ... ORDER BY ... LIMIT` 应因等待终态事务持有的索引/主键锁而失败，
SQL Server `READPAST` 应保持通过。

- [x] **Step 2: 用 InnoDB 证据确认根因**

RED 后读取 `SHOW ENGINE INNODB STATUS` 或锁等待元数据，确认等待发生在
`IX_fn_outbox_message_Pending` 与 `PRIMARY`，而不是容器、连接池或测试夹具问题。
若测试未复现锁等待，停止生产修改并只记录否定证据。

- [x] **Step 3: 实现最小 MySQL GREEN**

若 RED 证实问题，在现有 `ICommandTransaction` 内先执行固定参数化
`SELECT Id ... ORDER BY OccurredAtUtc, Id LIMIT @BatchSize FOR UPDATE SKIP LOCKED`，
空集合直接返回；否则按已锁定 `Ids` 更新 `LockId/LockedUntilUtc/Attempts`，再按
Lease 读取消息。Handler、终态更新与外部调用继续位于领取事务之外，SQL Server 路径
保持不变。

- [x] **Step 4: 聚焦、多轮与全量验证**

先运行新增双库场景；MySQL 并发场景至少连续三轮，SQL Server 至少一轮。随后运行
Release、Unit **454**、Compatibility **7**、Architecture **49**、Integration
**191**、Governance、Performance Governance、Naming、SQL Safety、Skills、
Workspace 与 `git diff --check`，并将 RED/GREEN、锁图与未验证项写入 Verification。

- [x] **Step 5: 规则与 Skill 复盘**

检查 `rules/performance-engineering.md` 与 `$fullnet-performance-hardening`：
现有锁顺序、`READPAST`、`SKIP LOCKED` 和“禁止用重试掩盖死锁”已覆盖时只记录无
新增规则/Skill；只有出现新的可复用缺口才按契约先行演进，禁止添加近义条目。

### Task 18: Outbox 显式有界并发与独立消息作用域

**Files:**
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxWorkerOptions.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Modify: `tests/Full.NET.UnitTests/Outbox/OutboxProcessorTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Messaging/OutboxRecoveryTests.cs`
- Modify: `docs/operations/outbox-worker-topology.md`
- Modify: `docs/verification/performance-hardening-foundation-2026-07-27.md`

**Interfaces:**
- Consumes: `OutboxWorkerOptions.BatchSize`、`IServiceScopeFactory`、Scoped
  `IIntegrationEventHandler`、Scoped `IOutboxStore` 与现有至少一次租约语义。
- Produces: `OutboxWorkerOptions.MaxConcurrency`，默认值 `1`、有效范围 `1..16`
  且不得超过 `BatchSize`；显式启用时最多并行处理该数量的消息，每条消息使用独立
  DI Scope、宿主租户上下文和数据库会话。

- [x] **Step 1: 建立配置与双库并发 RED**

扩展现有 Options Validator 用例，要求默认 `MaxConcurrency = 1`，并拒绝 `0`、`17`
及大于 `BatchSize` 的值。在现有 SQL Server/MySQL Outbox 租约用例中各使用独立干净
数据库插入四条同路由消息，注册 Scoped 闸门 Handler，以 `BatchSize = 4`、
`MaxConcurrency = 2` 调用 `ProcessOnceAsync`。测试必须在释放闸门前观测到两个
Handler 同时进入；处理完成后峰值恰为 2、四个 Scoped Handler 实例均不同、四条消息
均写入成功终态。旧实现应先因缺少 `MaxConcurrency` 编译失败；补齐属性但尚未并发时，
应因第二个 Handler 无法进入而超时失败。

- [x] **Step 2: 运行 RED 并确认失败归因**

Run:

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore --nologo
```

Expected: FAIL，错误只指向 `OutboxWorkerOptions.MaxConcurrency` 尚不存在；不得来自
测试夹具、容器或数据库连接。补齐最小属性/Validator 后再次运行双库聚焦，Expected:
FAIL，闸门超时并证明当前 `foreach` 串行路径不能达到并发 2。

- [x] **Step 3: 实现默认关闭的最小 GREEN**

`MaxConcurrency == 1` 继续在原批次 Scope 内按领取顺序解析 Handler、处理并写终态，
保持当前默认行为。`MaxConcurrency > 1` 时，先在领取 Scope 内完成 backlog 采样和
租约领取并释放该 Scope，再使用 `Parallel.ForEachAsync` 与
`MaxDegreeOfParallelism = MaxConcurrency` 处理本批；每条消息创建独立 Async Scope，
设置 Host 租户上下文，解析该 Scope 的 Handler 与 `IOutboxStore`，完成后清理上下文
并释放 Scope。禁止跨任务共享 `DbSession`、Scoped Handler 或
`CurrentTenantAccessor`；取消时未完成消息继续沿用租约到期恢复语义。

- [x] **Step 4: 聚焦、多轮与全量验证**

先运行 Outbox Unit 聚焦；SQL Server/MySQL 双库并发用例各至少一轮，MySQL 再连续复跑
两轮。随后运行 Release、Unit **454**、Compatibility **7**、Architecture **49**、
Integration **191**、Governance、Performance Governance、Naming、SQL Safety、
Skills、Workspace 与 `git diff --check`。记录该验证只证明有界执行、作用域隔离和
终态正确性；未运行生产等价负载测试时，不声明固定吞吐、P95/P99 或连接池容量。

- [x] **Step 5: 运维、规则与 Skill 复盘**

运维文档增加 `MaxConcurrency` 配置、默认关闭、无全局顺序保证、幂等 Handler、每消息
独立 Scope 与连接池预算说明。检查现有性能规则和 Skill 是否已覆盖“有界并发、顺序键、
作用域和连接池预算”；已有条目足够时只记录无新增规则/Skill，禁止添加近义约束。
