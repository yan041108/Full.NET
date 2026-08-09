# Kafka Consumer Group 协议与 Assignor 迁移

本文只说明 Full.NET Kafka Consumer 的运维迁移边界，不授权生产切流。`Messaging:DeliveryCutover:Enabled` 未通过 ADR-0006 门禁时必须保持 `false`。

## 为什么禁止普通滚动切换

存量 Classic Consumer 使用 eager Range Assignor。Cooperative Sticky 与 eager Assignor 不能在同一 Consumer Group 中混用；在 `maxUnavailable: 0` 的滚动发布中直接改变策略，会让新旧成员没有共同 Assignor，可能造成入组失败或持续 Rebalance。因此代码和 Helm 默认保持 `LegacyRange`，并用 `CooperativeStickyMigrationCompleted` 作为显式离线迁移确认。

## Classic Range → Cooperative Sticky

1. 记录 Consumer Group、Topic、当前成员、已提交 Offset、Lag、最老消息年龄和目标回退版本。
2. 停止接收新的发布切换操作，等待当前 Handler、Inbox 事务和待提交连续水位排空；不得手工越过未成功 Offset。
3. 在维护窗口把该 Group 的全部旧 Kafka Consumer 停止到零，确认 Broker 已无旧成员。当前 Worker 可能同时承载多个 Consumer Group，必须按实际部署拓扑评估停机影响。
4. 同时设置：

   - `Messaging:Kafka:ConsumerGroupProtocol=Classic`
   - `Messaging:Kafka:ClassicPartitionAssignment=CooperativeSticky`
   - `Messaging:Kafka:CooperativeStickyMigrationCompleted=true`

5. 启动新实例，验证成员稳定、分区唯一分配、连续 Offset、Inbox 去重、Lag 收敛，以及 Rebalance 次数和耗时。
6. 任一门禁失败时，先再次停止该 Group 全部成员，再整体恢复 `LegacyRange`；禁止新旧 Assignor 并存回滚。

## Classic → Kafka 4 Consumer Protocol

只有 Broker 兼容基线为 Kafka 4.x 且真实入组、故障恢复和回退演练通过后，才设置 `ConsumerGroupProtocol=Consumer` 与 `BrokerMajorVersion>=4`。该模式不发送 Classic-only 的 `partition.assignment.strategy`、`session.timeout.ms` 或客户端 heartbeat 参数；Broker 端 Consumer Group 超时和服务端 Assignor 必须纳入平台配置审查。

## Static Membership 限制

Helm Deployment 以 Pod 名注入 `group.instance.id`。同一 Pod 的容器进程重启可复用身份，但滚动替换生成的新 Pod 名不会继承旧身份，因此不能据此宣称滚动发布不再 Rebalance。跨 Pod 替换的稳定身份需要另行决定 StatefulSet/稳定实例槽位等拓扑，并验证唯一性、扩缩容、故障 Pod 回收和 Fence；在此之前只把 Static Membership 视为进程瞬时重启优化。

## 必须保存的证据

- 变更前后 Group 成员与 Assignor/Protocol；
- 已提交 Offset、Lag、最老消息年龄、Pause/Resume 和 Rebalance 指标；
- Inbox 重复命中、失败 Seek、Retry/DLQ、关闭排空和恢复结果；
- 使用的配置快照、镜像摘要、操作人、维护窗口、回退版本和实际回退耗时。

没有上述生产等价证据时，能力状态继续为 `Capacity-not-verified`。
