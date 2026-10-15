# AI 知识索引独立重建批次实施计划

**Goal:** 将 R08 的 `rebuild` 恢复提示交付为所有者显式确认、可幂等重放且不覆盖旧凭据的完整操作。

**Architecture:** 保持 AI 模块、现有 TXT/PDF/DOCX Worker、Dapper 和 AI 本地事务。新任务采用独立应用端 UUID v7；来源版本不变，以单调代次区分同一来源的重建。旧活动指针仅在新任务经过既有完整清单、向量与结算证明后原子切换。

**Tech Stack:** .NET 10、SQL Server/MySQL、System.Text.Json 源生成、Vue 3、生成式 TypeScript 客户端、MSTest、Vitest、Playwright。

## 执行约束

- 用户已授权在 main 连续开发，关联功能集中实现、集中测试、完成后提交，不推送。
- 按仓库 AGENTS.md 直接执行本批，不自动创建工作树、子代理或额外会话。
- 起始 HEAD 为 `e241102465d09a95eda8e3c67e07776f46de5104`；复用任务快照 `ai-native-sdk-security-r12j`，另外保存本批目标文件的修改前摘要。不得将其他对话的脏文件纳入提交。
- R12x 的 7894 项冻结后端输入及 R12aa 的 18 项冻结浏览器输入保持不变；本批新代码不借用其验收结论。
- 重型验收保持现有顺序：R12x SDK402 → 缓存夹具双库 2 项 → R12aa 浏览器 18 项。新批次在该队列之后验收；独立轻量编译和纯单元测试使用独立输出及普通锁。
- 身份与 Files Contract 调用不进入 AI 事务；不得查询其他模块的表，事务中不得调用模型。
- `reserved`、未结算或 `unknown` 外部费用保持阻断，不通过新任务绕过未知副作用。
- SQL Server 与 MySQL 的迁移、幂等、并发、半完成迁移恢复和租户行为必须成对验证。

## 已确认的调用链与设计

1. `246_AiKnowledgeIndexTask.sql` 的来源唯一约束目前为 `(VersionId,DocumentVersion,KnowledgeBaseVersion)`。不得删除旧任务、覆盖账本、伪造文件新版本或修改文档并发版本来获得重建空间。
2. 新增 `Generation`（既有任务为 0）、`RebuildRequestId` 和 `RebuildPreviousTaskId`。新唯一约束保留来源维度并追加代次；请求键按文档隔离。新任务仍由 `IIdGenerator` 分配主键。
3. 新请求携带 `clientRequestId`、`expectedTaskId`、`expectedDocumentVersion`、`expectedKnowledgeBaseVersion`、`confirmAdditionalCost`。租户、用户、来源、模型和预算由服务端推导。
4. 所有者必须具备现有页面/来源/入队权限、索引读取权限及独立 `ai.knowledge_documents.index_rebuild`。新交互会话可创建新代次，不能接管旧任务的冻结会话。
5. AI 短事务先锁定文档及当前来源，核对预期版本、最后任务和终态，再原子创建新任务与冻结会话。同键同载荷同会话复用已创建任务；同键不同载荷、其他会话及过期快照返回冲突。
6. `queued`/`running` 不允许重建；失败、取消或完成任务仍须通过当前来源、格式、模型批准、未知费用门禁。拒绝时不得写入任务、会话或账本。
7. 新代次入队后，旧代次不能再领取、恢复或写入派生数据；单独保留活动代次的读取证明，不能把“最新可写任务”作为“活动可读代次”。
8. `AiKnowledgeRetrievalSql` 已读取活动指针，但 TXT/PDF/DOCX 的激活证明最终仍使用 `AiKnowledgeIndexStatusSql.For`（最新任务）。重建必须同时增加活动代次查询并更新这三处证明；历史答案的批量来源复核也须绑定活动代次。
9. 就绪状态和重建任务状态分别表达：管理页可显示排队/失败的新任务；检索和已有答案在旧代次仍满足当前来源及模型批准时继续使用完整旧代次。新任务损坏、崩溃或取消不改活动指针。
10. Vue 使用对账得到的精确版本和任务，明确确认可能新增费用；同一请求未知结果后的重试保留请求键，切换文档/会话后清除。禁止自动重建、自动重发或跨资源处理迟到响应。
11. 默认关闭重建写入口。迁移兼容旧数据，但产生同源多代任务后，旧程序的单行绑定查询不再兼容；启用前必须完成 API/Worker 全实例升级。回退先关闭入口并停止新代次派发，保留兼容读取程序与历史数据；禁止直接回退到旧单任务二进制或删除新代次。
12. 新代次序号的分配及“最新任务”判定不得依赖时间戳或 UUID 排序。MySQL 目标表更新与子查询的限制必须用真实语句验证，不能只凭 SQL 文本守卫通过；涉及自引用更新时使用可证明物化的静态查询形状或同文档锁内的条件写入。

## 任务 1：独立代次重建纵向切片

**Files:**

- 新增：`src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/260_AiKnowledgeIndexRebuild.sql` 及 MySql 对等文件。
- 新增：`src/Modules/Full.NET.Modules.Ai.Contracts/AiKnowledgeIndexRebuildRequest.cs`。
- 新增：`src/Modules/Full.NET.Modules.Ai/Features/ManageKnowledgeIndexing/AiKnowledgeIndexRebuildService.cs`。
- 新增：`src/Modules/Full.NET.Modules.Ai/Persistence/AiKnowledgeIndexRebuildSql.cs`。
- 新增：`src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeIndexRebuildOptions.cs`，默认 `Enabled=false`，注册强类型配置及启动校验。
- 修改：同目录 `Endpoint.cs`、`AiModule.cs`、`AiAuthorizationContributor.cs`、`AiKnowledgeDocumentPermissions.cs`、`AiJsonSerializerContext.cs`。
- 修改：`AiKnowledgeIndexTaskSql.cs`、`AiKnowledgeIndexSessionSql.cs`、`AiKnowledgeIndexWorkerSql.cs`、`AiKnowledgeIndexRetrySql.cs`、`AiKnowledgeIndexActivationSql.cs`，所有恢复/派发/完成写入都拒绝已被接替的任务。
- 修改：`AiKnowledgeIndexStatusSql.cs`、`AiKnowledgeIndexActivationStore.cs`、`AiKnowledgeIndexPdfActivationStore.cs`、`AiKnowledgeIndexDocxActivationStore.cs`、`AiKnowledgeAnswerHistoryGuard.cs` 的读取证明选择，保留完整权限与范围复核。
- 修改：`ui/admin/src/components/AiKnowledgeIndexDialog.vue`、`ui/admin/src/api/ai-knowledge.ts`、中英文词条、生成式 OpenAPI/SDK 契约及精确权限目录。
- 测试：新增 `tests/Full.NET.UnitTests/Ai/AiKnowledgeIndexRebuildTests.cs`；扩展三种格式的活动证明、历史答案、恢复/租约、SQL 物化器/守卫以及 Vue 对话框测试。
- 集成：在现有 AI SQL Server/MySQL 测试类中增加同一组真实场景，复用现有 Factory，不增加新业务项目。

**Interfaces:**

- 新请求：`AiKnowledgeIndexRebuildRequest(Guid ClientRequestId, Guid ExpectedTaskId, int ExpectedDocumentVersion, int ExpectedKnowledgeBaseVersion, bool ConfirmAdditionalCost)`，未知 JSON 字段拒绝。
- 新服务：`Task<Result<AiKnowledgeIndexEnqueueResponse>> RebuildAsync(Guid knowledgeBaseId, Guid documentId, Guid versionId, AiKnowledgeIndexRebuildRequest request, SessionBindingSnapshot original, CancellationToken token)`。
- 新操作：`POST /api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index/rebuild`，成功 202；使用既有标准 ProblemDetails 与 `ai.knowledge.index_conflict`。
- 成功响应继续为已存在的 `AiKnowledgeIndexEnqueueResponse`；202 仅确认稳定入队，不代表 ready 或收费完成。

- [x] 已编写行为测试：Host/租户、两种交互会话、新登录创建独立代次、全部权限拒绝、同键重放、载荷/会话冲突、源变更、并发竞争、未知费用阻断、第二笔失败回滚、取消与授权等待后漂移。
- [x] 实现成对可恢复迁移。列、默认值、检查和索引分别探测；新唯一约束创建后再移除旧约束，半完成重放不能丢失来源唯一性。既有任务、批次、结算与活动指针不回填改写。
- [x] 实现短事务重建与精确 Endpoint/源生成/权限目录。对同文档竞争进行串行化；所有跨模块授权在事务外，并在提交后响应之前再次复核。
- [x] 实现代次写入防护与活动代次读取。先覆盖“新任务排队/失败期间旧索引仍可检索及核对历史答案”的回归，再完成原子切换与旧代次回收的整链验证。
- [x] 贯通生成客户端及 Vue 显式确认流程，覆盖权限撤销、重复点击、未知结果保留幂等键、资源切换及迟到成功/错误。
- [x] 已集中执行 AI Unit、Vue AI Unit、类型检查/构建、命名/SQL/契约/相关架构并记录实际发现和结果；AI 首轮 5 项失败的同源复查单独保留，不描述为单次全量通过。
- [x] 本批双库影响集 50/50、浏览器 7/7 已通过；新 SDK402/runtime13 API/Worker 已发布并验证本批 Native 路径。Native 首轮 40/42，修正退役夹具完整元数据后原失败 2/2 复查通过，不能描述为单次全量零失败。
- [x] 按 `requesting-code-review` 只读复审资金、隔离、幂等、代次读取和迁移恢复；修复重要发现后再集中验证受影响场景。
- [x] 实现批次已在 `main` 提交 `ffa23d0f7f5819b747d8502e5f6a5d49f2ff9fb9`；提交前精确暂存本任务差异，`git diff --check` 通过，保留共享工作区的既有改动。验收补充继续单独记录和提交。

## 必须取得的证据

| 场景 | 必须验证的结果 |
| --- | --- |
| 同一来源重建 | 新 UUID/代次、独立批次键和新冻结会话；来源文件版本、旧任务与账本逐字段不变 |
| 相同请求重放与并发 | 只创建一个新任务/会话；同键不同载荷拒绝，无第二次收费派发 |
| 新旧任务竞争 | 新代次创建后旧失败任务不可重试；迟到旧租约不可写入或切换指针 |
| 未知费用 | 任一相关未结算/未知操作均阻断新代次，不释放旧预留、不篡改费用 |
| 旧代次继续读取 | 新代次 queued/running/failed 时，TXT/PDF/DOCX 检索和历史答案均使用完整活动证明 |
| 原子切换 | 全部新载荷及已结算凭据有效才切换；损坏、取消、异常、崩溃保持旧指针 |
| 权限及租户 | 新权限独立控制；撤权、Host/租户转换及来源/模型变化均失败关闭 |
| 半完成迁移 | 旧结构升级、每一步部分完成后未记账重放，双库均收敛且保留历史数据 |
| Vue 交互 | 明确确认费用、未知结果同键重试、禁止重复点击、切换后不污染其他文档 |

## 状态

2026-10-15：生产入口、独立权限与默认关闭配置、静态 JSON、同文档事务/请求幂等、双库 260 迁移、代次写入栅栏、活动代次读取及 Vue 显式费用确认已实现。已编写双库并发、账本不变、未知费用、撤权、活动证明与原子切换，以及迁移恢复用例；核心双库 HTTP 与迁移恢复 4/4 已通过；扩大双库影响集 50/50 与浏览器 7/7 已通过；本批 Native AOT 已完成本地专项验收（包含失败修复后的定向复查），精确范围与保留边界见 23:12 节点；默认关闭开关继续保持。

本地验证：前端 AI 20 文件 254/254、Vue 类型检查与生产构建退出 0；命名/说明/UUID 门禁 33/33，SQL 安全 5/5，OpenAPI 211/211。后端模块、Unit 与 Integration Release 编译均退出 0、零警告/错误。AI 正式发现 UID 集合为 4878，首轮 4873 通过、5 个 DOCX 子进程用例超时；同源单独复查该类 26/26，包含全部 5 个失败 UID。原失败保留，不能表述为单次全量零失败。证据目录 artifacts/r08g，源清单分别 6307 项且前后未变；这些是当前共享工作区冻结输入，包含既有未提交改动，不能外推为干净 main 检出结果。

只读复审发现并修复 MySQL INSERT 目标表子查询与重建权限撤销后的后台继续执行两个问题；复审回看未发现新增 P1/P2。代次由数据库任务投影并经静态物化，附加权限覆盖会话、派发/续租及 TXT/PDF/DOCX 激活；历史读取继续使用当前读取者授权。浏览器新增费用确认、未知结果同键重试、精确权限和权威任务变更场景已于 20:49 实际执行，7/7、零失败/跳过/重试，真实父阶段退出 0。

18:28 系统关闭电源导致一次验证中断，真实父退出 1073807364；保留日志和系统事件，复用已通过构建后只重编译受影响项目，未把中断计为通过。R12ad 的 Native35 同样没有完整终态，原生结论继续待验收。


19:23 验证节点：`runtime-first-four` 实际 4/4、零失败/跳过，覆盖 SQL Server/MySQL 各自的 Host/租户 HTTP 重建与迁移恢复。原任务/会话/批次/账本/载荷保留、同键并发重放、未知费用阻断、两项附加权限撤销、旧活动证明保留及新指针原子切换均在受控 Files/模型边界下通过。真实父退出 0，新增容器自然清理并释放正式锁。`architecture-closure` 在补齐独立模板和 CI 文件清单后 235/235；原 230/235 缺输入失败保留，新输入清单 6311 项，不修改原冻结文件。多语言检查 7/7、测试矩阵与影响集工具检查 49/49。新增浏览器 7 项、既有索引/历史/清理的扩大双库回归及新 Native AOT 路径继续待验收，不据核心 4 项升级为完整 Verified。

20:49 起集中验收：`expanded` 的浏览器阶段 7/7 已通过，扩大双库影响集发现 50 个唯一 UID，运行尚未结束，不计为通过。随后 `artifacts/r08h` 独立冻结 6311 项输入，计划以 SDK402/runtime 10.0.13 重新发布 API/Worker，使用真实 HTTP 的新代次准入及幂等重放扩展既有 Native PDF/DOCX 场景，并覆盖原生 Worker 的新代次恢复；该阶段仍待执行。未借用缓存对话的原生产物：3380 项生产输入中有 28 项不一致。

21:19 扩大验收完成：`expanded` 双库实际 50/50、零失败/跳过，耗时 29 分 24 秒；发现的 50 个唯一 UID 与 TRX 逐一一致，父进程 60812 实际退出 0，新增容器自然清理并正常释放正式资源。浏览器 7/7 同时完成独立复核，证据 `artifacts/r08g/expanded-proof.json`。R08h 于 21:19:41 取得后续完整资源窗口，6311 项冻结输入初始核对、SDK402 探测均退出 0，开始 API/Worker 分析与新发布，Native 结果尚未取得。

23:12 本地专项验收完成：SDK 10.0.402 下 API/Worker AOT 分析均退出 0、零警告/错误；同一生产源码的架构 235/235、测试消费者编译退出 0。API/Worker 新 Native AOT 发布退出 0，77 个资产图确认编译器与运行时为 10.0.13，发布告警分别 17/15 项且精确门禁通过，不能表述为零发布告警。官方 NuGet 审计 5 个解析范围零发现，未据此声明框架运行时漏洞评估或容量结论。

当前同一过滤器实际发现 42 项，旧 35 UID 全部保留，新增缓存/Worker 租户/日志 7 项；原 35 清单不匹配、Node 引导遗漏和 Linux 脚本 CRLF 三次预检失败均保留。新生产源码 6311 项冻结输入始终未变，API/Worker 重新发布，不借用缓存对话或旧 R12x 原生产物。Native 首轮实际 40/42、零跳过，父退出 1；两项失败来自 DOCX 退役夹具默认首代与新场景的唯一键冲突。首次修夹具又因遗漏正代次配对字段触发检查约束，失败 2 项同样保留。最终只改测试夹具：复制真实 Generation 与前任务，为正代次另生成请求键，首代保持空绑定；生产代码与约束未改，复用同一两个 ELF，仅重编消费者，原失败 UID 2/2、零失败/跳过，真实父 88060 于 23:12:27 退出 0。汇总覆盖 42 个唯一 UID，`singleRunAllPassed=false`，不将复查汇总写成单次 42/42。

真实 Native HTTP 覆盖 PDF/DOCX 新代次准入、未确认费用/未知字段拒绝、同键重放、旧任务/会话保留、状态/对账及历史读取；PDF 原生 Worker 恢复包含新代次，共享会话/权限/物化边界真实运行。完整清单、向量和已结算凭据由受控夹具构造，不代表真实云模型、Native Worker 文件解析或完整 RAG 质量。复查同时执行 Host 新代次与租户首代持久化/退役分支。证据为 `artifacts/r08h-native-fixture-recheck2/aggregate-acceptance.json`、`resource-handoff.json`；两个产物 SHA 与原发布一致，6311 项输入最终核对退出 0、零源码漂移，新增容器自然清理、正式锁正常释放。容器 4 CPU/12 GiB、.NET 处理器 2、MSTest 顺序执行；共享工作区冻结输入包含既有未提交改动，不能外推为干净 main 或容量验收，保持 `Capacity-not-verified`。
