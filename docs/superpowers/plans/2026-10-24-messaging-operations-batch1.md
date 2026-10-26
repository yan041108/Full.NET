# Messaging 运维整改第一批

- 授权：2026-10-24 用户要求按更新后的整改建议实施第一批，关联实现完成后集中测试并直接修复发现的问题。
- 基线：`main / a77902afd87af1662af3b43206d85160eeec566b`，任务快照 `messaging-batch1-20261024`；保留既有文档和 Workflow/VForm 未提交改动。
- 范围：Legacy 死信查询与精确 Id 回队、真实 Kafka DLQ 持久查询投影及单条重放、可信操作者/理由审计、Vue 基础列表与恢复操作。切流证据、真实回退状态机和范围重放持久进度属于后续批次。

## 连续实施顺序

1. **基础设施和存储**：在 Data.Abstractions/Dapper 增加 Legacy 运维端口；在 Messaging.Abstractions 定义 DLQ 投影接收端口，Messaging 模块拥有投影表与双库迁移。Kafka Worker 独立消费已登记 Topic 的 `.dlq`，提交数据库投影后确认 Offset，重复读取按 Broker 位置收敛；保留可重放信封及损坏消息定位资料，不拆分业务 Inbox 事务。
2. **运维用例与安全**：新增两个来源的查询、详情与单条恢复；Legacy 回队在所有权事务门内核对状态并条件更新，不修改载荷或 Attempts；Kafka 重放使用持久信封经过原订阅/Inbox。人工写操作必须具备可信身份及理由，恢复与审计同事务，错误、取消和并发不得留下部分成功。补齐现有切流/回退/单条及范围重放的身份与理由。
3. **前端与契约**：默认展示 Legacy，Kafka DLQ 独立页签，按区域和动作权限展示；详情包含位置和恢复状态，必填理由经确认后提交，禁止无界批量恢复。补充源生成、物化器、OpenAPI/客户端契约及中文/英文文案。
4. **集中验证与修复**：覆盖实际 Legacy Worker/Kafka DLQ 失败到查询和恢复、权限、身份、理由、重复与并发、审计回滚、损坏信封和保留信封重放；对应数据场景做 SQL Server/MySQL 验证。运行受影响 Unit、Vue、契约和架构检查，按受影响路径做 AOT 分析及页面验证。只选择本次变更及必要直接边界，不运行全量。发现失败先定位并修复后复验。

## 交付约束

数据库变更使用同序双库可恢复迁移，生产运行不自动迁移；DLQ 投影默认随显式启用的 Kafka Worker 运行，Legacy 默认部署不需要 Kafka。投影数据库故障不得确认 Broker Offset，损坏信封保留查询记录并拒绝业务重放。全局 SQL 必须登记、参数化并由 Host 精确权限保护；不可用的真实依赖如实记录，不把模拟、跳过或旧证据记为实际通过。任务边界不作为提交或重复验收门禁，不自动提交或推送。

## 实施与验收记录

第一批实现和本地定向验证已完成；共享门禁的既有失败单列，后续批次不在本轮实施范围。已取得以下证据（相关集成共 21 个唯一用例最终通过，零跳过）：

- Unit 最终选择 `OperationalDeadLetterTests|KafkaRangeReplayOperationsServiceTests|MessagingAuthorizationContributorTests|KafkaMessagingOptionsTests|KafkaConsumerGroupThreadTests|KafkaGeneratedRouteResolutionTests`：44/44 通过、零跳过；Release 构建零警告/错误。先前 37 和 43 项均通过；补入异步作用域释放及 UTC 年份边界回归后，仓库输出被并行 Workflow 验收占用，停止本任务尚未取得锁的排队命令，未操作对方锁/进程。最终复用任务临时根已有 `aot` 目录作为独立 JIT 输出：`dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --configuration Release --nologo -m:2 --artifacts-path <任务临时根>/aot -v:quiet`，再执行该目录 `bin/Full.NET.UnitTests/release/Full.NET.UnitTests.dll --no-ansi --progress off --filter "FullyQualifiedName~OperationalDeadLetterTests|FullyQualifiedName~KafkaRangeReplayOperationsServiceTests|FullyQualifiedName~MessagingAuthorizationContributorTests|FullyQualifiedName~KafkaMessagingOptionsTests|FullyQualifiedName~KafkaConsumerGroupThreadTests|FullyQualifiedName~KafkaGeneratedRouteResolutionTests" --minimum-expected-tests 44 --report-trx --report-trx-filename <任务临时根>/unit-final.trx`。
- 真实双库纵向：隔离构建 Integration 项目，`FULLNET_TEST_REPOSITORY_ROOT=G:\wwwroot\github_fork\Full.NET`、`FULLNET_API_SCHEMA_TEMPLATE=0`，执行 `Full.NET.IntegrationTests.dll --no-ansi --progress off --timeout 15m --filter "FullyQualifiedName~MessagingBatchOneTests" --minimum-expected-tests 2 --report-trx --report-trx-filename <任务临时根>/batch-one.trx`：SQL Server/MySQL 2/2 通过、零跳过，4 分 09 秒。覆盖真实 Legacy Worker 失败、真实 Kafka 业务事务失败/发布 DLQ/持久投影、原信封与租户、查询/详情、写权限、理由边界、所有权冲突、并发回队、审计失败共同回滚、重复恢复、损坏信封拒绝、同一 Broker 位置去重及迁移 287 重跑（SQL Server 还模拟索引中断）。
- 相邻 Integration 选择 `MessagingApiMySqlTests|MessagingApiSqlServerTests|EventDeliveryCutoverTests|EventDeliveryRollbackTests`，共 19 个唯一用例最终通过。首轮达到 20 分钟时限，报告 16 通过、1 项 MySQL 迁移初始化超时，2 项 API 尚未计入，整轮未记通过；只补跑超时项与两个 API 用例，两个 API 通过，迁移项仍超时。最后将 `EventDeliveryRollbackTests.MySql_abort_failure_keeps_persisted_producer_fence` 独立执行（`--timeout 5m --minimum-expected-tests 1`），1/1 通过、零跳过，2 分 07 秒；未删除失败断言、提高生产超时或把中止记作通过。保留原 `neighbors.log`、补跑 TRX 和最终单项 TRX。
- `pnpm --filter @fullnet/admin typecheck` 通过；`pnpm --filter @fullnet/admin test -- src/api/messaging-operations.test.ts src/views/HostMessagingOpsView.test.ts`：8/8 通过。生产 `vite build` 通过。
- `pnpm --filter @fullnet/admin-real-stack-e2e test tests/messaging-operations.spec.mjs --project=vue-admin --output=<系统临时目录>`：1/1 通过；真实 SQL Server/Redis/API/Worker 与 Edge，页面无脚本错误，核对双来源响应、切换、提示和分页布局，并人工检查截图。该用例验证页面查询，恢复事务由双库纵向用例负责；不把空列表验收当作恢复验收。
- `node --test tests/openapi/messaging-dead-letters-contract.test.mjs tests/openapi/client-generation-readiness.test.mjs`：9/9 通过。新六接口与四个 Schema 从真实 Integration Host 导出合入；另将运行时与快照规范化为仅六项 Messaging 操作，路径和引用 Schema 深比较一致。保留任务开始时 Workflow 工作区快照及其生成函数，补登记已有设计资源接口，未用运行时旧视图覆盖其未提交契约。`pnpm openapi:client:snapshot --check --offline`、`pnpm openapi:client:generate --check` 通过。
- `pnpm test:naming`：36/36 通过；新双库迁移和两份持久化 SQL 的定向安全检查通过。全仓 SQL 安全命令 4/5，通过之外的一项为既有 MySQL 277 `RENAME TABLE` 的 FNSAFETY004，HEAD 中已存在，未改动该 AI 迁移或放宽规则。
- 定向 Architecture：`SqlDataScopeRulesTests|GlobalSqlStatementCatalogTests|NativeAotStaticBindingRulesTests|ModuleTableOwnershipTests|ModuleLocalTransactionBoundaryTests|OpenApiOperationIdentityRulesTests`，56/57 通过、零跳过。唯一失败 `Production_global_sql_statements_are_exactly_cataloged` 报告既有 AI cleanup 扫描的两处旧表片段、Workflow 设计资源的五项未登记 Global SQL；本次 Messaging 新 SQL 均为 HostOnly，没有出现在违规列表中。未修改其他任务实现或豁免门禁。
- `dotnet build src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj --configuration Release --nologo -m:2 -p:FullNetAotAnalysis=true --artifacts-path <系统临时目录>/aot -v:quiet`：通过，零警告/错误；这是 AOT 分析构建，未进行新代码 Native 发布运行验收。
- 修改 Worker 启动路径及时间边界后，以同样参数定向构建 `src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj`：最新 AOT 分析通过，零警告/错误；完成分析后该临时输出目录才复用于最终 JIT 单元验证。

验证中已修复：快照源生成把空 `byte[]?` 写为 base64 空串导致 null 语义丢失（改用忽略 null）；RDG 对位置记录 `[AsParameters]` 的 nullable 引用类型生成非法 `typeof`（改为显式标量查询参数）；Consumer 和投影 Worker 读取订阅目录时同步释放仅支持异步释放的 DbSession（改为异步作用域并补启动回归）；失败时间按转换后的 UTC 年份检查 MySQL 下界，避免正时区偏移变成 999 年堵塞投影；旧服务直调用测试在异步初始化后丢失 AsyncLocal 测试身份（测试专用固定上下文）；API 测试工厂支持经标记验证的显式测试根，适配 MTP 切换当前目录后的系统临时构建；Kafka EnsureTopics 同批存在新建成功/已存在结果时正确保持幂等。SQL Server 共享模板有残留文件时关闭模板复用，使用独立测试库，不删除共享模板文件。

本轮未执行全量测试、真实 Connect 回退/切流、CDC/Soak/容量矩阵或新的 Native 发布运行。不提升 `Production-verified` / 容量状态。可用审查工具没有本地独立审查能力，且用户未授权多代理；完成自审与定向边界检查，不将其描述为独立审查。未提交或推送代码。

最终核对：`git diff --check` 通过；本任务 JSON 与资源 XML 可解析，现行运维文档无第一批“验收中/无管理 API/审计缺操作者”的陈旧状态。分支仍为 `main`，HEAD 仍为 `a77902afd87af1662af3b43206d85160eeec566b`；保留任务开始前及并行会话的 Workflow/VForm 改动。

### 临时产物清理限制

任务临时根为 `C:\Users\Administrator\AppData\Local\Temp\fullnet-messaging-batch1-a3a8cdbc558d4e6e9a4e6f6f5495b95f`。已完成的 AOT 分析输出与快照探针申请清理时，以下原始命令被命令策略返回 `blocked by policy`，工具未称其为 Auto-review；没有换解释器或扩大范围重试，产物暂留：

```powershell
$taskRoot = [IO.Path]::GetFullPath('C:\Users\Administrator\AppData\Local\Temp\fullnet-messaging-batch1-a3a8cdbc558d4e6e9a4e6f6f5495b95f'); $aotOutput = [IO.Path]::GetFullPath((Join-Path $taskRoot 'aot')); $probeOutput = [IO.Path]::GetFullPath('C:\Users\Administrator\AppData\Local\Temp\fullnet-dlq-probe-c6979d8666d74fdf9a45d54f6aeb2e3b'); if ($aotOutput -ne ($taskRoot + '\aot') -or $probeOutput -ne 'C:\Users\Administrator\AppData\Local\Temp\fullnet-dlq-probe-c6979d8666d74fdf9a45d54f6aeb2e3b') { throw 'Unexpected cleanup path' }; Get-ChildItem -LiteralPath $aotOutput -Directory | Select-Object Name; Get-ChildItem -LiteralPath $probeOutput -File | Select-Object Name; Remove-Item -LiteralPath $aotOutput -Recurse -Force; Remove-Item -LiteralPath $probeOutput -Recurse -Force; Write-Output 'Removed task-owned AOT analysis outputs and completed snapshot probe.'
```

`pwsh -NoProfile -File C:\Users\Administrator\.codex\scripts\clean-build-output.ps1 -RepositoryRoot G:\wwwroot\github_fork\Full.NET` 预览拒绝未知布局（`Missing package.json next to the output; refusing unknown checkout layout.`）；该助手不支持本仓库 `ui/admin/dist`，未绕过助手删除或扩大目标。成功浏览器验收的全局 teardown 已正常执行，临时构建/日志仍在任务临时根中，未复制整仓或创建根 `artifacts/`。
