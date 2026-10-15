# AI 增量意图恢复实施计划

**Goal:** 所有者分批恢复历史确认来源与目录停用期间漏记的增量意图，继续由现有交互入口确认收费执行。

**Architecture:** 新增精确恢复权限及知识库级 POST，以当前可信范围、所有者、启用状态和最新已确认来源分页。逐文档复用原短事务和文档锁，不改事件 v1，不写收费任务或会话。Vue 一次请求一页，明确继续/重新扫描。

**Tech Stack:** .NET 10、Dapper、SQL Server/MySQL、静态 AOT 物化、生成 TypeScript 客户端与 Vue。

## 执行边界

- main 基线 `69e282e6233256f64087c82328bf71bd67850f2e`；任务快照 `ai-incremental-recovery-20261016`，官方入口通过运行时保护过滤排除三个禁止访问路径，工具源不变。
- 在当前会话集中开发和测试，不新建工作树、实现代理或独立执行流程；按用户既有授权开发完成后提交，不推送。
- 只读写 AI 自有数据，无迁移与跨模块表访问；权限授权在事务外，意图写入在单文档短事务中。
- 返回条数 1..50，按数据库内 DocumentId 稳定游标；扫描不是全局原子快照，来源变更/失败可从头幂等重扫。
- 操作独立授权 `ai.knowledge_documents.index_recover`；同时要求页面、版本及索引读取权，所有者与可信租户由服务和 SQL 校验，成员不能借读取权执行恢复。
- 不自动启用知识库、不自动恢复索引收费任务，不覆盖已有费用/会话/索引代次；独立重建仍默认关闭。
- 授权以每文档事务准入为边界；撤权不追溯取消已经开始的当前条，最多该条继续完成，下一条拒绝准入且响应403。真实双库覆盖锁等待期间撤权，不在AI事务内调用身份模块，也不承诺全局权限线性化。

## 同批交付

- [x] `AiKnowledgeIncrementalRecoveryTests` 先定义拒绝、分页、权限等待后撤销/范围变化、幂等计数及无收费写入的场景，再实现服务与静态 SQL。
- [x] 增加请求/响应、精确权限、Endpoint、AOT JSON 与行读取器；写入器返回实际变化并保护等待后的范围。
- [x] 生成契约和客户端、知识文档页分批恢复入口及中文/英文提示；切目录/会话后清空游标，不接收过期响应。
- [x] 真实双库验证历史缺投影、停用/恢复、重复、最新来源、跨租户/非所有者与删除；Unit 无法证明 SQL 锁与实际作用域，复用 FullNetApiFactory/SharedDatabaseFixture，不进入 messaging-heavy。
- [x] 功能完成后集中运行影响集、客户端及 AOT；原失败保留，原生发布与运行按本批新输入单列证据。只读复审后核对本批差异并提交。

## 文件与验证

生产入口位于 `Features/IncrementalKnowledgeIndexing/`，SQL 位于 `Persistence/AiKnowledgeIncrementalRecoverySql.cs`，DTO 位于 `Full.NET.Modules.Ai.Contracts/AiKnowledgeIncrementalRecoveryContracts.cs`。权限、模块注册、JsonSerializerContext 与静态行读取器同步修改。前端复用 `AiKnowledgeDirectory.vue`、`api/ai-knowledge.ts` 和 `admin-i18n/knowledge-processing.ts`；客户端从真实 OpenAPI 导出核对后生成。

验证命令与实际退出码保存到 `artifacts/ai-incremental-recovery-20261016`。先运行 `pnpm test:integration:affected:plan -- --snapshot ai-incremental-recovery-20261016 --phase slice`（使用同一保护加载器）；按合并 UID 的 AI 双库影响集执行。集中验证 Unit、架构、生成零漂移、前端组件/类型/构建与 API/Worker AOT 分析，原生产物另记。共享工作区既有改动保留，不将冻结快照外推为干净完整 main 或完整 AI 验收。

## 验收记录

- `backend3`：同源并核对 353 个单测二进制后复用 `backend2` 的 Unit 2743/2743；增量写入与恢复的双库影响集 8/8、Architecture 235/235，新 Unit20/Integration4 的 UID 全覆盖。API/Worker AOT 分析均为退出0、零警告零错误，后端父进程实际退出0且资源释放。
- `frontend4`：两库真实 OpenAPI 的操作与两个 DTO 一致，生成零漂移退出0；Client276/276、Vue64/64、类型检查与生产构建退出0。原 i18n4、命名33、SQL5及本地化7的输入范围未变化，保留先前通过证据。
- 首轮单测夹具的命名空间/可空编译失败已修正；完整 AI 命名空间100因37分21秒仅完成47项而主动取消，不计整组通过。前端资源探针失败和无测试启动的等待进程中断独立保留，均不计通过。
- 锁等待撤权用例是“真实数据库＋可控权威授权 Port”的服务集成，不外推为实际角色撤权或 HTTP403 的端到端验收；授权边界仍为逐文档事务准入。
- `native1`：新发布 API/Worker 的双库原生 2/2 通过，父进程实际退出0；两份 ELF、10.0.13 发布资产及既有精确警告门禁通过（API17/Worker15），5个 NuGet 审计范围均无发现。新进程日志已归集，容器、卷占用与测试锁已清理释放。后端源码与最终前端快照仅差真实契约及两个生成文件，桥接证据保存在 `contract-source-bridge.json`；不是干净 main 或完整 AI 模块的全量认证。
