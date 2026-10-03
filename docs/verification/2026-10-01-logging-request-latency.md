# 日志 HTTP 请求延迟本地验证

2026-10-01，Windows 10.0.19045、.NET 10.0.12、20 个逻辑处理器，Docker Desktop 所在共享开发主机。任务起点 `c609759576c4bf54ff9e743161ecb829ca8d4d4a`，任务快照 `logging-request-latency-20261001`。没有修改生产日志默认配置。

Release 基准创建随机 loopback Kestrel，调用正式 `AddFullNetServiceDefaults` / `UseFullNetRequestLogging`，三档使用相同端点，每十次响应一次预期 500。并发 8，每档预热 200 次、实测 2000 次，正序/逆序两轮。Console JSON 实际写本地文件，宿主释放后对账；不是空出口。实际响应状态和正文必须符合预期，全部延迟有限非负；开启日志每档必须有 2200 个不同 RequestId 和 220 条预期 500，投影档另须有 2200 组内容正确的请求/返回投影。失败不保留旧 passed 报告。

| 模式 | 第一轮 P99 ms | 第二轮 P99 ms | 日志/轮 | 投影/轮 |
| --- | ---: | ---: | ---: | ---: |
| Disabled | 0.6197 | 0.8433 | 0 | 0 |
| 100% Summary | 2.9821 | 3.6982 | 2200 | 0 |
| 安全分页投影 | 3.8798 | 3.3148 | 2200 | 2200 |

六档最终 HTTP 日志、RequestId、状态码与投影全部对账成功。释放前丢弃快照均为 0，字段明确命名 `preDisposalDroppedMessages`；这不证明最终释放过程中其他日志没有丢弃。静态审查发现原快照早于实际排空的问题，已通过收窄声明并补充最终唯一 RequestId/状态码校验解决。

原始证据位于忽略的本地目录 `artifacts/logging-request-latency/result.json` 与六个 JSONL 文件；结果保存全部 2000 条延迟/档、P50/P95/P99、CPU、分配、日志字节和环境。报告完成时间为 UTC 2026-09-30 22:04:21（北京时间 2026-10-01 06:04:21）。首次运行的 Windows 写句柄冲突已通过先释放输出文件再读取修复；新增状态校验首次重跑因错误字段名失败，按真实日志字段 `http.status_code` 修正，最终六档退出码 0。

验证命令与结果：

- `dotnet build benchmarks/Full.NET.Benchmarks -c Release --no-restore`：成功，0 警告/错误。
- `dotnet benchmarks/Full.NET.Benchmarks/bin/Release/net10.0/Full.NET.Benchmarks.dll logging-request-latency --output artifacts/logging-request-latency`：六档全部 verified，退出码 0；便捷入口为 `pnpm test:logging:request-latency:live`。
- `pnpm test:dotnet:unit -- --selection logging-delivery`：313/313 通过，0 跳过；验收判定占位实现曾产生 2 个失败，修复后 3/3 通过。
- `pnpm test:integration:tooling`：53/53 通过。
- `pnpm test:governance`：57/57 通过。
- `pnpm test:integration:partitions`：1098 项发现，无遗漏/重复；SQL Server/MySQL 各 173、迁移 496、基础设施 184、重型 Kafka 72。首次检查发现旧计数少算参数化重平衡批次用例，按逐分片实测更新 canonical 1097→1098、重型 71→72；这属于测试发现检查，不是运行全部集成用例。
- `git diff --check` 及新增文件 whitespace 检查：通过。

每档发送窗口仅 108–217ms，CPU/分配来自客户端与服务端共享进程、只计发送窗口，包含预热残余后台工作且不计最终排空。没有认证、数据库、Kafka，不设业务 SLO，也不根据短样本选路线。实际业务 API P99、持续容量、生产规模和多节点故障能力仍未由此验证；本地测试通过只验收本切片。
