# 独立日志消费者本地突发吞吐基线

- 日期：2026-10-01；授权：用户继续推进日志开发，使用本地实际测试验收。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`，分支 `codex/foundation-acceptance-20260926`，快照 `log-consumer-throughput-20261001`；保留既有改动，未提交或切流。
- 范围：新增有界基线与对账工具，单循环逐记录 ES/DLQ 确认后提交的正式实现不变；没有 .NET、数据库、AOT 或 Helm 模板变化。
- 环境：Windows、Docker Desktop、本地三节点 kind 共享主机，Intel Core i7-12700H（14 核/20 逻辑处理器），物理内存 68450914304 字节，Docker Linux 可见内存约 31.22 GiB，隔离单节点 Kafka/ES 测试容器。

## 场景与预算

沿现有 Production/Online TLS、吊销/更新证书、ES/DLQ/Offset 与指标/告警功能验证后，发送 1000 条日志，每条 JSON 精确 2048 UTF-8 字节，总 JSON 2048000 字节。使用单分区、单消费者，现有 Helm 消费者限制为 500m CPU/256Mi 内存。

发送前取得消费者 Pod UID、重启数与 cgroup CPU 计数。计时从 Kafka CLI 启动到最终 Broker 位点观察完成，要求不超过 120 秒，1000 条全部确认至 Offset 1003、lag=0。每批 100 个 ID 用 `_mget` 核对 found、唯一 ID 与完整源文档；原 DLQ 1 条不能增加。前后 Pod UID/重启数不能变化，最终必须 Ready；读取 CPU/节流计数差、内核 Pod 生命周期内存峰值及下游最终资源样本。

统计 JSON 字节不含 Kafka Key、协议、复制或 TLS 开销；耗时包含 Java CLI 与位点查询，不能解释为消费者最大吞吐。内存峰值覆盖 Pod 生命周期，Kafka/ES 的最终样本不是峰值。无应用请求，因此不产生请求 P99；不是持续输入、恢复追赶、多消费者/重平衡或万级容量认证。独立消费者默认 disabled/experimental 不变。

## 验证

最初缺文件导致模块导入失败不计 RED；建立可加载的空实现后，两项行为用例实际失败：仅计数未拒绝缺失、工作负载输出 0 而非 3。实现后用例 2/2 通过，覆盖缺失/重复/错误内容、条数上限、精确多字节 JSON 和重复 ID 拒绝。

| 命令 | 实际结果 |
| --- | --- |
| `pnpm test:log-consumer:throughput-proof` | 2/2，0 失败/跳过 |
| 两个新增/修改脚本的 `node --check` | 退出 0 |
| `pnpm test:inner -- --snapshot log-consumer-throughput-20261001` | 受影响工具 64/64，0 失败/跳过 |
| `pnpm test:governance` | 57/57，0 失败/跳过 |
| `pnpm test:log-consumer:throughput:live` | 退出 0，原功能链路及 1000 条对账通过，清理完成 |

报告 `artifacts/log-consumer-throughput/result.json`，namespace `fullnet-log-e2e-1650ef60`，完成时间 `2026-09-30T17:06:59.399Z`（本地 2026-10-01）。顶层记录数沿原三条功能阶段，新增突发与最终 Offset 在 `throughput` 中独立记录；全部断言、资源清理成功后才发布通过工件。

| 实测项 | 结果 |
| --- | --- |
| 新增文档逐 ID/完整内容核对 | 1000/1000 |
| 总 JSON 字节 | 2048000 |
| 含 CLI/提交观察开销耗时 | 25924.1858 毫秒 |
| 观察事件速率 | 38.574 条/秒 |
| 观察 JSON 字节速率 | 78999.588 字节/秒 |
| 消费者 cgroup CPU 增量 | 2744436 微秒 |
| cgroup throttled_usec 增量 | 1263218 微秒 |
| Pod 生命周期 memory.peak | 68648960 字节，约 65.47 MiB |
| 最终 Offset / lag / 新增 DLQ | 1003 / 0 / 0 |
| Pod UID / 重启数 / 就绪 | UID 未变、无新增重启、Ready |
| Kafka 最终资源样本 | CPU 2.42%、内存 358.7 MiB |
| ES 最终资源样本 | CPU 1.05%、内存 1.052 GiB |

实际 Production/Online 证书吊销使原功能位点停于 2，更新证书后重放至 3；指标抓取、TargetDown 触发/解除保持通过。`cleanupCompleted=true`，另行检查任务 namespace 与隔离容器已移除。本任务差异检查通过。

这是一次有界突发观测，尚无持续输入速率、消费延迟分位数、应用 P99、多分区并发或前后优化对比。不能据此宣称高吞吐或定位全部瓶颈。后续批量写入必须单独覆盖部分成功/失败、可靠 DLQ、连续 Offset 和重平衡，再用同场景对比；本次不打开独立消费者生产门禁。
