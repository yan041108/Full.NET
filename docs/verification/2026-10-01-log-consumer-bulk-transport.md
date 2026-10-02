# 独立日志消费者有界 Bulk 传输验证

- 日期：2026-10-01；授权：用户继续推进日志开发，以实际本地测试验收。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`，分支 `codex/foundation-acceptance-20260926`，快照 `log-consumer-bulk-transport-20261001`；保留既有改动，未提交或切流。
- 范围：`Full.NET.LogConsumer` ES 输出的批量传输 API、单测与既有 HTTPS ES 集成增量。业务数据库、Host.Api AOT 闭包及宿主消费循环未改变。

## 能力边界

`WriteBatchAsync` 允许 1–64 条 `LogDocumentWrite`，按输入顺序冻结目标 index/LogEventId；完整 NDJSON 请求（动作头、JSON、换行）最多 1 MiB。集合按经过验证的固定条数索引复制，预算前仅保存已有载荷长度与有限动作头，预算通过后分配一次批次正文。保留原始事件日期索引和幂等 ID。

网络发送和正文读取使用同一有限超时/取消 Token，默认 30 秒；读取响应最多 64 KiB+1，超限整批 Retry。HTTP 200 不能代表整批成功，既有解析器必须逐项核对顺序、index、ID 和状态；201/429/400 分别为 Succeeded/Retry/Isolate，缺项、错配、矛盾或非法响应整批 Retry。Isolate 只是需要可靠 DLQ 确认的后续动作，本层没有提交 Kafka Offset 或确认 DLQ。

`WriteAsync` 复用批量路径，但宿主仍通过 `SequentialLogDeliveryProcessor` 单条执行。批次协调、DLQ 顺序、连续提交与重平衡所有权尚未接入，不能用新传输 API 宣称运行吞吐提升或打开独立消费者生产门禁。

## 失败与修正

可编译占位实现的首次聚焦测试实际 5/7 通过、2 项行为失败：混合回执未得到 Succeeded，空批次未抛预算异常。实现后首次聚焦 7/7 通过。

实现时构建发现原载荷属性为 `ReadOnlySpan<byte>`，并非 `ReadOnlyMemory<byte>`；改为记录裁剪后长度，预算通过后从受控记录直接复制，重新构建通过。编译失败不作为 RED 或验收通过证据。

随后补充无效/超限响应、请求取消及响应头后正文挂起的截止验证。动作头预算用例证明正文总量尚未超过 1 MiB，但加动作头后仍拒绝；没有网络调用。

## 真实 ES 用例与审查

在既有固定版本 HTTPS ES/私有 CA/API Key 测试中，使用只允许固定索引写入的 API Key 一次批量写入两个新 ID，并重放同批；管理员只用于 ID 读回和计数，写入仍使用最小权限 API Key。初始文档数由 1 变 3，既有 Kafka 进程重放/重平衡/特征实验结束后的预期由 35 变 37；Bulk 重放没有新增重复文档。

独立静态增量审查未发现阻塞问题；审查指出动作头预算和响应正文挂起缺少直接测试，已补充上述用例。超限响应用合法 JSON 和正确目标构造，若没有读取上限会被误判为成功，避免仅用无效 JSON 证明响应预算。

| 命令 | 实际结果 |
| --- | --- |
| `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo -clp:ErrorsOnly` | 退出 0，0 警告/错误 |
| `pnpm test:dotnet:unit -- --selection logging-delivery` | 302/302，0 失败/跳过 |
| `pnpm test:dotnet:unit -- --filter FullyQualifiedName~ElasticsearchLogDocumentSinkTests --minimum-expected-tests 10` | 最终响应预算用例修订后 10/10，0 失败/跳过，构建 0 警告/错误 |
| 本地 Linux SDK 容器执行 `logging-kafka` 选择集 | 14/14，0 失败/跳过，实际耗时 5 分 58 秒 |
| `pnpm test:governance` | 57/57，0 失败/跳过 |

Linux 使用 `fullnet-native-aot-publish-sdk:10.0`，挂载本仓库和 Docker socket，`TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`、`FULLNET_TESTCONTAINERS_REUSE=0`。选择集为 KafkaLogDeliveryTests、KafkaLogConsumerDeliveryTests、KafkaLogConsumerElasticsearchReplayTests、KafkaLogConsumerElasticsearchTlsTests，最低发现 14；TRX 为 `artifacts/log-consumer-bulk-transport/test-results/linux-logging-kafka.trx`，另行解析确认 total=14/passed=14/failed=0/notExecuted=0，测试结束隔离容器全部移除。

集合冻结从遍历改为验证条数后按索引复制，随后重建集成程序集并以最终日志单测验证；真实回归证明 Bulk 传输协议、TLS/API Key 和原单条消费的兼容性。最终超限响应用例构造时原始插值字符串遇到嵌套大括号编译错误，改用测试内 JsonSerializer 生成合法载荷，聚焦 10/10 通过。没有运行宿主批次协调或新的吞吐对比，本任务差异检查通过。
