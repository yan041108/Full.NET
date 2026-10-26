# Messaging 运维整改第二批：安全切换闭环

> 交付状态（2026-10-24）：第二批核心实现与本地定向验证完成，当前为受约束 Pilot；默认正式切流关闭。完整结果及未验证项见文末“最终交付结果”，历史过程中的待执行状态不代表最终状态。

- 授权：用户在第一批完成后要求继续；按既有整改文档推进第二批，关联实现完成后集中测试，发现问题直接修复。
- 基线：`main / a77902afd87af1662af3b43206d85160eeec566b`，复用已创建快照 `messaging-batch2-20261024`；保留第一批和其他会话的 Workflow/VForm 改动。
- 约束：默认关闭正式切流；外部控制调用不进入数据库事务；双库一致；不自动提交、推送或运行全量测试。

## 连续实施

1. **持久回退协议**：模块拥有回退操作日志，代次、资源快照和暂停/恢复意图先落库；以数据库会话锁串行化控制动作。最终所有权与操作终态同事务提交。异常后重新读取权威状态，已提交 Legacy 的代次绝不恢复 Connector，未知状态保留保护。
2. **跨实例恢复**：增加受现有精确回退权限保护的状态查询和恢复入口。恢复同一代次，核对当前 CdcKafka 与 Preparing，使用原资源快照；恢复请求成功后仍须确认任务 RUNNING，才清理生产者 fence。重启、重复、并发、取消及恢复失败均可重试且失败关闭。
3. **真实控制面**：先等待 CDC 覆盖已冻结的生产者位点，再暂停并确认所有任务停止；排空目录内全部订阅组及重试 Topic，重新确认位点和停止状态。共享 Connector 不允许单流暂停。API 单独装配控制面，不启动常驻消费者。
4. **切流证据门禁**：引入服务端切流证明读取边界，缺失、过期、环境或配置不一致、影子观察不完整、目标 Connector/订阅实例不可用均拒绝。证据必须来自实际观察，不能使用请求布尔值或 Enabled 配置充当证据。

## 集中验证

覆盖暂停后失败、HTTP 结果丢失、最终事务提交结果不明、重启后恢复、不同代次拒绝、恢复失败保留 fence、多实例竞争、共享 Connector 拒绝、CDC 追赶先于暂停、多组及重试排空、切流证据失败关闭及 API 不启动消费者。数据及迁移场景按相同输入在 SQL Server/MySQL 验证；复用现有任务临时构建目录。最后执行相关 Unit/Integration、命名/SQL 与直接依赖架构检查、API/Worker AOT 分析和差异检查，记录实际命令、结果与未验证项。测试集中执行，步骤之间不设提交、全套测试或人工批准门槛。

## 共享输入短暂冻结（已解除）

收到 VForm 会话通过工具发送的共享宿主验收协调请求后，已到达可编译边界并冻结 Messaging 生产源码、公共契约、生成输入及对应测试；期间仅整理独立文档和只读核查，不进入后续实现步骤。分支/HEAD 仍为 `main / a77902afd87af1662af3b43206d85160eeec566b`。

VForm 会话完成自己的固定输入验收并明确通知恢复后，已将带摘要保护的 23 份候选变更及三个定向回归测试文件应用到当前工作区，继续本批实现。其原生/托管与真实栈通过只属于 VForm 场景，不外推为 Messaging 业务验收；本批后续以实际代码和直接边界重新验证。

最后修复点：`DeliveryRollbackRecoveryService` 补齐 `Full.NET.Abstractions.Messaging`（`ICommandTransaction`）；真实回退适配器的 `Position` 更正为现行契约 `ProducerFencePosition`。随后用本任务已有系统临时目录分别 Release 构建 `Full.NET.Modules.Messaging.csproj`、`Full.NET.Messaging.Kafka.csproj`、`Full.NET.Data.Dapper.csproj`，均退出码 0、零警告/错误。原编译失败已修复，不把这些构建当作行为验收。完整功能仍处实施中，集中测试尚未开始；VForm 会话只读此记录即可确认冻结就绪，不需要回信。

## 冻结期间只读核查的后续检查项

- 验证样本须包含完整可信信封元数据；载荷条数和总字节同时有界，任何缺失、重复、损坏或截断均不能建立证明。扫描固定高水位后须检测 Topic 拓扑/保留窗口变化。
- 目标探测除 RUNNING 和活跃组外，还须核对实际 EventRouter 的路由及源表映射；稳定资源摘要包含环境、真实 Connector 配置、订阅实例和重试范围，不能把任意健康 Connector 当作目标发布链路。
- 暂停前冻结原 Connector 配置摘要，恢复时防止同名 Connector 被重建或改用途；默认目录为 CdcKafka 而没有持久所有权行的其他流也必须纳入共享影响拒绝检查。
- 状态迁移不能丢失“是否已经发出暂停意图”：仅绑定资源但尚未暂停时，不应主动恢复原先就已暂停的 Connector。用不可回退的暂停意图字段区分，并持久化初始操作者/理由；人工恢复请求在外部调用前记录审计意图。
- 状态查询须支持从事件流定位最近操作，使准备提交响应丢失或进程重启后仍可发现代次；最终回退审计应关联本次证明/操作标识。
- 对已有切流/回退夹具显式建立测试证明及测试探针，不能为旧用例关闭生产证据门禁。新增真实 Broker 的影子扫描用例与双库恢复/提交结果丢失用例，区分 HTTP 模拟控制面和真实 Debezium。
- 补登记新增 SQL、数据库元数据注释、JSON/物化闭包、OpenAPI 与测试选择；只对相关变更集中验收，不调整无关任务或现存失败。

官方语义核对：Kafka Connect 的暂停/恢复为异步控制，不能把 PUT 成功当作全部任务状态证明（[Kafka Connect 运维](https://kafka.apache.org/37/kafka-connect/administration/)）；SQL Server CDC 的 `__$start_lsn` 是变更所属事务的提交位点，全库 Capture 高水位不能单独证明指定流全部源行已捕获（[SQL Server CDC](https://learn.microsoft.com/en-us/sql/relational-databases/track-changes/about-change-data-capture-sql-server/)）。本批据此增加全任务状态核对及冻结流捕获完整性检查；这些设计仍需本地行为验证。

## 冻结期间的隔离回归与候选补丁

用户再次要求继续后，在原任务临时根的 `batch2-drafts/` 准备定向补丁和测试，未修改共享源码、契约或测试输入，也未复制整仓。`stage-hardening.mjs` 只生成带源文件摘要的候选变更；`overlay.targets` 仅让独立编译输出读取少量候选源码，不能当作正式工作区已经应用。恢复后再按实际工作区统一验证。

- 对冻结前已编译程序集执行 `FrozenRegression.csproj` 的聚焦回归：18 项，15 通过、3 失败、零跳过。三个实际失败分别为租户头不一致、分区拓扑变化、保留窗口变化仍产生证明；原始记录为临时根 `batch2-frozen-regression.trx`。这是缺陷复现，不能记通过。
- 候选补丁增加完整信封比对、固定窗口末尾复核、样本条数及总字节限制、持久暂停意图与操作者/理由、原 Connector 配置身份核对、按流定位最近操作及恢复前审计。隔离 Release 编译模块和 Kafka Provider 均零警告/错误；首轮候选回归 21 项全部通过、零跳过，记录为 `batch2-candidate-unit.trx`。之后新增的边界仍需再次验证，不能沿用该结果覆盖新修改。
- 隔离编译前后确认 VForm 的 1008 项验收输入摘要未变：`339f930d7e8234eb256d7184d0d87564f82981350bfd3ef777897e0cbea32652`。
- 新双库用例草稿覆盖无/过期/失配证明、源数据变化、暂停后失败、恢复失败保留 fence、错误代次、重建服务容器后的恢复、重复恢复、准备及最终提交响应丢失、迁移重跑，并用真实 Broker 验证有界影子扫描及实际消费组。外部 Connector 状态使用 HTTP/端口故障模型，不能据此宣称真实 Debezium 端到端通过。

本批仍未验收，正式切流保持默认关闭。候选文件、临时程序集、测试发现数和工作区应用状态必须分别核对后才交付。

候选补丁后续推进：测试驱动已修复测试夹具错误使用 Dapper 内部类型的问题，改用公开 Outbox 端口和原 DI 工厂进行提交故障注入；隔离 Integration Release 构建零警告/错误。补入真实 HTTP 管理客户端的暂停响应丢失、恢复任务 FAILED、未暂停不发送 resume、配置变化拒绝、目录默认 CdcKafka 影响及非法超前 Offset，候选 Unit 26 项通过、零跳过（`batch2-candidate-unit-final.trx`）。新增加的 Worker 独立 ClientId、Broker/Topic 资源身份摘要及真实 Broker 用例仍待后续编译与运行验证。

最终自审还需核对：Connect 服务实际 Kafka ClusterId 与排空所观察的 Broker 必须一致；恢复资源摘要应描述源库、路由与目标资源，不能因包含凭据而形成可离线猜测的口令摘要或要求恢复过期口令。资源身份或共享影响无法证明时保持失败关闭，不通过请求布尔值放行。

## 当前工作区集中验证（2026-10-24）

当前基线仍为 main / a77902afd87af1662af3b43206d85160eeec566b。复用第一批任务临时根 C:/Users/Administrator/AppData/Local/Temp/fullnet-messaging-batch1-a3a8cdbc558d4e6e9a4e6f6f5495b95f；候选补丁和隔离结果仅作为缺陷复现历史，以下才是正式工作区结果。

- Release 构建 Unit 和 Integration（-m:1、各自已有 --artifacts-path）均成功，零警告/错误。直接运行 Unit 选择 KafkaDeliveryControlTests、KafkaCutoverShadowProbeTests、CdcDeliveryPositionTests、KafkaConnectAdminClientTests、KafkaConsumerLagObserverTests，最低发现 53：53/53 通过、零跳过，记录 batch2-unit.trx。末轮自审增加排空结束时重新观察 Broker ClusterId 的拒绝场景，并将生产样本信封改为全部显式字段；最终 54 项仍待运行，不能沿用 53 项覆盖新增修改。
- Integration MessagingDeliveryControlTests（最低 2、15m）在真实 SQL Server/MySQL 与真实 Kafka Broker 上 2/2 通过、零跳过，1m44s；记录 batch2-integration.trx。覆盖无/过期/失配证明、源变化、Legacy 积压、其他活动 CDC 流、暂停和恢复失败、重启接管、重复恢复、准备/最终提交响应丢失、迁移 291/292 重跑、真实 Broker 完整影子集合和重复拒绝。Connect 使用 HTTP 状态/故障模型，不能认作真实 Debezium 验收。
- 后续给 SQL Server 用例补充真实数据库执行的 CT 完整集查询测试（受控 CT 投影，非 Agent 捕获），给原两个 Messaging API 用例补充四个新接口的精确权限、404、理由和失败关闭；相邻 19 项加此 SQL Server 用例正在集中运行，记录 batch2-neighbors.trx。
- pnpm test:naming 最初 35/36：291 MySQL 草稿的 PREPARE/EXECUTE 被 FNSQL002 拒绝。未引入豁免，将未发布新迁移收敛为一次静态完整建表，双库成对删除草稿增列兼容块；随后 36/36 通过。当前全列 CREATE 与幂等建表仍保留，不能把本任务早期未发布草稿表当作支持的升级起点。
- Architecture 六类定向选择共 57 项：初次临时目录启动因既有测试从 AppContext.BaseDirectory 向上寻找仓库而 52 失败、5 通过。复用刚编译的输出复制到该项目标准 ignored bin，未复制源码或整仓；重跑 56/57 通过、零跳过（batch2-architecture-retry.trx）。唯一失败 Production_global_sql_statements_are_exactly_cataloged 的 48 条均为既有 AI 扫描片段或其他会话 Workflow 新 SQL/漂移，本批 Messaging SQL 没有违规；未修改其他任务代码或增加豁免。
- node --test tests/testing/test-matrix.test.mjs：6/7，唯一失败是其他会话加入 workflowActivityIntegration 后，该旧用例仍严格要求仅 unit/compatibility/architecture 三键。本批新增命名 Unit 选择、291/292 双库映射与重型分片登记，独立 messaging-control-matrix.test.mjs 2/2 通过；最终最低发现数已随新增回归调整为 54，待最终复核。

最终交付前仍需完成：当前 54 项 Unit、相邻 Integration 的实际结果、真实 Host 导出的四接口契约合并/生成检查、API/Worker AOT 分析、实际文档与差异核对。独立复核没有可用且已授权的 reviewer；本地自审不作独立复核。默认关闭、Pilot 与 Capacity-not-verified 均保持。

集中验证后续：相邻 Integration 20 项运行 15m19s，18 通过、2 个 API 失败、零跳过；两个失败均为本次新断言将空理由误判为 422，既有统一映射明确 Validation→400。修正为 400，并增加 messaging.operations.reason_invalid 稳定码断言；仅重建并补跑两项 API，不重跑其余通过场景。54 项最终 Unit 已全部通过、零跳过（batch2-unit-final.trx），Release 构建零警告/错误。API/Worker AOT 分析和 API 补跑仍在进行。

API AOT 分析 7m56s、Worker AOT 分析 23s，均退出码 0、零警告/错误。末轮资源核查追加显式 key StringConverter/value ByteArrayConverter 门禁，拒绝 JSON Converter 假就绪，并更新对应 HTTP/真实 Broker 夹具。该局部变化会重新构建 Integration 和当前五个定向 Unit 源文件；Unit 采用已有临时夹具工程直接引用工作区源码与新鲜正常 JIT 生产 DLL（无候选生产覆盖、无源码副本），最低发现 55，保持 54 项正式 Unit 通过的原始记录，明确区分两种编译入口。之后只复验新双库/Broker 两项和受影响 AOT 增量，不重复其他已通过的 17 项切流/回退。

## 最终交付结果

- 最终生产源码、直接相关测试与双库 291/292 共 279 项输入摘要：104b9a5caf2ea409929853b145cea61b3ebc3d970778b2901a5fa65937e6ee99；验证完成后逐项未漂移。分支 main，HEAD a77902afd87af1662af3b43206d85160eeec566b。未提交、未推送，保留其他会话改动。
- 新鲜正常 JIT Integration Release 构建零警告/错误。FocusedWorktree.csproj 直接编译当前仓库五个 Unit 文件（MSTest.Sdk 4.3.2、NSubstitute 6.0.0、Confluent.Kafka 2.15.0；引用该构建产出的真实生产 DLL），55/55 通过、零跳过，原始记录 batch2-unit-55.trx。没有覆盖候选生产源码、复制整仓或删除断言；54 项正常 Unit 项目通过记录保留。最终局部验证将“JSON Converter 伪就绪”纳入最低发现 55，未重编全部无关测试。
- 同一正常 Integration 输出补跑 MessagingDeliveryControlTests：SQL Server/MySQL 与真实 Broker 2/2 通过，零跳过，1m48s（batch2-integration-final.trx）。两个原 Messaging API 用例补跑 2/2 通过、零跳过，2m01s（batch2-api-retry.trx）。合并复用其余 17 个已通过切流/回退用例，共 21 个不同 Integration 用例通过，没有把重复执行计为新增覆盖。
- 末轮 API AOT 分析 51s、Worker 22s，均零警告/错误、退出码 0（batch2-api-aot-final.log、batch2-worker-aot-final.log）。仅为 FullNetAotAnalysis=true 构建，不是本批 Linux Native 发布/运行验收。
- 四个新 HTTP 操作从真实 Integration Host 导出，按 operationId 规范化后仅合并本批路径及引用 Schema，和当前快照逐项一致；保留其他会话 Workflow 契约与生成函数。pnpm openapi:client:snapshot --check --offline、pnpm openapi:client:generate --check 均通过；pnpm --filter @fullnet/client-contracts build 通过。定向 Node：新控制面契约、生成就绪与矩阵 11/11；CDC 状态同步 4/4；Runbook 定向 1/1；既有死信契约 2/2，通过且零跳过。
- 命名最终 36/36；架构实际 56/57，唯一失败的 48 条属于 AI/Workflow 共享目录不一致，本批 Messaging 无违规。测试矩阵旧检查 6/7，唯一失败为其他会话新增 workflowActivityIntegration 与旧三键断言冲突；本批独立矩阵检查通过。均保留原始失败，不改动无关任务或放宽门禁。
- 本任务 git diff --check、当前分支与 git status 核对通过；新文件另做空白检查。系统临时根继续作为连续整改的唯一复用构建/原始失败证据目录，不新增仓库根 artifacts。第一批已记录的清理命令策略拒绝不通过其他解释器或扩大路径绕过。

### 可复验入口

任务临时根沿用上文绝对路径。正常 Integration 构建为 dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --configuration Release --nologo -m:1 --artifacts-path <临时根>/browser-stack/build -v:quiet。55 项入口为 dotnet build <临时根>/batch2-drafts/FocusedWorktree.csproj --configuration Release --nologo -m:1，随后 dotnet <临时根>/batch2-drafts/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --timeout 5m --minimum-expected-tests 55 --report-trx --report-trx-filename <临时根>/batch2-unit-55.trx。它包含当前 KafkaDeliveryControlTests、KafkaCutoverShadowProbeTests、CdcDeliveryPositionTests、KafkaConnectAdminClientTests、KafkaConsumerLagObserverTests 五份源码。

Integration 复验使用 FULLNET_TEST_REPOSITORY_ROOT=G:/wwwroot/github_fork/Full.NET、FULLNET_API_SCHEMA_TEMPLATE=0，在项目 withTestRun 重型资源上下文运行已编译 DLL。新双库选择 FullyQualifiedName~MessagingDeliveryControlTests、最低 2；API 选择 FullyQualifiedName~MessagingApiMySqlTests|FullyQualifiedName~MessagingApiSqlServerTests、最低 2；相邻切流/回退选择 FullyQualifiedName~EventDeliveryCutoverTests|FullyQualifiedName~EventDeliveryRollbackTests、最低 17。新失败或变更才复跑相关部分，不能自动扩为全量测试。

AOT 入口为 dotnet build src/Hosts/Full.NET.Host.<Api|Worker>/Full.NET.Host.<Api|Worker>.csproj --configuration Release --nologo -m:1 -p:FullNetAotAnalysis=true --artifacts-path <临时根>/aot -v:quiet。与正常 JIT 构建分开输出，禁止拿 AOT 条件编译 DLL 代替普通集成运行输入。

### 边界与未验证项

这是单活动 CDC 试点控制协议：固定目标 Topic、专用源库/表、显式原始字节 Converter、完整保留源样本不超过 10000/32 MiB、最新事件不超过 5 分钟、证明有效期 5 分钟；历史 CT 缺口、资源变更和未知位点均失败关闭。默认 LegacyPolling/ShadowCdc 业务路由没有自动追加影子镜像，自动影子准备仍需后续专门交付；无可信输入时不能建立证明。

本批使用真实双库与 Broker，但 Connect 生命周期为 HTTP 故障模型；SQL Server 捕获完整性查询用受控 CT 投影，不是 Agent/Debezium 捕获。没有执行真实 Connect/Agent 全链路、多进程网络故障接管、Soak、N+1/DR 或容量演练，也未重跑本批 Native 发布运行。服务容器销毁重建和新数据库作用域恢复的通过，不能外推为全部多实例故障窗口通过。第三批范围重放进度/结果未知状态及总览/表单仍待实施。正式切流保持默认关闭，保持 Build-verified / Pilot 与 Capacity-not-verified，不标记 Production-verified。

独立复核尚未完成：[requesting-code-review](C:/Users/Administrator/.codex/plugins/cache/superpowers-local/superpowers/7.0.0-codex.3/skills/requesting-code-review/SKILL.md) 要求“Request independent review for required critical changes.”；当前没有可用且已授权的本地独立审查工具/审查者，也没有委派或向他人发送请求的授权。本地自审与以上定向证据不标作独立复核；本次不部署、不合并。
