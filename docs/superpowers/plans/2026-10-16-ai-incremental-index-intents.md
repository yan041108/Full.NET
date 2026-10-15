# AI 文件变更增量意图实施计划

**Goal:** 文件版本确认完成后可靠产生幂等增量索引意图，由现有当前会话入队入口确认有费用的执行。

**Architecture:** AI 导入完成状态与 MemoryPack v1 业务事件同事务写 Outbox。Worker 只写 AI 自有最新来源投影，不读取文件、不请求模型、不保存或接管用户会话；版本列表展示待确认/已入队状态。Files 不可变文件仍经既有 Contract Port 访问。

**Tech Stack:** .NET 10、Dapper、SQL Server/MySQL、MemoryPack、Vue。

## 约束与边界

- main 执行，任务基线 `6ee1771e29590d93c01371564e952c23771d4055`；保护共享工作区无关改动。
- 明确生产者 AI 导入确认、消费者 AI 增量意图；只访问 AI 自有表，Outbox 沿现有受信租户和本地事务。
- 不自动派发模型，不追加费用授权，不修改旧费用、代次、活动指针或原会话；用户沿现有精确权限及会话入口执行。
- 使用已有 LegacyPolling 单一投递所有权，不扩展 CDC/Kafka 范围。重复/乱序/删除由权威文档与不可变版本确认状态收敛。
- 新投影双库成对迁移、可重跑；Host/Tenant 物化与 SQL 保持静态 AOT 闭包。
- 本批不宣称整个 R08、RAG、云模型质量或容量完成；重建默认关闭不变。
- 按用户授权在当前会话集中开发测试，不创建工作树或实现代理。测试资源按共享锁和交接复用。

## 同批交付

- [x] 导入完成与来源确认事件原子写入；同键重复确认不再发事件，失败整体回滚。
- [x] Worker 自然幂等消费，按最新已确认版本创建增量意图；旧消息不覆盖新来源，删除/禁用/租户不符不建立可用意图。
- [x] 双库投影迁移、版本列表状态与中文/英文显示；现有显式入队后状态反映已创建任务。
- [x] 覆盖事务失败、重复/乱序、租户/删除竞争、已有任务；集中运行相关 Unit、双库 Integration、结构和前端检查，AOT 按实际证据分别记录。
- [x] 独立只读审查，检查本批 diff、验证证据并提交；未运行范围保持待验收。

## 文件边界

- `Features/ManageKnowledgeVersions/AiKnowledgeImportStore.cs`：确认事务与 Outbox。
- `Features/IncrementalKnowledgeIndexing/`：具体事件、处理器、投影写入。
- `Persistence/AiKnowledgeIncrementalIndexSql.cs`：范围与最新来源条件。
- 双库 `261_AiKnowledgeIncrementalIndex.sql`：AI 最新来源意图。
- 既有版本读取/静态物化和 `AiModule`：真实消费与状态；Vue 既有版本列按机器码翻译。
- Unit/Integration：只在真实事务和 SQL 层断言可靠性与隔离。

## 实际审计发现的最小安全补丁

官方 NuGet 审计发现 SignalR Redis 回传层间接引入的 MessagePack 2.5.302 命中 GHSA-qhrr-8q5h-9q3h。只在现有中央传递版本固定中增加 2.5.303，Annotations 沿包声明解析，不扩大协议或抑制公告。旧图两个新 ELF 的发布成功及审计失败保留，原生运行尚未执行。

在旧版本用有界的数组/映射头和有效嵌套回归复现后，新冻结图集中验证 Realtime/AI Unit、真实 Redis 跨节点恢复、Notifications 双库修复、AI 增量及迁移、架构和宿主分析；再以新图重新发布 API/Worker，执行双库 JSON SignalR 与增量意图等 Native 专项。新旧图证据分别记录，不将旧产物或旧图结果冒充新依赖验收。

## 本地专项验收（2026-10-16）

旧图成功发布两个 ELF 后，官方包审计发现 MessagePack 2.5.302 命中 [GHSA-qhrr-8q5h-9q3h](https://github.com/advisories/GHSA-qhrr-8q5h-9q3h)，因此旧图 Native 未执行。中央仅固定修复版 2.5.303，Annotations 按声明解析；两个官方包仓库签名验证0。小型数组/映射预算回归在旧302实际2失败/1通过，补丁新图通过；保留原失败，不扩大协议或抑制公告。

- `node artifacts/ai-incremental-20261016/run-security.mjs artifacts/ai-incremental-20261016/acceptance17 artifacts/ai-incremental-20261016/acceptance5 artifacts/cache-hardening/cache-capacity-budget-resource-release-20261016.json 7d4f704857dcbd25c8f912503e03890d53732881b0c9f092e6ca805f219b3ed0` 实际父退出 0。最终 AI/Realtime Unit 186/186、架构 235/235，零失败/跳过；新补丁图双库 11/11，覆盖增量意图4、迁移恢复2、Redis恢复3与通知修复2，零失败/跳过。旧图的14项两轮覆盖记录独立保留，不冒充新依赖图结果。
- 命名 33/33、SQL 安全 5/5、多语言 7/7、测试工具 49/49；前端同摘要复用 i18n 3/3、Vue 60/60，类型与生产构建退出 0。未把原后端失败改为成功。
- `node artifacts/ai-incremental-20261016/run-native-security.mjs` 在 SDK 10.0.402、Linux x64 容器中重新发布 API/Worker；compiler/runtime 10.0.13。API/Worker 分析均零警告/错误；发布第三方告警 17/15 项通过原精确门禁，官方 NuGet 包审计 5 范围零发现，不包括框架/运行时漏洞评估。
- 新原生产物专项 6/6、零失败/跳过，覆盖双库 Native API 来源确认、Native Worker 的 Host→Tenant→Host 消费、既有 AI API 与 JSON SignalR 场景。不是整个 44 项核心原生集合的重跑，也不证明真实云模型或 Native Worker 文件解析、完整 RAG、生产容量。
- 冻结输入 6383 项，前后核对通过，仅两个解析工具的锁文件按已验证 SDK13 ILLink 内容摘要发生精确变化；实际构建含共享工作区既有未提交代码。提交只投影本批增量，保留其他对话改动。
- 原编译、Mock、HTTP 夹具、命名/测试配置和租户写入边界名单遗漏等失败记录保留。精确 `IndexRead`、独立信封作用域及删除串行边界经只读复审，最终无可证实 P1/P2。
- 历史导入回填、禁用期间自动补偿、元数据事件与有费用自动派发仍在后续范围；独立索引重建默认关闭，`Capacity-not-verified` 保持。
