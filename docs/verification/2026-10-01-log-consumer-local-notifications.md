# 独立日志消费者本地通知闭环验证

- 日期：2026-10-01；授权：用户继续推进日志开发，仅本地测试，不向外部接收器发送消息。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`，分支 `codex/foundation-acceptance-20260926`，快照 `log-consumer-notification-20261001`；保留既有工作区改动，未提交或切流。
- 范围：可选通知实验入口、测试 Webhook、隔离 Alertmanager、正式重启规则的真实通知交付；没有 .NET、业务数据库或 AOT 变化。
- 环境：Windows、Docker Desktop、本地 kind 三节点 linux/amd64，共享一台主机；复用 Operator/Prometheus/KSM 本地监控工具。

## 实现

`pnpm test:log-consumer:notifications:live` 在原[真实重启实验](2026-10-01-log-consumer-restart-monitoring.md)上增加随机命名空间的 Alertmanager 与 Webhook。默认路由丢弃无关事件，只有本次 namespace、Pod 与正式规则精确匹配才投递；只开放 ClusterIP，不设置外部邮件/聊天接收器。`send_resolved=true` 和配置语义依据[官方 Webhook 文档](https://prometheus.io/docs/alerting/latest/configuration/#webhook_config)。

Webhook 必须匹配版本、接收器、状态、任务 namespace/Pod/规则、有效时间与指纹。正文最多 65536 字节，事件最多 100；拒绝超限、无效或无关输入，不保存原始注解和额外标签。API 转发仅供本地读取交付证明，测试不会向 Webhook 手工注入成功事件。

专用本地 Prometheus 的通知端点前置为空；临时修改 `spec.alerting`，退出时比较资源版本与本任务字段值，再恢复精确原值。请求结果未知也检查恢复，不覆盖其他人的字段变更。恢复与命名空间删除都要实际成功才发布通过工件；报告证明 CRD 配置字段恢复，运行配置传播遵守 Operator 正常调谐。

## 运行证据

| 命令 | 实际结果 |
| --- | --- |
| `node --test tests/deployment/log-alert-webhook-receiver.test.mjs` | 4/4；包括身份/状态拒绝、信息白名单、65537 字节拒绝和 100 条上限 |
| `pnpm test:observability-deploy` | 13/13，原 9 项部署检查与 4 项接收端测试，0 失败/跳过 |
| `pnpm test:inner -- --snapshot log-consumer-notification-20261001` | 受影响工具目标，64/64，0 失败/跳过 |
| `pnpm test:log-consumer:notifications:live` | 最终修订版退出 0，四阶段通知实际交付，清理成功 |
| 两个新增脚本及修改 runner 的 `node --check` | 退出 0 |
| `pnpm test:governance` | 57/57，0 失败/跳过 |

最终报告 `artifacts/log-consumer-notifications/result.json`，随机命名空间 `fullnet-log-restart-99aec6cd`，完成时间 `2026-09-30T16:24:01.875Z`（本地 2026-10-01）：

- 实际消费者缺启动配置退出 1、重启 3 次；真实 KSM 与正式规则触发告警。
- Webhook 按阶段实际收到 `firing → resolved → firing → resolved`；标签排除后等待对应 resolved，恢复后等待新 firing，删除 Pod 后才等待最终 resolved。
- 四次通知身份准确、指纹一致，不用较早 resolved 证明后续阶段，也不将 Prometheus 告警存在等同于通知送达。
- `cleanupCompleted=true`、`alertingConfigurationRestored=true`：任务 namespace、消费者、规则、Alertmanager、接收端、ConfigMap、Service、转发进程和临时镜像归档已清理，Prometheus 原通知字段已恢复。可复用本地工具及 Docker 镜像缓存保留。

Alertmanager 使用 v0.34.0，本次拉取摘要 `sha256:690c7b525f4367aa91f73e2f91c632206d32e97c6384bdbf2fb7a861b420340d`；接收端 Node 镜像固定摘要 `sha256:ebfe2f90462722a7a4de65e91990e97fe0d401c70e0e762c5b53302f905ec1c1`。运行准备命令见[平台说明](../../deploy/observability/README.md#independent-log-consumer-monitoring)。

## 失败与审查修正

第一轮 kind 导入失败：Docker Desktop 多架构索引引用了未缓存的其他平台内容，实际报 `content digest ... not found`。改为 `docker image save --platform=linux/amd64` 导出到任务 mkdtemp 后再导入 kind，退出清理归档。

下一轮接收端退出 0，API 验证失败：ConfigMap 投影是符号链接，入口字符串比较把直接执行误判为导入。改用真实路径比较，最终实际 Pod 稳定 Running、通知完整到达。失败轮次没有发布通过工件，命名空间清理完成，通知端点尚未修改。

独立审查发现最终 resolved 可能误消费标签排除阶段的旧事件；改为四阶段逐次等待并推进游标，复审通过。接收端超限路径先发送 413 再终止请求，真实 HTTP 边界测试通过。

本记录关闭本地 Webhook 通知链路缺口。没有验证具体外部邮件/聊天接收器、人的接收确认、通知 HA/故障重试、健康服务恢复、容量或请求 P99；独立消费者默认 disabled/experimental 不变。
