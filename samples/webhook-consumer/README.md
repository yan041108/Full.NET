# Webhook Consumer 样例

演示如何验证 Full.NET Webhook 投递签名。

## 验证脚本

```bash
node samples/webhook-consumer/verify-signature.mjs \
  --secret your-signing-secret \
  --payload '{"eventId":"00000000-0000-7000-8000-000000000001"}' \
  --signature <header-value>
```

签名算法：HMAC-SHA256(payload, secret) 十六进制大写，与 `X-FullNet-Signature` 请求头比对。
