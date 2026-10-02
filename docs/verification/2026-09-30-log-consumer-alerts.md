# 独立日志消费者指标与告警评估验证

- 日期：2026-09-30；授权：用户继续推进日志开发，按全项目本地验收标准实施。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`，任务快照 `logging-consumer-alerts`；保护既有工作区改动，未提交或切流。
- 范围：独立 JIT 消费者、可选 PodMonitor、Prometheus 规则和本地验证脚本；API/Worker Native AOT 与业务数据库行为未改变。
- 环境：Windows/.NET 10、Docker Desktop、本地 kind 三节点；外部隔离单节点 TLS Kafka/HTTPS Elasticsearch、Prometheus v3.15.0 测试容器。

## 实现

10 秒 SDK 统计回调只读取已有统计，不增加同步网络查询。按实际分配的分区身份聚合 `consumer_lag`，不使用 stored/app 位点。统计限 262144 字符、深度 16、128 个已分配分区；缺失、负值、重复、非 UP Broker、身份变化或超过 30 秒的原始统计样本均未知，输出 `lag_known=0`、`lag_records=-1`，避免假零。该值仍是 SDK 缓存快照，不等于强实时或整个消费组积压；语义核对[官方 librdkafka Statistics](https://github.com/confluentinc/librdkafka/blob/master/STATISTICS.md)。

ES/DLQ/Offset 观测适配器只有九条固定结果序列，在实际确认后计数；异常、取消、重试与位点提交顺序不变。固定类别计数不携带 Topic、身份、原始事件或异常消息。快速进程失败可能在抓取前丢失计数，以目标不可达和 Kubernetes 重启告警补充。

PodMonitor 默认关闭，启用依赖 Operator CRD、实际标签/命名空间选择器和监控命名空间匹配。重启规则依赖 kube-state-metrics 的应用标签 allowlist，配置与实际标签检查已写入[平台说明](../../deploy/observability/README.md#independent-log-consumer-monitoring)，不隐式假设平台已接线。

## 已完成验证

| 命令或实验 | 实际结果 |
| --- | --- |
| Unit/Integration 项目 Release 构建 | 两者退出 0，0 警告/错误 |
| `pnpm test:dotnet:unit -- --no-build --selection logging-delivery` | 最终 297/297，0 失败/跳过，含新增 12 项统计/观测边界用例 |
| Linux SDK 容器运行 Kafka 日志四类集成测试（下方命令） | 最终 14/14，0 失败/跳过，退出 0，5m53s；覆盖 ES/DLQ/Offset、TLS/SASL、崩溃/重平衡和真实进程 SIGTERM |
| `pnpm test:observability-alerts` | promtool 规则/配置检查及 Fluent Bit、消费者两组告警触发与解除用例全部通过 |
| `pnpm test:helm` | 26/26，0 失败/跳过；最终 schema 增加必填 PodMonitor 后其聚焦合约另通过 5/5 |
| `pnpm test:observability-deploy` | 9/9，0 失败/跳过 |
| `pnpm test:integration:tooling` | 53/53，0 失败/跳过 |
| `pnpm test:integration:partitions` | 1097 项，无遗漏/重复 |
| `pnpm test:governance` | 57/57，0 失败/跳过 |
| `docker build -f deploy/containers/log-consumer.Dockerfile -t fullnet-log-consumer:local-20260930 .` | 最终镜像构建退出 0，镜像 ID `sha256:f39de82a033b1b5b14ac1d62d173f868f628ac59f4de61038e3b30b857ac9363` |
| `pnpm test:log-consumer:kubernetes:live` | 最终退出 0；真实 Pod→Prometheus：up=1、lag_known=1、lag_records=0、ES confirmed=1、DLQ confirmed=1、Offset confirmed=2 |

真实 Prometheus 使用仓库正式规则：主动中断测试端口转发桥后，TargetDown 按原 1 分钟等待触发；重建转发桥后 up 恢复为 1，告警解除。此实验验证采集目标不可达的评估闭环，不冒充 Pod 自身故障，也没有发送外部通知。CRL 吊销/换证与 ES/DLQ/Offset 重放同时回归：3 条来源记录 → 2 条 ES 文档 + 1 条 DLQ，最终 Offset=3、lag=0。

最终本地工件：`artifacts/log-consumer-kubernetes/result.json`；命名空间 `fullnet-log-e2e-3033d1b0`，完成时间 `2026-09-30T15:11:48.004Z`，`prometheusScrapeVerified=true`、`targetDownAlertFiredAndResolved=true`、`cleanupCompleted=true`。随机专用命名空间、Secret、4 个测试容器、端口转发与临时私钥目录在发布通过报告前全部清理。

最终 Linux 回归使用已重新构建的程序集执行：

```powershell
docker run --rm --network host -v /var/run/docker.sock:/var/run/docker.sock -v /g/wwwroot/github_fork/Full.NET:/src -w /src -e DOCKER_HOST=unix:///var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e FULLNET_TESTCONTAINERS_REUSE=0 fullnet-native-aot-publish-sdk:10.0 dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --filter 'FullyQualifiedName~KafkaLogDeliveryTests|FullyQualifiedName~KafkaLogConsumerDeliveryTests|FullyQualifiedName~KafkaLogConsumerElasticsearchReplayTests|FullyQualifiedName~KafkaLogConsumerElasticsearchTlsTests' --minimum-expected-tests 14 --timeout 15m --results-directory artifacts/log-consumer-alerts/test-results --report-trx --report-trx-filename linux-logging-kafka.trx
```

最终 TRX：`artifacts/log-consumer-alerts/test-results/linux-logging-kafka.trx`。任务 slice 影响集为 integration-matrix、logging-kafka，已按实际范围验证；不复用补充 Broker DOWN 保护前的通过结果来证明最终代码。

## 失败与审查修正

新增五项告警用例先在缺少规则时失败，补规则后实际 promtool 通过。初次构建误用不存在的枚举成员 Confirmed，核对既有枚举后改为 Succeeded，最终构建通过。

`BrokerFailureDoesNotRefreshCachedZeroLag` 先真实失败：Broker DOWN 的缓存零值仍 known=1。补每个已分配分区对应 Broker UP 检查后通过，最终日志聚焦单测全部通过。

独立审查指出 kube-state-metrics 标签导出前提遗漏、信号提前结束的端口转发可能错过 close 而卡住清理；已补平台配置说明，并在进程启动时保存关闭 Promise，在所有退出路径复用。复核未发现阻塞项。

未验证：真实 Operator 自动发现 PodMonitor、目标平台 kube-state-metrics allowlist 和重启规则接线、Alertmanager 通知接收器送达、集群内 Kafka/ES HA、容量/请求 P99。未改变独立消费者 experimental 门禁，不承诺吞吐提升。
