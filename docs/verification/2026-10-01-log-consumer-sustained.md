# 独立日志消费者持续输入与恢复追赶验收

- 日期：2026-10-01（Asia/Shanghai）；分支 `codex/foundation-acceptance-20260926`，基线 `c609759576c4bf54ff9e743161ecb829ca8d4d4a`。
- 任务快照：`log-consumer-sustained-20261001`。仅新增本地实验工具/判定、显式命令和说明；没有修改消费运行代码或应用请求路径。

## 固定范围

本地 `kind-fullnet-local` 三节点共享 Docker Desktop 主机；隔离单节点 Kafka、HTTPS ES、正式 JIT Consumer。复用 `fullnet-log-consumer:local-batch-20261001`，批次上限 8，CPU/内存请求 100m/128Mi，上限 500m/256Mi。主机 Intel Core i7-12700H、14 核/20 逻辑处理器、物理内存 68450914304 字节，Docker 内存约 31.22 GiB。

单 Kafka Console Producer 连接 `acks=all`，Node 每 200ms 发送 10 条，3000 个唯一 ID、每条精确 2048 字节 UTF-8 JSON；发送持续至少 59 秒、最多 90 秒。第 20 秒暂停随机任务 ES 容器至少 10 秒、最多 20 秒，然后恢复。每约 5 秒加 CLI 执行时间读取来源位点，采样积压最多 1000 条；必须在发送尚未结束且来源末位点未达 3003 时观察到 lag=0。采样阶段取查询开始时状态，时间为返回时刻，恢复时间是含 CLI 开销的观察值，不是精确事件延迟。

排空期限从最后发送完成时起算，Producer 退出和 observer 收尾同样消耗 30 秒预算；每次位点查询最多 10 秒且不超过剩余预算，返回后再次核对绝对期限。成功还要求逐 ID/完整内容读回、最终 Offset=3003/lag=0、DLQ 不新增、Pod UID 不变、无额外重启和内存峰值不超过 256Mi。失败会取消发送与观察、恢复已暂停的任务容器，外层移除所有任务资源；全部清理后才发布 passed 工件。

## 失败测试与审查修正

占位判定实现的 RED 为 2/2 失败（缺少有效结果、超限不拒绝），实现后 2/2 通过。独立审查发现初版排空起算晚且慢 CLI 可绕过 waitFor 时间窗；新增“发送结束 61 秒、最终确认 91.001 秒”的负例，实际 RED 为 1 通过/1 失败。按上述绝对期限修正后 2/2 通过，独立复核确认问题已消除，无新增阻塞问题。

初轮真实数据保存在 `artifacts/log-consumer-sustained/pre-deadline-fix-result.json`，只能作为修正前的实验资料；本轮最终验收使用修正代码重新完整运行，不将初轮报告作为最终通过。

## 验证状态

| 命令 | 实际结果 |
| --- | --- |
| `node --test tests/deployment/log-consumer-sustained-proof.test.mjs tests/deployment/log-consumer-throughput-proof.test.mjs tests/deployment/log-consumer-contract.test.mjs` | 最终判定修正后 10/10，失败/跳过 0 |
| `pnpm test:integration:tooling` | 53/53，失败/跳过 0 |
| `pnpm test:governance` | 57/57，失败/跳过 0 |
| `pnpm test:log-consumer:sustained:live` | 修正后完整重跑退出 0，3000 条逐 ID/内容对账、资源清理通过 |

任务影响集为 integration-tooling，没有新的 .NET 运行代码，故不重跑上轮 310 项日志单测和 15 项 Kafka/ES 回归，也不将历史结果冒充本轮执行。

本场景不包含 HTTP 请求，不能证明应用请求 P50/P95/P99；60 秒持续输入和 10 秒下游暂停不能外推长期 Soak、最大吞吐、其他数据/分区规模或高可用。

## 最终真实结果

最终报告：`artifacts/log-consumer-sustained/result.json`，namespace `fullnet-log-e2e-69a08806`，完成 UTC `2026-09-30T21:45:07.512Z`（本地 2026-10-01）。顶层仍记录初始三条功能阶段，新增持续负载在 `sustained` 内独立记录。源镜像沿上轮固定构建，kind 实际 Pod imageID 为 `docker.io/library/import-2026-09-30@sha256:6a646e13eed07f21493f99b01c79aa03bf7423cb393a8c7cfc188ac7f7b21194`；这是导入后运行时身份，不直接等同 Docker 多平台构建摘要。

| 指标 | 实际观察 |
| --- | ---: |
| 事件/JSON 字节总量 | 3000 / 6144000 |
| 发送持续时间/实际提交 stdin 的节奏 | 61.366 秒 / 48.89 条每秒 |
| ES 暂停窗口 | 10.354 秒 |
| 最大采样积压 | 462 条（上限 1000） |
| ES 恢复到观察追平 | 14.135 秒 |
| 发送结束到最终确认 | 2.283 秒（绝对上限 30 秒） |
| Consumer cgroup CPU/节流增量 | 3.464 秒 / 0.436 秒 |
| Pod 生命周期内存峰值 | 63209472 字节（约 60.28 MiB，上限 256Mi） |
| 最终 Offset / lag | 3003 / 0 |
| 新增 DLQ / 压测额外重启 | 0 / 0 |

第 30.479 秒的 paused 样本 committed=997/sourceEnd=1459/lag=462；ES 在 30.527 秒恢复。第 37.619 秒采样 lag=7，第 44.662 秒 sourceEnd=2159/lag=0，早于 61.366 秒发送结束且 sourceEnd 小于 3003，证明输入继续时恢复追平。第 63.649 秒最终确认全部记录。采样不是完整高频积压曲线，462 仅是采样最大值，不能排除查询间更高的瞬时积压。

最终报告 `cleanupCompleted=true`，任务 Kafka/ES/CRL/Prometheus 容器和 namespace 已移除；既有 kind 与本地日志监控 namespace 保留。独立审查的排空问题已修复并重新完整验证，此范围可按本地标准验收；请求 P99 和长时间/更高规模仍未验证。
