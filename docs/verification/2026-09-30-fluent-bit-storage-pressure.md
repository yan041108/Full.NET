# Fluent Bit 缓冲指标与 ENOSPC 局部验证（2026-09-30）

- 范围：基线提交 `c609759576c4bf54ff9e743161ecb829ca8d4d4a` 加当前工作区候选；固定摘要的 Fluent Bit 4.1.1 与 Prometheus 3.15.0，Windows Docker Desktop。
- 指标核对：固定镜像在 `/api/v1/metrics/prometheus` 不暴露旧规则引用的 `fluentbit_storage_chunks_up/total`。`/api/v2/metrics/prometheus` 暴露 `fluentbit_output_chunk_available_capacity_percent{name="fullnet_priority_forward"}`，本地带 `storage.total_limit_size=1MB` 的不可达 Forward 样本观察到约 98% 剩余容量；该指标不等于物理磁盘剩余字节。测试抓取已切换 v2，`pnpm test:observability-alerts` 的规则与合成触发/恢复用例通过。真实 Prometheus v2 抓取仍能触发两路原有丢弃告警及采集器目标失联告警。
- 满盘特征：`pnpm test:observability-enospc:live` 使用 64 KiB Docker tmpfs、文件系统缓冲、每秒 100 条 dummy 记录和不可达 Forward。固定镜像报 `errno=28 No space left on device` 与 `input chunk` 写入失败，进程仍运行；本地两次运行分别观测 `retries=2/3`，均为 `successes=0`、`outputDrops=0`。脚本固定这些可观测事实，成功退出只表示特征得到复现，**不表示磁盘满无丢失**。
- 输入指标盲区：同一固定镜像的 v2 抓取在输入 chunk 写失败时，`fluentbit_input_ingestion_paused{name="dummy.0"}=0`、`fluentbit_input_storage_overlimit{name="dummy.0"}=0`，且输出丢弃仍为零。输入已接收计数可能在首次 ENOSPC 后短暂继续增加，不能以单次计数或停滞推断写盘成功/失败。现有 Prometheus 规则没有该输入错误的专用指标；目标环境须以独立错误观测和端到端 ID 对账验证。
- 队列溢出特征：`pnpm test:observability-queue-overflow:live` 将 Forward 逻辑限额缩到 `64KB`、每秒输入一万条记录且接收端不可达。固定镜像在输出丢弃计数超过零时仍报告 93.6% 可用容量，输出成功数为零；本地一次观测丢弃 624 条。这个缩小的测试限额不代表生产配置的 `256MB` 队列时间预算，但证明 `<20%`、持续五分钟不能充当丢失前预警。脚本固定这一反例，成功退出不等于容量门禁通过。
- 规则修正：`FullNetFluentBitForwardQueueCapacityLow` 仅报告两路 Forward 逻辑队列剩余容量低于 20% 持续五分钟，不承诺先于丢弃；丢弃由独立输出丢失告警检测。原 `FullNetFluentBitDiskFull` 只依赖 PVC 指标，候选实际为 `emptyDir`，因此改为明确的 `FullNetNodeDiskPressure` 节点风险告警。两个条件均不能衡量本 Pod 的 `emptyDir` 实际字节占用。
- 未验证：Kubernetes `emptyDir` 的 2 GiB `sizeLimit` 与 Docker tmpfs 的 ENOSPC 行为不同。目标集群须测实际用量、驱逐/写失败时序、日志 ID 缺口、告警通知和恢复追赶；精确提交的 GitHub Actions 终态仍缺。生产 Collector 门禁保持关闭，容量仍为 `Capacity-not-verified`。
