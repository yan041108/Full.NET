# 五项性能修复验证

基线提交 `49345c3c9823360071433adb4928e09691a35d62`，分支 `main`；任务快照 `performance-fixes-20261019`。本批保留此前安全、资金和分层任务的工作区修改，没有提交、推送或部署。实现顺序以两个 P1 为先，计划见 `docs/superpowers/plans/2026-10-19-performance-remediation.md`。

## 实现边界

1. Workflow 恢复来源按种类稳定排序并限制 BatchSize，按现有 OpenOccupancyKey 排除 pending/failed/dead_lettered 占用，唯一索引继续保护并发插入，保留插入去重。发现间隔与消费排空解耦，默认 15 秒；失败也退避。
2. Identity 提供集合授权，一次读取会话和权限快照，冻结全部必需权限并逐一判定。AI 原有读取前后和输出前的重检调用保留，不跨检查点缓存。
3. Notifications 默认串行，大于 1 时使用有界并发，每条投递独立 DI Scope、租户上下文、DbSession 和事务；原续租、超时、幂等键和终态 CAS 保留。
4. Regions tree SQL 限制父节点、相对深度和返回预算；超过 10,000 节点以稳定错误码 / 422 拒绝。Vue 改为 children 懒加载，刷新替换表格代次并取消旧请求。
5. Document、ImportExport、Reporting 满批立即继续；未满、空批和异常按 Poll 退避，保持动态配置和取消行为。

## 已取得证据

本机为 Windows 10 Pro 10.0.19045，Intel Core i7-12700H（14 核 / 20 逻辑处理器），约 64 GiB RAM；Node 24.12.0、.NET SDK 10.0.402。Docker Linux VM 上限约 19.29 GiB / 20 CPU。数据库镜像为 SQL Server 2022 CU14 与 MySQL 8.0；实际服务器版本为 SQL Server 16.00.4135、MySQL 8.0.46，已写入 TRX。数据库容器与其他历史验证共用，结果仅反映此次本地范围。

| 检查 | 实际结果 |
| --- | --- |
| 新增及关联 Release Unit | 34/34 通过，0 跳过；构建 0 警告、0 错误 |
| Vue 完整 Unit | 285 文件、1293/1293 用例通过 |
| Vue typecheck 与生产构建 | 通过 |
| Vue 包体预算 | 3/3 通过 |
| 命名 / SQL 安全 / 性能治理 | 34/34、5/5、67/67 通过 |
| 真实栈 Playwright 供给器契约 | 43/43 通过 |
| Integration 工具链 / 治理 | 164/164、85/85 通过 |
| 双库主影响集与定向修正 | 首轮 345 项为 341 通过、2 夹具失败、2 凭据跳过；修正后两项性能用例及关联四项双库回归均通过，详见下文 |
| 完整 Release Unit 首轮 | 8955 项：8952 通过、2 测试替身失败、1 平台跳过；8m38s。两项替身修正后，关联 5 项复测通过 |
| Linux API / Worker Native AOT publish | 两者成功；9 / 7 条既有允许清单内警告；可执行文件 139,710,400 / 88,756,616 bytes |
| Linux Release Integration 构建 | 0 警告、0 错误 |
| API / Worker AOT analyzers | 均通过；Worker 已恢复普通 JIT 产物 |
| Architecture 首轮及修正复测 | 首轮 237 通过、2 失败；精确登记及静态声明修正后关联 4/4 通过，0 跳过 |
| 最终静态 SQL 预算 | 6/6 通过，0 跳过；Release 构建 0 警告、0 错误 |
| Linux Native 外部进程验收 | 4/4 通过，0 跳过；API 关键 HTTP 与 Worker 恢复投影各覆盖双库 |
| 双库真实栈 Playwright | 6/6 通过，0 跳过、0 重试后通过；覆盖 API、页面懒加载 CRUD 与受限账号 403 |

Vue 初始静态图 69 个 chunk：minified 1,493,686 bytes，gzip 389,128 bytes，Brotli 323,971 bytes。相对于仓库预算，minified +4.71%、gzip +3.24%，均未超过 5% 门槛。Regions 页面 chunk `AdministrativeRegionsView-Dkl8SWQh.js` 为 48,434 / 15,612 / 14,160 bytes（minified / gzip / Brotli）。这些是当前产物尺寸，不是本批前后收益对比。

初轮默认前端并行执行出现 App 测试超时，停止该轮后以 `--maxWorkers=2 --testTimeout=15000` 完整复跑通过。新增 C# 测试曾暴露可空标注、内部投影的动态代理限制及四个旧集成测试替身未实现新契约，均已修正；构建期间修改测试源码的一轮被入口拒绝登记，没有使用该产物冒充验证成功。

测试矩阵按实际发现数补齐：本轮新增 20 项 Unit、2 项双库 Integration；另补上此前分层批次未登记的 6 项 Integration，canonical full 为 1342、infrastructure 为 294。没有降低发现阈值或过滤失败用例。

主要可重复入口：`pnpm test:integration:affected -- --snapshot performance-fixes-20261019 --phase merge`；Vue 完整回归为 `pnpm --filter @fullnet/admin test -- --maxWorkers=2 --testTimeout=15000`，另运行 typecheck、生产构建与 `pnpm test:bundle-budgets`。C# 使用 Release 产物，最小发现阈值按更新后的矩阵检查；后续定向重跑不替代已选中的双库影响集。

性能用例在同一提供程序、数据库、数据集内比较旧查询形状与候选实现；授权、Workflow 与 Regions 一次预热后各取 5 个样本。样本很少，所报 P95/P99 均为样本最大值；不代表稳定尾延迟。Workflow 比较已有占用下的 3 次扫描加 108 次无效插入与源端排除后的 3 次扫描；Regions 新增 1004 条夹具，连同 14 条 Baseline 比较 1018 行全量物化与局部 1 行读取；授权比较同一边界四权限的 SQL 次数。通知仅比较串行与并发 4 各一组 8 条同类慢提供程序投递，不计算尾延迟。查询计划为 SQL Server 正式执行 SQL 的缓存编译计划 / MySQL EXPLAIN，不是实际 CPU、IO 或全系统负载测试。没有新增索引或索引写放大；没有据此声明生产 QPS、万人在途或容量等级提升。

沿用用户授权的独立只读审查检查了全部五项实现和后续增量。审查发现 MySQL 计划采集连接未带 Binary16，已改为同一正式连接字符串策略并经只读复核确认，实际查询原本使用的执行器不受影响；计划和性能用例已在修正后重新采集并通过。后续还将满扫描判定改为三类数量的最大值，回归明确覆盖“每类 1、BatchSize=2、合计 3 也不能重复扫描”；另独立检查实际持久化 108 条任务，不能用返回值变更掩盖漏插。没有遗留已知的实现审查阻断项。最后还复核了八条静态 SQL 声明与原构造器的文本、名称及 Scope 完全等价；全局 SQL 目录精确拆成六个提供程序条目并增加占用、状态、排序及批次片段约束。

## 双库本地对照

服务器实际版本为 SQL Server `16.00.4135`、MySQL `8.0.46`。下表为 Windows Release JIT，同库同数据集交替测量；SQL Server 取最终单项成功复测，MySQL 取六项复测中的成功结果。每格为旧形状 → 候选，单位 ms；最大值同时作为这五个样本的 P95/P99，不表示稳定尾延迟。Regions 基线只测旧全量查询及物化，不含旧 C# 构树；候选调用局部 GetTreeAsync，不能据此视为完整 HTTP 请求的前后基准。

| 场景 | SQL Server P50 | SQL Server 最大 | MySQL P50 | MySQL 最大 |
| --- | --- | --- | --- | --- |
| 同一边界四权限 | 10.03 → 2.60 | 12.55 → 3.06 | 11.15 → 2.41 | 13.14 → 2.62 |
| 已有 108 项占用的恢复发现 | 91.82 → 7.07 | 116.87 → 7.45 | 138.83 → 4.29 | 143.79 → 6.31 |
| 局部区域、相对深度 1 | 2.42 → 0.71 | 3.28 → 1.23 | 3.29 → 1.89 | 5.72 → 2.91 |

对应结构计数：Host 四权限由 8 次读取变 2 次；恢复发现由 3 次来源读取加 108 次无效插入变为 3 次读取、0 插入，并独立断言 108 条任务实际持久化；区域查询由物化 1018 行变为 1 行，另以 MaxNodes=2 的大分支查询验证数据库返回预算。

通知使用测试 Adapter，每次发送等待 150 ms：SQL Server 的 8 条串行 / 并发 4 分别为 1558.42 / 391.16 ms，MySQL 为 1676.60 / 460.66 ms；两库实测峰值均为 1 / 4，每条终态 sent 且恰好一次 Attempt，没有重复幂等键。这是每档一组批次，不计算分位数、不代表真实 SMTP / 短信服务吞吐。三个其他 Worker 仅验证满批 / 部分批 / 空批与动态配置的调度边界，不宣称吞吐实测。

MySQL 恢复占用使用 `UX_fn_workflow_recovery_task_OpenOccupancy` 的 eq_ref，区域锚点和子节点使用 ParentId 索引；SQL Server 区域计划包含 ParentId Index Seek、递归 Spool 与 Top，恢复小夹具选择了聚簇索引扫描，不能报告为已证明占用索引 Seek，也不能把返回预算等同于物理读取行数上限。本轮没有据小夹具强制索引提示或增加写放大；大基数 IO、锁等待、CPU、分配和生产负载仍未实测。

SQL Server 计划采集最初使用 SHOWPLAN 会话状态加 QueryFirst，未得到计划行；已改为按数据库 ID 和完整 SQL 文本读取正式执行器刚运行的缓存编译计划，找不到必须失败，成功记录见 `performance-sqlserver-plan.trx`。缓存编译计划属于优化器估算结果，参考 [Microsoft SQL Server 计划文档](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-exec-query-plan-transact-sql?view=sql-server-ver16)。MySQL 使用与正式策略一致的 Binary16 连接采集 EXPLAIN；两库原始计划完整保存在 TRX。

## 既有依赖审计问题

`pnpm audit:clients` 返回 1：`security/client-audit-policy.json` 中 uni-app 工具链的 Vite 例外 `GHSA-fx2h-pf6j-xcff` 于 2026-10-18 到期。本轮未修改该依赖或延期安全例外，审计不计为通过。后续用户授权的 2026-10-20 独立批次已修复该问题，未延期例外；见 [客户端依赖审计修复](client-dependency-audit-remediation-2026-10-20.md)，本段保留本批当时的失败事实。`pnpm licenses list --prod --json` 返回 0；未新增第三方依赖或改变许可边界。

## 后续集中验证记录

双库主影响集使用本轮首个 Release Integration 构建，实际结束于 2026-10-20：341 通过，SQL Server / MySQL 性能用例各失败一项，SMTP / 阿里云短信实际凭据未配置各跳过一项。两个失败均为新增 Workflow 性能夹具给必填 FormVersionId 写入 NULL；已补齐同模块表单定义、发布版本及引用链，不放宽数据库约束。重新构建后六项双库测试为 5 通过、1 SQL Server 计划采集失败；MySQL 性能、双库 Regions 生命周期及恢复 Worker 四项均通过。修正 SHOWPLAN 采集方式后，SQL Server 性能单项再次新构建通过，因此六个用例均有成功结果。组合证据覆盖所选 345 项中的 343 项成功与 2 项凭据跳过；没有把任何首轮失败日志改称成功，也未宣称跑过完整 1342 项集成矩阵。

完整 Unit 和原生构建的首次排队曾因默认 30 分钟等待超时退出，那两次尚未开始实际执行；仅延长排队预算后重排，测试执行超时、范围及发现数门槛不变。Linux Native AOT 发布与测试构建均成功，原生外部进程四项全部通过：SQL Server / MySQL 的关键 HTTP（含新增区域及非空 tree）和 Workflow 恢复后 Inbox 投影。构建容器 4 CPU / 10 GiB，运行容器 4 CPU / 8 GiB，只读挂载源码、复用既有缓存，完成后 --rm 退出；不是完整 Native / Worker 矩阵或并行通知 Native 实测。最后仅将八条 SQL 的构造器移动到静态字段，SQL 文本 / 名称 / Scope 未变，独立只读审查确认等价，并以新构建的架构与 SQL 预算定向检查验证；原生证据复用等价实现，没有重复昂贵发布。

Architecture 首轮 239 项为 237 通过、2 失败、0 跳过：通知新 Host 作用域尚未登记到精确文件清单；SQL 静态声明方式与 Workflow 全局 SQL 目录未同步。已将通知文件精确登记并说明可信持久任务 / finally 清理边界；SQL helper 只返回原字符串，八个 SqlStatement 显式静态构造，目录由原三条精确拆成双库六条并加强片段检查，没有增加目录通配、忽略项或运行时构造豁免。四项相关架构检查已在最终新构建中 4/4 通过、0 跳过；其中两项首轮失败均取得成功结果，其余首轮 237 项的实现边界未变。最终六项 Workflow / Regions SQL 预算检查也以新构建 6/6 通过、0 跳过。

真实页面初轮 SQL Server 的懒加载 CRUD 与受限账号拒绝访问通过，API 测试因旧夹具 datasetVersion=e2e-时间戳不符合正式数字版本规则而返回 400。夹具改为有效 2.0.0，预览断言严格要求 200；生产校验未放宽。随后双库各 3 项全部通过，0 跳过、0 flaky，正常完成 teardown。最终的命名 34 项、SQL 安全 5 项、性能治理 67 项与治理 85 项也在静态声明 / 目录增量后复跑通过。当前性能样本不足以声明生产容量提升，保持 `Capacity-not-verified`。

完整 Unit 逐项核对后，两个失败仅为新增并行通知用例设置 NSubstitute 返回值时触发内部投影类型的动态代理限制，尚未进入生产执行边界；已改为显式 IQueryExecutor 领取替身，保留全部并行取消、失租、作用域隔离和无载荷读取断言，关联 5 项新构建复测已通过，0 跳过，结果为系统临时目录 fullnet-layering-tests/performance-notification-lease.trx。集合授权 10 项、Workflow 扫描预算 4 项、Regions 预算 2 项、AI 集合检查点 1 项、Worker 排空 1 项均在完整运行中 Passed。唯一跳过为 Windows 无法执行的 Linux FIFO 替换句柄场景。

排队脚本在最后 5 项测试加入前已读取最小数 8591，实际启动参数如实保留；此次无过滤发现 8955，已重新按当前矩阵 8596 核验，并逐项确认新增 20 项全部被发现（其中两项替身失败如上），没有以旧最小数代替当前门槛。原始完整结果为系统临时目录 `fullnet-layering-tests/performance-unit-full.trx`，日志为 `fullnet-performance-unit-full.log`。

## 原始记录与入口

原始结果保存在本机系统临时目录 `C:/Users/Administrator/AppData/Local/Temp`（运行时 ADMINI~1 为同一目录的短路径）：

- 主影响集：`fullnet-performance-integration-validated.log`；当时输出为 Release TestResults 下 `Full.NET.IntegrationTests-affected-combined.trx`。2026-10-20 后续批次复用该标准文件名前，按原始开始时间和 345 项结果核对后保全至系统临时目录 `fullnet-layering-tests/performance-first-batch-affected-20261020.trx`，未用后续结果覆盖历史证据。
- Unit：`fullnet-performance-unit-full.log`、`fullnet-layering-tests/performance-unit-full.trx`；修正替身后为 `performance-notification-lease.trx`。
- 双库复测：`fullnet-performance-followup.log` / `fullnet-layering-tests/performance-authority-recovery.trx`；最终 SQL Server 为 `fullnet-performance-plan.log` / `performance-sqlserver-plan.trx`。计划正文及所有样本均在 TRX StdOut。
- AOT / Architecture：`fullnet-performance-gates.log`；最终边界复测为 `fullnet-performance-boundary.log` / `fullnet-layering-tests/performance-architecture-boundaries.trx`。
- 最后静态 SQL 声明的预算复测：`fullnet-performance-budget.log` / `fullnet-layering-tests/performance-static-sql-budget.trx`。
- Native：`fullnet-performance-native-build.log`、`fullnet-performance-native-runtime.log`；结果为复用缓存 `fullnet-review-native-2gnp8ed6/test-build/bin/Full.NET.IntegrationTests/release/TestResults/performance-native.trx`。
- Browser：`fullnet-performance-browser.log`，结构化结果在 `fullnet-performance-browser-final/SqlServer.json` 与 `MySql.json`；初次失败 trace 保留在 `fullnet-performance-browser`。
- 前端：`fullnet-performance-ui-bounded.log`、`fullnet-performance-ui-build.log`、`fullnet-performance-bundle.log`；最终轻量门禁为 `fullnet-performance-{naming,sql,performance,governance}-delivery.log`。

Architecture 使用 `node scripts/testing/run-dotnet-test-suite.mjs architecture`，C# 定向复测按现有 `withTestRun` / `prepareTestBuild(mode: fresh)` / `enterHeavyTestPhase` 进入资源门禁，Release DLL 的 `--filter` 精确选择相关类并检查发现数量。Playwright 使用既有真实栈入口、`host-administrative-regions.spec.mjs --project vue-admin`，分别指定 SqlServer / MySql，未跳过全局 setup / teardown。原始失败与后续复测分别保存，不覆盖失败证据。

## 交付核对

2026-10-20 02:39（Asia/Shanghai）核对：五项实现与集中验证已完成，两个 P1 优先交付；所有本批发现的断言失败均修正并取得新构建复测成功。完整 Unit 组合证据为 8954 个不同用例成功、1 个 Windows 平台跳过，Architecture 组合覆盖 239 项；这是首轮及定向修正的组合结果，不是改写首轮退出码或再跑一次完整套件。独立只读复核无遗留阻断项。既有客户端依赖审计仍失败，外部提供程序凭据跳过及容量未验证保持明确记录。`git diff --check` 无空白错误，分支 / HEAD 未变，保留此前工作区改动，未提交或推送。

## 本地产物边界

复用既有系统临时原生构建缓存与已有真实栈构建目录，未复制整仓或新增根目录 artifacts。原生缓存此前清理的 `blocked by policy` 原命令和拒绝保存在 `docs/verification/project-review-remediation-2026-10-19.md`，本轮不换解释器重试删除。Vue 构建输出清理曾按规定执行预览 `pwsh -NoProfile -File C:/Users/Administrator/.codex/scripts/clean-build-output.ps1 -RepositoryRoot G:/wwwroot/github_fork/Full.NET`，脚本以 `Missing package.json next to the output; refusing unknown checkout layout.` 拒绝该布局；未扩大目标或自行删除 `ui/admin/dist`，不宣称这些产物已清理。

## 2026-10-20 第二轮五项修复

任务快照 `performance-followup-20261020`，分支及基线同上。开始前工作区已有 214 项差异；继续保留此前任务实现。此节独立记录本轮结果，不将上一轮证据当作本轮新实现的验收。

- ImportExport / Reporting：有界原生 UUID 游标跨轮推进，停用候选不占据永久首批，最多一次回卷；领取和执行前仍读取当前租户状态。
- Files：只读取直接子目录，单父 1000 项预算、创建深度 32、超限显式 422；直接演进 `children` 到 `hasChildren`，同步生成客户端和 Vue 懒加载。取消旧请求与 generation 同时保护成功和错误回写。
- Identity：角色版本 CAS 后锁定完整新旧用户并集，JSON 集合写入、每 1000 项分块轮换独立安全戳并撤销所有旧会话；Result 失败和异常均回滚同一本地事务。不同角色并发用例提供本地交错证据，不能据 `ORDER BY` 声称全局无死锁。
- Document：整轮版本尝试预算包括失败；有效保留历史同时遵守最小版本数，避免不可删除首批候选。
- Notifications：每波仅领取可执行槽位，整轮批次上限不变；续租、取消、外发前核验和终态 CAS 保留。

已授权的独立只读复核发现并修正了完整并集锁定前的分阶段锁死锁路径、最小保留版本阻塞候选及旧目录请求错误回写竞态。没有省略不变成员的撤销，也未缓存租户活动或会话授权结果。

本轮首轮聚焦 Unit 为 139 项中的 137 项成功、2 项失败：测试代理不能访问内部 Document 行类型，改为显式 IQueryExecutor 替身后，139/139 成功、0 跳过，Release 构建 0 警告/错误。另一次构建因补充测试期间输入变化被冻结门禁拒绝登记，未运行该产物。Integration 两次初始编译分别暴露测试友元缺失、原生断言可空标注，均修正后取得零警告构建；没有将这些失败报告为通过。

Vue 聚焦单测最终为 2 文件、13/13 成功（含刷新取消竞态）；共享客户端契约为 66 文件、290/290。Vue typecheck、生产构建通过，初始静态图 69 个 chunk：minified 1,494,809 bytes、gzip 389,595 bytes；三项现有包体预算均通过，未修改预算。目录契约及兼容性定向检查最终 24/24 成功，精确允许已授权的递归字段替换和唯一新增 parentId UUID 查询，仍拒绝删除认证、删除其他属性、改写 hasChildren 类型、额外参数和必填参数。只读复核指出的通用可选 query 白名单已移除，未以通配豁免掩盖契约变化。

轻量检查已取得治理 85/85、工具链 153/153、命名 34/34、SQL 安全 5/5、性能治理 67/67；分区核对 1342 项无遗漏/重复。影响集入口额外运行的工具链 164 项和治理 85 项也通过。OpenAPI 全组首轮另有两项既有失败指向 `ui/admin/src/api/log-timeline.ts` 缺少 Vue 覆盖 manifest，属于本轮五项之外，保留失败事实；目录契约失败则已修正并复测。

当前批次未新增索引、第三方依赖、安全例外或兼容层。没有同环境完整旧实现 A/B 延迟/CPU/IO/分配测量，不把工作量预算、局部查询计划或回归通过解释为生产吞吐提升，保持 `Capacity-not-verified`。

本轮宿主为 Windows、20 逻辑处理器、63.7 GiB 内存；Docker VM 显示 19.29 GiB。JIT Integration 使用 Release、MSTest 2 workers；原生构建和运行分别限制 4 CPU / 10 GiB、4 CPU / 8 GiB。资源与负载发生器共享本机，测试结果不能代表独立生产节点。

双库首轮在真实目录查询中发现两项 Host 文件管理失败（MySQL、SQL Server 均 HTTP 500）。最小复现使用当前 Dapper 与 DataTableReader，确认 `HostFolderTreeNode` record 构造器的布尔参数无法匹配 SQL CASE 的整数列，原伪查询单测未覆盖这一映射边界。已改为内部 `HostFolderChildRecord` 属性投影再映射公开 DTO，AOT 物化器同步，SQL 和公开契约不变；新增真实 Dapper 的 Int32/Int64、分支/叶节点与空父级回归。Unit 发现门槛增为 141（全局 8607），修正后的聚焦 Unit 与双库四项（文件管理、性能场景各双库）集中复测；原生发布等待这两个前置成功后才启动。原首轮继续完成其他独立场景，不把旧 DLL 上的失败改称成功。

完整首轮为 274 项：224 通过、4 失败、46 跳过（44 项 Windows 不能执行的 Linux Native 用例、2 项外部提供程序凭据缺失），耗时 1h16m。四项失败是两库 Host 文件管理、两库性能综合场景；MySQL 性能场景已实际通过三档通知断言后遇到同一目录映射问题，SQL Server 场景在冻结时钟下未领取到任务。SQL Server 的 NextAttemptAtUtc 为 datetime2(6)，冻结的高精度 Now 可能比舍入后的可执行时刻更早；测试改用真实时间加偏移，不放宽生产领取/租约条件，仍在每次模拟发送推进 50 秒。角色撤销场景先于模拟时间推进，并新增旧令牌撤销前必须返回 200 的前置证明，避免以未来 nbf 拒绝冒充撤销验证。

运行时 OpenAPI 首次导出测试本身通过，但规范化因 manifest 中五个 EnterpriseRequest 示例操作缺失而失败；原文档工厂未组合示例模块。已同步两库文档工厂的模块集合，不从 manifest 删除操作、不以手工快照代替运行时导出。修正后将重新构建导出；API/Worker 生产组合不因此添加示例模块。

目录映射修正后的聚焦 Unit 141/141 通过，0 跳过。API 和 Worker AOT 分析均通过。完整 Architecture 发现角色成员服务为分块排序直接引用 `System.Data.SqlTypes.SqlGuid`，违反业务模块不得依赖 ADO.NET 类型的规则；已改为沿用双库锁定查询的返回顺序，并追加缺失旧用户，撤销集合不缩小。既有千人成员用例新增逆序查询结果的序列断言，只读复核无新增阻断；最终新构建 Unit 与该架构规则复测单独记录，不能将先前 141 项成功覆盖到此后的实现。

### 第二轮集中复测与真实锁证据

目录映射和时钟修正后的双库四项先取得 3 成功、1 SQL Server 角色成员并发死锁。死锁图显示一方持角色 PK 的 X 锁等待用户 U 锁，另一方持用户 U 锁并在集合插入的外键校验上等待角色 S 锁；实际缓存计划确认角色父表采用 PK Scan，用户父表为 PK Seek。只在 SQL Server 的该集合 INSERT 加 `OPTION (LOOP JOIN)`，不取消外键、不引入全局互斥或重试。无写入的同库 SHOWPLAN 对照及最终生产执行器缓存计划均确认两条外键按 UserId、RoleId 做 PK EQ Seek。该处理与 [Microsoft SQLCAT 对外键扫描阻塞的分析](https://download.microsoft.com/download/0/F/B/0FBFAA46-2BFD-478F-8E56-7BF3C672DF9D/SQLCAT%27s%20Guide%20to%20Relational%20Engine.pdf)一致；hint 作用于该语句所有 JOIN，此处无其他显式业务 JOIN，不扩大到其他语句。

保留原 12 轮独立作用域的不同角色并发场景，不吞异常、不重试；另在两库实际创建 1001 个 Host 用户，经生产服务两次替换，验证跨 1000 项分块、成员数量、1001 个独立安全戳及不变成员再次轮换。失败 Result 和中途异常仍回滚角色版本、成员、安全戳和会话；同一令牌撤销前可用、撤销后拒绝。最终四项 4/4 成功、0 跳过，耗时 2m15.748s，两库上述并发和大集合断言均执行成功。此证据只覆盖所测交错和集合规模，不声称全局无死锁或生产容量。

首轮 274 项与最终四项的组合覆盖为 228 项成功、46 项平台/凭据跳过；没有再次全跑 274 项，也没有把首轮失败退出码改称成功。增加回归期间，一次双库构建被输入冻结检查拒绝登记，该产物未执行；冻结源码后重新构建并完成上述复测。最终 SQL hint 后的新构建 Unit 141/141、0 跳过，架构定向 1/1 成功；与首轮其余 238 项形成 239 项架构组合成功证据。

真实页面首轮 SQL Server 为 2 成功、1 失败：目录展开已通过，但新增页面登录按 `SingleSessionPerClient` 撤销了此前 API 令牌，后续上传返回 401。将页面登录移到全部 API 操作之后，未改变生产会话策略。最终 SQL Server/MySQL 各 3/3、共 6/6，0 跳过、0 flaky，覆盖真实三级目录逐层加载、文件元数据、受限账号 API 403 和导航裁剪。另有模拟接口浏览器 2/2，独立记录，不代替真实栈。

两库运行时 OpenAPI 导出各 1/1 成功，规范化、客户端生成及零漂移检查成功；四个生成 TypeScript 文件 SHA256 与此前已测版本完全相同，复用 Vue 13 项、共享契约 290 项及类型/构建/包体证据。完整 OpenAPI 最终为 211/213，两项失败仍指向本批之外的 `log-timeline.ts` 覆盖清单缺失。最终 SQL 安全 5/5、性能治理 67/67；治理最新为 84/85，工作区 `Full.NET.slnx` 中 07:17 修改的四个 `Type="Website"` 目录项目使静态闭包测试报 EISDIR，未修改该任务外差异或增加忽略。前述较早的 85/85 不覆盖这次解决方案变化。当前 `pnpm audit:clients` 与 `pnpm licenses list --prod --json` 均返回 0，无新增或延期安全例外。

本轮原始记录均在系统临时目录：

- 主影响集：`fullnet-performance-followup-integration-complete.log`、`fullnet-layering-tests/performance-followup-affected-20261020.trx`。
- 四项复测：原死锁结果为 `fullnet-performance-followup-mapping-final.log`、`performance-followup-mapping-before-deadlock-fix.trx`；最终为 `fullnet-performance-followup-mapping-deadlock-final.log`、`performance-followup-mapping.trx`，两份 TRX 均在 `fullnet-layering-tests`。
- 死锁/计划：`fullnet-performance-followup-deadlock.xml`、`fullnet-performance-followup-insert-plan.xml`、`fullnet-performance-followup-insert-loop-plan.xml`；最终正式缓存计划在四项成功 TRX 中。
- Unit/架构：`fullnet-performance-followup-delivery.log`、`fullnet-layering-tests/performance-followup-unit-delivery.trx`、`performance-followup-architecture-delivery.trx`；完整首轮架构为 `fullnet-performance-followup-gates.log`。
- 真实页面：`fullnet-performance-followup-real-browser-final.log`、`fullnet-performance-followup-real-browser-final/{SqlServer,MySql}.json`；首轮失败 trace 保留于不带 `-final` 的同名目录。
- 契约：`fullnet-performance-followup-snapshot-final.log`、`fullnet-performance-followup-openapi-runtime-final.log`。原导出失败、代理限制、源码冻结拒绝及其他首轮失败日志分别保留。
- 最终轻量检查：`fullnet-performance-followup-{sql,performance,governance}-delivery.log`、`fullnet-performance-followup-audit-clients.log`、`fullnet-performance-followup-licenses.log`。

执行入口沿用 `pnpm test:integration:affected -- --snapshot performance-followup-20261020`。修正后的 C# 定向组通过现有 `withTestRun`、`prepareTestBuild(mode: fresh)`、Release DLL 和发现数量门槛执行；四项 filter 为两个 `PerformanceApi*Tests` 及双库 `Host_file_management_follows_contract`，Unit 使用矩阵 `performance-followup` 选择。真实页面调用既有 `host-files.spec.mjs --project vue-admin`，两库各选择加载列表、受限账号、虚拟目录三个完整场景，不跳过全局 setup/teardown。契约入口为 `snapshot-client-openapi.mjs --update --no-build`、生成和 `--check`；未手工代替运行时导出。

Native 首轮 API、Worker 发布和 Linux 测试构建均成功，API 9 条、Worker 7 条依赖警告通过既有门禁。但四项运行结果为 2 成功、2 失败：两库 Worker 均成功，两库 API 均在非空目录接口返回 500，进程记录 `NotSupportedException`。去除 DTO 的递归 `Children` 后，原先间接生成的 `IReadOnlyList<HostFolderTreeNode>` JSON 元数据消失，Mapper 仍以该声明类型返回；数组元数据不能代替接口元数据。新增无反射回归先实际失败于 `GetTypeInfo` 返回 null，再显式登记这一列表类型，根/叶序列化和全部 141 项 Unit 新构建通过。未开启反射、改写 Mapper 或变更公开响应类型，独立只读复核确认调用链和最小修复。

原生首轮日志保全为 `fullnet-performance-followup-native-{build,runtime}-before-json-fix.log`，TRX 为复用缓存 `fullnet-review-native-2gnp8ed6/test-build/bin/Full.NET.IntegrationTests/release/TestResults/performance-followup-native-before-json-fix.trx`；对应原生进程故障日志仍在该缓存的 `runtime-artifacts`。最小复现为 `fullnet-performance-followup-json-red.log` / `fullnet-layering-tests/performance-followup-json-red.trx`，最终 Unit 为 `fullnet-performance-followup-unit-json-final.log` / `performance-followup-unit-json-final.trx`。

修正后的 API/Worker Native AOT 发布、Linux 测试构建及警告门禁均通过（API 9 条、Worker 7 条既有允许警告，未扩大允许清单）。最终原生运行四项 4/4 成功、0 失败、0 跳过，耗时 5m55.522s，覆盖两库关键 HTTP（含非空目录根/子查询和批量角色成员写入）及两库 Workflow 恢复到 Inbox 的独立 Worker 流程；不是完整 44 项 Native 矩阵或原生通知容量实测。API/Worker 可执行文件分别为 139,760,352 / 88,794,344 bytes，不据此推断请求性能。最终日志为 `fullnet-performance-followup-native-{build,runtime}.log`，TRX 为同一缓存 TestResults 下 `performance-followup-native.trx`；核对原始计数为 executed=passed=4，failed=notExecuted=0。原生测试使用当前源码只读挂载与既有缓存，未复制整仓；容器正常 `--rm` 退出。

2026-10-20 09:38（Asia/Shanghai）交付核对：本轮五项实现、针对失败的修正、集中验证和已授权独立只读复核均完成，无本批已知实现阻断。原生构建前后对 7388 项非文档输入（含未跟踪源码和隐式项目 `.user` 状态）摘要一致：`90f602f4c70c6aa01c4c2169cd6a297bc64909b063cffd6a3ef0c7777a029ab6`。最后只补验证与计划文档，生产和测试源码未再变化。分支仍为 main、HEAD 未变，保留其他工作区差异，未提交、推送或部署。

本批目标回归通过不等于全项目门禁全部通过：OpenAPI 两项清单失败、解决方案目录项目的一项治理失败以及影响集 46 项平台/凭据跳过均按前述事实保留。容量仍为 `Capacity-not-verified`。根 `artifacts` 目录及存量内容为此前产物（目录最后修改为 2026-10-17），本轮不向其中新增；不宣称已清理整个目录。既有缓存清理的命令策略拒绝和 Vue 清理 helper 的布局拒绝继续保留，不绕过重试。
