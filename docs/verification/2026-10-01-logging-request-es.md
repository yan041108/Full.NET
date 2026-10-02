# 真实 HTTP 请求至 Elasticsearch 本地闭环

任务基线 `c609759576c4bf54ff9e743161ecb829ca8d4d4a`，任务快照 `logging-request-es-20261001`。2026-10-01 Windows 10.0.19045、.NET 10.0.12、20 个逻辑处理器、Docker Desktop 单机；没有修改生产配置或实施切流。

新增 `Projected_http_logs_are_confirmed_in_elasticsearch_before_offset_completion`，纳入 `pnpm test:logging:request-kafka:live` 两项分片。真实 loopback Kestrel 与正式 ApplicationKafka 静态出口，TLS Kafka 4.1.2，Projected 200 次预热/5000 次实测、目标 500 次/秒、最多 8 个请求循环。宿主 Stop/Dispose 后冻结双 Topic 高位点，按最多 8 条使用正式 BatchLogDeliveryProcessor、ElasticsearchLogDocumentSink 和 KafkaLogOffsetCommitter；Completed 与提交条数均须一致，任何 Retry 或隔离立即失败。

ES 使用与既有 Replay 测试相同的固定镜像摘要 `82ac14f43fe701992e601f4cc81e1c0d7dbc5a2576d8cd736006452925df4026`，单节点、512MiB Java Heap、本地 HTTP 无认证；仅测试传输将 Sink 的固定 HTTPS 测试地址改写为该本地地址。没有放宽生产 Sink HTTPS 限制，也不将此报告为 ES TLS/API Key 验收。

来源记录含冻结版本 1、30 天保留与事件时间。目标名另以固定前缀/版本/分类/事件 UTC 日期独立验证，再对每个索引 refresh/count，200 个 ID 一组 `_mget`，检查目标索引、完整唯一 ID 集合及全部 `_source` 深比较，包含请求/返回投影。最终每分区实际 committed offset 必须等于冻结 Broker 高位点。结果保存真实 offset/brokerEnd，不仅保存通过布尔值。

首次真实闭环通过：5206 条 ES 文档，包括 5200 条 HTTP Operation 与 6 条宿主启动等日志，两个固定日期/分类索引，所有来源字段与 Broker 收据一致，隔离为零。原 Broker Summary/Projected 与 ES 新用例完整两项分片也通过。随后补充独立索引名校验和实际位点保存，对最终代码另行构建和单项重跑；最终结果以本地 `artifacts/logging-request-es/result.json` 为准。

最终加固后单项重跑通过（1/1、0 跳过、59.446 秒）：5206 条文档/两个索引逐项一致，HTTP 日志/唯一 RequestId/安全投影均为 5200，预期 500 为 520，Console 镜像 0 字节。实际提交 General=4686、Priority=520，分别精确等于对应 Broker 高位点；隔离为零。固定请求档实际约 499.85 次/秒，HTTP P99 2.4671ms，此延迟不包含最终 ES 消费与调度等待，不作为端到端投递 P99。
验证过程与命令：

- 最终单项命令：`dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --filter FullyQualifiedName~Projected_http_logs_are_confirmed_in_elasticsearch --minimum-expected-tests 1 --timeout 5m --no-ansi --progress off --report-trx --report-trx-filename KafkaLogRequestElasticsearchTests.trx`，1/1 通过。
- `git diff --check` 与本轮新增文件 whitespace 检查：通过。
- 新闭环占位 RED：1/1 失败，原因 NotImplementedException；正式实现后真实 ES/Kafka 单项 GREEN。
- `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore`：最终顺序重建成功，0 警告/错误。
- `pnpm test:logging:request-kafka:live`：原 Broker 两档与 ES 新用例共 2/2 通过、0 跳过，约 88 秒；此轮在最终索引/位点加固重新构建之前，未冒充最后构建验证。
- `pnpm test:integration:tooling`：54/54；ES 共享夹具路径误落 Outbox 的回归 RED/GREEN 已覆盖，改为自身真实请求分片。
- `pnpm test:governance`：57/57。
- `pnpm test:integration:partitions`：1100 项发现，无遗漏/重复，重型 Kafka=74、日志 Kafka=17、请求日志分片=2；只验证发现集合，未执行全部集成。
- 一次构建因同时进行的发现进程在 Windows 锁住目标 DLL 而失败，改为发现/运行结束后顺序重建；失败未报告为通过。

实际 Broker JSONL、HTTP/调度样本与通过报告分别保留在忽略的 `artifacts/logging-request-es`；报告只在 Kafka/ES 夹具与临时 CA 释放完成后写入。独立只读审查未发现阻塞项，路由共用逻辑风险已通过固定索引名断言加固。

本场景消费发生于请求结束后的同一测试进程，不是独立消费进程或持续并发生产消费压测。请求延迟窗口不含 ES 最终消费，不能据此宣称完整 ES 交付 P99；没有 Collector 同轮对比、ES TLS、多 Broker HA 或长期容量结论。
