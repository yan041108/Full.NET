# Messaging 完整切流与回退真实链路验证

基线 `main / a77902afd87af1662af3b43206d85160eeec566b`，快照 `messaging-cutover-rollback-real-20261025`。用户明确授权补齐完整真实链路、直接修复问题并更新相关文档。保留既有和其他会话改动，不提交、不推送、不运行无关全量测试；普通生产配置保持默认关闭。

## 当前验收结果（2026-10-25）

- `batch5-integration-timestamp-guard.trx`：完整真实双库 **2 通过、0 失败、0 跳过**，14 分 08.904 秒。两库均经真实业务 HTTP、Legacy/影子证明、HTTP 切流、Debezium/Kafka/Inbox，再验证实际暂停后的审计故障恢复、屏障业务 503 且零落库、成功回退、Completed 重复恢复不 resume、Legacy 后续交付与正式 Topic 高水位不变。每库四条投影、两条正式源/Inbox、两条 Legacy 处理结果及操作者/审计完成对账。
- `batch5-unit-timestamp-guard.trx`：**46 通过、0 失败、0 跳过**；`batch5-control-boundaries.trx`：持久控制面双库 **2 通过、0 失败、0 跳过**，2 分 56.265 秒。后者补充结果未知、持久恢复与 Broker 资源模型，并非另两项真实 Connect 场景。
- `node --test tests/deployment/messaging-cdc-contract.test.mjs tests/testing/messaging-control-matrix.test.mjs`：**18/18 通过**。中英 fence 资源唯一且非空，7 份受影响文档的 114 个 Markdown 文件目标与空白检查通过；最终输入、分支、状态与差异检查按本节交付记录核对。
- 主链路及补充控制面复用完全相同的 JIT DLL，运行前后所有 DLL 摘要相同；21 项相关源码/模板/脚本摘要匹配。集成程序集 SHA256 为 `baabcbcca4e61dc116e71834bde346d836b63a45848f38229160a675369c665b`，完整摘要在 `batch5-binaries-timestamp-guard.json` 与 `batch5-control-boundaries-binaries.json`。
- 原始记录位于系统临时根 `C:/Users/Administrator/AppData/Local/Temp/fullnet-messaging-batch1-a3a8cdbc558d4e6e9a4e6f6f5495b95f`。完整场景回收自己的 MySQL 测试库、独享 SQL Server/Connect/Kafka；补充控制面使用既有有界 Ryuk ACK 入口，独享 SQL Server 3 GiB/2 核、MySQL 1.5 GiB/1 核、Kafka 2 GiB/1 核，持续核对预算/OOM，`batch5-boundary-resources.jsonl` 已登记 `reaper-clean`，未接管共享容器。
- API/Worker AOT 分析构建均通过，**0 告警、0 错误**，分别为 1 分 05.31 秒和 24.02 秒（`batch5-control-aot-current-api.log` / `worker.log`）。使用 `dotnet build ... -c Release --no-restore -m:1 -p:FullNetAotAnalysis=true --artifacts-path <任务临时根>/aot -v:quiet`，与 JIT 输出隔离。这不是 Native 发布或原生进程验收。
- 独立复核未执行；本地同进程生产 Worker 组件不能证明多进程重启、跨节点断网、Soak、告警矩阵或容量，保持默认关闭和 `Capacity-not-verified`。本轮只执行直接受影响检查，不将其他会话的 Workflow/VForm 改动列为已验收。
- 交付核对：分支仍为 `main`，HEAD 仍为上述基线；21 项验收输入摘要一致，32 条相关路径的 `git diff --check`、现行源文件空白检查及 7 份文档的 114 个 Markdown 文件目标检查通过。完整脏工作区状态记录为 `batch5-final-git-status.txt`，保留既有和其他会话变更，未提交或推送。文件清理受命令策略阻断，详见下方清理状态，不计为已完成清理。

### 实际运行入口与参数

本批临时入口内部使用仓库 `withTestRun` 资源池，编译申请 2 核/3 GiB、`-m:1`，完成后释放构建名额再申请重型阶段。以下为实际命令记录；临时构建清理后不能直接使用旧 DLL。

文件清理状态：已预览并确认六个生成后端 `bin/obj` 目录无相关活动进程、无 reparse point，约 3295.9 MiB；实际 PowerShell 删除命令在 CreateProcess 被 `blocked by policy` 拒绝，未执行。当前 `approval_policy=never`，没有批准入口，未换解释器或脚本重试，六目录保留。精确命令及拒绝见临时根 `batch5-cleanup-policy.md`，预览见 `batch5-cleanup-preview.json`。本次不重试先前 UI 构建输出的清理拒绝，也不删除源文件、原始失败、TRX、摘要或共享资源。

```powershell
$taskTempRoot = 'C:/Users/Administrator/AppData/Local/Temp/fullnet-messaging-batch1-a3a8cdbc558d4e6e9a4e6f6f5495b95f'
node "$taskTempRoot/batch5-real-control.mjs" all timestamp-guard
node "$taskTempRoot/batch5-control-boundaries.mjs"
node "$taskTempRoot/batch5-control-aot-current.mjs"
node --test tests/deployment/messaging-cdc-contract.test.mjs tests/testing/messaging-control-matrix.test.mjs
```

主入口实际构建 `tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore --nologo -m:1 --artifacts-path <任务临时根>/browser-stack/build -v:quiet`，以该目录实际 DLL 执行 `--filter FullyQualifiedName~MessagingCutoverRollbackDebeziumTests --minimum-expected-tests 2 --timeout 35m`；补充控制面执行 `--filter FullyQualifiedName~MessagingDeliveryControlTests --minimum-expected-tests 2 --timeout 18m`，严格核对两项均 Passed。单元从既有临时聚焦项目编译仓库实际源文件，过滤 `KafkaConnectorDeploymentContractTests|MessagingProducerFenceHttpTests|KafkaDeliveryControlTests|KafkaCutoverShadowProbeTests`，实际发现并通过 46 项；未运行全量套件。

## 连续实施与验收范围

1. 核对生产控制面、部署模板、真实业务 HTTP 和 Worker 组件的调用链。已发现真实配置与路由身份检查有脱节：部署默认 JSON 转换器不满足控制面要求的原始字节传输；SQL Server 影子路由检查仍强制整数时间字段，与 ZonedTimestamp 模板不一致。先通过针对性回归记录原缺陷，再同步修正模板与严格校验，不放宽来源、资源或副作用门禁。
2. 建立双库真实 HTTP 场景：实际 Organization API 写业务、真实 Legacy 处理器生成 Identity 投影；受控影子经 Debezium/Kafka，正式消费者提前就绪；HTTP 生成证明并切换所有权；后续业务写入只能进正式追加表，由真实 Kafka Worker 经 Inbox 提交 Identity 投影。拒绝匿名控制、缺失证明、重复切换和不正确的所有权。
3. 真实回退使用生产就绪 reader、Connect REST、Broker 分区/组/重试积压和真实 CDC 位点；在最终本地审计处做一次可控故障，证明实际暂停后补偿恢复 Connector，再解除 producer fence。冻结期间的实际业务 HTTP 必须失败且业务/Outbox 一起回滚。故障后新业务仍能经 CDC 消费。
4. 再次执行成功回退，核对持久操作 Completed、Legacy 所有权、实际 Connector 保持暂停；重复恢复 Completed 不能恢复旧发布者。回退后新业务由 Legacy 处理，正式 Outbox/Broker 不再新增该事件，完整核对业务、投影、Inbox、所有权、边界、操作者和审计。
5. 关联实现完成后集中验证：最小路由单元回归、双库两个完整场景、直接受影响部署/矩阵及必要构建/AOT 检查。源码和二进制固定，构建在名额池完成后释放构建资源再申请重型阶段；沿用已有外部临时构建目录，不复制整仓。前置失败仅阻塞依赖路径，修复后只重跑受影响场景。

## 真实边界

使用独享、设置硬限额的 SQL Server Agent 容器；MySQL 创建本批独立数据库。真实 Kafka/Connect、业务处理器、持久读写和 HTTP 授权不使用就绪替身，不直接改所有权、不手工标记处理完成、不用测试 Produce 假造业务事件。允许的故障点只位于最终审计事务，用来验证真实外部暂停后的补偿。宿主可以在同一测试进程中运行生产 Worker 组件；不能据此声称多进程重启、跨节点网络隔离、Soak、容量或独立审查通过。

通过后集中同步运维文档、能力状态和用户整改报告，保留原始失败与最终结果；回收本批 Connector、独享容器和测试库，保留必要的最小证据。

## 缺陷与最小复现

- 部署/路由回归 `batch5-unit-red.trx` 实际 8 项中 6 失败、2 通过，无跳过：四份模板未显式配置 ByteArrayConverter；SQL Server 无整数时间字段的正确配置被拒绝，反而会使 EventRouter 失败的字段配置被接受。已同步修复四份模板与按提供程序的严格检查。
- 生产者 fence 异常原来会进入通用未知错误 500。通过模块真实管道入口的中英两例 `batch5-fence-red.trx` 均复现未映射异常。采用已有 BeforeEndpoints 扩展点在模块内映射为脱敏 ProblemDetails 503、稳定错误码和 Retry-After，不改变事务回滚，不增加基础层对 Messaging 的反向依赖。
- 测试在生产 readiness reader 完成真实暂停/排空后设置推进点，用于检查其他 HTTP 写入被 producer fence 拒绝；推进后才让最终审计事务失败。包装器不修改生产证明，也不替代 Connect、Broker、CDC 或业务处理器。
- 首次完整编译仅在测试夹具调用私有资源标签方法处失败，已把既有测试辅助方法改为 internal，方法体不变；没有改变生产资源政策。正常 Release 构建随后零告警/零错误，46 项配置、HTTP 屏障及直接控制面单元通过，无跳过（`batch5-unit-current.trx`）。部署及矩阵 18 项通过。
- 首轮真实运行 `batch5-integration-current.trx` 实际 2 失败、0 通过、0 跳过：MySQL 在 300 存储过程缺少 DELIMITER 处失败，SQL Server 在初始源位点等待时任务失败。前者按既有 296/299 模式仅补过程分隔符，下一轮增加缺账、缺索引恢复；后者增加任务 FAILED 状态与脱敏异常采集，不提高等待上限、不把失败算作环境跳过。
- 诊断轮 `batch5-integration-diagnostic.trx` 仍为 2 失败、0 通过、0 跳过（7 分 41 秒）：MySQL 的迁移和缺索引、缺账恢复已通过；两库 Connector 均实际报 `Invalid schema type for ByteArrayConverter: STRUCT`。EventRouter 会保留心跳、Schema Change 等元数据，标准 ByteArrayConverter 不能处理它们。按 [Debezium 3.4 官方传输配置](https://debezium.io/documentation/reference/3.4/transformations/outbox-event-router.html#_using_apache_avro_as_the_payload_format)，应采用 BinaryDataConverter 原样传业务字节，并为非二进制元数据配置 JSON delegate；生产资源身份必须同时绑定这三个选项，不能只在测试中关闭元数据来避开故障。
- 模板元数据回归先实际失败 4 项、其余 10 项通过；四份模板与生产校验现统一为 BinaryDataConverter + 无 schema 的 JSON delegate，移除任一配置或替换回标准 ByteArrayConverter 均拒绝，delegate 变化也改变资源摘要。模板/矩阵 18 项通过；真实复验仍待结果。当前可用物理内存约 7.5 GiB，不满足默认 6 GiB 构建加 4 GiB 桌面保留量，已结束仅排队、无 dotnet 子进程的本批入口，并核对归属后清理自己的工作区锁；新入口申请 2 核/3 GiB，继续遵守同一资源池，不修改全局策略、不抢占其他会话。
- `batch5-integration-binary-delegate.trx` 实际 2 失败、0 通过、0 跳过（10 分 24 秒）：真实 Connector 已健康并提供初始源位点，原 STRUCT 转换失败消失；两库均在缺少证明的控制请求前收到 `identity.session_not_active`。测试先登录控制端，再登录同一账户的业务端，默认单会话策略正确撤销了前一令牌；只给本夹具配置既有 `Identity:SessionLoginPolicy=AllowMultiple`，保留真实会话、租户与权限验证。该轮构建零告警/零错误，更新后的配置和控制面单元 46 项通过（`batch5-unit-binary-delegate.trx`）；生产代码未变时复用此结果，仅重建必要集成输入并复验真实路径。
- 会话修正轮 `batch5-integration-sessions.trx` 实际 2 失败、0 通过、0 跳过（13 分 19 秒）：双库均越过身份检查，并正确返回 422 / `messaging.delivery.cutover_precondition_failed`。新测试误把业务规则拒绝写成 409；按照当前 `ErrorType.BusinessRule` 的契约修正缺证明及重复切流断言，并核对机器码，服务端语义不变。不是通过修改产品契约适配测试。
- `batch5-build-http-contract.log` 在共享 Workflow 的局部变量重名处编译失败（CS0136）；只读检查时原会话已改为 `voteActivation`，本批没有改 Workflow 实现。下一次正常增量构建 `batch5-build-http-contract-fixed.log` 零告警/零错误（4 分 22 秒），继续实际双库链路。环境为 Windows 10 x64、i7-12700H、20 逻辑核、63.75 GiB 物理内存、.NET SDK 10.0.402；Docker 可用总内存约 19.29 GiB，Kafka/Connect 各限 2 GiB，独享 SQL Server 限 3 GiB。上述机器与资源范围不构成容量认证。
- 真实 MySQL 已通过业务 HTTP → 受控影子证明 → HTTP 切流 → Debezium/Kafka → Inbox/Identity 投影，但回退在暂停前的位点追赶处卡住。只读现场 `batch5-mysql-fence-observation.log` 记录正式事件已消费、所有权 2/fence 1、正式表 1 行；数据库 Binlog 为 `mysql-bin.000010 / 84026474`，Connector flush 后连续停在业务行 `84021033`。模板未启用心跳，且使用 3.4 无效的 `heartbeat.topics.prefix`；静默的捕获表不能替控制表提交推进 Kafka offset。新增模板回归先 4 失败/10 通过；现四份模板改为 `topic.heartbeat.prefix`，MySQL 显式启用 1000ms 心跳，生产守卫校验并绑定心跳配置、拒绝旧键与 SQL heartbeat action，不放宽源位点覆盖。相关模板/矩阵 18 项通过；下一轮重建并重跑受影响单元和双库真实场景。
- `batch5-integration-http-contract-fixed.trx` 最终为 1 通过、1 失败、0 跳过（16 分 59 秒）。MySQL 在真实暂停前因位点追赶超时失败；SQL Server 的完整真实链路实际通过（5 分 19 秒），包括 503 业务写入及零落库、最终审计异常后的真实恢复/解除 fence、恢复后 CDC 继续交付、成功回退、Completed 重复恢复不 resume、后续 Legacy 投影与审计身份。当前心跳/资源摘要守卫已演进，不能把该历史 SQL Server 通过直接当作新输入已通过；统一配置后再跑双库，并补正式 Topic 高水位不变的直接检查。
- 心跳修正轮 `batch5-unit-heartbeat.trx` 为 46 通过、0 失败、0 跳过；`batch5-integration-heartbeat.trx` 为 1 通过、1 失败、0 跳过（12 分 55 秒）。SQL Server 再次完整通过，MySQL 已实际完成两条回退分支和正式 Topic 高水位检查，最终四投影汇总却为 0；四次单条投影检查均已成功。原因是本批汇总用的原始 MySQL 连接未采用 Binary16 GUID 策略，而单条检查使用统一策略；改用 `MySqlConnectionStringPolicy.Create`，保持四条投影要求，不删除该断言。
- 顺着路由校验补充正式 SQL Server 时间字段回归，`batch5-unit-timestamp-red.trx` 实际 45 通过、1 失败、0 跳过：仅 sqlserver/capacity 错误地接受 `OccurredAtUtc` 整数时间字段。原限制仅在 shadow 分支，现同步到正式路由；MySQL 正式路径及部署时间语义不扩大变更。下一轮固定新代码、模板和测试输入，集中复验 46 项与两个真实链路。
- AOT 首次构建在相邻 Workflow 新文件缺少 `ICommandTransaction` 命名空间处失败；本批只为 `WorkflowVoteDeadlineProcessor` 与 `WorkflowVoteTodoService` 补 `Full.NET.Abstractions.Messaging` 两行 import。第二次暴露前者缺少 `WorkflowManagementScope` 父命名空间及级联错误，已由原会话补 `Features` 导入；本批未改 Workflow 业务逻辑。失败日志 `batch5-control-aot-api.log` / `batch5-control-aot-imports-api.log` 保留，最终当前 API/Worker 分析均零告警/零错误。相邻工作继续独立验收，不由本批通过结果覆盖。
