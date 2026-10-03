# ApplicationKafka 真实 HTTP 请求本地验证

2026-10-01，Windows 10.0.19045、.NET 10.0.12、20 个逻辑处理器，Docker Desktop 单机 Kafka 4.1.2 TLS 夹具。任务基线 `c609759576c4bf54ff9e743161ecb829ca8d4d4a`，快照 `logging-request-kafka-20261001`。无生产配置或切流变更。

复用真实 Kestrel/正式 B2 中间件和 `KafkaLogSnapshotExporter.Create`，显式 `ApplicationKafka`，冻结 IndexRouteVersion=1、保留 30 天。Summary/安全分页投影各预热 200 次、实测 5000 次，目标 500 次/秒、最多 8 个发送循环，发送预算 30 秒、整项测试 5 分钟。请求线程不等待 Broker ACK。默认正式 Producer 使用 acks=all、幂等、独立双 Topic/后台通道；测试 Broker 单副本不验证集群复制可靠性。

| 模式 | P50 ms | P95 ms | HTTP P99 ms | 调度落后 P99 ms | 实际请求/秒 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Summary | 0.5161 | 1.2627 | 2.2456 | 15.3493 | 499.35 |
| 安全投影 | 0.4293 | 0.9025 | 1.9468 | 15.5148 | 499.46 |

每档发送窗口约 10.01 秒。宿主 Stop/Dispose 排空后，以双 Topic 最终高位点为边界读取 Broker 收据，检查消息 Key=LogEventId、Priority/General Topic 路由；两档各 5200 个不同 RequestId、520 条预期 500，Projected 5200 组正确投影，Summary 不含任一 Payload。两档 Console 镜像均为 0 字节。释放前宿主丢弃快照为 0，只声明该采样时点；最终 Broker 对账是 HTTP 交付证据，不把 SDK 接收当作持久确认。

CPU/托管分配来自共享客户端/服务端发送窗口，Summary 5421.875ms/163054720 字节，Projected 1703.125ms/183876648 字节；存在启动和 JIT 波动，不作为每日志完整成本或投影更快的结论。CPU 不包含独立 Kafka 容器与最终排空。HTTP P99 从实际发送起算，不含约 15.5ms 的调度落后，发送不是严格均匀的 2ms 请求流。

本地忽略目录 `artifacts/logging-request-kafka` 保存结果、Console 文件和实际 Broker JSONL（仅规范化末尾行分隔符），全部 HTTP/调度样本、字节数及范围。Broker 收据字节数 Summary=6480708、Projected=7172307，包含启动等非 HTTP 事件，不是纯 HTTP 吞吐。测试找到仓库根路径，避免 MTP 切换工作目录后误写到 bin 目录。通过报告只在 TLS 夹具及临时 CA 释放完成之后写入。

实际验证：

- 真实 Broker RED：占位出口导致 1/1 失败；实现后首次运行因快照换行与 WriteAllLines 叠加产生空 JSON 行而失败，检查原始 Broker 文件定位并修复；最终完整重跑 GREEN 1/1，0 跳过。
- `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore`：成功，0 警告/错误。
- `pnpm test:logging:request-kafka:live`：Summary/Projected 两档及清理通过，1/1，约 30.6 秒，生成专属 TRX。
- `pnpm test:dotnet:unit -- --selection logging-delivery`：315/315，0 跳过，构建无警告/错误。
- `pnpm test:integration:tooling`：54/54；新增影响集负例 RED/GREEN，确保此日志测试选择自身分片，不误落 Outbox。
- `pnpm test:integration:partitions`：1099 项发现无遗漏/重复，重型 Kafka=73、日志 Kafka=16；发现检查不等于运行全部集成。
- 原 Local 短档六组兼容重跑通过；独立审查未发现阻塞项，Summary 单侧 Payload 建议已加固。
- `pnpm test:governance`：57/57；`git diff --check` 与本轮新增文件 whitespace 检查：通过。

下一步仍需同轮 Collector 与 ApplicationKafka、同安全/采样/封套、同 Broker/消费者的比较。本次只有正式 ApplicationKafka 单轮两档，没有 Collector 同轮证据、ES 写入、长期 Soak、多 Broker HA 或完整认证/数据库业务 API 容量结论。
