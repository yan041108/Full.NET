# Messaging 真实受控影子 CDC 验收

基线 `main / a77902afd87af1662af3b43206d85160eeec566b`，任务快照 `messaging-real-cdc-20261025`。承接前三批整改，保留既有及其他会话改动，不提交、不推送，不运行全量测试。

## 最终本地验收（2026-10-25）

本批已完成。`batch4-cdc-verified.trx` 实际 **3 通过、0 失败、0 跳过**，6 分 38.744 秒；其中 MySQL 真实场景 4 分 13.995 秒、SQL Server Agent 真实场景 2 分 22.729 秒，另 1 项是模板回归，不把它计成真实链路用例。

- 两库均从真实 Legacy 事务写入独立影子表，经实际 Debezium 3.4.3.Final / Kafka 4.1.2 交付，并通过生产 `CompareShadowAsync`。覆盖回滚无样本、相同 ID/载荷、精确发生时间与采集代次、暂停期间不交付、恢复后补发、正式追加表零副本、业务 Inbox 零写入。
- 修复测试工厂漏解析影子 Topic、正式源场景误用诊断源模板、隔离输出找不到仓库资源，以及旧传输 Topic 无版本后缀造成证明拒绝的问题。正式源传输测试显式选择正式源模板；生产门禁未放宽。SQL Server 时间协议澄清只改注释和文档，不增加转换器或改变证明语义。
- 实际运行 `node <临时根>/batch4-real-cdc.mjs all verified`：正常增量 Release 构建零告警/零错误，1 分 27.75 秒；在同一资源上下文切换到重型阶段后执行上述三个用例。脚本记录依赖 DLL 前后 SHA-256（`batch4-dependencies-verified.json`），未复制源码，测试中未重建产物。
- `node --test tests/deployment/messaging-cdc-contract.test.mjs tests/testing/messaging-control-matrix.test.mjs` 实际 **13/13 通过**。定向发现确认本批三个 UID 只归属消息重型分片；全局发现数量仍有其他共享输入的 16 项差异，不能报告全局分片门禁通过。
- 未改接口、数据库结构、页面或生产执行逻辑，未重复第三批单元、浏览器或 AOT 套件。本批只验收实际改变的测试工厂与真实受控影子链路。
- 最终场景退出 0，重型锁已释放，独享 SQL Server、Connect、Kafka 与网络已由 Dispose 回收。共享 MySQL 中本批两个测试库在核对专属生产者标记、2 条 Legacy/影子记录、正式表为零以及无活动连接后清理；保留原始 TRX、日志与失败证据。

原始记录沿用系统临时根 `C:/Users/Administrator/AppData/Local/Temp/fullnet-messaging-batch1-a3a8cdbc558d4e6e9a4e6f6f5495b95f`。本批不能证明业务 HTTP → 完整切流/回退、多进程重启、断网、Soak、容量或独立复核已通过。生产采集与正式切流默认关闭，`Capacity-not-verified` 保持。

## 范围与连续顺序

1. 核对部署模板与 CDC 测试工厂，区分正式追加表的传输测试与受控 Legacy 影子采集。测试动态替换专用 Topic，拒绝未解析占位符；读取仓库路径支持当前隔离输出。
2. 添加真实 MySQL Binlog、SQL Server Agent → Debezium → Kafka 的受控采集场景。通过真实业务事务中的 `IOutboxWriter` 生成 Legacy 与独立影子样本，不使用测试 Produce 或手工样本伪造 CDC。核对回滚不发出、同 ID/载荷/代次及精确元数据、正式追加表零副本、Connector 完整暂停/恢复后的积压交付。影子消息只观察，不调用业务消费者。
3. SQL Server Agent 使用本批独立、有硬资源上限的容器，不重启共享 SQL Server。测试删除本批 Connector，独立容器由场景 Dispose 回收；共享资源不接管。
4. 本批实现后集中运行上述两个场景及直接受影响的部署/夹具契约检查。最小复现按需先行；失败按首个错误边界修复，只重跑受影响场景。沿用第三批 JIT 输出目录并增量构建当前必要依赖，记录实际构建参数及输入，不将相邻 Workflow 代码算作已验收。

## 完成条件与证据边界

- 两个实际场景均通过，无失败或跳过；已启动链路超时、转换失败应为失败，不能报告环境跳过即通过。
- SQL Server 的 `datetimeoffset` 是 ZonedTimestamp，不能直接作为 EventRouter 的整数时间字段；单独核对 Kafka 时间与精确 `occurred_at_utc` 头的角色，配置与证明窗口文档必须一致，不能在测试工厂隐藏产品配置缺陷。
- 不扩大为实际业务 HTTP → 正式切流/回退、多进程恢复、Soak、容量或独立审查认证。采集及正式切流默认关闭，`Capacity-not-verified` 保持。
- 实际命令、结果、源输入摘要及必要失败原始记录放在沿用的系统临时任务根，完成后集中同步本计划、运维文档和用户整改报告。

## 定位与调度记录（历史状态，最终结果见文首）

- 初次 `BuildProjectReferences=false` 构建失败：旧 Workflow 依赖缺少当前测试使用的 `WorkflowPermissions.TodosTransfer`；新场景缺少 Api/Identity 命名空间与容器 HostConfig 空值初始化。修正本批代码，并恢复必要依赖的正常增量构建；没有移除相关测试或更改 Workflow 实现。原始编译记录 `batch4-build-red.log` 保留，不算业务回归失败证据。
- 时间协议参考 [Debezium 3.4 SQL Server 类型映射](https://debezium.io/documentation/reference/3.4/connectors/sqlserver.html#sqlserver-data-types)及 [Outbox Event Router](https://debezium.io/documentation/reference/3.4/transformations/outbox-event-router.html)。SQL Server `DATETIMEOFFSET` 是字符串 ZonedTimestamp；未配置事件时间字段时 Kafka Record 默认使用 Debezium 处理时间，精确业务时间仍须由消息头保存并逐项比较。
- 正常增量构建通过（零告警、零错误，`batch4-build-prerequisite.log`）。最小回归 `Shadow_connector_factory_resolves_topic_and_keeps_capture_metadata` 实际失败 1 项、无跳过（`batch4-cdc-prerequisite.trx`），首错为未解析的 `${FULLNET_SHADOW_TOPIC}`。随后工厂显式区分正式源/诊断源，并解析部署变量；两个来源不互相伪装。
- 首轮真实 MySQL 场景到达最终比较，暂停/恢复、精确消息时间及载荷检查已执行，但比较返回 null。只读查看 Broker 头与生产路由守卫后，首因是测试工厂沿用旧传输测试的“前缀 + EventType”主题，该值没有 `.v1`，生产证明正确拒绝了不符合 TopicCode 的地址。修复应让受控采集测试按正式 Topic 目录派生版本化影子地址，而不是放宽生产守卫。源位点等待已通过，没有证据支持此前考虑的心跳缺失假设，故不追加心跳配置。
- 全局分片发现为 1370、登记为 1354，超出本批新增的 3 项另有 16 项共享工作区差异。保留该失败，不代其他会话调整数量。定向发现 `batch4-discovery.json` 已确认本批三个 UID 在消息重型分片各出现一次，在 infrastructure 中为零；不代表全局数量门禁通过。
- 首轮最终报告 `batch4-cdc-first.trx`：3 项中 1 通过、2 失败、0 跳过，用时 6 分 46 秒；双库均停在相同的最终比较断言，SQL Server Agent 已真实产生并交付两条样本。受控影子地址已改由版本化 Topic 目录派生，工厂回归同步检查 TopicCode；正在复验，首轮不报告为通过。独享 SQL Server、Connect、Kafka 与网络均由场景 Dispose 实际回收，共享数据库容器未删除。
- 首轮释放重型资源后，相邻 Workflow 会话取得编译资源并要求短稳定窗口；我方非文档源码保持固定，复验入口正常排队，不抢占、删除其他会话锁或进程。
