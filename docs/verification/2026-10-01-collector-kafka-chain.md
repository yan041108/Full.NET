# Collector 真实请求快照至 TLS Kafka 与 HTTPS ES

基线 `c609759576c4bf54ff9e743161ecb829ca8d4d4a`，任务快照 `logging-collector-chain-20261001`。本地 Windows 10.0.19045、.NET 10.0.12、20 个逻辑处理器、Docker Desktop 单机。默认 Forward 配置与生产开关未改变。

新增 `Collector_cri_requests_reach_tls_kafka_and_https_es_without_application_kafka_mirrors`，真实 Kestrel 使用正式 `DeliveryMode=Collector`、冻结索引版本 1/保留 30 天、安全请求返回投影。200 次预热、5000 次实测、目标 500 次/秒、最多 8 个发送循环。先完成宿主释放和原始 Console JSON 对账，再将全部快照转成 CRI F 行。这个 HTTP 延迟窗口不含回放期间的平台消费，不能当作持续采集或两路线同轮性能对比。

固定 Fluent Bit 镜像 `4.1.1@sha256:2a5cb41f99b7c5f3386bb34d42ce3b19eb47fbef6d93b64c0bd4afe9e6ec389c` 使用仓库现有 CRI、多行、可信 Pod 标签、解析和优先分类过滤。仅改本地路径、元数据预装和候选 Kafka 出口：两 Topic、LogEventId Key、JSON、附加 `collector.timestamp`，TLS CA 和主机名验证、acks=all、幂等、15 秒消息期限、每路 32MiB/10000 条 librdkafka 缓冲与有限满队列重试；Priority/B2 保留原有不同的重试和磁盘预算。参考 [Fluent Bit 官方 Kafka Producer 文档](https://docs.fluentbit.io/manual/data-pipeline/outputs/kafka)。这份出口位于测试夹具及本地 `collector.conf` 工件，没有替换部署参考中的 Forward/S3。

本地 Kafka 4.1.2 夹具额外提供未映射到宿主的 Collector SSL 监听；Fluent Bit 只共享该测试 Broker 网络命名空间，不依赖 Docker Desktop host 网络开关。父进程从正式外部 TLS 监听读取来源，正式独立消费者以批次 8 写入真实 HTTPS ES。ES 使用前一切片的固定镜像、私有 CA、仅 `fn-logs-1-*` 的 write/auto_configure API Key；临时 CA NoCheck 仅在 Development 允许，Production 吊销策略未改。

每条来源另生成一个独立 UUID v7 的 ApplicationKafka Pod 镜像，正文伪造 Collector 标签，HTTP RequestId 带 mirror 前缀；镜像不得替代原始 ID。来源按唯一 LogEventId/Key、Topic 分类、全部原字段比较，仅移除仓库过滤策略规定的 DiagnosticGroup/kubernetes/tenant_id/user_id，允许 CRI time/stream/_p 与候选 Kafka timestamp 元字段，_p 必须为 F。完整输入完成由实际指标证明：tail 输入必须恰好为两倍来源条数，两 Kafka 输出 proc_records 合计恰好为来源条数，指标原文保留。随后采集器须正常退出、退出码 0，再冻结 Broker 高位点；所有 Topic 高位点合计必须等于来源条数，不能遗漏尾部镜像或重复。

父进程不写 ES 或提交 Offset，只等待正式消费者 Group 的连续确认位点精确等于冻结高位点，DLQ 高位点零、消费者仍存活。ES 逐索引 count/全部 ID `_mget`，完整来源深比较。仅强制结束自己的消费子进程，确认退出并释放全部容器、临时 CA 后才写通过报告；不将子进程清理称为优雅停机验收。

新增用例占位 RED：NotImplementedException、1 失败、0 跳过；首轮实现 GREEN：1/1、80.238 秒，5206 文档/两个索引，General=4686、Priority=520，最终位点一致。独立审查指出镜像共享 ID 会掩盖替代误投，以及 Collector 报告错误标注 Local；两项均已修复，增加独立镜像 ID、实际全输入指标和正常退出验证，复核未发现新的问题。

最终加固后的 Collector 报告：5206 条原始来源、5206 条独立镜像；实际 tail 输入 10412、Kafka B2 输出 4686、Priority 输出 520，镜像全部排除。Collector 正常退出码 0，Broker General=4686/Priority=520，均等于正式独立消费者提交位点；ES 5206 文档/两个索引逐字段一致，DLQ=0，进程退出与资源清理完成。模式正确为 Collector，释放前丢弃为零；仅请求窗口约 499.89 次/秒，HTTP P99 2.6101ms，不能包含在平台回放吞吐或投递 P99 中。

本轮验证：Release Integration 构建成功、0 警告/错误；日志单测 315/315、工具测试 54/54、治理测试 57/57；1102 项集成发现无遗漏/重复（messaging-heavy=76、日志 Kafka=19、日志请求=4），未运行全部集成。实际来源、Broker 收据、HTTP/调度样本、Collector 配置/日志/指标及独立进程诊断保留于 `artifacts/logging-collector-chain`。

最终代码实际执行：

- `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore`：成功，0 警告/错误。
- `pnpm test:logging:request-kafka:live`：4/4 通过、0 跳过、229.238 秒；原 Broker Summary/Projected、同进程 ES、独立进程 HTTPS ES 和 Collector 全链回放均通过。
- `pnpm test:dotnet:unit -- --selection logging-delivery`：315/315 通过、0 跳过。
- `pnpm test:integration:tooling`：54/54；新增测试/夹具路径精确选择日志请求分片。
- `pnpm test:governance`：57/57。
- `pnpm test:integration:partitions`：1102 项发现集合闭合，不代表全部集成已执行。
- `git -c core.safecrlf=false diff --check` 和本任务新增文件 whitespace 检查：通过。

剩余范围：持续实时 CRI 采集与同轮路线资源/P99 对比、Collector Kafka 故障恢复与进程崩溃确认边界、完整业务 API 与长期最大容量。本有限成功回放不放宽 B2 丢失预算，也不解除消费者实验开关。
