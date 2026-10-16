# AI 增量意图自动补偿实施计划

**Goal:** 默认关闭的 Worker 自动补齐历史来源及知识库停用期间遗漏的增量意图，收费索引继续显式确认。

**Architecture:** 有界扫描 AI 自有来源元数据，每条创建独立可信 Host/Tenant 作用域，复用现有文档锁和单调版本写入器。进程内串行游标绕回重试失败项，重启或多实例重复扫描由数据库幂等收敛；不保存或接管用户会话。

**Tech Stack:** .NET 10、Dapper、SQL Server/MySQL、Worker 静态 DI 与 Native AOT。

## 边界

- main 基线 `a7ae167050271f65c173eb06a2b57278b161c06d`，快照 `ai-incremental-auto-recovery-20261016`；保护共享工作区和明确禁止访问的路径。
- 当前会话连续实施、集中测试，不创建工作树或实现代理；完成后提交、不推送。
- 不修改事件 v1、261 迁移、HTTP/JSON 契约和前端；自动补偿只写已有来源意图，不触发文件读取、模型、费用、任务或会话。
- 新 Worker 配置 `FullNet:Ai:KnowledgeIncrementalRecoveryWorker`：默认关闭，批量 1..32（默认4），轮询 1000..60000ms（默认10000）；单项30秒发出取消请求，下游须合作取消；满批及失败后仍等待。
- Global SQL 只返回 AI 最小身份锚点，在安全目录逐条登记。扫描不是授权；每条在独立受信范围重新读取启用状态、墓碑和最新已确认来源。
- 游标按数据库 DocumentId 比较，不用 C# Guid 排序。失败向前推进、末页/空页绕回；取消不吞掉，进程重启从头幂等重扫。不保证全库快照、补偿时限或吞吐。
- 不产生新的 Outbox 事件；先前已消费但禁用时跳过的事件保持原收据，补偿独立从权威来源收敛。

## 同批实施与验收

- [x] Unit 定义默认关闭、选项边界、Worker/API 注册隔离、Host 扫描约束、范围变化/取消、异常推进与绕回、同进程互斥和无收费依赖场景。
- [x] 完成配置、游标、扫描 SQL、协调器、Hosted 循环、精确安全目录与闭合日志阶段；复用既有静态四列行读取器。
- [x] 双库集成复用 FullNetApiFactory/SharedDatabaseFixture，编写混合 Host/Tenant 范围、最新完成来源、未完成/删除/禁用拒绝、重新启用、重启与竞争幂等，收费任务/会话不增加的回归。数据库按自身 UUID 顺序扫描，严格 Host→Tenant→Host 顺序由 Unit 验证；两个实例扫描同一候选使用屏障，但不声称测量了数据库内部锁等待。Unit 不能证明真实 SQL/事务，因此新增聚焦 Integration，不进入 messaging-heavy；首轮原8项通过、2项夹具会话失败，修复后精确复查2/2；按10个唯一UID并集验收，原失败保留，不称单次全组通过。
- [x] 扩展既有两项 Native API/Worker 用例，保留显式恢复断言，再由全新 Worker 自动补偿三个范围及重新启用目录；已发现2个原生UID；续跑完成全新API/Worker ELF发布及Native双库2/2，实际范围见下方续跑记录。
- [x] 冻结源码并完成受影响 Unit/Architecture、双库和 API/Worker AOT 分析；保留历史失败与复用证据，只按实际范围报告。
- [x] 完成全新 API/Worker ELF 发布和 Native 双库2/2；前后2547个消费文件一致，原资源失败与既有治理/分区限制保留。
- [x] 完成只读复审及本批精确差异检查；开发交付策略为 main 提交、不推送，保留所有无关改动。

## 文件责任

`Features/IncrementalKnowledgeIndexing/AiKnowledgeIncrementalRecoveryWorkerOptions.cs` 管配置，`AiKnowledgeIncrementalRecoveryCursor.cs` 管单实例进度，`AiKnowledgeIncrementalRecoveryWorker.cs` 管扫描与隔离，`AiKnowledgeIncrementalRecoveryHostedProcessor.cs` 管退出与轮询；`Persistence/AiKnowledgeIncrementalRecoveryWorkerSql.cs` 只查 AI 表。AiModule 只在 Worker 注册循环，Diagnostics 增加固定日志阶段，`global-sql-statements.json` 精确登记新全局扫描。测试位于 Ai 的同名 Unit/Integration 及既有 `NativeApiKnowledgeIncrementalIndexE2ETests`。

实际命令、源码清单、UID、父进程退出与资源释放证据保存在 `artifacts/ai-incremental-auto-recovery-20261016`。共享资源按正式锁和跨对话整窗交接，不能把上一批产物当作新代码验收。完整 AI、真实云模型和容量仍不由此关闭。

## 开发提交时的验证记录

- 最终冻结输入7794项，`input-source5.json` SHA-256 `5fc86019e6b1690a3693e6aa1266ae54a1aba548bf8adb43c28dccb9d1f609c7`；包含共享工作区既有改动，不是clean-main或全AI验收。
- Unit2792/2792，新增补偿24项均通过。Architecture首轮234通过/1失败，根因为两个新可信范围文件漏登记；精确补登并只读复审后，完整235/235通过，严格相等门禁保留。
- 双库首轮8通过/2新夹具失败，均为切换租户后复用旧Host令牌而收到401，尚未执行Worker。只把Host来源的真实HTTP创建移至切换前，随后使用新租户令牌，生产认证和Worker断言未改；精确补跑原失败2个UID，2/2通过。并集合计10个唯一通过UID，`singleRunAllPassed=false`，原组不改为通过。
- 架构补登记时1982个消费文件不变；夹具修复重编后2547个消费文件仅Integration DLL/PDB变化。复用原Unit与Architecture的实际TRX，保留原始失败、身份与摘要证明；没有重复整套构建。
- API和Worker AOT分析均退出0、零警告/错误。命名33/33、SQL安全5/5，冻结共享输入的关联治理81/81；受保护注释审计fixture组未运行。提交候选治理11通过/1既有TRX断言失败，无新增失败；实际五个主分区1269项无遗漏/重复，canonical1263仍有迁移2项、基础设施4项的继承计数漂移，严格入口未过，迁移子分区未补验。
- 新原生发布已开始，但宿主可用提交内存反复低于1GiB；用户选择稍后清理并保留进度。本轮仅结束自有原生容器，容器实际退出143、持有进程句柄的监督观察父进程退出1；新ELF发布与Native运行均未完成，不能报告通过。冻结源码、SDK/NuGet和构建缓存保留，新增容器已清理、正式normal/heavy资源已释放。
- 本地宿主为Windows x64、i7-12700H、64GB；Linux SDK10.0.402，重型容器4CPU/12GiB，托管处理器2、运行时GC堆2GiB，原生发布不设置GC堆上限。没有真实云模型或容量实测，保持Capacity-not-verified。

开发验证汇总为 `artifacts/ai-incremental-auto-recovery-20261016/development-acceptance.json`，尚不是完整原生交付验收。提交结果以Git与 `commit-proof.json` 为准；后续仅接续剩余原生验收，不重跑已验证且输入未变的核心回归。

## Native 续跑验收（2026-10-16）

- 功能提交 `9f27f8ce` 的冻结输入未变化；复用上述已通过的核心回归，发布前后2547个Unit/Architecture/Integration消费文件逐项摘要一致，未重新编译测试程序集。
- SDK10.0.402，API ELF 142201600字节、Worker ELF 91529560字节，分别绑定摘要 `7e185cbb51b274170de4642480ce138117ecb8e93607cf0cdd810cb8eb74d3b0`、`a2979301a8cc7bba6a77c324afdb6227d0af2078e01fc83c9c3c153ae66484ca`。发布、10.0.13编译器/运行时资产及精确警告门禁实际通过。
- Native两个唯一UID在全新API/Worker进程下2/2通过，保留6份本次进程日志。NuGet正式源对五个消费/发布范围审计零包漏洞，未包含框架运行时漏洞评估；不扩大为完整AI、clean-main或容量验收。
- 发布父进程实际退出0，正式heavy运行结果passed；自有容器清理及normal/heavy释放已独立核对。前次中断、候选治理1项既有失败、canonical计数漂移6项和未补验迁移子分区均保留。
- 本次证据：`artifacts/ai-incremental-auto-recovery-native-resume-20261016/native-acceptance.json`；唯一资源回执SHA-256 `c9051e99981ae752e91f3c08ae19afbc297acdbb4967463a22ea3454bd3939ad`。原开发证明保持原样，结论继续为Capacity-not-verified。
