# 独立日志消费者重启监控接线验证

- 日期：2026-09-30 开始，2026-10-01 本地完成；授权：用户继续推进日志开发，按本地实际测试验收。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`，分支 `codex/foundation-acceptance-20260926`，快照 `log-consumer-restart-alert-20260930`；保护既有改动，未提交或切流。
- 范围：可选本地监控 Overlay、独立随机命名空间实验、实际 kube-state-metrics 和正式重启告警。没有 .NET、数据库、API/Worker AOT 代码变化；不重复未变化的 Kafka/ES 验收。
- 环境：Windows、Docker Desktop、Kubernetes v1.37.0 的本地 kind 三节点共享主机；复用专用 Operator/Prometheus release `fullnet-log-monitor`，本次升级为 revision 2。

## 接线

[本地 Overlay](../../deploy/observability/log-consumer-restart-local-values.yaml)叠加在固定 kube-prometheus-stack 89.2.0 的原 Operator 配置上。实际 Helm 渲染与安装确认：

- kube-state-metrics 仅获得 pods `list/watch`，仅启用 Pod collector。
- 参数为 `--metric-allowlist=kube_pod_labels,kube_pod_container_status_restarts_total`，标签为 `--metric-labels-allowlist=pods=[app.kubernetes.io/name]`；未使用标签通配符。
- ServiceMonitor 5 秒采样，`honorLabels: true`，保留指标所属工作负载的 namespace，不被抓取服务 namespace 替换。
- ServiceMonitor 仅从专用监控命名空间选取，测试 PrometheusRule 使用显式标签选择器。
- KSM 有资源边界，实际版本 v2.20.0，Kubernetes 返回 imageID 为 `registry.k8s.io/kube-state-metrics/kube-state-metrics@sha256:42cfe3723a5f058171c627537fb57a3ea0f26e4380fa18555a95cb1a1b4cfc5b`；其余监控组件沿[前次发现记录](2026-09-30-log-consumer-operator-discovery.md)。

安装与重复执行命令见[平台说明](../../deploy/observability/README.md#independent-log-consumer-monitoring)。本地监控 release/CRD 保留复用，已有 Fluent Bit release 未更改。

## 实际验证

脚本创建随机命名空间，以真实消费者镜像 `fullnet-log-consumer:local-20260930` 故意缺少 bootstrap 配置，在创建下游客户端前抛出 `ArgumentException` 并退出 1；不使用 Secret。先验证初次失败日志，再等待真实 Kubernetes 重启和 Prometheus 采样，不注入模拟重启计数。

从仓库 `prometheus-rules.yaml` 读取正式 `FullNetLogConsumerRestarting` 表达式，不缩短窗口或阈值：

```promql
increase(kube_pod_container_status_restarts_total{container="consumer"}[5m]) > 2
and on (namespace, pod) kube_pod_labels{label_app_kubernetes_io_name="fullnet-log-consumer"} == 1
```

| 命令 | 实际结果 |
| --- | --- |
| `helm upgrade ... -f log-consumer-operator-local-values.yaml -f log-consumer-restart-local-values.yaml --wait --timeout 5m`（完整命令见平台说明） | 退出 0，KSM 实际 Ready；渲染确认最小权限、指标/标签范围和选择器 |
| `pnpm test:log-consumer:restart-alert:live` | 最终修订版退出 0，真实重启 3 次，正式告警触发 |
| `node --check eng/testing/log-consumer-restart-alert-smoke.mjs` | 退出 0 |
| `pnpm test:inner -- --snapshot log-consumer-restart-alert-20260930` | 受影响目标 integration-tooling，64/64，0 失败/跳过 |
| `pnpm test:observability-deploy` | 9/9，0 失败/跳过 |
| `pnpm test:governance` | 57/57，0 失败/跳过 |

最终工件 `artifacts/log-consumer-restart-alert/result.json`，命名空间 `fullnet-log-restart-3b6cbeaa`，完成时间 `2026-09-30T16:02:28.914Z`（本地 2026-10-01）：

- 实际 exporter 输出工作负载 namespace、Pod、container 和规范化应用标签；不是只检查配置文本。
- `restartCount=3`，Pod 原生状态也确认至少 3 次重启及最后一次 exit 1；`officialRuleFired=true`。
- 同一仍有重启指标的 Pod 改为非消费者标签后，正式告警解除；恢复消费者标签后重新触发，排除无指标假通过。
- 删除测试 Pod 后，实际重启序列及告警消失；`removedPodAlertResolved=true`。这证明测试对象移除后的解除，不代表健康消费者恢复。
- 随机命名空间、Pod、PrometheusRule 和 API 转发进程清理后才发布报告，`cleanupCompleted=true`。

## 失败与审查

前两轮在读取多次重启后的 `--previous` 日志时未匹配预期文本，导致整轮失败，没有发布通过工件，任务命名空间已清理。直接运行镜像及同配置临时 Pod 都确认准确的 `ArgumentException`。最终调整为初次启动失败时验证当前日志，随后独立验证真实重启、exit 1 和正式告警；未将 CRI 回收等推测记为已证实根因，未删除异常类型或重启断言。日志请求限制为 5 秒，轮询期限为 60 秒。最终完整重跑通过。

独立审查实际渲染两层 Helm 配置，确认 pods 最小读取权限、两个指标、标签 allowlist 与 `honorLabels`；对日志验证移动完成增量复审，无阻断项。

本切片关闭本地 kube-state-metrics 标签/指标接线缺口。没有发送外部通知，没有验证健康服务恢复、下游 HA、容量或请求 P99；独立消费者默认 disabled/experimental 未改变。
