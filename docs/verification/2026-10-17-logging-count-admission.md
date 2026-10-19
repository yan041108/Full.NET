# 日志条数准入验证

日期：2026-10-17。Git 基线：`42d6714ee48ed58447fe608b7122089f1e58bd55`，当前共享工作树含未提交修改；不是该提交的纯净树性能。验证对象为普通日志队列满载时的快照构建与拒绝行为。

## 环境与场景

Windows 10 22H2 / i7-12700H（14 核、20 逻辑处理器），SDK 10.0.402、.NET 10.0.13、BenchmarkDotNet 0.15.8，`DOTNET_PROCESSOR_COUNT=2`。共享开发主机，不能据此推导生产容量。经 `withTestRun(heavy: true)` 和 `prepareTestBuild` 运行基准，输出进入系统临时目录，只保留 CSV/Markdown 最小证据。

`LoggingSaturatedQueueBenchmarks` 使用公开宿主入口、Collector 模式，条数容量 1、字节预算 1 MiB；首条后台输出阻塞，第二条占满队列。调用线程持续提交 32/2048 字符载荷，每次基准调用包含 16,384 次日志调用。每轮验证队列满载和精确丢弃数。单个生产者，不含 HTTP、数据库、Kafka 或 ES。

命令：Release 构建后执行 `Full.NET.Benchmarks.dll --filter *LoggingSaturatedQueueBenchmarks* --job Short --inProcess --artifacts <系统临时目录>`。3 轮预热、3 轮测量、1 次启动。

## 修改前基线

| 载荷字符数 | 平均调用时间 | 标准差 | 99.9% CI 半宽 | 每次调用分配 |
| --- | --- | --- | --- | --- |
| 32 | 6.823 μs | 0.3504 μs | 6.393 μs | 5.04 KB |
| 2048 | 14.166 μs | 0.5338 μs | 9.738 μs | 19.37 KB |

原始 CSV/Markdown：`C:/Users/Administrator/AppData/Local/Temp/fullnet-log-admission-AjCxen/`。以上为短基准，置信区间较宽；未测请求 P99、并发生产者、CPU 饱和或端到端吞吐。

## 修改后同参数对比

2026-10-17 19:49（北京时间）按相同宿主、SDK、载荷、预热和测量参数复验；正式入口重新校验构建输入，Release 0 警告/0 错误，两个场景断言通过，进程退出码 0。Sink SHA-256 与下文修改后值一致。

| 载荷字符数 | 修改前分配 | 修改后分配 | 修改后平均调用时间 | 标准差 | 99.9% CI 半宽 |
| --- | --- | --- | --- | --- | --- |
| 32 | 5.04 KB | 1.42 KB | 4.086 μs | 0.8769 μs | 15.999 μs |
| 2048 | 19.37 KB | 1.42 KB | 2.701 μs | 0.4166 μs | 7.601 μs |

修改后原始 CSV/Markdown：`C:/Users/Administrator/AppData/Local/Temp/fullnet-log-admission-1g6Bit/`。结果支持本场景调用线程分配减少，结合回归测试证明满队列不再构建快照。两组修改后样本存在迭代不足 100ms 告警，置信区间宽且与基线重叠；耗时仅作观察，不认定稳定延迟收益，不承诺固定提速比例。1.42 KB 也说明 ILogger/Serilog 入口仍有事件构造分配，不是零分配。该对比不能替代未饱和路径成本、多生产者公平性、混合优先级洪峰或请求 P99。

## 行为修改与验证

`FullNetBoundedAsyncSink` 在快照构建前用 CAS 预留条数名额，再预留最大封套字节预算。条数名额覆盖构建中和队列中事件，字节拒绝、快照失败、关闭竞态与未入队均归还；消费或主动放弃取出事件时归还条数名额。在途事件字节仍计费至下游调用结束。普通与高优先级实例继续独立。

新增 `Count_admission_precedes_snapshot_and_failed_build_returns_slot`：构建失败后可重新占用名额；条数满后拒绝超大事件但不再增加快照超限数；拒绝不改变已预留字节；关闭后字节归零。修改前用正式 Unit 过滤入口复现失败（预期 OversizeCount=1，实际=2），修改后集中验收通过。

`node scripts/testing/run-dotnet-test-suite.mjs unit --selection logging-delivery --reuse-build`：Release 0 警告/0 错误；387 项，386 通过，1 项 Linux FIFO 在 Windows 跳过，0 失败。测试矩阵同步增加本项。受影响 `git diff --check` 通过。

修改后 Sink 文件 SHA-256：`0B566EFEACF389F8DD7FA55B0311503532C4486F22A9FABC7200C1CF1B50D970`。

## 未完成范围

饱和场景短基准对比已完成；混合优先级洪峰、Local 维护成本、正式请求 P99、实际 AOT/原生仍按整改计划推进。本修订后另完成 SQL Server 日志控制面、关联时间线和诊断策略三项实际集成测试，3/3 通过、0 跳过；MySQL 现有通过证据来自条数准入修订前，具体输入与范围见整改计划。本记录不宣布整个日志模块验收完成。
