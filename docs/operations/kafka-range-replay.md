# Kafka 一次性范围重放

## 1. 适用边界

该能力用于按已批准 Topic 的时间段或 Offset 区间修复投影、核对 Inbox 幂等行为。它不是常驻 Consumer，也不是清空 DLQ 的批处理捷径。

- 重放 Consumer 使用唯一临时 GroupId 和显式 `Assign`，不调用正式 Group 的 Offset Commit。
- 开始前固定每个分区的起止 Offset；Offset 请求的上下界均包含，时间请求的结束时间为不包含边界。
- Topic 必须来自静态 `IIntegrationEventSubscriptionCatalog`，消息仍通过原有 Envelope 校验、订阅 Catalog、Inbox、业务事务与 Handler。
- 重复 MessageId 由 Inbox 返回 `AlreadyProcessed`；同 ID 不同 PayloadHash 失败关闭。
- 底层请求模型保留 1–100000 的离线工具边界，但当前 HTTP 同步入口最多允许 1000 条，并受 `Messaging:KafkaReplay:MaximumSynchronousMessages` 的更小配置约束；达到上限时返回 `LimitReached=true`，不得自动扩大范围。
- API 重放默认关闭。只有显式设置 `Messaging:KafkaReplay:Enabled=true`，同时配置 Kafka `BootstrapServers`、独立 `ClientId`，并把整个同步操作超时限制在 5–45 秒内才会开放。单次最多处理 32 个分区；空分区列表只有在 Topic 实际分区数不超过该上限时才允许。

## 2. 权限与审计

API 为 `POST /api/v1/messaging/kafka/replay`，必须具备独立权限 `messaging.kafka.range_replay`。DLQ 单条重放权限不能替代该权限。

请求必须提供 1–512 字符审计原因。系统先提交 `requested` 领域审计，成功后再提交同一操作标识的 `success` 结果；执行失败、取消或超时会使用独立短超时令牌尽力提交 `failure` 终态审计；首条审计失败时不会访问 Broker。进程被强杀或审计存储不可用时仍可能只有 `requested`，运维人员必须先核对 Inbox 与正式 Group 水位，再决定是否用剩余范围重试。

## 3. CLI

CLI 通过受保护 API 执行，令牌只从环境变量读取，避免出现在进程参数与 Shell 历史中。

```powershell
$env:FULLNET_ACCESS_TOKEN = '<host access token>'
dotnet run --project src/Tools/Full.NET.Messaging.Cli -- `
  --api-base-uri https://fullnet.example.com `
  --topic messaging.organization-unit-changed.v1 `
  --consumer fullnet.identity.organization-unit-projection `
  --from-offset 1200 `
  --to-offset 1299 `
  --partitions 0 `
  --max-messages 100 `
  --reason 'repair organization projection gap'
```

时间范围使用 `--from-time` 与 `--to-time`，必须传 UTC 时间；时间与 Offset 参数不得混用。

Helm 默认保持 `api.kafkaReplay.enabled=false`。启用时必须在 API 角色 Release 中配置 `api.kafkaReplay.bootstrapSecretName`，Chart 才会向 API 注入 Broker Secret、同步消息上限和执行超时；Worker 的 Kafka Secret 不会隐式共享给 API。生产/预发布只允许 `Ssl` 或 `SaslSsl`；使用 `SaslSsl` 时还必须配置机制以及用户名、密码 Secret，禁止把凭据写入普通 Values。

## 4. 执行前后核对

执行前记录正式 Consumer Group 的分区提交水位、目标 Inbox 行数、目标投影版本和当前 Lag。执行后必须确认：

1. 正式 Group 提交水位未因重放改变；
2. `Processed + AlreadyProcessed + Rejected = ScannedMessages`；
3. `LimitReached=false`，或已人工计算下一段不重叠范围；
4. Inbox 没有 PayloadHash 冲突，业务投影对账收敛；
5. `requested` 与 `success/failure` 终态审计成对；只有 `requested` 时按不确定执行处理，禁止直接整段盲重放。

## 5. 仍未解除的生产门禁

该工具已具备构建、真实 Kafka 固定水位、Dispatcher/Inbox 重复副作用阻断和双数据库 API 权限证据，但当前只批准小于等于 1000 条、最多 32 分区、最长 45 秒的同步运维窗口。每个同步 Kafka 元数据调用前后都会检查取消，单次原生调用仍可能占用最多 10 秒余量；该余量与 5 秒终态审计共同保持在默认 60 秒 Ingress 窗口内。大范围或长时间重放必须另行实现持久化异步作业，不得通过提高 Ingress 超时绕过。整体状态继续为 `Capacity-not-verified`，不得据此提升 CDC/Kafka 主链路的生产认证状态。
