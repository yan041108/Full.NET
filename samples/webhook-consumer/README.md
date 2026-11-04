# Webhook Consumer 样例

使用 Node.js 24 内置模块启动真实 HTTP 接收端，验证当前工作流完成事件签名，并在确认 HTTP 前保存消费回执。无需安装额外依赖。

## 启动

准备一个只允许服务账号读取的 UTF-8 秘密文件，内容与订阅的独立随机签名密钥一致（16～128 字符，可有一个结尾换行）。通过环境变量引用文件，不把秘密放进命令参数、源码、日志或 Git。

```powershell
$env:FULLNET_WEBHOOK_SIGNING_SECRET_FILE = 'C:\Secure\webhook-signing-secret'
$env:FULLNET_WEBHOOK_TENANT_ID = '<订阅所属企业 UUID>'
$env:FULLNET_WEBHOOK_RECEIPTS_FILE = 'C:\FullNetData\webhook-consumer\receipts.ndjson'
pnpm sample:webhook-consumer
```

默认只监听 `127.0.0.1:8085`，使用 `POST /webhooks`；可通过 `FULLNET_WEBHOOK_HOST`、`FULLNET_WEBHOOK_PORT` 修改监听。`GET /health` 仅说明进程正在监听，不证明账本容量、平台连通性或业务交付成功。正式 Webhook 目标须使用具有有效证书的 HTTPS 入口，可由反向代理转发至本监听器；遵守发送方现有公网地址/SSRF 限制，不为样例放行回环或内网订阅地址。

接收端固定核对配置的企业及 `fullnet.workflow.instance.completed`。签名覆盖请求正文的原始 UTF-8 字节，使用 HMAC-SHA256 和恒定时间比较；事件头须匹配已签名信封。投递 ID 用于诊断，不作为事件去重权威。

## 回执与重复消费

首次接收按原事件 ID 保存企业、事件类型、正文摘要、原投递 ID、接收时间及实例记录结果。文件追加并 flush 成功后返回 200；相同事件/正文再次收到，返回原消费结果及 `duplicate: true`，不同投递 ID 不产生第二条结果。相同事件 ID 携带不同正文或来源返回 409，原回执不变。正文、业务引用和签名秘密不写入账本。

样例只在单进程本地账本中记录工作流实例事实。账本的 `.lock` 文件防止两个进程同时写入，普通关闭会释放；异常退出留下锁时，先确认原进程已结束、保存并核对账本后再人工处理锁。半条或损坏记录拒绝启动，不自动截断、清空或跳过。部署时保留同一账本文件及访问权限，不能通过换一个空路径恢复服务并遗忘去重事实。

默认正文最多 256 KiB，账本最多 10,000 条或 8 MiB，每分钟最多 60 次请求，最多 16 个在途处理、64 个连接；正文接收有超时，分块上传同样受限。达到账本上限后新事件返回 503，已有重复仍返回原结果，不自动删除历史。这里不提供多实例数据库、账本轮转、独立外部副作用或断电之外的存储保证；把样例扩展为真实业务时，将 Inbox 唯一键与业务写入放入自己的数据库事务，外部副作用走可靠事件或幂等接口。

当前签名协议没有独立的签名投递时间头，`occurredAtUtc` 是业务发生时间。样例不使用短时间窗拒绝合法的延迟重试，也不声称提供签名新鲜度；保存的事件回执负责重复识别。协议时间窗及轮换能力继续按 F13-R2 演进。

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

需要撤销接收能力时，先在所属企业明确暂停原订阅，再停止接收进程并撤回秘密文件访问；已领取或已发出的请求可能仍在途。恢复或更换接收设置前核对原事件与历史，不复制旧任务成新业务事件。当前订阅没有原地密钥轮换端点；更换密钥须由订阅所有者明确创建并启用相匹配的新配置，原订阅及历史不自动迁移。接收程序若需要回调平台 API，应另外配置最小权限凭据并通过其所属管理入口撤销，本样例不持有平台访问令牌。

## 验证脚本

```bash
node samples/webhook-consumer/verify-signature.mjs \
  --secret-file /secure/webhook-signing-secret \
  --payload-file /data/original-request-body.json \
  --signature <header-value>
```

签名算法：HMAC-SHA256(payload, secret) 十六进制大写，与 `X-FullNet-Signature` 请求头比对。

运行本样例的定向验证：`pnpm test:webhook-consumer`。测试使用真实本地 HTTP 和独立系统临时账本，不连接平台后端或外部服务。
