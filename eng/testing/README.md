# Full.NET 测试矩阵与本地门禁

权威数字与分片定义只在 [`test-matrix.json`](test-matrix.json)。本文只描述**如何少误跑**。

2026-09-30 起，全部项目验收采用[本地实际通过即可验收](../../rules/development-quality.md#11-测试与验证)的标准。CI 为可选自动回归；真实双库、浏览器、Kafka、Linux Native AOT 与容量测试均可在本地完成。失败、跳过与未测范围仍不得报告为通过。

## 日常漏斗

| 阶段 | 何时 | 命令 |
| --- | --- | --- |
| inner | 每次改代码 | `pnpm test:inner -- --snapshot <id>`；先审查时加 `--plan` |
| slice | 纵向功能切片关闭 | `pnpm test:slice -- --snapshot <id>` |
| merge | PR / 合并候选 | `pnpm test:integration:affected -- --snapshot <id> --phase merge` |
| 完整验收 / 可选 main CI | 发布回归 | 按矩阵分批执行互斥 Integration 分片（含 `messaging-heavy`） |

`pnpm test:inner` / `pnpm test:slice` 是 `test:integration:affected --phase inner|slice` 的薄封装。工作区脏或跨窗口时先 `pnpm test:task:start -- <task-id>`，后续一律 `--snapshot <task-id>`。

## 不要做的事

- 本地允许显式 `pnpm test:integration:full` 或按矩阵分批执行；普通修改优先受影响验证，不自动运行全量。
- **inner 禁止** `pnpm test:e2e:real`、完整 `pnpm test:e2e:admin` 和 `messaging-heavy`。真实栈只用于 `Verified` 或 CORS/Cookie/Session 缺陷。
- 改文档 / 纯前端 / 纯 `benchmarks/` 不必跑 Integration。
- merge 默认不跑 `messaging-heavy`；Kafka/CDC/Capacity 变更在本地 slice 验证，完整重测可用 `pnpm test:integration:messaging-heavy` 或可选 CI。
- 需要本地 merge 也跑重测时，追加 `--include-heavy`。
- 禁止多个用例共享可变业务库；只读 schema 模板克隆到独立库、本地 Testcontainers 复用是允许的加速。

## 按变更选门禁

| 变更 | 最低验证 |
| --- | --- |
| 单模块 CRUD（不动 SQL/租户/认证） | Unit + affected inner/slice |
| SQL / 事务 / 租户 / 迁移 | 同场景 SQL Server **与** MySQL（slice/merge） |
| Messaging / Kafka / CDC / Connect | Unit + affected slice（含 `messaging-heavy`） |
| 共享宿主 / Composition / Identity / Tenancy | inner 立即跑登记聚焦集；merge 追加 Smoke |
| 测试矩阵 / 选择器 | `pnpm test:integration:partitions` + `pnpm test:governance` |

## inner 与双库

`inner` 聚焦测试只强制 **MySQL** Provider，Smoke 也只跑 MySQL 项；`slice` 与 `merge` 仍要求双库。SQL/迁移/租户相关变更不得在 inner 阶段单独宣布完成。

## 慢测与分片

- `pnpm test:log-consumer:throughput-proof`：快速验证逐 ID/完整内容对账、缺失/重复拒绝与固定字节工作负载生成。
- `pnpm test:observability:request-replay:live`：先运行持续请求档生成 `artifacts/logging-request-sustained/Projected-1.jsonl`，再将 5200 条真实 HTTP 投影快照和 5200 个不同合法 UUID v7 的 ApplicationKafka 镜像送入固定 Fluent Bit 的正式 CRI/可信元数据/过滤链。固定 15 秒预装回放，8MiB 输入上限，文件出口替代 Forward；逐 ID/路由/字段/投影对账，要求 520 Priority/4680 B2、镜像全部排除。绑定单次读取源摘要，保留实际两路输出，确认容器/临时目录清理后写 `artifacts/collector-request-replay/result.json`；不代表 Kafka/ES/ACK、持续请求采集延迟或吞吐，失败不会保留旧通过报告。
- `pnpm test:logging:request-kafka:live`：先 `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release`，运行独立 `logging-request-kafka` 分片（5 项/8 分钟），实际 TLS Kafka 4.1.2 + 正式 ApplicationKafka 出口，Summary/Projected 各 200 预热/5000 实测/目标 500 次每秒。宿主释放后消费双 Topic 至固定高位点，核对 Key/ID/优先路由、5200 唯一 RequestId/520 预期 500、投影，Summary 不含任一 Payload。收据 JSONL 仅规范化行分隔符，结果与样本在仓库 `artifacts/logging-request-kafka`；TLS 夹具清理后才写通过，原 Local 命令兼容。第二项将真实 Projected 记录经正式最多 8 条批次协调器和实际 Kafka Committer 写入固定 ES 明文夹具，全部来源/投影及最终位点对账，结果在 `artifacts/logging-request-es`。第三项在请求前启动正式独立消费者，使用私有 CA/受限 API Key 的 HTTPS ES，父进程不写 ES/提交 Offset，等待最多 120 秒排空、确认最终位点与全部文档、DLQ 为零，结束自身子进程与清理夹具后写 `artifacts/logging-request-process/result.json`。没有 Collector 同轮比较、长期容量或端到端投递 P99 结论；临时 CA NoCheck 仅用于 Development，清理不是优雅停机验证。
- 上述分片第四项为 Collector 真实请求快照的有限 CRI 回放：200 预热/5000 实测/目标 500 次每秒后，固定 Fluent Bit 使用现有可信 Pod 过滤与候选 TLS Kafka 双出口，独立消费者写 HTTPS ES。每条镜像使用不同 UUID v7，必须排除；实际 tail 输入=两倍来源、两路 Kafka 输出=来源，正常退出后冻结高位点并核对 ES 全文档/最终 Offset/DLQ=0。配置、日志、指标与结果在 `artifacts/logging-collector-chain`；默认 Forward 配置未改变，不称为实时采集或路线性能比较。
- 第五项在请求前启动采集器/消费者，通过本地实时 Console→CRI 桥接绑定空目录；只在实测发送窗口选择事件时间不早于窗口开始的 HTTP 收据，实际 Group 同分区提交必须达到该事件 offset+1，排除预热与请求结束后的确认。最终全部字段/镜像输入指标/位点/ES 对账，资源清理后写 `artifacts/logging-live-collector/result.json`。共享进程资源含桥接和观察成本，不当作 Kubernetes CRI、路线优劣或容量证据。
- `pnpm test:logging:request-latency:sustained`：复用上述真实 Kestrel/Console 对照，显式 `--sustained`，每档预热 200/实测 5000、目标 500 次/秒，计划发送跨度 9998ms，最多 8 个发送循环。使用单调时钟和可取消等待，单档发送最多 30 秒、六档总预算 5 分钟；调度落后延迟与 HTTP 延迟分别保存原始数组和分位数，避免掩盖客户端跟不上。最终开启日志每档须有 5200 个不同 RequestId、520 条预期 500，Projected 须有 5200 组投影。独立结果在 `artifacts/logging-request-sustained`，原短样本不被覆盖；该有限持续场景仍不代表长期 Soak、业务 API 或 Kafka 容量。
- `pnpm test:logging:request-latency:live`：Release 基准项目创建随机 loopback Kestrel，复用正式 B2 HTTP 日志中间件，对比 Disabled、100% Summary、安全分页请求/返回投影；并发 8、每档预热 200/实测 2000 请求、10% 预期 500，正序/逆序各一轮。Console JSON 接到独立本地文件，最终释放宿主后对账日志/投影、唯一 RequestId、预期 500 数量，并要求释放前丢弃计数为零，才写 `artifacts/logging-request-latency/result.json`，保留全部延迟样本及日志。请求超时 10 秒，总运行取消预算 5 分钟，失败清空旧结果并恢复 Console/释放宿主。该短样本是日志中间件局部特征，客户端与服务端共享进程，不代表带认证/数据库/Kafka 的完整 Host.Api P99、资源独占成本或容量。释放前丢弃快照不证明停机全过程无其他日志丢弃；CPU/分配只覆盖发送窗口，未包含最终排空。
- `pnpm test:log-consumer:sustained-proof`：持续输入/恢复追赶判定的快速负例验证，缺少真实积压、未在输入期间追平、位点不一致或超预算均拒绝。
- `pnpm test:log-consumer:sustained:live`：使用 `fullnet-log-consumer:local-batch-20261001` 和批次上限 8（先按容器 Dockerfile 构建该标签），单连接按 200ms/10 条节奏发送 3000 条、每条 2048 字节 JSON，持续至少 59 秒、最多 90 秒。第 20 秒暂停任务 ES 10 秒，要求观测积压不超过 1000 条、输入仍继续时追平，发送结束后最多 30 秒排空；逐 ID/内容对账、最终 Offset=3003/lag=0、DLQ 不新增、Pod 不更换或额外重启。结果在清理后存入 `artifacts/log-consumer-sustained/result.json`，原功能计数与 `sustained` 分开。暂停只涉及随机任务容器，失败也恢复/清理；这是有限本地场景，非长期容量或请求 P99，不加入日常 inner。
- `pnpm test:log-consumer:throughput:live`：复用本地 Kubernetes 链路和 TLS 吊销恢复实验，随后追加 1000 条、每条 2048 字节 JSON，要求总发送到提交观察不超过 120 秒；逐 ID 核对 ES、DLQ 不新增、Offset=1003/lag=0，记录消费者 cgroup CPU/节流和 Pod 生命周期内存峰值。报告为 `artifacts/log-consumer-throughput/result.json`，清理后才发布。单分区单消费者突发基线不代表持续容量或应用 P99；顶层计数为原三条功能阶段，新增突发及最终位点在 `throughput` 中独立记录。

- `pnpm test:log-consumer:notifications:live`：复用重启实验，按[本地通知说明](../../deploy/observability/README.md#independent-log-consumer-monitoring)预载固定镜像。临时 Alertmanager→集群内 Webhook 实际交付四阶段同指纹通知，恢复专用 Prometheus 的原 `spec.alerting` 并清理后发布结果 `artifacts/log-consumer-notifications/result.json`。不发送外部通知，不加入日常 inner；接收端边界测试已纳入 `pnpm test:observability-deploy`。
- `pnpm test:log-consumer:notification-retry:live`：显式拒绝前两次有效通知，核对真实 Alertmanager 对同一 firing 指纹的 `503 → 503 → 200` 重试，再完成四阶段通知和配置恢复。失败尝试不算成功接收，保留证据有界；独立报告为 `artifacts/log-consumer-notification-retry/result.json`。仅验收短暂 HTTP 503，不覆盖通知进程重启、HA、长期故障或容量/P99。
- `pnpm test:log-consumer:notification-restart:live`：首次 firing 送达后删除并重建隔离 Alertmanager，校验 UID 改变、新 Pod 就绪、同指纹 firing 重新送达，再验证恢复/再次触发/恢复。独立报告为 `artifacts/log-consumer-notification-restart/result.json`；emptyDir 丢失，证明 Prometheus 重发后的重新接收，不证明持久队列恢复或 HA。
- `pnpm test:log-consumer:notification-outage:live`：从首次有效通知开始以单调时钟持续 60 秒返回 503，故障期间不确认接收，核对实际重试时间与同指纹恢复交付，再完成四阶段通知。报告为 `artifacts/log-consumer-notification-outage/result.json`，恢复配置及清理后才发布；不推断任意时长故障、持久恢复或 HA。

- `pnpm test:log-consumer:restart-alert:live`：按[本地重启 Overlay](../../deploy/observability/README.md#independent-log-consumer-monitoring)接线 kube-state-metrics。真实消费者缺配置退出，验证实际重启指标、工作负载命名空间、应用标签、正式规则触发、标签排除/恢复和删除测试 Pod 后解除。复用 Operator，无 Kafka/ES 重测；不验证健康恢复或外部通知，结果在清理成功后存于 `artifacts/log-consumer-restart-alert/result.json`。

- `pnpm test:log-consumer:operator:live`：在上述真实链路上增加 PodMonitor 自动发现、命名空间/对象双选择器排除和 Pod IP 直接采集验证。先按[本地监控配置](../../deploy/observability/README.md#independent-log-consumer-monitoring)安装固定版本工具。工具 release/CRD 保留复用，随机消费者及 Secret/容器照常清理；结果存于 `artifacts/log-consumer-operator/result.json`。不验收 kube-state-metrics、通知或容量。

- `pnpm test:log-consumer:kubernetes:live`：显式运行本地 kind 独立消费者链路。先构建 `fullnet-log-consumer:local-20260930` 镜像，要求 Docker Desktop 和 `kind-fullnet-local`/`fullnet-local` 集群存在。脚本创建随机专用命名空间与隔离 Kafka/ES/Prometheus 容器，验证 Production/Online CRL、ES/DLQ 和 Offset，实际采集 Pod 的积压/确认指标，并通过中断与恢复测试转发桥验证正式 TargetDown 规则触发/解除；这不是 Pod 真实故障的模拟。退出清理短期凭据；不加入日常 inner，也不验证集群 Kafka/ES 高可用或容量。结果写入 `artifacts/log-consumer-kubernetes/result.json`，清理成功后才发布，重跑先清除旧结果。
- `pnpm test:observability-alerts`：实际 promtool 校验规则、采集配置和 Fluent Bit/独立消费者的告警触发及恢复用例；不代替 kube-state-metrics 标签接线或通知送达验证。

- `pnpm test:integration:durations`：分析 TRX，找 Top 慢测。
- `pnpm test:integration:messaging-heavy`：Kafka/CDC/Capacity Docker 重测（51 项，与 infrastructure 分离）。
- 新增 Integration 前先读 [`rules/development-quality.md`](../../rules/development-quality.md) §11.2。

## 推荐内循环

`pnpm test:logging:secret-boundary:live`（先 Release 构建集成测试项目）独立运行 Collector/ApplicationKafka 两项三记录安全链路：随机秘密和受限详情实际进入日志调用，检查 Console/Producer 安全来源、Broker 与全部 ES 字段、最终 Offset/DLQ 和清理后诊断。结果为 `artifacts/logging-secret-boundary/<route>/result.json`；不测容量，不覆盖独立 File Sink、旧 Sink、归档或 B1 库。见[验证记录](../../docs/verification/2026-10-01-logging-secret-boundary.md)。

日志两路线的独立比较使用 `pnpm test:logging:route-comparison:live`（先 Release 构建集成测试项目）。此独立分片按 Collector → ApplicationKafka → ApplicationKafka → Collector 运行四档固定请求，子进程测量、父进程验证全部 ES 文档/Offset/DLQ 与清理，结果在 `artifacts/logging-route-comparison/result.json`。实测请求身份与预热分离；CPU/GC 排除父进程桥接和观察，但客户端/服务端仍在同一被测进程。它不增加日常 `logging-request-kafka` 五项分片成本；范围与比较说明见[验证记录](../../docs/verification/2026-10-01-logging-route-comparison.md)。

```powershell
pnpm test:task:start -- my-feature-20260816
dotnet build Full.NET.slnx -c Release
pnpm test:dotnet:unit -- --no-build --filter FullyQualifiedName~YourArea --minimum-expected-tests $expectedCount
pnpm test:inner -- --snapshot my-feature-20260816 --plan
pnpm test:inner -- --snapshot my-feature-20260816
```

功能切片关闭后再 `pnpm test:slice -- --snapshot my-feature-20260816`；PR 前 `--phase merge` 与 `pnpm test:governance`。`test:e2e:real` 不要放进 inner。
