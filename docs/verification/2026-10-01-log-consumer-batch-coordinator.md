# 独立日志消费者有界批次协调验证

- 日期：2026-10-01（Asia/Shanghai）。分支 `codex/foundation-acceptance-20260926`，基线 `c609759576c4bf54ff9e743161ecb829ca8d4d4a`。
- 任务快照：`log-consumer-batch-coordinator-20261001`。保护既有日志与无关客户端改动，未提交或推送。
- 范围：独立 JIT Kafka→ES 消费进程，未改 Host.Api 请求路径、业务数据库或 AOT 发布闭包。

## 行为与预算

`FULLNET_LOG_CONSUMER_BATCH_MAX_RECORDS` 和 Helm `batchMaxRecords` 支持 1..8，默认 1 保持单条。大于 1 时只从单 Poll 循环用零等待收集已可用消息，没有后台并行 Consumer 或凑批延迟。超限载荷/原始 Key 留给原单条隔离路径；一个合法批次最多持有 8×65536 字节原始载荷及有限 Key，解析副本、NDJSON 和 SDK 内存另外存在，不声称整个进程受此上限约束。

批量 ES 回执严格逐项对应固定索引/ID；坏记录先取得 DLQ Broker 确认。只提交首个 Retry/隔离失败前的交付前缀，按每分区末条提交一次。Kafka Offset 可以存在数字空洞，要求实际交付顺序递增。后续已经写入但未提交的成功项依固定 ID 重放。

收集和 ES/DLQ/提交边界检查 assignment epoch。异步等待期间不 Poll 时，epoch 不一定立即更新，因此 Broker/SDK 对旧成员提交的拒绝仍是必要兜底。取消、未知回执与提交异常不得返回 Completed。多个分区提交并非事务：之前提交成功的分区仍有效，后续失败分区重放。提交操作指标与事件数分开；异常退出前健康摘要可能少计部分分区，权威位点以 Broker 为准。慢下游可能超过 MaxPoll，失败退出重放，不保证任意延迟下持续推进。

## 已执行验证

可编译占位实现的 RED：6 项中 4 失败、2 通过，构建无警告/错误；失败覆盖连续前缀、DLQ ACK、跨分区及取消缺失。实现后 6/6；补充跨分区提交间所有权失效、ES 回执时取消和 Broker 提交异常。

| 命令 | 实际结果 |
| --- | --- |
| `pnpm test:dotnet:unit -- --selection logging-delivery` | 最终 310/310，失败/跳过 0，构建 0 警告/错误 |
| `node --test tests/deployment/log-consumer-contract.test.mjs tests/deployment/log-consumer-throughput-proof.test.mjs` | 8/8，失败/跳过 0 |
| `pnpm test:integration:tooling` | 53/53，失败/跳过 0 |
| `pnpm test:governance` | 57/57，失败/跳过 0 |
| `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo -clp:ErrorsOnly` | 退出 0，0 警告/错误 |
| Linux SDK 容器执行 `logging-kafka` 选择集 | 15/15，失败/跳过 0，耗时 6 分 22.938 秒 |

独立静态及增量审查均无阻塞问题；针对审查的验证缺口补充了取消/提交测试和真实批次重平衡用例。

Linux 使用 `fullnet-native-aot-publish-sdk:10.0`、仓库与 Docker socket 挂载、host 网络、`TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`、`FULLNET_TESTCONTAINERS_REUSE=0`，直接执行新建的集成 DLL。过滤 KafkaLogDeliveryTests、KafkaLogConsumerDeliveryTests、KafkaLogConsumerElasticsearchReplayTests、KafkaLogConsumerElasticsearchTlsTests，最低发现 15，超时 15m，TRX 为 `artifacts/log-consumer-batch-coordinator/test-results/linux-logging-kafka.trx`。

真实重平衡用例保留单条并增加批次 DataRow：故意在 ES 等待期间不 Poll，使旧成员超过测试专用 9 秒 MaxPoll；新成员重取原 Offset，旧批次即使 `ownsAssignment` 返回 true 也被 SDK/Broker 拒绝提交，随后新成员确认并提交。这个用例是一条记录的批次，不能证明所有多分区重平衡时序。HTTPS ES 进程测试显式配置批次 8，覆盖无效 API Key 不提交、真实写入、ES 写完但回执未返回时 SIGKILL、重启后同 ID 版本增加且无重复文档、32 条记录完成。TLS/SASL/ACL、DLQ 拒绝与恢复、原单条 ES 映射隔离测试仍在选择集中。测试结束后任务容器清理，未删除既有本地集群和监控资源。

## 同镜像本地突发对比

本地 Docker Desktop/kind 三节点共享一台主机，Kafka/HTTPS ES 为隔离的单节点测试容器。镜像 `fullnet-log-consumer:local-batch-20261001`，摘要 `sha256:24982f33f39212b0f725458b2d8f003a034753fb5431d359fad483e230804f43`；摘要取自本轮一次构建及运行后的 `docker image inspect`，两份原始结果只保存 tag；两次运行之间未重建或更换该 tag。两次独立顺序运行，资源请求 100m/128Mi、上限 500m/256Mi，唯一配置差异为批次上限 1 与 8。计时含发送 CLI 和查询 Broker 提交的观察开销，逐 ID 内容读回在计时之后。

主机为 Intel Core i7-12700H（14 核/20 逻辑处理器），物理内存 68450914304 字节；Docker 可用内存约 31.22 GiB。吞吐计时期间没有并行运行这次真实集成套件；集成在两组资源清理后才启动。

| 指标 | 单条 | 最多 8 条 |
| --- | ---: | ---: |
| 事件/单条 JSON 字节 | 1000 / 2048 | 1000 / 2048 |
| 发送至提交观察耗时 | 30.213 秒 | 8.456 秒 |
| 观察事件/秒 | 33.10 | 118.26 |
| 观察 JSON 字节/秒 | 67784.35 | 242203.86 |
| Consumer cgroup CPU 增量 | 3.829 秒 | 1.032 秒 |
| cgroup 节流增量 | 0.103 秒 | 0.391 秒 |
| Pod 生命周期内存峰值 | 67735552 字节 | 61825024 字节 |
| 最终 Offset / lag | 1003 / 0 | 1003 / 0 |
| 新增 DLQ / 压测期间额外重启 | 0 / 0 | 0 / 0 |

两组均先完成 Production/Online 私有 CA、吊销拒绝、原 Offset 保留、证书恢复重放及正式 target-down 告警恢复，再进行突发对账。单条组初始两条记录有 2 次确认提交，批量组有 1 次，说明指标已按提交操作计数。全部任务资源清理后才发布 passed 结果。

| 命令 | 证据 |
| --- | --- |
| `node eng/testing/log-consumer-kubernetes-smoke.mjs --docker-desktop --throughput --batch-records=1` | 退出 0；`artifacts/log-consumer-batch-1/result.json`；namespace `fullnet-log-e2e-4dafddb2`，完成 UTC `2026-09-30T21:13:31.620Z` |
| `node eng/testing/log-consumer-kubernetes-smoke.mjs --docker-desktop --throughput --batch-records=8` | 退出 0；`artifacts/log-consumer-batch-8/result.json`；namespace `fullnet-log-e2e-9c18d3d5`，完成 UTC `2026-09-30T21:17:18.114Z` |

这次单次观察速率约为 3.57 倍，不能外推持续输入容量、最大 QPS、请求 P99、HA 或不同数据规模。默认仍为 1，运维可显式选择最多 8 条并按实际资源复测；历史 25.924 秒基线未覆盖，本次使用同一新镜像作比较。
