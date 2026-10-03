# 日志两路线隔离进程比较

本轮增加 `pnpm test:logging:route-comparison:live`，在同一台 Windows / Docker Desktop 主机按 Collector → ApplicationKafka → ApplicationKafka → Collector 顺序运行四个独立场景。每档使用新 TLS Kafka、HTTPS Elasticsearch、受限 API Key 与正式独立消费进程；请求进程使用相同 Kestrel、投影中间件、8 个发送循环、200 次预热、5000 次实测和目标 500 次/秒。

请求子进程仅产生测量报告，`measurementOnly=true`、`receiptVerificationDeferred=true` 不代表交付验收。父进程承担 CRI 桥接和 Broker 观察，核对全部文档、字段、优先路由、最终 Offset 与 DLQ；清理请求进程、消费者及容器后才产生总报告 `artifacts/logging-route-comparison/result.json`。CRI 桥接与观察的 CPU/GC 不计入请求进程，仍共享主机资源。请求进程同时承载压测客户端和服务端，不能解释为纯 API 服务成本。

正式请求以 `RequestId=logging-probe:measured:{sequence}` 标识，预热使用不同前缀。实测日志数量必须为 5000；发送窗口内必须实际观察到其中一条具体 HTTP 记录所在分区已提交越过该记录，确认时间必须早于窗口结束。不能只按日志时间划分阶段，避免预热响应与中间件最终日志之间的时序差异造成误判。

每档核对 5200 条唯一 HTTP 日志、520 条预期 HTTP 500、安全分页请求/返回投影，全部 Broker 记录与 ES 文档逐 ID/字段一致。Collector 还要求可信输入和镜像输入合计 10412 条，镜像全部排除。发送末工作集是瞬时值，不是内存峰值；排空时间包括请求进程完成后的桥接、Collector 停止、来源对账和最终 Offset 等待，不能作为纯消费服务延迟比较。

## 本轮结果

最终修复版本四档通过，测试 1/1、0 跳过，耗时 4 分 33 秒。运行环境为 Windows 10.0.19045、.NET 10.0.12、20 个逻辑处理器、共享 Docker Desktop 主机。

| 路线/轮次 | HTTP P99 ms | 实际请求/秒 | 请求进程 CPU ms | GC 分配 MiB | 发送末工作集 MiB | 实测日志字节/秒 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Collector / 1 | 2.5027 | 499.67 | 9750.000 | 190.28 | 93.07 | 735255 |
| ApplicationKafka / 1 | 2.2878 | 500.01 | 8609.375 | 181.41 | 107.06 | 691755 |
| ApplicationKafka / 2 | 2.2369 | 499.76 | 6468.750 | 180.87 | 103.04 | 691406 |
| Collector / 2 | 2.1733 | 499.93 | 5828.125 | 190.56 | 93.05 | 735641 |

每档实测日志恰为 5000，全部 ES 文档为 5206，共 20824；每档 General 最终 Offset=4686、Priority=520，分别等于最终高位点，DLQ=0、请求出口丢弃计数=0、进程和容器清理完成。Collector 每档排除 5206 条镜像输入；两路线逐来源字段对账，Collector 保留规定的采集元数据，因此日志字节数不同。每档还有具体实测 HTTP 记录在发送窗口内完成真实 Offset 提交证明。收尾排空分别为 8.17/14.32/10.57/7.22 秒，含测试观察与收尾，不作为纯消费者吞吐结论。

保留每档原始 HTTP/调度延迟、Broker JSONL、子进程测量与有界诊断，总报告只在四档全部通过后写入。第一次通过后，独立审查指出固定 Release 路径和按日志时间分阶段的问题；已改为当前构建配置/框架路径及显式请求身份，再完整重跑四档。修正版复核未发现新增问题。

## 配置选择

两条路线继续通过现有 `FullNet:Logging:DeliveryMode` 配置选择。Collector 适合已有统一采集治理和平台缓冲的部署；ApplicationKafka 适合简化日志网络路径并由应用维护后台出口的部署。按请求 P99、每实例日志字节吞吐、CPU/内存、丢弃与故障恢复共同选择。此次固定短时负载不用于自动修改默认配置，也不证明任何队列永远不会成为瓶颈。

本轮 P99/CPU 的优势随顺序改变，不构成稳定性能胜出证据；ApplicationKafka GC 分配较少，但发送末工作集较高。保留两路线配置是此次比较的选择，不追加队列绝对更快或 Kafka 必然更好的判断。

本地测试是项目验收依据；这份比较只覆盖固定请求与单机容器链路。尚不能据此声明完整业务 API、最大容量、长期 Soak、高可用或故障恢复已验证。

## 验证命令

- `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore -v quiet`：0 警告、0 错误。
- `pnpm test:logging:route-comparison:live`：最终修正版 1/1，通过四档，0 跳过。
- `pnpm test:dotnet:unit -- --selection logging-delivery`：315/315，0 跳过，构建 0 警告/错误。
- 直接运行集成测试 DLL，筛选 `Sustained_http_logs_and_projections_are_readable_from_tls_broker`：1/1，0 跳过；验证原收据核对路径没有被延期标志改变。
- 直接运行 Release Benchmark DLL 的 `logging-request-latency --output artifacts/logging-request-latency`：Disabled/Summary/Projected 正反序六档均通过。
- `pnpm test:integration:tooling`：54/54；`pnpm test:governance`：57/57。
- `pnpm test:integration:partitions`：1104 项发现，无遗漏或重复。发现检查不代表全部集成测试执行通过。
- `git -c core.safecrlf=false diff --check`：通过；工作区既有其他修改保留。
