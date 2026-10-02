# Collector 请求期间实时采集与确认

基线 `c609759576c4bf54ff9e743161ecb829ca8d4d4a`，任务快照 `logging-live-collector-20261001`。2026-10-01 本地 Windows 10.0.19045、.NET 10.0.12、20 个逻辑处理器、Docker Desktop 单机；默认出口及消费者实验开关未改。

新增 `Collector_streams_http_logs_and_commits_before_requests_complete`。TLS Kafka 4.1.2、HTTPS ES 私有 CA/受限 API Key、正式批次 8 消费子进程和固定 Fluent Bit 4.1.1 均在请求前启动。真实 Kestrel 仍以 Collector 入口运行 200 次预热/5000 次实测、目标 500 次每秒、安全请求返回投影。与有限回放共用可信 Pod 标签、优先分类和候选 Kafka 双出口；实时场景绑定初始为空的 CRI 目录，没有预装请求来源。

`LiveCollectorCriBridge` 持续以共享只读句柄读取 Console JSON 文件，保留未完成的字符行，只有完整行才生成 CRI F 行并分别追加到来源 Pod/独立 ID 镜像 Pod 文件。UTF-8 解码保留跨缓冲边界状态，字符缓冲 8192、单行最多 32768 字符、总条数最多 10000；异步读取和等待接受取消，结束后必须排空到完整行，任务与文件句柄在 finally 中释放。桥接模拟本地运行时生成 CRI 的过程，不是 Kubernetes CRI 日志驱动或节点 DaemonSet 实测。

独立 Broker 读取器与请求同时运行，收据最多 10000 条。Runner 在实测发送窗口开始时发布 UTC 边界及状态，在 SendAsync 结束或异常时关闭状态；此窗口不含预热、停止宿主或最终排空。只选择窗口内收到、OccurredAtUtc 不早于实测开始的 HTTP 事件，保存其具体 Topic/分区和 offset+1。正式消费者 Group 的同分区 committed 必须在窗口仍开放时达到该目标；任意启动/预热正值或请求结束后的提交不能满足。这项检查证明至少一条实测 HTTP 日志在实测请求仍进行时得到消费者确认，不代表所有日志实时追平或投递延迟 SLO。

请求结束且宿主释放后，桥接读取到 EOF 并确认没有半行，再按全部原始来源对账。保留有限回放的独立镜像 ID、tail 输入恰好两倍来源、Kafka 输出恰好来源、采集器正常退出码 0、冻结最终高位点、正式消费者 committed=高位点、DLQ=0、ES 全部文档及字段一致。容器、CA、子进程清理完成后才写通过报告。来源、实时 CRI、Broker 收据、Collector 配置/指标/日志、HTTP/调度样本及具体确认位置在 `artifacts/logging-live-collector`。

新增用例占位 RED：NotImplementedException、1 失败、0 跳过。初版 live GREEN 为 1/1、64.642 秒；独立审查发现任意正 Offset 与延迟预热收据可能掩盖未处理实测 HTTP，修复为具体实测事件及同分区确认目标，并通过只读复核。

最终加固后的实时报告：实测发送窗口内观察到 108 条实测 HTTP 收据，首条证明为 Priority 分区 offset=20，窗口内实际 committed=56，越过记录 offset+1=21。完整来源 5206 条、独立镜像 5206 条，实际 tail 输入 10412，镜像全部排除；ES 5206 文档/两个索引逐字段一致，General=4686、Priority=520 均等于最终高位点，DLQ=0，正常采集器退出、消费子进程结束与资源清理已确认。请求实测约 499.56 次/秒，HTTP P99=3.1380ms，包含本地桥接/观察对共享进程的影响，不是交付延迟或长期实时追平结论。

本轮日志单测 315/315、工具 54/54、治理 57/57；1103 项集成发现集合闭合（重型 Kafka=77、日志 Kafka=20、日志请求=5），未执行全部集成。请求分片总预算改为 8 分钟，因为增加真实实时用例；每个 Collector 用例仍有独立 5 分钟总取消期限。

最终验证命令与结果：

- `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore`：成功，0 警告/错误。
- `pnpm test:logging:request-kafka:live`：最终加固版本 5/5、0 跳过、275.061 秒；含原回放、实时 Collector、Broker 两档、同进程 ES、独立进程 HTTPS ES。
- `pnpm test:dotnet:unit -- --selection logging-delivery`：315/315、0 跳过。
- `pnpm test:integration:tooling`：54/54；实时桥接路径选择自身请求分片。
- `pnpm test:governance`：57/57。
- `pnpm test:integration:partitions`：1103 项发现无遗漏/重复，不代表执行全部集成。
- `git -c core.safecrlf=false diff --check` 与本轮新增文件 whitespace 检查：通过。

性能边界：HTTP/CPU/分配统计仍来自同一客户端和宿主进程，实时桥接、Broker 观察与字段处理也占用该进程资源。Collector 与 ApplicationKafka 各场景采用相同有限请求档，但没有通过正反序多轮、资源隔离或统一运行时采集开销的比较，不能仅按当前两份 P99 或 CPU 数字宣布路线优劣。后续需要同场景多轮对照及 Broker 故障恢复；长期容量和真实 Kubernetes CRI 仍按范围验证。

同一最终请求分片运行中的单轮本地观测（均为 5000 实测/目标 500 次每秒、投影、独立 HTTPS ES 消费）：

| 场景 | HTTP P50/P95/P99（ms） | 实际请求/秒 | 释放前丢弃 |
| --- | --- | --- | --- |
| Collector 实时测试桥接 | 0.8923 / 1.6402 / 3.1380 | 499.56 | 0 |
| ApplicationKafka | 0.6665 / 1.2744 / 2.5557 | 499.55 | 0 |

共享进程 CPU/累计 GC 分配分别为 10296.875ms/336682008 bytes 与 1656.25ms/184106648 bytes，不是应用宿主独占 CPU、峰值内存或平台总资源；第一项包含实时桥接/收据读取、第二项请求窗口不含最终父进程收据读取，不能用两数推导生产路线资源差距。两入口继续通过配置选择，本轮不改变默认路线。
