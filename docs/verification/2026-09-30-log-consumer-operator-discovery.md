# 独立日志消费者 Operator 自动发现验证

- 日期：2026-09-30；授权：用户继续推进日志开发，按本地实际测试验收。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`，分支 `codex/foundation-acceptance-20260926`，任务快照 `log-consumer-operator-20260930`；保护既有工作区改动，未提交或切流。
- 范围：本地轻量监控配置、显式 Operator 实验入口、PodMonitor 双选择器和真实 Pod 采集；未修改 .NET、数据库或 API/Worker AOT 路径。
- 环境：Windows、Docker Desktop、`kind-fullnet-local` 三节点共享主机，Kubernetes v1.37.0；随机消费者连接外部隔离单节点 TLS Kafka/HTTPS ES。此为功能验收，未执行容量或请求 P99。

## 实现与部署

使用[本地监控配置](../../deploy/observability/log-consumer-operator-local-values.yaml)安装独立 release `fullnet-log-monitor`，命名空间 `fullnet-local-log-monitoring`。关闭 Grafana、Alertmanager、kube-state-metrics、node exporter、默认集群规则与采集；Prometheus/Operator 有资源限制，数据保留 1 小时。PodMonitor 与命名空间都要求 `fullnet.io/log-smoke=selected`，消费者 NetworkPolicy 的监控命名空间与该工具一致。

固定上游 kube-prometheus-stack 89.2.0，归档 SHA-256 `f594687a8d7e471b2bc90886843500a0849354e550794ebde42264792987a323` 与官方索引一致；来源与复现安装命令见[平台说明](../../deploy/observability/README.md#independent-log-consumer-monitoring)。本机 Helm 的已有 Fluent 仓库缓存缺失，先 `helm repo update fluent` 修复；官方仓库索引通过 curl 读取，归档从官方 GitHub release 下载，未绕过证书校验。

实际运行镜像：

| 组件 | 版本 | 本地 Kubernetes 返回的 imageID 摘要 |
| --- | --- | --- |
| Prometheus Operator | v0.93.1 | `e52bb28fd41c98dd407c7a8cba8bdcfe7eabd7447e250afaf1fe7bb816dedbff` |
| config-reloader | v0.93.1 | `428f088fe6fe07ab138bda92113664b04848a1dc408e4d3680a60ecdb55d1a65` |
| Operator 管理的 Prometheus | v3.14.0-distroless | `50c707e96da5ade383cb1707790576480485e93de06aa60ad8802cb5f744bd0a` |

这三项来自上游固定 chart 的版本标签，imageID 记录本次实际内容；不是将所有平台镜像默认替换为这些版本。消费者复用已构建 `fullnet-log-consumer:local-20260930`，不重新构建未变化的 .NET 源码。

## 最终验证

| 命令 | 实际结果 |
| --- | --- |
| `helm upgrade --install fullnet-log-monitor ... --wait --timeout 5m`（完整参数见平台说明） | 退出 0，Operator 与 Prometheus 实际 Running/Ready |
| `node --check eng/testing/log-consumer-operator-probe.mjs` 与主 smoke 脚本 | 退出 0 |
| `pnpm test:log-consumer:operator:live` | 修订后完整运行退出 0，报告在任务清理后发布 |
| `pnpm test:helm` | 26/26，0 失败/跳过 |
| `node --test tests/deployment/observability-contract.test.mjs` | 9/9，0 失败/跳过 |
| `pnpm test:inner -- --snapshot log-consumer-operator-20260930` | 受影响目标 integration-tooling，实际 64/64，0 失败/跳过 |
| `pnpm test:governance` | 57/57，0 失败/跳过 |

最终工件 `artifacts/log-consumer-operator/result.json`，随机命名空间 `fullnet-log-e2e-42066922`，完成时间 `2026-09-30T15:42:23.625Z`：

- 先取得真实 target up，再分别排除 PodMonitor 标签和命名空间标签；每项等待目标消失，保持 20 秒观察，恢复后再取得 up，不依赖刚启动时的空目标。
- `operatorDiscovery.verified=true`；两层排除与 `directPodScrapeVerified` 均 true。实际 scrape pool 为 `podMonitor/fullnet-log-e2e-42066922/log-consumer/0`，job 为 `fullnet-log-consumer`，URL 严格匹配该真实 Pod IP 的 `8080/metrics`；Prometheus 查询消费者 `lag_known=1`。
- 3 条来源记录 → 2 条 ES 文档 + 1 条原始毒消息 DLQ；最终 Offset=3、lag=0。Production/Online CRL 请求 4 次，吊销时保留 Offset 2，换新序列号证书后重放到 3。
- 原独立静态 Prometheus 仍用于正式 TargetDown 规则的测试采集桥中断/恢复，触发与解除通过；该场景与 Operator 自动发现分别验证，不冒充 Operator 目标故障或通知送达。
- `cleanupCompleted=true`：随机命名空间、Secret、四个隔离下游/静态 Prometheus 容器、转发进程和短期私钥目录已清理。专用本地监控 release、其命名空间及集群 Operator CRD 明确保留复用；已有 Fluent Bit release 未改变。

## 审查与边界

初版在取得正例前观察初始空目标，被独立审查指出可能把未调谐状态当作排除成功；改为“真实 up → 排除 → 消失且稳定 → 恢复 up”，两层分别执行。初版通过工件不作为最终修订证据，重跑先删除旧报告；修订代码复审未发现阻断项。

本切片关闭本地 Operator 自动发现的验证缺口。未验收 kube-state-metrics 重启标签接线、Alertmanager 通知送达、集群 Kafka/ES HA、CNI 网络策略实际执行、容量或请求 P99；独立消费者默认 disabled/experimental 配置未改变。现有功能或可靠性记录不因本次监控测试自动升级为容量认证。
