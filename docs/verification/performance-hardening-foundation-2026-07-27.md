# 性能硬化基础验证（2026-07-27）

## 1. 范围与环境

- 基线提交：`660340c5a6543100dd9f7d41a9d264d461f0cf6b`
- 工作分支：`codex/performance-hardening`
- 环境：Windows、.NET SDK `10.0.400-preview.0.26322.102`、Node.js `24.12.0`、pnpm `10.26.0`
- 数据库：SQL Server/MySQL Testcontainers
- 本轮边界：只实施可由确定性回归证明且不改变安全、租户、事务或至少一次语义的改进；不宣称生产 QPS 或 P99 已提升

## 2. 已确认瓶颈与变更

| 热点 | 变更前证据 | 变更 | 可验证结果 |
| --- | --- | --- | --- |
| Dapper 可观测性 | Executor 只有 Debug 日志，无法按稳定 Statement 聚合耗时、调用量与失败 | 新增 `Full.NET.Data.Dapper` Meter 注册和执行次数、失败次数、耗时直方图；标签只含 Statement、Provider、操作与结果 | `DapperTelemetryTests` **2/2**，并锁定无 SQL、用户、租户标签 |
| Outbox/Jobs 空等 | 每轮处理后固定等待 Poll，即使已领取满 BatchSize | 满批次返回零等待，未满批次保留配置 Poll | 聚焦回归分别覆盖满批和未满批 |
| Jobs N+1 | 同批每条执行分别读取 Definition | 同批去重 ID 后使用参数化 `IN @Ids` 一次读取，Handler 仍保持逐条顺序执行 | 两条执行只触发一次 Definition 查询 |
| Audit 列表往返 | 访问、操作和异常日志都先查询总数，再单独查询当前页，每个列表请求固定两次数据库命令 | SQL Server/MySQL 均通过 `QueryMultiple` 在一个命令中顺序读取总数和当前页，保留原过滤、排序和 OFFSET 契约 | 单元锁定每条列表只调用一次多结果执行器；双库真实 API 各 **3/3** |
| Vue 首包 | 所有业务页静态导入，入口 JS `740.98 kB`、gzip `210.99 kB` | 保留 Overview 首屏静态加载，其余页面改为路由动态导入 | 入口资产降至 `146.74 kB`、gzip `47.54 kB`，业务页形成独立 chunk |
| Vue ECharts chunk | 工作台只使用折线图，但模块入口还注册柱图、饼图、标题、图例和 Dataset；延迟 chunk `559016` bytes、gzip `187747` bytes | 只保留 Line、Grid、Tooltip 与 Canvas，并为延迟资产增加独立预算 | chunk 降至 `496501` bytes、gzip `166669` bytes，分别下降约 `11.18%/11.23%`，不再触发 500 kB 告警 |
| Layui 首包 | `app.js` 静态导入并初始化 23 个业务控制器，首屏静态 JavaScript `691484` bytes、gzip `192765` bytes | 新增按路由动态导入注册表，并发触发共享一次加载，路由过期或应用卸载后不启动数据请求 | 首屏静态 JavaScript 降至 `592147` bytes、gzip `180170` bytes，23 个控制器形成独立 chunk |
| Layui 运行库 | `main.js` 在会话恢复前同步导入完整 Layui 运行库，入口资产 `532536` bytes、gzip `164857` bytes | 会话恢复完成并经过下一次绘制后再导入运行库，以渐进增强方式统一刷新语言和组件 | 首屏静态依赖图降至 `198567/54392` bytes；运行库成为独立延迟资产 `394758/126525` bytes，并在受控浏览器样本中于 FCP 后开始加载 |
| 包体退化 | 只有 Vite 的单 chunk 告警，没有可审查相对基线 | 新增 Vue/Layui 首屏静态 import 图预算脚本、契约测试和 CI 门禁 | Vue `585792/193619` bytes、Layui `592147/180170` bytes，允许最多 5% 相对退化 |

Vue 入口资产本身的 minified/gzip 分别下降约 **80.2%/77.5%**。把入口的同步 import 递归计入后，当前首屏静态 JavaScript 为 `585792` bytes、gzip `193619` bytes；相对原单入口资产约下降 **20.9%/8.2%**。后者更接近网络首屏成本，因此不把入口文件降幅直接表述为总首屏收益。

Layui 经两阶段拆分后，首屏静态 import 图由 `691484/192765` bytes 降至 `198567/54392` bytes，minified/gzip 分别下降约 **71.28%/71.78%**。业务控制器只在认证用户实际进入对应路由时下载和初始化；完整 Layui 运行库延迟到会话主流程和首个可见界面完成后加载。静态图与运行库合计为 `593325/180917` bytes，较只拆控制器时略增约 **0.20%/0.41%**，因此本项收益是减少首屏同步下载和解析，而不是降低总下载量。

## 3. RED / GREEN 证据

| 能力 | RED | GREEN |
| --- | --- | --- |
| 项目 Skill | `pnpm test:skills` 因目标 Skill 目录缺失失败 | 模块 Skill **52** 项、性能 Skill **31** 项契约通过；`quick_validate.py` 通过 |
| Dapper 指标 | 因 `DapperTelemetry`/操作类型缺失而编译失败 | 聚焦 **2/2** |
| Worker 排空 | 因缺少批次数返回与延迟决策而编译失败 | Outbox/Jobs 聚焦总计 **19/19** |
| Jobs Definition 合并 | 初版仍观测到两次 Definition 查询，期望一次 | 参数化批量查询后聚焦通过 |
| Audit 分页往返 | 三个服务没有多结果执行器依赖，双 Provider 的六个场景均因三参数构造函数缺失而编译失败 | 聚焦 **8/8**、Unit 全量 **427/427**；SQL Server/MySQL 审计 API 各 **3/3** |
| Vue 路由拆包 | 非首屏 route component 为静态对象而非异步函数 | 聚焦 **1/1**，Vue 全量 **205/205** |
| Vue ECharts 精简 | 合同观测到 `BarChart`、`PieChart` 和未使用组件；延迟资产预算能力不存在 | 图表聚焦 **4/4**，性能治理 **3/3**，Vue 全量 **206/206** |
| Layui 路由拆包 | 注册表模块尚不存在，聚焦测试无法解析导入 | 聚焦 **3/3**，Layui 全量 **98/98** |
| Layui 运行库延迟加载 | 运行时调度模块不存在、应用不暴露渐进增强入口、`main.js` 仍静态导入 Layui | 聚焦 **17/17**，Layui 全量 **101/101**；浏览器确认登录界面与 Layui 组件恢复正常 |
| 包体预算 | 契约测试因预算模块不存在而失败 | 脚本契约 **3/3**；Vue 首屏、Vue 图表延迟 chunk、Layui 首屏与 Layui 运行库延迟资产当前基线均通过 |

## 4. 前端构建

| 目标 | 结果 |
| --- | --- |
| Vue | 生产构建通过；入口 `146.74 kB` / gzip `47.54 kB`；首屏静态依赖图 17 个 JS chunk，`585792` / gzip `193619` bytes |
| Vue 延迟 chunk | ECharts `496.50 kB` / Vite gzip `168.10 kB`；预算脚本按固定 Node gzip 口径记录 `496501/166669` bytes，不再触发单 chunk 大小告警 |
| Layui | 生产构建通过；入口 `138.47 kB` / Vite gzip `38.70 kB`；23 个业务控制器形成路由 chunk；首屏静态依赖图 `198567/54392` bytes |
| Layui 延迟运行库 | 独立资产 `394.75 kB` / Vite gzip `127.61 kB`；预算脚本按固定 Node gzip 口径记录 `394758/126525` bytes；Layui 构建不再产生 500 kB 大 chunk 告警 |
| 自动门禁 | `pnpm test:bundle-budgets` 通过；CI 在 `build:clients` 后执行 |

Vite 展示值与预算脚本值存在四舍五入和 gzip 实现口径差异；预算的前后比较始终使用同一 Node `gzipSync` 实现。

## 5. 受控浏览器 A/B

使用本地 Chromium、冷缓存、4 倍 CPU 降速，并以固定 `401` 响应拦截 `/api/**`，交替运行“静态导入 Layui 的临时基线”和当前候选各 10 次。两组在等待运行库后均满足 `loginVisible=true`、`layuiReady=true`。以下为中位数，只用于证明本地加载顺序和相对变化，不代表生产用户或真实后端：

| 指标 | 静态导入基线 | 延迟加载候选 | 变化 |
| --- | ---: | ---: | ---: |
| FCP | `228.0 ms` | `238.0 ms` | `+4.39%`，未改善 |
| DOMContentLoaded | `537.6 ms` | `317.0 ms` | `-41.03%` |
| Load | `686.5 ms` | `475.8 ms` | `-30.69%` |
| 首屏主脚本 resource duration | `45.85 ms` | `27.20 ms` | `-40.68%` |
| 延迟运行库开始时间 | 不适用 | `943.05 ms` | 晚于候选 FCP `238.0 ms` |

结论仅限于：完整 Layui 运行库已移出首屏同步路径，且本地受控样本中的 DOMContentLoaded、Load 和主脚本资源耗时下降；FCP 没有改善。生产结论仍需真实网络、后端、设备分层和 P50/P95/P99。

## 6. 完整验证

| 命令 | 结果 |
| --- | --- |
| `pnpm test:skills` | **PASS**，52 + 31 项契约 |
| `quick_validate.py .agents/skills/fullnet-performance-hardening` | **PASS** |
| `pnpm test:governance` | **11/11** |
| `pnpm test:naming` | **23/23** |
| `pnpm test:performance-governance` | **3/3** |
| `pnpm test:bundle-budgets` | **PASS**，Vue 首屏 `585792/193619`、Vue 图表 `496501/166669`、Layui 首屏 `198567/54392`、Layui 运行库 `394758/126525`，均为基线 `+0.00%` |
| `pnpm --filter @fullnet/admin test` | **209/209** |
| `pnpm --filter @fullnet/admin-layui test` | **102/102** |
| Vue/Layui production build | **PASS**；两端均无大 chunk 告警 |
| `dotnet build Full.NET.slnx -c Release` | **PASS**，0 warning / 0 error |
| Unit / Compatibility / Architecture | **454/454**、**7/7**、**49/49** |
| Audit API SQL Server/MySQL 聚焦 | **3/3 + 3/3**，失败 0、跳过 0 |
| `pnpm test:integration:full` | **191/191**，失败 0、跳过 0，`30m45s`，进程退出码 0 |

2026-07-28 提交前复验再次运行上述非容器门禁与全量双库 Integration：Release 构建
**0 warning / 0 error**，Unit/Compatibility/Architecture 分别为
**454/454**、**7/7**、**49/49**，Vue/Layui 分别为 **209/209**、**102/102**，
OpenAPI 向后兼容与包体预算通过；`pnpm test:integration:full` 为
**191/191**、失败 0、跳过 0、耗时 `30m50s`。该结果用于确认当前工作树可以作为
性能硬化基础检查点提交，不替代第 7 节仍要求的生产等价容量证据。

## 7. 未验证项与后续门禁

以下项目需要独立基线、真实数据规模、SQL Server/MySQL 执行计划、P50/P95/P99、失败恢复和资源上限，不在本轮静态推断后直接修改：

1. 认证 Session/API Key/安全戳缓存，必须先冻结撤销时效和 fail-closed；
2. Audit 异步化或批量化，必须先设计事务/Outbox、背压、排空与崩溃丢失预算；
3. 审计大表已完成受控 100,000 行双库基准，确认 SQL Server 可选谓词计划敏感、
   MySQL 深 OFFSET 退化和双库无界 contains 全表成本；MySQL 固定索引 Hint 与通用
   延迟物化均未通过全场景门禁，显式 cursor API 已按第 14 节落地并验证；contains
   搜索设施仍须基于生产等价分布与 SLA 独立验证；
4. Outbox/Jobs 有界并发，必须先定义顺序键、Handler 作用域与连接池预算；
5. Vue 首屏公共依赖、Layui 运行库按实际组件用量定制，以及 Brotli/CDN 缓存收益；
6. 生产等价负载测试与固定性能预算。

## 8. 规则与 Skills 复盘

- 规则已演进：新增 `rules/performance-engineering.md`，固化性能证据、低基数标签、双库、可靠性、Worker 与包体门禁。
- 重复出现的 ECharts 与 Layui 延迟资产证据达到演进门槛：规则和 `fullnet-performance-hardening` Skill 均新增“首屏静态图与大体积延迟 chunk 分别预算”的约束，禁止只移动依赖来隐藏总体积回涨。
- Skill 已新增：`fullnet-performance-hardening`，覆盖请求链、SQL/分页、缓存、Audit、Outbox/Jobs、基准和客户端包体；RED 契约、项目契约和官方校验器均有证据。
- 自动化已新增：Skill 多合同枚举、Vue 路由懒加载回归、Vue/Layui 首屏静态 JavaScript及大体积延迟资产相对预算与 CI 门禁。
- Audit QueryMultiple 已由现有性能规则、模块交付 Skill 和性能硬化 Skill 明确覆盖；辅助方法构造 `SqlStatement` 的单次实现缺陷已被既有 Architecture 门禁阻止，因此本阶段不新增近义规则或 Skill，仅同步两项 Skill reference 的 Unit 门槛。
- 审计大表基准没有暴露新的通用规则缺口：现有规则已要求代表性规模、双库计划、百分位、参数敏感性和禁止猜测性索引/契约变更；`fullnet-performance-hardening` 的参考地图已补充可复现命令、工件要求和解释边界，无需再创建近义 Skill。
- SQL Server 计划稳定性 A/B 继续复用同一 Skill；参考地图已补充
  `sqlserver-plan-ab` 命令、三策略、混合请求顺序和 CompileCPU 判读边界。
  该工作流没有形成新的独立职责，因此不新增 Skill；项目契约 31 项和官方校验均通过。
- 固定谓词生产落地没有产生新的强制规则：现有性能规则已覆盖参数化、形状上限、
  Provider 分支和真实复测，现有 Architecture 扫描器又准确阻止了普通方法构造
  `SqlStatement`；因此按单次实现缺陷修正，不新增近义规则。
- 显式游标实现没有产生新的强制规则：`rules/performance-engineering.md` 已覆盖稳定
  游标、双库计划和深 OFFSET 证据。性能 Skill 的 reference 通过 RED 契约补充
  `cursor-ab`、有序 ID 等价性及“无精确总数”边界；项目契约 52/31 与官方校验通过。
- Skills 复盘将 `fullnet-release-verification` 自动化候选更新为第 12 次证据：
  Docker daemon 停止再次在容器创建前阻断 SQL Server/MySQL 聚焦测试。该问题适合
  收敛到 Integration preflight/脚本，不创建新的判断型 Skill；两个现有项目 Skill
  契约和性能 Skill 官方校验均通过。
- MySQL 索引 Hint A/B 没有产生新规则：现有性能规则已经要求代表性规模、P50/P95/P99、
  执行计划和其他场景不退化，本次正是依靠该门禁拒绝“计划看起来更好”的错误生产
  候选。性能 Skill 不新增或拆分；只在现有 reference 同步 `mysql-index-ab` 命令、
  成对交替采样和判定边界，项目契约与官方校验继续通过。
- MySQL 延迟物化 A/B 同样没有产生新规则或独立 Skill：现有门禁准确区分了“深页
  可重复改善”与“筛选场景尾延迟不稳定”。性能 Skill reference 增加可复现命令、
  有序 ID 等价性和停止 OFFSET 微调的边界；这属于既有工作流同步，不扩大 Skill 职责。

## 9. 审计大表双库基准与执行计划

### 9.1 方法与环境

- 源版本标识：`1.0.0+660340c5a6543100dd9f7d41a9d264d461f0cf6b`；实际运行于
  `codex/performance-hardening` 未提交工作树，包含本记录所述变更。
- 运行时：Windows `10.0.19045`、.NET `10.0.9`、20 个逻辑处理器。
- Provider：SQL Server 2022 CU14
  `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04`；MySQL `8.0.46`
  `mysql:8.0`。
- 数据集：`fn_auditing_access_log` 100,000 行，均匀覆盖 30 天；10% 路径包含
  `/api/v1/settings`，20% 为 POST，5% 为 500；应用端生成 UUID v7。
- 查询：与 `AccessLogSql.PageFilteredSqlServer/MySql` 规范化换行后完全一致，
  每次完整消费精确总数和当前页；页面大小 50，并发 1。
- 采样：每个场景预热 5 次、采样 30 次，按最近秩计算 P50/P95/P99。
- 工件：
  `BenchmarkDotNet.Artifacts/auditing-query/formal-100k-v2-20260727/`，包含原始样本、
  `summary.json`、SQL Server `STATISTICS XML` 实际计划和 MySQL
  `EXPLAIN FORMAT=JSON`；目录按仓库约定不提交，可由 README 命令重建。

该实验是本机 Testcontainers 受控样本，不是生产 SLA，也不能用于两个 Provider
之间的绝对性能排名。

### 9.2 延迟结果

| Provider | 场景 | P50 ms | P95 ms | P99 ms | P95 / 首屏 |
| --- | --- | ---: | ---: | ---: | ---: |
| SQL Server | `first_page` | 27.123 | 30.284 | 30.756 | 1.00 |
| SQL Server | `deep_offset` | 55.803 | 68.939 | 73.756 | 2.28 |
| SQL Server | `contains_unbounded` | 243.315 | 284.693 | 302.493 | 9.40 |
| SQL Server | `contains_bounded` | 23.035 | 27.853 | 30.005 | 0.92 |
| MySQL | `first_page` | 7.413 | 9.377 | 9.904 | 1.00 |
| MySQL | `deep_offset` | 104.786 | 118.567 | 120.984 | 12.64 |
| MySQL | `contains_unbounded` | 54.747 | 63.269 | 63.928 | 6.75 |
| MySQL | `contains_bounded` | 8.368 | 9.865 | 12.547 | 1.05 |

`contains_bounded` 的精确总数为 333，`contains_unbounded` 为 10,000；其他两个
场景总数为 100,000。全部列表均返回 50 行。

### 9.3 执行计划事实

- SQL Server 首屏的 count 对聚集时间索引读取 100,000 行、2,348 个逻辑页；
  list 按相同索引逆序读取 50 行、24 个逻辑页。
- SQL Server 深 OFFSET 的 count 和 list 均读取 100,000 行、2,348 个逻辑页；
  list 计划记录 `@Offset` 编译值为 0、运行值为 99,950。
- SQL Server 无界 contains 的 count 读取 100,000 行、2,348 个逻辑页，计划样本
  CPU/Elapsed 均为 213 ms；list 为取得 50 个匹配项读取 500 行、35 个逻辑页。
- SQL Server 最近一天 contains 的 count 仍读取 100,000 行。实际计划显示
  `@FromUtc/@ToUtc/@PathContains` 的编译值均为 NULL、运行值非 NULL，证明首屏
  首次编译的可选谓词计划被后续筛选场景复用；list 读取 500 行。
- MySQL 首屏 count 为 `access_type=ALL`、估算扫描 100,000 行；list 使用
  `(OccurredAtUtc, Id)` 逆序索引且无 filesort。
- MySQL 深 OFFSET 的 list 选择全表访问并 `using_filesort=true`，估算扫描
  100,000 行，计划成本 `110000.25`。
- MySQL 无界 contains 的 count 为全表访问、估算扫描 100,000 行；最近一天
  contains 的 count/list 均使用时间索引 range，估算扫描 3,334 行，list 无
  filesort。

### 9.4 决策与停止条件

1. 本轮不增加普通 B-tree 索引。子串 contains 不可 SARG，现有双库计划没有证明
   新增普通索引能消除全表扫描，盲目增加只会放大高频审计写入。
2. 本轮不静默改成游标分页。当前响应要求精确总数，单独把 list 改成 cursor
   仍会保留 count 全扫；新增 cursor 或 `includeTotal=false` 必须作为显式 API
   契约设计，并同步 Vue/Layui 与兼容性测试。
3. SQL Server 可选谓词计划稳定性 A/B 已完成，分支 SQL 在两种请求顺序下均降低
   P95 和逻辑读，且不承担每请求重编译；下一步必须作为独立生产变更，为固定谓词
   组合生成参数化 SQL，并验证最多 32 个有界形状、三类 Audit SQL 和双库 API。
4. contains 下一候选优先评估强制/默认时间上界；若产品确需跨长期数据子串搜索，
   必须独立设计 SQL Server/MySQL 搜索设施、语义一致性、写放大和运维边界。

### 9.5 基准工具修复记录

首次 SQL Server 采集使用 `SHOWPLAN_ALL/SHOWPLAN_XML` 时只得到空结果，旧的
“非空”检查又被自写结果集标题误导。最终改为执行后排空数据结果并只接受包含
`<ShowPlanXML>` 的 `STATISTICS XML` 结果；1,000 行双库冒烟和 100,000 行正式
矩阵均重新执行。旧目录 `formal-100k-20260727` 中的 SQL Server 计划无效，不作为
本记录证据；最终证据只引用 `formal-100k-v2-20260727`。

首次全量 Architecture 门禁以 **47/49** 失败，准确识别 benchmark 对 MySQL Provider
与正式迁移器的新增直接引用。修复方式是只把
`benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj` 登记为隔离基准的非运行时
消费者，API/Worker 的迁移依赖禁令保持不变；重建后全量 **49/49**。

## 10. SQL Server 可选谓词计划稳定性 A/B

### 10.1 实验设计

- 命令：
  `dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --mode sqlserver-plan-ab --providers sqlserver --rows 100000 --warmup 5 --iterations 30`
- 工件：
  `BenchmarkDotNet.Artifacts/auditing-query-sqlserver-ab/formal-100k-v1-20260727/`；
  包含 12 个 workload、24 份 `STATISTICS XML` 实际计划、原始样本与 `summary.json`。
- 数据、机器、镜像、页面大小和百分位算法与第 9 节一致；每个策略/顺序组合先在
  隔离容器执行 `DBCC FREEPROCCACHE`。
- 顺序：`broad_first` 先请求首屏再请求最近一天 contains；
  `bounded_first` 反向执行，用于检验首次编译场景对后续请求的影响。
- 策略：`current_optional` 为当前生产可选谓词；`branch_specific` 只拼接实际存在的
  固定参数化谓词；`recompile` 为当前 SQL 的 count/list 各追加
  `OPTION (RECOMPILE)`。

### 10.2 延迟结果

| 顺序 | 场景 | Current P95/P99 ms | Branch P95/P99 ms | Recompile P95/P99 ms |
| --- | --- | ---: | ---: | ---: |
| `broad_first` | `first_page` | 40.281 / 41.413 | 16.004 / 16.156 | 21.089 / 22.394 |
| `broad_first` | `contains_bounded` | 41.123 / 44.953 | 16.705 / 17.334 | 26.187 / 37.025 |
| `bounded_first` | `contains_bounded` | 33.687 / 35.305 | 18.836 / 33.774 | 21.142 / 23.682 |
| `bounded_first` | `first_page` | 31.519 / 32.735 | 19.234 / 26.394 | 18.295 / 18.629 |

相对当前策略，分支 SQL 的四个 P95 分别下降约 **60.3%**、**59.4%**、**44.1%**
和 **39.0%**。该数据是本机受控实验，不是生产收益承诺；P99 样本只有 30 次，
尤其 `bounded_first/contains_bounded` 的单次尾部波动仍需更长压力窗口确认。

### 10.3 计划与编译事实

| 场景 | Current count 逻辑读/读行 | Branch/Recompile count 逻辑读/读行 | Current → Branch list 逻辑读 |
| --- | ---: | ---: | ---: |
| `first_page` | 2,348 / 100,000 | 606 / 100,000 | 24 → 24 |
| `contains_bounded` | 2,348 / 100,000 | 81 / 3,334 | 35 → 16 |

- 分支首屏 count 可选择较窄索引完成精确总数；最近一天 contains 的 count 使用时间
  range 后只读 3,334 行，不再由无筛选首请求缓存的通用计划扫描 100,000 行。
- 两种请求顺序下，分支策略为不同谓词形状产生独立缓存计划，因此执行计划不再由
  第一个请求的 NULL 参数形状共享。
- A/B 计划采集显示分支 count/list 的单次编译 CPU 为 0～1 ms；recompile 也是
  0～1 ms，但该成本发生在每次执行。缓存策略的 CompileCPU 是该缓存计划的一次
  编译成本，不能与 recompile 的每请求成本直接累计比较。
- `current_optional/broad_first` 捕获的 count 缓存计划记录 104 ms CompileCPU；
  这是一次实际计划编译样本，不代表每请求成本，也不作为单独决策依据。

### 10.4 决策

1. 生产候选选择 `branch_specific`，不选择 `OPTION (RECOMPILE)`。它在四个组合中
   全部改善 P95，减少 count/list 读取，并避免每请求重新编译。
2. 生产 SQL 已在第 11 节按该候选落地：三类 Audit SQL 使用固定白名单片段和参数值，
   5 个可选筛选最多产生 32 个有界形状；未插值列名或用户输入。
3. MySQL 当前最近一天 contains 已使用时间 range，因此不得机械照搬 SQL Server
   策略；生产实现仍须保持 MySQL 现状并运行 SQL Server/MySQL 审计 API 聚焦测试。
4. 深 OFFSET 和无界 contains 仍未解决，继续服从第 9.4 节的游标契约与搜索设施
   停止条件；本 A/B 不能被描述为消除所有审计大表瓶颈。

## 11. SQL Server 固定谓词分支生产落地

### 11.1 实现与边界

- Access/Operation 根据 5 个规范化筛选位选择最多 32 个缓存形状，Exception 根据
  4 个筛选位选择最多 16 个缓存形状；所有形状在对应 SQL 类型初始化时生成。
- 每个形状只组合模块内固定谓词，所有值仍通过 Dapper 参数传递；Statement Name、
  `HostOnly` Scope、单次 `QueryMultiple`、精确总数、稳定排序、分页参数和详情查询
  均保持不变。
- MySQL 继续使用原有 `PageFilteredMySql` 可选谓词，不机械复制 SQL Server 的
  Provider 专用优化。
- 动态形状通过一个静态 `HostOnly` 原型 clone。Architecture 只对
  `AuditingSqlServerPageStatementBuilder.CreateVariants` 精确放行 Name/Text clone，
  扫描器仍拒绝运行时构造以及修改 Scope/TenantBinding。

### 11.2 RED / GREEN 与双库验证

- RED：三类 SQL 尚无 `CreatePageFilteredSqlServer` 接口，聚焦测试编译失败。
- GREEN：三类服务与全部 32/32/16 形状、生产/benchmark SQL 防漂移合计
  **15/15**；Unit 全量 **436/436**。
- SQL Server/MySQL 审计 API 聚焦各 **3/3**，失败 0、跳过 0。首次执行因本机
  Docker daemon 停止而在容器创建前失败；恢复 daemon 后原命令重跑通过，
  该基础设施失败不计为 SQL 验证通过。
- Release 构建 **0 warning / 0 error**；Compatibility **7/7**、Architecture
  **49/49**。Architecture 首次 **48/49** 准确阻止普通方法中的 `SqlStatement`
  构造，改用现有安全 clone 模式后全量通过。

### 11.3 同环境生产等价复测

- 命令参数：SQL Server 100,000 行、预热 5、采样 30、并发 1；策略和两种请求
  顺序与第 10 节相同。
- 工件：
  `BenchmarkDotNet.Artifacts/auditing-query-sqlserver-ab/formal-100k-production-v1-20260727/`；
  12 个 workload、24 份实际计划，每个 workload 30 个成功样本。
- 单元测试保证 `branch_specific` 的 Access 有界 SQL 与生产形状规范化换行后完全
  一致。

| 顺序 | 场景 | Current P95 ms | Production-equivalent P95 ms | 变化 |
| --- | --- | ---: | ---: | ---: |
| `broad_first` | `first_page` | 23.748 | 10.417 | -56.13% |
| `broad_first` | `contains_bounded` | 25.364 | 14.029 | -44.69% |
| `bounded_first` | `contains_bounded` | 21.132 | 12.338 | -41.62% |
| `bounded_first` | `first_page` | 23.759 | 12.613 | -46.91% |

分支 count 的逻辑读继续保持首屏 **606**、最近一天 contains **81**；后者实际
读取 **3,334** 行，未回退到当前可选谓词计划的 **2,348** 个逻辑读和
**100,000** 实际读行。该结果仅证明本机受控样本中的相对变化，不承诺生产固定
P95/QPS；深 OFFSET、无界 contains 和生产分布仍服从第 9.4 节停止条件。

## 12. MySQL 深 OFFSET 时间索引 Hint A/B

### 12.1 方法与完整性

- 模式：`mysql-index-ab`，仅接受 `--providers mysql`。
- 策略：`current_optimizer` 使用当前生产等价 SQL；
  `force_occurred_at_index` 只在 list 的固定表名后增加固定
  `FORCE INDEX (IX_fn_auditing_access_log_OccurredAtUtc_Id)`，count、参数、筛选、
  排序和分页保持一致。
- 每个场景先分别预热两策略，再对 30 个样本逐轮反转
  `current → force` / `force → current`，降低固定执行顺序偏差。
- 工件：
  `BenchmarkDotNet.Artifacts/auditing-query-mysql-index-ab/formal-100k-v1-20260727/`；
  8 个 workload、16 份 `EXPLAIN FORMAT=JSON`、每项 30 个成功样本。

### 12.2 结果

| 场景 | Current P50/P95/P99 ms | Force P50/P95/P99 ms | Force 相对变化 P50/P95/P99 |
| --- | ---: | ---: | ---: |
| `first_page` | 7.134 / 7.903 / 8.470 | 6.909 / 9.628 / 10.406 | -3.15% / +21.83% / +22.85% |
| `deep_offset` | 85.619 / 149.189 / 272.211 | 102.765 / 135.183 / 180.174 | +20.03% / -9.39% / -33.81% |
| `contains_unbounded` | 42.598 / 52.695 / 56.524 | 43.778 / 51.720 / 56.862 | +2.77% / -1.85% / +0.60% |
| `contains_bounded` | 6.714 / 7.723 / 7.889 | 6.796 / 8.278 / 9.761 | +1.23% / +7.18% / +23.73% |

深页当前计划为 `access_type=ALL`、估算读取 100,000 行且
`using_filesort=true`；Hint 计划使用
`IX_fn_auditing_access_log_OccurredAtUtc_Id`、`access_type=index`、
估算读取 100,000 行且 `using_filesort=false`。计划形态改善没有转化为全面延迟
改善：深页 P50 退化，首屏和有界 contains 尾延迟也明显退化。

### 12.3 决策

1. 拒绝将全局 `FORCE INDEX` 落入生产 SQL。它没有满足“深页全部百分位改善且其他
   场景不退化”的门禁，不能只凭消除 filesort 作决策。
2. 不猜测 Offset 阈值。100,000 行单点样本不足以证明一个跨数据分布稳定的分支阈值。
3. 下一无 API 变更候选可评估 late materialization：先仅在覆盖时间索引中完成
   Offset/Limit，再按 50 个 Id 回表；若仍不能全面改善，则停止 OFFSET 微调并设计
   显式 cursor/API 契约。
4. 本 Task 没有修改生产 SQL、迁移、公共 API 或 MySQL 运行时行为。

## 13. MySQL 深 OFFSET 延迟物化 A/B

### 13.1 方法与完整性

- 模式：`mysql-late-materialization-ab`，仅接受 `--providers mysql`。
- 策略：`current_optimizer` 使用当前生产等价 SQL；`late_materialization`
  在内层派生表只投影 `Id, OccurredAtUtc` 并完成过滤、稳定排序和 OFFSET/LIMIT，
  外层按主键回表取得完整行并再次按 `(OccurredAtUtc DESC, Id DESC)` 排序。
- 候选不使用 `FORCE INDEX`，不接受动态表名、索引名或用户 SQL 输入；count、参数和
  公共分页语义不变。
- 每次执行除总数和返回行数外，还规范化比较有序 ID 签名；两次正式矩阵的 8 个
  workload 均严格等价，每项均有 30 个成功样本和非空 count/list 计划。
- 工件：
  `BenchmarkDotNet.Artifacts/auditing-query-mysql-late-materialization-ab/formal-100k-v1-20260727/`
  与
  `BenchmarkDotNet.Artifacts/auditing-query-mysql-late-materialization-ab/formal-100k-v2-20260727/`。

### 13.2 两次独立容器结果

| 运行 | 场景 | Current P50/P95/P99 ms | Late P50/P95/P99 ms | Late 相对变化 P50/P95/P99 |
| --- | --- | ---: | ---: | ---: |
| v1 | `first_page` | 6.663 / 7.527 / 7.908 | 6.850 / 7.446 / 7.673 | +2.80% / -1.07% / -2.98% |
| v1 | `deep_offset` | 87.085 / 99.726 / 104.935 | 66.579 / 83.622 / 90.896 | -23.55% / -16.15% / -13.38% |
| v1 | `contains_unbounded` | 50.082 / 94.172 / 155.311 | 48.751 / 147.380 / 241.990 | -2.66% / +56.50% / +55.81% |
| v1 | `contains_bounded` | 13.645 / 20.174 / 21.352 | 13.883 / 17.672 / 18.473 | +1.74% / -12.40% / -13.48% |
| v2 | `first_page` | 6.635 / 8.687 / 29.424 | 6.686 / 8.050 / 8.462 | +0.76% / -7.33% / -71.24% |
| v2 | `deep_offset` | 79.462 / 108.147 / 134.998 | 63.298 / 83.285 / 88.119 | -20.34% / -22.99% / -34.73% |
| v2 | `contains_unbounded` | 42.096 / 55.714 / 59.415 | 44.540 / 55.070 / 58.950 | +5.80% / -1.16% / -0.78% |
| v2 | `contains_bounded` | 6.872 / 9.259 / 9.692 | 6.707 / 10.236 / 12.047 | -2.40% / +10.55% / +24.30% |

深 OFFSET 在两次独立容器中均改善全部百分位：P50 下降 **20.34%～23.55%**，
P95 下降 **16.15%～22.99%**，P99 下降 **13.38%～34.73%**。但筛选场景的
尾延迟方向不稳定：无界 contains 在 v1 的 P95/P99 分别退化 **56.50%/55.81%**，
有界 contains 在 v2 的 P95/P99 分别退化 **10.55%/24.30%**。首屏 v2 的当前
P99 也出现单次高尾样本，因此不能把该次 -71.24% 当作稳定收益。

### 13.3 执行计划事实

- 当前深页 list 为 `access_type=ALL`、估算扫描 100,000 行且
  `using_filesort=true`。
- 延迟物化深页将窄键集物化后通过 `PRIMARY` 进行 `eq_ref` 回表，但 MySQL
  `EXPLAIN FORMAT=JSON` 仍估算内层和外层排序处理 100,000 行；计划成本估算没有
  单独证明收益，实际配对延迟才是本结论依据。
- 无界 contains 的候选内层选择时间索引反向扫描并在索引访问中检查
  `RequestPath`，再物化 50 个键回表。两次运行的尾延迟方向相反，说明该形态对
  当前受控环境的缓存、IO 或极端样本敏感，不能作为通用生产形状。

### 13.4 决策

1. 拒绝把通用延迟物化 SQL 落入生产。它没有满足筛选场景 P95/P99 稳定不退化的
   门禁，即使深 OFFSET 改善可重复也不足以扩大到所有查询。
2. 不根据两次 100,000 行实验猜测 Offset、`PathContains` 或其他筛选分支阈值；
   当前矩阵没有覆盖不同基数、并发和全部筛选组合。
3. 停止继续修补 OFFSET。下一阶段应设计显式 cursor API 契约，保留稳定
   `(OccurredAtUtc, Id)` 排序、租户/Host 范围、兼容层和精确总数取舍；contains
   搜索另行服从有界时间范围或专用搜索设施门禁。
4. 本 Task 仅新增隔离 benchmark、严格等价性门禁和 Skill reference；没有修改
   生产 SQL、迁移、公共 API 或 MySQL 运行时行为。

## 14. 访问日志显式游标分页

### 14.1 契约与实现

- 保留 `GET /api/v1/auditing/access-logs` 的精确总数/OFFSET 契约，新增
  `GET /api/v1/auditing/access-logs/cursor`；两者继续使用
  `auditing.access.read` 与 `SqlDataScope.HostOnly`。
- 游标为版本化 Base64Url 二进制载荷，携带 UTC ticks、网络字节序 UUID 和规范化
  筛选 SHA-256 摘要。摘要只阻止游标误用于另一组筛选，不承担授权或防篡改职责。
- SQL Server 使用第一批/后续批次各 32 个固定可选谓词形状；MySQL 使用两个固定
  参数化 Statement。后续批次均按
  `(OccurredAtUtc DESC, Id DESC)` 和二元 keyset 读取 `limit + 1`，不执行 COUNT。
- Vue 与 Layui 首屏均改用游标端点，“加载更多”仅追加服务端有序记录；旧客户端分页
  函数仍保留。
- 未增加数据库迁移、索引、缓存、Broker 或可靠性语义变化。

### 14.2 正确性证据

- 游标单元测试 **8/8**：版本、畸形输入、空 ID、筛选不匹配、双库 SQL、HostOnly、
  无 COUNT/OFFSET、`limit + 1`、下一边界和非法游标零数据库调用。
- 基准契约聚焦 **18/18**，其中新增 `cursor-ab` mode、双库生产 SQL 防漂移和
  keyset 形状断言。
- 共享契约 **4/4**、Vue 聚焦 **3/3**、Layui 聚焦 **2/2**、Vue typecheck 通过；
  OpenAPI 离线契约 **58/58**。
- SQL Server/MySQL 真实 API 各 **1/1**：旧/新列表权限 403、首批、下一批无重复、
  三条相同 `OccurredAtUtc` 记录跨 `limit=2` 页边界后保持 3 个唯一 ID、非法游标
  稳定 400、详情兼容和运行时 OpenAPI 均通过。

### 14.3 10 万行生产等价 A/B

- 工件：
  `BenchmarkDotNet.Artifacts/auditing-query-cursor-ab/formal-100k-production-v2-20260727/`。
- 条件：100,000 行、页面 50、深 OFFSET 99,950、预热 5、成对交替采样 30、
  并发 1；每个 Provider 的两个策略返回总行数、行数和有序 ID 签名完全一致。
- OFFSET 策略模拟旧端点的 COUNT＋深页列表；cursor 策略模拟新端点的单次 keyset
  列表。两者响应语义不同，不能用该结果静默替换需要精确总数的调用。

| Provider | OFFSET P50/P95/P99 ms | Cursor P50/P95/P99 ms | Cursor 降幅 P50/P95/P99 |
| --- | ---: | ---: | ---: |
| SQL Server | 27.698 / 33.160 / 33.742 | 2.037 / 2.667 / 3.916 | 92.65% / 91.96% / 88.39% |
| MySQL | 83.040 / 100.714 / 123.613 | 2.448 / 3.352 / 3.562 | 97.05% / 96.67% / 97.12% |

SQL Server 实际计划中，旧 count/list 分别读取 100,000 行并产生 606/2,348 次逻辑读；
cursor list 读取 50 行、8 次逻辑读。MySQL 旧 count/list 均估算检查 100,000 行，
list 为 `ALL + filesort`；cursor list 为时间/ID 索引 `range`、估算 51 行且无
filesort。

### 14.4 结论与边界

1. 生产落地显式游标端点，并让只需向后浏览的双管理端使用该端点；保留旧精确总数 API。
2. 受控双库样本证明当前 10 万行深页场景显著改善，但不承诺生产固定 QPS、SLA 或所有
   筛选基数下的相同降幅。
3. 无界 contains 并未因游标而变成可扩展搜索；仍须时间范围、专用搜索设施或独立证据。
4. 后续若修改排序键、筛选规范化、UUID 物理语义或索引，必须重跑游标兼容和双库 A/B。

## 15. MySQL Jobs 多 Worker 领取死锁

### 15.1 RED 与根因

- 修复前全量 Integration 在
  `Host_job_definition_and_trigger_follow_contract_with_mysql` 失败：32 个并发任务中
  1 个被记录为 `Failed`，应用日志中的原始异常为 MySQL deadlock。
- `SHOW ENGINE INNODB STATUS` 的最近死锁图显示，领取
  `UPDATE ... ORDER BY CreatedAtUtc, Id LIMIT 8` 先持有
  `IX_fn_jobs_execution_PendingLease` 再等待目标 `PRIMARY`；成功终态更新先持有同一
  任务的 `PRIMARY`，因修改 `Status/LeaseExpiresAtUtc` 再等待该二级索引。
- 这是稳定的主键/二级索引锁顺序反转，不是容器噪声；禁止通过重试测试或放宽最终
  `Succeeded` 断言隐藏。

### 15.2 修复

- MySQL 8 领取改为 `ICommandTransaction` 内的短事务：先按稳定顺序
  `SELECT Id ... FOR UPDATE SKIP LOCKED`，再按已锁定主键集合更新租约，最后按
  Lease 读取本批记录并提交。
- 终态更新已持有主键时，领取者跳过该行而不是等待；Handler 与定义批量读取仍在领取
  事务提交后执行。
- SQL Server 继续使用既有 `UPDLOCK`/`READPAST`/`ROWLOCK` 单语句领取，未改变其
  运行语义。

### 15.3 新鲜聚焦验证

| 验证 | 结果 |
| --- | ---: |
| Jobs Runner/Hosted Unit | 7/7 |
| MySQL Jobs 多 Worker（连续独立运行） | 3/3 |
| SQL Server Jobs 多 Worker | 1/1 |
| 项目 Skills 契约 | 33/33 |

### 15.4 最终全量验证

- Release `dotnet build Full.NET.slnx -c Release --no-restore --nologo`：
  **0 warnings / 0 errors**。
- Unit **454/454**、Compatibility **7/7**、Architecture **49/49**。
- 修复后完整 SQL Server/MySQL Integration：**191/191**，失败 0、跳过 0，
  耗时 **27m 32s 569ms**；TRX：
  `Full.NET.IntegrationTests-full-20260727-234733.trx`。
- Governance **11/11**、Performance Governance **3/3**、Naming **23/23**、
  SQL Safety **5/5**、Workspace 与 `git diff --check` 通过。
- Skill 演进：契约先因缺少“锁顺序”和 `SKIP LOCKED` 按预期失败；更新
  `rules/performance-engineering.md` 与 `$fullnet-performance-hardening` 后，
  项目 Skill **33/33** 且官方 `quick_validate.py` 通过。

## 16. MySQL Outbox 领取锁顺序硬化

### 16.1 RED 与锁等待证据

- 在现有 SQL Server/MySQL Outbox 租约恢复用例中加入终态锁竞争：消息租约过期后，
  独立 `ReadCommitted` 事务执行生产等价的 `MarkProcessed` 更新并保持未提交，另一
  Scope 同时调用 `AcquireAsync`，要求 3 秒内返回空集合。
- SQL Server 的既有 `UPDLOCK`/`READPAST` 路径通过；修复前 MySQL 在旧
  `UPDATE ... ORDER BY OccurredAtUtc LIMIT 1` 处被取消，抛出
  `OperationCanceledException: Query execution was interrupted`。RED 工件：
  `tests/Full.NET.IntegrationTests/bin/Release/net10.0/TestResults/OutboxMySqlTerminalLock-RED.trx`。
- 阻塞期间 `SHOW PROCESSLIST` 将领取语句标记为 `System lock`；
  `SHOW ENGINE INNODB STATUS` 显示领取事务等待
  `IX_fn_outbox_message_Pending` 上的排他记录锁，终态事务同时持有目标行锁。由此排除
  容器启动、连接池和测试夹具问题，并确认旧领取与终态更新存在不一致锁顺序。

### 16.2 修复与语义边界

- MySQL 8 领取改为现有 `ICommandTransaction` 内的短事务：先按
  `(OccurredAtUtc, Id)` 稳定顺序执行
  `SELECT Id ... FOR UPDATE SKIP LOCKED`，空集合直接返回；非空时按已锁定主键集合
  更新 `LockId/LockedUntilUtc/Attempts`，再按本次 Lease 读取消息并提交。
- 终态更新持锁时，领取者跳过该行而不是等待命令超时。Handler、终态更新和外部调用
  仍在领取事务之外；`OutboxEnvelope`、租约、尝试次数、重试与 Dead Letter 契约不变。
- SQL Server 领取 SQL 未修改；没有新增迁移、索引、公共 API 或可靠性语义。

### 16.3 新鲜聚焦验证

| 验证 | 结果 |
| --- | ---: |
| MySQL Outbox 终态锁竞争（连续独立运行） | 3/3 |
| SQL Server Outbox 终态锁竞争 | 1/1 |
| Release 构建 | 0 warnings / 0 errors |
| Unit / Compatibility / Architecture | 454/454、7/7、49/49 |
| Governance / Performance Governance | 11/11、3/3 |
| Naming / SQL Safety | 23/23、5/5 |
| 项目 Skills / 官方 Skill 校验 | 52 + 33 项、通过 |
| Workspace | 通过 |

### 16.4 最终全量验证

- 完整 SQL Server/MySQL Integration：**191/191**，失败 0、跳过 0，
  耗时 **30m 06s 792ms**；测试进程退出码 0，结束后 Docker 容器为 0。
- `git diff --check` 通过；工作区保留本性能硬化分支的累计变更以及用户既有
  `.cache/`、`.tmp/art-design-pro/`，未提交、未推送。

### 16.5 规则与 Skill 复盘

- 本次无新增规则：`rules/performance-engineering.md` 已明确要求数据库队列领取与终态
  更新保持一致锁顺序，SQL Server 评估 `UPDLOCK`/`READPAST`，MySQL 8 在短事务内以
  `FOR UPDATE SKIP LOCKED` 锁定候选后按主键更新，并禁止用测试重试掩盖死锁。
- 本次无 Skills 变化：`fullnet-performance-hardening` 已覆盖同一判断流程、Provider
  分支、事务边界与多 Worker 验证要求；Outbox 只是第二个真实消费者，没有暴露新的
  触发范围、异常路径或稳定工作流缺口。项目契约与官方校验继续通过。

## 17. Outbox 显式有界并发与独立消息作用域

### 17.1 RED 与失败归因

- 配置与双库测试先于实现落地。首次构建只出现
  `CS0117: OutboxWorkerOptions` 不包含 `MaxConcurrency`，证明配置能力尚不存在；
  补齐属性和 Validator 后，SQL Server/MySQL 聚焦均在等待第二个 Handler 进入时
  `TimeoutException`，证明旧 `foreach` 路径只能串行处理。
- RED 使用四条同路由消息、Scoped 闸门 Handler、`BatchSize = 4` 与
  `MaxConcurrency = 2`。失败发生在释放闸门之前，不来自数据库启动、迁移、租约领取
  或终态更新。
- `dotnet test <csproj>` 在当前 MSTest.Sdk/Microsoft.Testing.Platform 入口下使用
  该类名过滤时返回零测试和退出码 5；改为直接执行已构建测试 DLL，并保留
  `--minimum-expected-tests` 后，聚焦 12 项被正确发现。该问题没有通过放宽门槛掩盖。

### 17.2 实现与语义边界

- 新增 `OutboxWorker:MaxConcurrency`，默认 `1`、有效范围 `1..16` 且不得超过
  `BatchSize`。默认值继续在原批次 Scope 内按领取顺序串行处理，不改变既有部署行为。
- 显式配置大于 1 时，Worker 先完成 backlog 采样和数据库租约领取并释放批次 Scope，
  再用 `Parallel.ForEachAsync` 执行本批。每条消息创建独立 Async Scope、Host 租户
  上下文、Scoped Handler 与 `IOutboxStore`，禁止跨并发任务共享数据库会话。
- 该开关不改变至少一次投递、租约、重试、最大尝试或 Dead Letter 语义，也不提供
  全局完成顺序保证。依赖聚合内顺序的 Handler 必须保持并发 1，或先设计显式顺序键；
  Handler 仍须幂等。
- 当前 Outbox 不续租。并发度不是长耗时 Handler 的租约修复手段；连接池和下游预算
  必须按单 Worker 并发上限乘以副本数评估。

### 17.3 聚焦与多轮证据

| 验证 | 结果 |
| --- | ---: |
| Outbox Unit | 12/12 |
| SQL Server 租约、终态锁竞争与有界并发 | 1/1 |
| MySQL 租约、终态锁竞争与有界并发（连续独立运行） | 3/3 |

双库并发路径均观测到峰值并发恰为 2、四个不同 Scoped Handler 实例、四次处理和四条
成功终态。该证据只证明并发上限、作用域隔离与数据库终态正确，不声明生产固定吞吐、
P95/P99 或连接池容量。

### 17.4 最终全量验证

- Release `dotnet build Full.NET.slnx -c Release --no-restore --nologo`：
  **0 warnings / 0 errors**。
- Unit **454/454**、Compatibility **7/7**、Architecture **49/49**。
- 完整 SQL Server/MySQL Integration：**191/191**，失败 0、跳过 0，耗时
  **29m 37s 667ms**，进程退出码 0；TRX：
  `Full.NET.IntegrationTests-full.trx`。
- Governance **11/11**、Performance Governance **3/3**、Naming **23/23**、
  SQL Safety **5/5**、Workspace 通过。
- 项目 Skill 契约 **52 + 33** 项、`fullnet-performance-hardening` 官方
  `quick_validate.py` 通过。

### 17.5 规则与 Skill 复盘

- 本次无新增规则：`rules/performance-engineering.md` 已要求并行处理定义顺序语义、
  Handler 作用域、连接池预算和最大并发，并禁止无界 `Task.WhenAll`。
- 本次无 Skills 变化：`fullnet-performance-hardening` 已覆盖同一门禁、租约与双库
  验证流程；实现和运维文档补齐的是具体消费者证据，没有出现新的稳定工作流缺口。
