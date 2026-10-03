# Fluent Bit Collector 路由与故障样本（2026-09-29）

代码基线为 `c609759576c4bf54ff9e743161ecb829ca8d4d4a` 加未提交的日志切片。环境为 Windows Docker Desktop 的 Linux 容器，Docker Client/Server 29.6.2、Node.js 24.12.0。采集器固定为 `cr.fluentbit.io/fluent/fluent-bit:4.1.1@sha256:2a5cb41f99b7c5f3386bb34d42ce3b19eb47fbef6d93b64c0bd4afe9e6ec389c`。样本使用仓库候选配置的单 Tail、CRI/Docker、Kubernetes 预加载元数据和两路重标记；文件输出仅用于可核对的本地目的地。没有模拟 Kubernetes 节点总负载。

```powershell
node eng/testing/fluent-bit-collector-route-smoke.mjs --docker-desktop
node eng/testing/fluent-bit-collector-route-smoke.mjs --finite-retry-probe --docker-desktop
```

常规样本通过：Collector 标签记录进入普通/优先两路，Local、ApplicationKafka、旧版或缺失元数据被排除；缺失封套字段被拒绝，临时字段未流出。优雅重启和静稳输出后的 SIGKILL 重启只输出新增事件。另以不可达 Forward 和无限重试运行 12 秒，SIGKILL 后删除源日志、保留缓冲目录并改接文件输出，原始事件均按 ID 恢复。删除源日志用于排除 Tail 重读造成的假阳性。

有限重试探针把不可达窗口延长到 130 秒，分别使用候选配置的 Priority `Retry_Limit=5` 和 BestEffort `Retry_Limit=3`；其他解析与缓冲配置不变。重启前同样删除源日志，重启后改接文件输出。该次输出为：

```json
{"retryLimit":{"priority":5,"bestEffort":3},"faultSeconds":130,"recovered":["0199aabb-ccdd-7000-8000-000000000002","0199aabb-ccdd-7000-8000-000000000003","0199aabb-ccdd-7000-8000-000000000009"],"missing":["0199aabb-ccdd-7000-8000-000000000001","0199aabb-ccdd-7000-8000-000000000004"]}
```

两个普通事件缺失，证明当前有限重试条件下“启用文件系统缓冲”不能推出故障期间无丢失。优先事件在这次 130 秒样本中恢复，不代表延长故障、重试耗尽或缓冲满后的保证。探针只输出观测结果，不设固定缺失数断言，因为退避调度会改变耗尽时间；常规 CI 样本保持确定性。此实验没有验证原 Forward 下游恢复后的 ACK、真实 Kafka/ES、`storage.total_limit_size` 达限、满盘、Pod/节点丢失、跨节点持久性或容量。`Collector` 生产门禁保持关闭；`Capacity-not-verified`。

同一固定镜像和 130 秒窗口的后续复测在终止前从采集器 HTTP 指标端点读取 Prometheus 文本。手动探针用固定摘要的 Node 24.21.0 Alpine 辅助容器共享采集器网络命名空间，读取 `127.0.0.1:2020/api/v1/metrics/prometheus`；不读取平台 Prometheus。该次结果：

```json
{"retryLimit":{"priority":5,"bestEffort":3},"faultSeconds":130,"recovered":[],"missing":["0199aabb-ccdd-7000-8000-000000000001","0199aabb-ccdd-7000-8000-000000000002","0199aabb-ccdd-7000-8000-000000000003","0199aabb-ccdd-7000-8000-000000000009","0199aabb-ccdd-7000-8000-000000000004"],"outputMetrics":{"fullnet_b2_forward.dropped_records_total":2,"fullnet_priority_forward.dropped_records_total":3,"fullnet_b2_forward.retries_failed_total":1,"fullnet_priority_forward.retries_failed_total":1}}
```

两路输出的 `dropped_records_total` 与删除源文件后缺失的事件数分别相等，`retries_failed_total` 按 chunk 计为 1。两次 130 秒结果不同，说明退避调度会影响达到有限重试上限的时间；探针不固定断言丢弃数，而按每路“恢复记录数 + 输出丢弃记录数 = 输入有效记录数”对账。指标的记录数与 chunk 含义依据 [Fluent Bit 4.1 监控文档](https://docs.fluentbit.io/manual/4.1/administration/monitoring)。这些只是采集器本地指标，仍未证明 Prometheus 实际抓取、告警触发或通知交付。

使用固定 `prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e` 运行 `pnpm test:observability-alerts`，`promtool check rules` 输出 `SUCCESS: 25 rules found`，`promtool test rules` 输出 `SUCCESS`。规则单测见 [fluent-bit-output-alerts.promtest.yaml](../../tests/deployment/fluent-bit-output-alerts.promtest.yaml)，覆盖故障前不触发、Priority/B2 丢弃触发、只有重试不触发、归档别名，以及 `fluent-bit` 固定抓取目标 `up=0` 持续一分钟后告警、恢复后解除。新增目标失联断言先在缺少规则时 RED，补规则后通过。它使用合成时间序列评估 PromQL，仍未验证目标平台的抓取或通知。

新增 [fluent-bit-prometheus-alert-smoke.mjs](../../eng/testing/fluent-bit-prometheus-alert-smoke.mjs) 供专项工作流进行真实 Prometheus 抓取与 firing 告警烟测。其共用的 [抓取配置](../../tests/deployment/fluent-bit-prometheus-scrape.yml) 经固定 Prometheus `promtool check config` 检查，输出 `SUCCESS: 1 rule files found` 和有效配置确认；脚本语法、静态部署契约和工作流接线检查也通过。

同一 Windows Docker Desktop 环境执行 `node eng/testing/fluent-bit-prometheus-alert-smoke.mjs --docker-desktop`，退出码为 0，输出 `Prometheus fired both log-loss alerts and the Fluent Bit target-down alert.`。烟测先确认 `up=1`、两路丢弃计数为零且没有 firing 告警，再向不可达 Forward 的两路注入日志；真实 Prometheus 抓取到两路丢弃计数增长，并观测 Priority critical 与 BestEffort warning 告警均进入 firing。随后停止 Fluent Bit 容器，确认固定抓取目标 `up=0` 且 `FullNetFluentBitTargetDown` 进入 firing。容器和临时夹具由脚本清理。该规则依赖 `job=fluent-bit` 且目标仍在 Prometheus 服务发现中；目标从发现中消失、生产混合入口与 DaemonSet 不可用仍须平台监控处理。精确提交的 GitHub Actions 终态、目标平台抓取、Alertmanager 通知以及下游交付确认仍待独立验收；此结果不解除生产 Collector 门禁。

这项告警烟测使用专门的故障注入配置，把两路重试限制均设为 1，以便确定性地产生丢弃并检查 Prometheus 规则；它不验证当前 Priority `Retry_Limit=False` 的正常积压或缓冲满路径。生产候选路径仍须另外做容量与满盘注入。

后续候选把 Priority Forward 的 `Retry_Limit` 改为 `False`，B2 仍为 3。固定镜像的 130 秒不可达 Forward 探针在 SIGKILL 后删除源文件、保留缓冲并改用文件输出恢复，退出码为 0：Priority 三条原始 ID 全部恢复、丢弃记录数和重试耗尽数均为 0；B2 两条缺失、丢弃记录数为 2、重试耗尽 chunk 数为 1。静态契约也固定两路不同策略。此结果只消除该短时故障样本中的 Priority 重试耗尽缺口；输出队列满、磁盘满、节点失效、真实下游 ACK 与长期故障仍未验证，不能宣称 Priority 无损或打开生产门禁。

两路 Forward 候选随后加入 `Require_ack_response On`。`pnpm test:observability-deploy` 的配置契约 8/8 通过，固定 Fluent Bit 镜像的 `node eng/testing/fluent-bit-collector-route-smoke.mjs --docker-desktop` 路由/不可达输出烟测通过，证明当前版本接受该配置并保持原路由行为。烟测没有运行支持 Forward ACK 的接收端，因此不证明 ACK 到达、接收端持久化或最终可查询。

新增固定镜像双容器 `node eng/testing/fluent-bit-forward-ack-smoke.mjs --docker-desktop`：发送端从候选配置提取两路 Forward 输出，仅把目标地址和本地测试传输替换为 Docker 网络；接收端用 Fluent Bit Forward 输入与文件输出。Priority/B2 各一个 ID 到达一次，发送端 `fluentbit_output_proc_records_total` 各为 1、丢弃数为 0。随后用只读 TCP、不发 Forward ACK 的接收端替换正常接收端；它确认收到后续 Priority 数据，但发送端成功记录数保持 1，后续 ID 没有被误计为成功。此测试证明固定版本的协议 ACK 开关实际生效，仍不证明接收端在回复 ACK 前已刷盘，也不代表生产接收端兼容、TLS/证书或最终 ES/Kafka 投递确认。

同一脚本又把无 ACK 的待发 ID 交给 `storage.type filesystem`、`storage.sync full`、`storage.checksum on` 的固定版 Forward 接收端，故意让其下游不可达。发送端收到协议 ACK 后成功计数从 1 增为 2，此时文件出口仍没有该 ID。接收端随后 SIGKILL，保留同一缓冲目录并改为文件输出重启；原 ID 恰好恢复一次，本地运行退出码 0。这证明该固定镜像、挂载目录和单事件故障窗口内，ACK 后的接收端缓冲可跨进程崩溃恢复；它不证明生产接收端配置、卷/节点故障、满盘、长期积压、接收端在写入缓冲前错误 ACK 的所有竞争窗口，或最终下游确认。

审查后强化无 ACK 负例：原测试在 TCP 接收端读到 ID 后立刻看成功计数，未跨越等待期；要求实际产生重试后才检查时，固定镜像在保持连接但不回应的 60 秒内没有重试，测试按预期失败。根因与 [Fluent Bit Networking 配置](https://docs.fluentbit.io/manual/administration/networking)一致：`net.io_timeout` 默认 `0s`，没有该连接空闲 I/O 的超时上限。候选两路 Forward 都设置为 `5s`；再次实测无 ACK 接收端收到 ID 后发送端出现重试、成功计数仍不增加，然后 ACK 接收端缓冲并经 SIGKILL 恢复通过。静态契约 8/8 通过。专项 CI 总超时由 8 分钟提高为 15 分钟，容纳串行路由、ACK、Prometheus 故障探针及冷镜像准备；精确提交的 Actions 终态仍待取得。
