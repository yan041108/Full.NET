# 固定节奏 HTTP 日志本地验证

2026-10-01，Windows 10.0.19045、.NET 10.0.12、20 个逻辑处理器。任务基线 `c609759576c4bf54ff9e743161ecb829ca8d4d4a`，快照 `logging-request-sustained-20261001`。同一共享开发主机，没有修改生产默认配置。

新增 `pnpm test:logging:request-latency:sustained`，复用前一切片真实 loopback Kestrel、正式 B2 中间件和实际 Console JSON 文件。显式 `--sustained` 每档预热 200/实测 5000，目标 500 次/秒，最多 8 个发送循环，首末计划发送跨度 9998ms。单调时钟控制发送时点，单档发送 30 秒取消/完成判定、总预算 5 分钟；异常不能留下旧通过报告。三档正反序各一轮，10% 为预期 500。

| 模式/轮 | HTTP P50 ms | P95 ms | P99 ms | 调度落后 P99 ms | 实际请求/秒 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Disabled/1 | 0.4250 | 0.9042 | 1.2843 | 15.5798 | 499.72 |
| Summary/1 | 0.4608 | 0.9816 | 1.7152 | 15.6293 | 499.51 |
| Projected/1 | 0.4755 | 0.9637 | 1.6798 | 15.4936 | 499.40 |
| Projected/2 | 0.4633 | 0.9475 | 2.0083 | 15.5394 | 499.62 |
| Summary/2 | 0.4318 | 0.9916 | 1.7067 | 15.5238 | 499.46 |
| Disabled/2 | 0.3457 | 0.6716 | 0.9986 | 15.6442 | 499.61 |

每档实际发送窗口 10.006–10.012 秒，六档共 30000 次实测。开启日志四档最终各 5200 个不同 RequestId、520 条预期 500；两档 Projected 各 5200 组内容正确的请求/返回投影。无异常响应，释放前丢弃快照均为 0。最终文件逐条对账在宿主释放之后完成；释放前快照不证明停机过程中其他日志无丢弃。

**限制：**调度落后 P99 约 15.5ms，实际发送存在延后/追赶，不能称为严格均匀的 2ms 请求流。HTTP 延迟从实际发送到响应正文完成，不包含调度落后；不能将表内 HTTP P99 当作从计划时点算起的响应 P99。原始两类数组分别保存，可进一步分析。CPU/分配仅共享客户端/服务端发送窗口，包含预热残余后台工作且不计最终排空，CPU 两轮有明显波动。Console 包含资源等非 HTTP 日志，不能把文件总字节直接当作应用 Kafka 吞吐。

实际命令与证据：

- `pnpm test:dotnet:unit -- --filter FullyQualifiedName~LoggingRequestEvidenceTests --minimum-expected-tests 5`：占位实现 RED 为 2 失败/3 通过；实现后 GREEN 5/5，通过构建无警告/错误。
- `pnpm test:dotnet:unit -- --selection logging-delivery`：315/315 通过、0 跳过，构建无警告/错误。
- `dotnet benchmarks/Full.NET.Benchmarks/bin/Release/net10.0/Full.NET.Benchmarks.dll logging-request-latency --output artifacts/logging-request-sustained --sustained`：六档 verified，退出码 0。
- `pnpm test:integration:tooling`：53/53 通过。
- `pnpm test:governance`：57/57 通过。
- `pnpm test:integration:partitions`：1098 项发现，无遗漏/重复；这是发现检查，未运行全部集成用例。
- 原命令无 `--sustained`，输出到 `artifacts/logging-request-short-compatibility`：短档六组 verified，退出码 0。
- `git diff --check` 与新增验证记录 whitespace 检查：通过。

忽略的本地原始目录 `artifacts/logging-request-sustained` 包含结果 JSON 和六个实际日志文件，保留全部 HTTP/调度落后样本、资源与环境。报告完成于北京时间 2026-10-01 06:12:06。独立只读审查未发现本轮阻塞项。

本地测试通过验收此有限场景，不代表长期 Soak、最大容量、认证/数据库业务 API P99 或 Collector/ApplicationKafka 同载荷路线对比。既有短样本入口保持兼容，输出目录独立。
