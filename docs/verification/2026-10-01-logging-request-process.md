# 真实请求经独立消费者至 HTTPS Elasticsearch

基线 `c609759576c4bf54ff9e743161ecb829ca8d4d4a`，任务快照 `logging-request-process-20261001`。2026-10-01 本地 Windows 10.0.19045、.NET 10.0.12、20 个逻辑处理器、Docker Desktop 单机。未修改生产配置或实施切流。

新增 `Projected_http_logs_are_delivered_by_standalone_consumer_over_https`，复用真实 Kestrel、正式 ApplicationKafka 静态出口及 TLS Kafka 4.1.2。独立启动正式 `Full.NET.Host.LogConsumer.dll`，获得启动输出且进程存活后才发送 HTTP 请求，批次最多 8 条。请求档为 200 次预热、5000 次实测、目标 500 次/秒、最多 8 个请求循环，全部开启安全请求/返回投影。消费进程与请求宿主同时运行，最终排空上限 120 秒；不是每秒 500 条的长期稳态承载证明。

固定 ES 镜像摘要 `82ac14f43fe701992e601f4cc81e1c0d7dbc5a2576d8cd736006452925df4026`，512MiB Java Heap、单节点，开启 HTTPS 和认证。临时 RSA 私有 CA 签发服务端证书，客户端保留证书链及主机名校验；SAN 覆盖本地及显式 Docker 宿主地址。测试 Development 使用 NoCheck，因为临时 CA 不提供 CRL；Production 默认 Online 吊销策略未变。子进程仅获得 `fn-logs-1-*` 范围的 `write`/`auto_configure` API Key，没有集群权限，测试父进程的管理员客户端只用于创建 Key 和核对文档。凭据不写入结果工件或进程环境诊断。

宿主释放后冻结 General/Priority 高位点，父进程只读取 Broker 来源收据，不执行 ES 写入或 Offset Commit。等待独立消费者所属 Group 的实际 committed 精确等于冻结高位点，检查消费者仍存活、DLQ 高位点为零。按独立计算的版本/分类/事件 UTC 日期索引逐个 refresh/count，全部 ID 分组 `_mget`，验证目标索引、唯一 ID、完整 `_source` 与原始 Broker 快照一致。排空后仅强制结束本测试的子进程并确认退出，Kafka/ES 容器及临时 CA 清理后写通过报告；这一步不是优雅停机验收。

新增用例先以 NotImplementedException 实际失败，再完成实现。首轮 GREEN 为 1/1、0 跳过、64.450 秒：5206 条 ES 文档、两个索引，含 5200 条 HTTP 日志/唯一 RequestId/请求返回投影与 520 个预期 500；Console 镜像为 0 字节，宿主释放前丢弃为零。General committed=4686、Priority=520，均等于最终高位点；DLQ 为零，子进程已退出。5000 次实测约 499.91 次/秒，HTTP P99 3.0813ms，不含调度等待及最终 ES 交付等待，不能当作端到端投递 P99。

独立审查发现 Kafka 启动/管理请求未接入总期限、HTTPS SAN 未覆盖 Docker 宿主覆盖值，均已修复。Kafka 启动传播五分钟总取消令牌；建 Topic 管理请求和操作各有 10 秒上限，重试延迟接受取消。最终构建与回归结果见下方，实际工件在 `artifacts/logging-request-process`，含 Broker 原始收据、HTTP/调度样本、进程诊断和 `result.json`。

验证命令与结果：

- `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore`：修复后成功，0 警告/错误。
- `pnpm test:dotnet:unit -- --selection logging-delivery`：315/315，0 跳过。
- `pnpm test:integration:tooling`：54/54。
- `pnpm test:governance`：57/57。
- `pnpm test:logging:request-kafka:live`：修复后最终三项 3/3，0 跳过，148.124 秒，包含原 Broker 两档、同进程 ES 和新独立进程 HTTPS ES。
- `pnpm test:integration:partitions`：1101 项发现，无遗漏/重复；重型 Kafka 75 项，日志请求分片 3 项、日志 Kafka 分片 18 项。不代表执行了全部集成。
- `git -c core.safecrlf=false diff --check` 与本任务新增文件 whitespace 检查：通过。

最终三项回归中的独立进程报告：5206 条 ES 文档/两个索引全部一致，5200 HTTP 日志/唯一 RequestId/安全投影，520 预期 500；General=4686、Priority=520，精确等于各自 Broker 高位点，DLQ=0，释放前丢弃=0，Console 镜像=0。请求实测约 499.63 次/秒、HTTP P99 2.7113ms；进程退出与资源清理已确认。独立消费者可在请求期间消费，但本测试没有单独度量请求期间的消费速率或端到端交付延迟。

这项证据补齐独立消费进程及 HTTPS ES 的真实请求纵向闭环。没有 Collector 同轮全链比较、完整认证/数据库业务 API P99、多 Broker HA 或长期最大容量结论；消费者实验开关的默认值未改变。
