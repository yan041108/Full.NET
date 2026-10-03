# Kafka Connect Elasticsearch Sink 源码准入筛查（2026-09-30）

- 范围：LG00 日志 Kafka → ES 的消费者确认边界候选；只读源码/许可筛查，不是运行、故障或容量验收。
- 仓库基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`；本记录所述日志实现仍为未提交工作区，生产 Collector 与 Kafka 消费门禁关闭。
- 方法：固定候选源码提交，追踪 Kafka Connect `flush` → 写入器 `commit` → ES Bulk 响应处理，并与 [Kafka `SinkTask.preCommit` 契约](https://kafka.apache.org/37/javadoc/org/apache/kafka/connect/sink/SinkTask.html)比较；核查上游许可证与目标 ES 适用性。未运行候选插件。

| 候选 | 核查证据 | LG00 结果 |
| --- | --- | --- |
| IBM Kafka Connect Elasticsearch Sink，源码提交 `276c755442f68824716c88adeb2aa02754a52da7`，Apache-2.0 | [`ElasticWriter.commit()`](https://github.com/ibm-messaging/kafka-connect-elastic-sink/blob/276c755442f68824716c88adeb2aa02754a52da7/src/main/java/com/ibm/eventstreams/connect/elasticsink/ElasticWriter.java#L327-L335) 对 Bulk HTTP 2xx 中的 `errors=true` 只 `log.error`，然后重置失败计数；[`ElasticSinkTask.flush()`](https://github.com/ibm-messaging/kafka-connect-elastic-sink/blob/276c755442f68824716c88adeb2aa02754a52da7/src/main/java/com/ibm/eventstreams/connect/elasticsink/ElasticSinkTask.java#L104-L115) 调用 `writer.commit()`。 | 该版本不能证明逐项最终写入先于 Offset 提交；可能将失败项当作已完成，可靠档排除。 |
| Confluent Elasticsearch Sink | [官方概览](https://docs.confluent.io/kafka-connectors/elasticsearch/current/overview.html)声明至少一次并描述部分失败/DLQ；[源码仓库](https://github.com/confluentinc/kafka-connect-elasticsearch)使用 Confluent Community License。 | 文档声明不足以替代真实逐项/DLQ/Offset 故障实验；进入 Full.NET MIT 发布物前须完成许可评估，暂不选用。 |
| Aiven OpenSearch Connector | [源码仓库](https://github.com/Aiven-Open/opensearch-connector-for-apache-kafka)为 Apache-2.0、目标为 OpenSearch。 | 目标不是当前 ES 组合；未验证 ES 兼容性、Bulk 部分失败及 Offset，暂不选用。 |

上述结论只适用于列出的来源与固定版本，不能推断所有 Kafka Connect 插件均不可靠。`SinkTask.preCommit` 提供提交前限制水位的接口，但不会自动修复插件吞掉 Bulk 单项错误的实现。LG00 将在不改变生产门禁的前提下研发独立平台 SinkConfirmed 候选；只有真实 Broker、ES/DLQ 故障、重平衡、崩溃窗口及容量证据通过后，才可决定是否替代原消费基线。未验证任何插件的运行时行为，也未批准第三方许可或生产切流。
