# Kubernetes 独立日志消费者链路与 CRL 验证

- 日期：2026-09-30；授权：用户继续推进日志模块，沿全项目本地验收标准执行。
- 基线：`c609759576c4bf54ff9e743161ecb829ca8d4d4a`；任务快照：`logging-consumer-kubernetes-delivery`，保护既有工作区改动。
- 环境：Windows、Node.js 24.12.0、Docker Desktop、本地 `kind-fullnet-local` 三节点（共享主机）；独立消费者 JIT 镜像 `fullnet-log-consumer:local-20260930`。Kafka 4.1.2、Elasticsearch 9.5.4 为外部隔离单节点测试容器，镜像摘要固定于脚本。
- 状态：功能链路本地验收通过，独立消费者仍为 experimental；没有生产部署或业务切流。

## 实际实验

命令：`pnpm test:log-consumer:kubernetes:live`，退出 0。稳定入口调用 `eng/testing/log-consumer-kubernetes-smoke.mjs --docker-desktop`，要求已构建上述消费者镜像及已有本地 kind 集群。新增脚本不改变宿主投递代码，不需重新发布 API/Worker Native AOT。

脚本使用短期测试 CA、实际 HTTP CRL 分发地址及测试服务端证书。Kafka 通过 TLS 外部监听访问；ES 使用 HTTPS、独立 API Key，仅允许写入本次固定日期索引。Helm 使用原有 Production/Online 配置及只读公钥 CA Secret；没有使用 NoCheck 或跳过主机名验证。Kafka 内部明文 CLI 仅在隔离的单节点 Broker 容器内用于测试布置和断言，不是应用或消费者出口。

| 场景 | 实际结果 |
| --- | --- |
| 合法事件 + 毒消息 | ES 按事件 ID 读回合法事件，DLQ 读回原始 `not-json`；Offset=2、lag=0，Pod ready，初始重启 0 |
| 吊销 ES 服务端证书 | CRL 实际包含被吊销序列号；消费者异常退出 1，脱敏日志类型为 HttpRequestException；Offset=2、log-end=3、lag=1 |
| 吊销期间无错误确认 | 待处理合法事件 ES 查询 404，DLQ end-offset 仍为 1；没有将 TLS 故障当成毒消息隔离 |
| 更换证书恢复 | 使用同 CA 签发新序列号，保留已有吊销条目；ES 重启后消费者自动重放，ES 按 ID 读回第二条文档，Offset=3、lag=0，Pod ready |
| Online 实际执行 | CRL 服务记录 4 次请求；Production 配置不降级 |
| 退出清理 | 专用命名空间、Secret、3 个下游测试容器、临时证书/私钥目录全部删除后才发布通过工件 |

最终工件：`artifacts/log-consumer-kubernetes/result.json`，`passed=true`、`cleanupCompleted=true`；实验命名空间 `fullnet-log-e2e-edf80c91`，完成时间 `2026-09-30T14:33:18.148Z`。每次运行先删除旧结果，失败不能保留上次通过工件；清理错误会失败关闭。容器删除只按本进程随机生成的精确名称进行。

其他验证：`node --check eng/testing/log-consumer-kubernetes-smoke.mjs` 退出 0；消费者 Helm 合约 4/4、`pnpm test:integration:tooling` 53/53、`pnpm test:governance` 57/57，均无失败/跳过。任务 slice 影响集仅为 integration-tooling，未修改 .NET/业务数据库行为；无需重跑不受影响的双库集合。

## 失败证据与修正

最初负向实验错误预期退出码 3，实际取得 HttpRequestException、退出 1，测试超时失败。根因是断言混淆了宿主异常退出和明确 Retry 返回；修正断言为退出 1 并检查脱敏异常类型，保持投递规则不变，完整重跑通过。

独立审查发现报告先于清理写入会留下误导工件，已改为清理全部成功后写报告，并让清理失败退出非 0；使用最终脚本重新执行完整实验通过，复核无阻塞项。

## 范围

本次证明 kind 中 Helm 消费者连接外部真实 Kafka/ES 的确认、隔离、位点及 ES CRL 恢复边界。未证明集群内 Kafka/ES 部署和 HA、NetworkPolicy 在 CNI 的实际执行、生产目标 PKI 或 OCSP、Kafka 客户端吊销策略、容量/请求 P99 或 Lag/故障分类告警闭环。历史完整 Kafka/ES 14 项回归见[运维交付记录](2026-09-30-log-consumer-operations.md)，本记录不冒充重跑全部集合。
