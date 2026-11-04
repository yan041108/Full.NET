# Webhook 签名 v1

本规范按现行产品化计划冻结投递时间、密钥与事件身份的签名边界。当前开发底座直接升级发送器、接收样例及 SDK；接收方配置公开 KeyId 和对应秘密后再启用订阅。

事件正文保持 PascalCase 与原 UTF-8 字节，业务发生时间 `OccurredAtUtc` 保留原事实。每次实际外发生成新的 Unix 秒投递时间，重试和人工重放保留原 EventId/DeliveryId。

| HTTP 头 | 规范 |
| --- | --- |
| X-FullNet-Signature-Version | 固定 `1` |
| X-FullNet-Key-Id | 当前签名密钥的公开 UUID；标准 D 格式 |
| X-FullNet-Timestamp | 非负 Unix 秒，十进制且无前导零 |
| X-FullNet-Event-Id | 原业务事件 UUID |
| X-FullNet-Delivery-Id | 原投递 UUID |
| X-FullNet-Event-Type | 原事件键，1～128 个可打印 ASCII 字符 |
| X-FullNet-Signature | HMAC-SHA256 的 64 位十六进制摘要 |

对原始请求体字节计算 SHA-256，转为大写十六进制。以下字段依次以单个 LF 连接，末尾不加换行；UUID 统一为小写 D 格式，时间为规范十进制：

```text
fullnet.webhook/1
<timestamp>
<keyId>
<eventId>
<deliveryId>
<eventType>
<payloadSha256UpperHex>
```

将此规范字符串按无 BOM UTF-8 编码，使用原秘密的 UTF-8 字节计算 HMAC-SHA256。发送器输出大写签名，接收方按字节进行恒定时间验证，十六进制大小写均可读取；不能解析正文再序列化后验签，也不能遗漏元数据。

接收方只接受受保护配置中明确允许的 KeyId，并据此解析秘密，不把未认证的 KeyId 当作远程地址。默认过去/未来容差均为 300 秒，可由受信策略设置 1～3600 秒；独立读取秘密和加密验证后再次检查时间，避免等待跨越窗口。签名未知版本、缺少/重复头、无效身份、秘密不可取得、超窗或不匹配均不能确认消费。系统保持可靠时间同步，业务发生时间不用于替代投递时间。

验签通过后仍须核对正文企业、事件键和事件身份；以原事件身份在接收方事务 Inbox 去重并与业务写入同提交。同事件不同正文拒绝，当前时间窗内的重复请求仍可能出现；重试更新投递时间与签名，不能因此变成新业务事件。旧签名不能延长到窗口以外，SDK 和样例不自动遗忘去重回执。

订阅公开 SigningKeyId；初始身份复用应用端生成的订阅 UUID，历史迁移只回填缺失身份，不改秘密或投递事实。明确密钥轮换另按 F13-R2 后续批次交付，接收方按自己的受信策略接续允许的 KeyId；本规范不隐式启用轮换或保留无期限旧密钥。

跨语言固定测试向量与原文位于 `contracts/webhooks/signature-v1.vector.json`。向量秘密为公开测试数据，生产使用独立受保护随机秘密。发送器、接收样例和安装包均核对同一向量。
