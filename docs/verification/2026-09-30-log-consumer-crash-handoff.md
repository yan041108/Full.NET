# 日志 Consumer SIGKILL 与分区交接局部验证（2026-09-30）

代码基线为 `c609759576c4bf54ff9e743161ecb829ca8d4d4a` 加当前未提交日志切片。环境为 Windows 10 22H2、Intel Core i7-12700H、.NET SDK 10.0.401、Docker Desktop；测试使用 Apache Kafka 4.1.2 TLS Broker 与固定摘要的 Elasticsearch 9.5.4 HTTPS 容器。自签测试 CA 无 CRL，独立 Consumer 只在 `DOTNET_ENVIRONMENT=Development` 显式使用 `ES_REVOCATION_MODE=NoCheck`；这不是生产证书吊销证据。

`KafkaLogConsumerElasticsearchTlsTests` 在来源 Offset 1 准备第二条有效记录。一次性测试 HTTPS 代理使用最小写权限 API Key 将其 Bulk 请求转发给真实 ES，检查 HTTP 200 与 `errors=false` 后**暂扣**返回给 Consumer 的回执。此时对独立 Consumer 进程执行强制终止；来源 Group Offset 保持 1，而 ES 已可按该 `LogEventId` 查询文档。原进程没有主动 LeaveGroup，重启同组 Consumer 后等待 Kafka 会话失效和分区重新分配，最终提交 Offset 2。重放前后同一文档 `_version` 增加 1，索引文档总数仍为 3，证明重复写入使用原 ID 覆盖，而不只是空提交。代理仅是测试代码，生产 Consumer 没有故障注入开关。

`KafkaLogConsumerDeliveryTests` 另以真实 TLS Broker 验证正常成员离组交接：旧成员已取得但未提交 Offset 0，替代成员取得同一记录；旧 Consumer 关闭后的迟到提交抛出 `ObjectDisposedException`，替代成员确认后提交 Offset 1。这个测试只证明**正常离组后的重读和旧句柄无法提交**。另一 TLS Kafka 用例把旧成员 MaxPoll 限于测试专用的 9 秒，并让其写入处理停在受控 Sink；Kafka 报告 MAXPOLL、旧成员离组，新成员读取原 Offset 0。释放旧处理后 Broker 拒绝其迟到提交，新成员确认后提交 Offset 1。它覆盖这一受控的活跃处理失效与接管，不代表生产默认 300 秒 MaxPoll 下所有撤销时序。

验证命令：

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore -v quiet
node scripts/testing/run-integration-shard.mjs logging-kafka
pnpm test:integration:partitions
pnpm test:integration:tooling
```

本地目标用例 Debug 2/2 通过，活跃重平衡目标用例 Debug 1/1 通过；Release 构建 0 警告、0 错误，`logging-kafka` 分片 13/13 通过（5m 42s）；Integration 分片覆盖检查 1096 项无遗漏或重复，测试工具 53/53 通过，`git diff --check` 退出码 0。随后仅为新增轻量探针重跑 Release ES HTTPS 目标用例，1/1 通过。SIGKILL 测试首先暴露测试代理的 Windows Schannel 临时私钥限制，改为 PFX 临时导入后通过；随后暴露测试 30 秒恢复观察期短于 Kafka SDK 默认 45 秒会话失效超时，改为 90 秒上界后通过。这些修复未调整生产 Consumer 配置。

按用户选择，仅在同一测试进程追加 32 条固定约 300 字节的有效诊断记录，由单个独立 Consumer 顺序写入真实 ES，并以来源 Offset 34 和 ES 文档计数核对全部完成。Release 本机一次样本：wire 9600 字节，生产首条至提交末条 0.705 秒，约 45.4 事件/秒、13,608 wire 字节/秒；Consumer 期间 CPU 时间增量 281 毫秒，进程峰值 Working Set 63,688,704 字节。原始输出在本地忽略目录 `tests/Full.NET.IntegrationTests/bin/Release/net10.0/TestResults/Full.NET.IntegrationTests-log-consumer-local-probe.trx`。该计时包含逐条 Kafka Produce、ES 写入和 Offset 查询轮询；CPU 仅为 Consumer 进程，峰值内存包含其此前启动与崩溃恢复阶段，不能作为纯消费热路径成本，也没有请求 P99 或压力饱和样本。

**Capacity-not-verified。** 本地测试只涉及少量记录，容器启动和 Group 会话失效占据主要时长，不能据此计算持续事件/字节吞吐、请求 P99、CPU/RSS 饱和或恢复追赶速率。LG08 仍须在专用生产等价环境按相同载荷比较宿主 Local/Summary、Collector 与直发 Kafka；记录每实例请求 P50/P95/P99、事件/字节速率、优先流年龄/丢弃、Collector 与 Consumer CPU/RSS、Kafka 积压、ES 可查询延迟，以及 Broker/ES 故障后的净追赶速度。生产默认 Poll 预算下其他重平衡撤销时序、Pod/节点故障、生产 CA/CRL/OCSP 和正式 Linux CI 也未由本测试覆盖。生产 Collector 门禁保持关闭。
