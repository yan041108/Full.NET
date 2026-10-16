# AI 文档元数据可靠事件实施计划

**Goal:** 标题或描述成功更新后可靠通知 Worker，幂等补齐最新已确认来源意图；不产生收费执行。

**Architecture:** 文档 CAS 更新、当前响应读取与 MemoryPack v1 Outbox 同一短事务提交。Worker 从可信信封建立独立范围，以有界单文档查询取得当前最新完成来源，复用既有文档锁与单调写入器；删除和停用由权威源即时拒绝，迟到事件不能复活文档。

**Tech Stack:** .NET 10、Dapper、SQL Server/MySQL、MemoryPack、静态 DI 与 Native AOT。

## 边界

- main 基线 `eb4c2d216a2072af3206f7025e01b60ce8dc78ee`，任务快照 `ai-metadata-events-20261016`；保护既有改动及禁止访问路径。
- 当前会话连续开发、集中验收，不创建工作树或实现代理；完成后按现有授权精确提交、不推送。
- 只扩展文档标题/描述更新；不改变 HTTP、JSON、权限、前端、数据库结构、既有来源事件 v1 或知识库配置。
- 新事件 `fullnet.ai.knowledge_document.metadata_updated` v1，只含知识库、文档、已提交文档版本和可信 TenantId。事件不携带正文、标题、描述、用户或会话。
- 成功 CAS 才发事件；不存在、输入无效、版本冲突不发事件。Outbox 失败或等待后范围变化必须回滚全部事务。
- 消费只从 AI 自有表读取一条当前最新已完成来源；事件版本大于权威文档版本时不执行，较旧事件可补齐当前最新来源。查询后再由既有写入器复核最新来源、启用、墓碑和范围。
- 重复、乱序与并发投递自然幂等；不把元数据版本当作文件版本，不覆盖费用、索引代次或会话，不读取文件/模型，不自动派发收费任务。
- KnowledgeBase 名称等继续权威读取，不添加无消费者的泛化事件、缓存通知或无界知识库扇出。部署先升级全部 Worker，再启用新 API 生产者；保留单一 LegacyPolling 投递所有权。

## 同批实现与验收

- [x] 定义成功/冲突/无效/故障回滚、可信范围变化、信封拒绝、缺失或未来版本来源、重复及乱序场景；完成生产者、事件和有界消费者。
- [x] 注册 Worker/Topic、复用静态四列来源读取器，精确登记新增可信范围文件；不扩大架构豁免。
- [x] 扩展既有 SQL Server/MySQL 四个场景，验证真实 HTTP 更新、事务 Outbox、失败回滚、并发消费和删除竞争。四项均在首轮完整结果帧中通过，复用 FullNetApiFactory/SharedDatabaseFixture；Unit 不能证明真实事务和提供程序语义，不进入 messaging-heavy。
- [ ] 扩展既有 Native 双库两项，证明关闭自动扫描时元数据更新由真实 Worker 消费并补齐投影；原来源、显式恢复、自动补偿断言保留。
- [ ] 冻结输入并集中执行相关 Unit、Architecture、治理、双库及 API/Worker AOT/Native；原失败、未执行和继承门禁分别记录。
- [x] 只读复审、精确差异与 UTF-8 检查，更新实际受影响操作说明/活动状态，本次增量提交覆盖开发实现；Native完整验收仍单独待办。

## 文件责任

`ManageKnowledgeDocuments/AiKnowledgeDocumentService.cs` 负责生产者事务；`IncrementalKnowledgeIndexing/AiKnowledgeDocumentMetadataUpdated.cs` 与同名 Handler 管持久协议及可信消费；`Persistence/AiKnowledgeMetadataSourceSql.cs` 管有界双库权威读取。`AiModule` 注册静态消费者和 Topic。新增 Unit 场景并扩展既有 Integration/Native，保持 UID 范围，记录实际发现与结果。

证据保存到 `artifacts/ai-metadata-events-20261016`。首窗以缓存 r4 的暂停资源回执接续；后续修正、续跑窗口逐一绑定前窗真实终态与新资源回执，缓存后续压力等待本批完整窗口结束。真实云模型、收费自动派发、完整 RAG/AI 及容量仍未验收，保持 Capacity-not-verified。

## 当前验证进度

相关 Unit 2824/2824（含新增元数据场景32项）、命名33/33、SQL安全5/5及相关治理81/81实际通过，零失败/跳过。首轮架构234/235，唯一失败是新增精确路径未按字典序放置；只调整该行顺序后，架构235/235实际通过，未扩大清单成员。

修正快照 source4 只有架构测试文件发生变化，复用 source3 已通过的 Unit；首轮架构失败、原始结果及输入差异完整保留。AI双库首轮沿用低内存时的单worker设置，达到51分钟预算后取消，实际运行退出3、父退出1；原会话报告58项通过、零失败/跳过，但整集未完成。

使用与实际消费者摘要一致的官方 `Microsoft.Testing.Extensions.TrxReport` 2.3.2读取器，从58个完整持久帧恢复58个Passed UID，包含本批新增双库四项。原二进制流、终态和摘要保留，不制造原会话TRX或单窗全部通过结论。启动attempt3前确认2547个消费者文件摘要保持一致，按44个缺失UID使用仓库默认双worker补验；Unit、架构及已有58项不重复运行。

attempt3 双worker补验期间，MySQL预算场景的进程全局 MeterListener 接收到并行SQL Server场景的已提交指标。只修正测试观察：独立W3C Trace隔离、另一个Trace真实预留/零用量结算的反向验证；原物理提交前零指标、异常回滚、提交后指标与费用断言全部保留，生产账本和指标不变。该修复经只读复审未发现确定P1/P2。

宿主随后关机并于2026-10-16 14:38:41（Asia/Shanghai）重新启动，attempt3父进程与监督进程未写出终态，实际退出码未知，不能补记为0或1。官方读取器恢复41个完整结果帧：40项通过、上述1项失败；旧预算SQL Server通过项因共享断言已修改而不复用，39项可复用。原状态、失败与中断保留，未制造TRX。

source5相对source4只改上述测试辅助文件，生产、Unit和架构输入不变；仅两个预算入口调用该辅助。attempt4重新编译Integration，定向5/5通过（零失败/跳过，实际运行退出0），包含两个预算提供程序；AI双库影响集按58+39+5共102个唯一UID并集覆盖，不称单窗全过。命名33/33、SQL安全5/5及相关治理81/81在source5重新通过。

API/Worker AOT分析均通过，零警告/错误；Unit与架构消费者未变，Integration仅DLL/PDB因上述测试辅助修改而重新编译。开发验收通过，精确提交13个受影响文件，不推送。Native核心实际发现44项，但发布前官方NuGet审计首次TLS握手被重置，父进程实际退出1，尚未生成本批ELF或执行Native运行测试；保留该失败并接续网络定位，不升级本批为完整验收。

canonical分片计数仍有继承的6项漂移（migrations +2、infrastructure +4），严格计数门禁退出1，UID分片并集及迁移子组结构验证通过；没有执行整套1269项运行测试。共享冻结工作区不代表干净main、完整AI/RAG或容量验收。
