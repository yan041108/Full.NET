# Webhook Consumer 样例

使用 Node.js 24 内置模块启动真实 HTTP 接收端，验证当前工作流完成事件签名，并在确认 HTTP 前保存消费回执。无需安装额外依赖。

## 启动

准备一个只允许服务账号读取的 UTF-8 秘密文件，内容与订阅的独立随机签名密钥一致（16～128 字符，可有一个结尾换行）。通过环境变量引用文件，不把秘密放进命令参数、源码、日志或 Git。

```powershell
$env:FULLNET_WEBHOOK_SIGNING_SECRET_FILE = 'C:\Secure\webhook-signing-secret'
$env:FULLNET_WEBHOOK_TENANT_ID = '<订阅所属企业 UUID>'
$env:FULLNET_WEBHOOK_KEY_ID = '<订阅返回的 signingKeyId>'
$env:FULLNET_WEBHOOK_RECEIPTS_FILE = 'C:\FullNetData\webhook-consumer\receipts.ndjson'
pnpm sample:webhook-consumer
```

默认只监听 `127.0.0.1:8085`，使用 `POST /webhooks`；可通过 `FULLNET_WEBHOOK_HOST`、`FULLNET_WEBHOOK_PORT` 修改监听。`GET /health` 仅说明进程正在监听，不证明账本容量、平台连通性或业务交付成功。正式 Webhook 目标须使用具有有效证书的 HTTPS 入口，可由反向代理转发至本监听器；遵守发送方现有公网地址/SSRF 限制，不为样例放行回环或内网订阅地址。

接收端固定核对配置的企业及 `fullnet.workflow.instance.completed`。生产事件正文使用 PascalCase（`EventId`、`TenantId`、`EventType`、`OccurredAtUtc`、`Data`），与管理 API 的 camelCase 分开；[共用报文夹具](../../contracts/webhooks/workflow-instance-completed-v1.json)由 .NET 生产序列化和本接收端测试共同核对。签名覆盖请求正文的原始 UTF-8 字节，使用 HMAC-SHA256 和恒定时间比较；事件头须匹配已签名信封。投递 ID 用于诊断，不作为事件去重权威。

## 回执与重复消费

首次接收按原事件 ID 保存企业、事件类型、正文摘要、原投递 ID、接收时间及实例记录结果。文件追加并 flush 成功后返回 200；相同事件/正文再次收到，返回原消费结果及 `duplicate: true`，不同投递 ID 不产生第二条结果。相同事件 ID 携带不同正文或来源返回 409，原回执不变。正文、业务引用和签名秘密不写入账本。

样例只在单进程本地账本中记录工作流实例事实。账本的 `.lock` 文件防止两个进程同时写入，普通关闭会释放；异常退出留下锁时，先确认原进程已结束、保存并核对账本后再人工处理锁。半条或损坏记录拒绝启动，不自动截断、清空或跳过。部署时保留同一账本文件及访问权限，不能通过换一个空路径恢复服务并遗忘去重事实。

默认正文最多 256 KiB，账本最多 10,000 条或 8 MiB，每分钟最多 60 次请求，最多 16 个在途处理、64 个连接；正文接收有超时，分块上传同样受限。达到账本上限后新事件返回 503，已有重复仍返回原结果，不自动删除历史。这里不提供多实例数据库、账本轮转、独立外部副作用或断电之外的存储保证；把样例扩展为真实业务时，将 Inbox 唯一键与业务写入放入自己的数据库事务，外部副作用走可靠事件或幂等接口。

样例按 [签名 v1](../../docs/development/webhook-protocol.md) 同时验证版本、配置允许的 KeyId、投递时间、事件/投递身份、事件键和原文字节摘要。默认过去/未来容差均为 300 秒，可用 `FULLNET_WEBHOOK_SIGNATURE_WINDOW_SECONDS` 设置 1～3600 秒；保持系统时间同步。`OccurredAtUtc` 是业务发生时间，发送方在每次延迟重试时生成新的签名投递时间，仍用原事件回执去重。未知键、缺少/重复头、超窗或元数据被改动均不确认消费；轮换先准备并配置接收端，再明确激活；接续与退役步骤见下文。

## 准备、激活与旧键退役

在平台原订阅准备一份新随机秘密，记录返回的固定 operationId 和新 signingKeyId；准备不会改变发送方当前密钥。先为接收端配置新键并设置旧键截止时间，再在平台核对当前订阅修订，明确激活。取消准备会清除平台的准备秘密、保留审计，原活动密钥继续工作；接收端也应明确撤回不再使用的新秘密文件引用。

轮换时可以使用受保护 JSON 文件保存两个秘密文件引用：

```json
{
  "keys": [
    { "keyId": "<旧公开 KeyId>", "secretFile": "C:/Secure/webhook-old", "acceptUntilUtc": "<明确 UTC ISO 时间，不超过启动后一小时>" },
    { "keyId": "<准备返回的新公开 KeyId>", "secretFile": "C:/Secure/webhook-new", "acceptUntilUtc": null }
  ]
}
```

替换占位符；文件不存放秘密明文，只引用绝对文件路径。环境只设置 `FULLNET_WEBHOOK_SIGNING_KEYS_FILE`，移除原单键的 `FULLNET_WEBHOOK_SIGNING_SECRET_FILE` 和 `FULLNET_WEBHOOK_KEY_ID`，不能同时指定两种配置。正常停止旧接收进程，使用同一回执账本重启后核对就绪，再激活平台新键。配置启动时冻结，不自动监听文件变化；最多两个不同 KeyId，恰好一个可没有截止时间，有限截止时间的剩余期限最多 3600 秒。截止时即使旧秘密签了新时间也拒绝，不使用请求头延长允许期；显式移除旧引用及访问权限时保留原业务回执。

发送器在每次实际外发前读取当前活动身份/秘密，因此同批已领取的后续投递也会使用新键；已经进入发送边界的旧请求仍可能到达。按自己的网络延迟与可靠性策略选择有界接续期，不能把激活或签名时间窗当作即时撤回请求。旧请求被拒绝后，后续重试使用新键并保留原事件身份；达到发送方重试上限时仍须按原投递核对并明确重放，不新建业务事件。

## 排错与撤销

应用错误使用标准 HTTP 状态与 `application/problem+json`，机器码放在 `code`，不输出秘密、原始异常或正文。

| 状态 | 处理 |
| --- | --- |
| 400 `webhook_consumer.payload_invalid` | 核对企业、事件键、原事件头、UTC 时间和工作流数据；不要改签名后绕过来源检查。 |
| 401 `webhook_consumer.signature_invalid` | 核对秘密文件、原始字节和是否被代理重写；不要记录秘密，不把验签失败当作成功消费。 |
| 409 `webhook_consumer.event_conflict` | 核对原事件与正文摘要，保留原回执，禁止覆盖后重新消费。 |
| 413 / 415 | 核对正文预算、JSON 类型及压缩/编码；分块请求仍受大小限制。 |
| 429 `webhook_consumer.rate_limited` | 按 `Retry-After` 退避，保留原事件身份；发送方仍受自己的重试上限约束。 |
| 503 `webhook_consumer.store_unavailable` | 核对锁、磁盘、权限、容量及完整性；写入失败后本进程停止接纳，核对账本后重启，不清空账本。 |

需要撤销接收能力时，先在所属企业明确暂停原订阅，再停止接收进程并撤回秘密文件访问；已领取或已发出的请求可能仍在途。恢复或更换接收设置前核对原事件与历史，不复制旧任务成新业务事件。密钥轮换使用原订阅的准备/激活/取消接口，保留原订阅和历史，不复制为另一业务事件。接收程序若需要回调平台 API，应另外配置最小权限凭据并通过其所属管理入口撤销，本样例不持有平台访问令牌。

## 验证脚本

```bash
node samples/webhook-consumer/verify-signature.mjs \
  --secret-file /secure/webhook-signing-secret \
  --key-id <configured-key-id> \
  --payload-file /data/original-request-body.json \
  --metadata-file /data/request-metadata.json \
  --signature <header-value>
```

元数据 JSON 包含请求头对应的 `version`、`keyId`、`timestamp`、`eventId`、`deliveryId`、`eventType` 字符串。`--key-id` 来自受保护配置；脚本按签名 v1 规范串进行 HMAC-SHA256 验证，并核对实际当前时间窗，历史请求可用于诊断，不能当作当前有效签名。

运行本样例的定向验证：`pnpm test:webhook-consumer`。测试使用真实本地 HTTP 和独立系统临时账本，不连接平台后端或外部服务。

需要验证实际平台业务链时，按 SDK README 准备双库、Redis 与后端构建后运行 `pnpm test:integration-sdk:package`：企业申请实际提交审批并完成，独立 Worker 从事务 Outbox 扇出 Webhook，真实本机接收端验签/保存回执，独立安装包读取同一原始报文并核对业务身份。测试还覆盖入队确认丢失后的原事件重试及重复接收，使用各自临时账本并正常关闭；仅测试传输转发至回环，不改变生产 SSRF 规则。
