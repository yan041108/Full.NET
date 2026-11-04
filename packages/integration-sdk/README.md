# Full.NET TypeScript 接入包

当前交付企业 Webhook 的八个管理操作、生成模型/响应守卫及工作流完成事件验签。包使用标准 fetch/Web Crypto，零运行时依赖，独立于 Vue 管理端和内部实现；支持 Node.js 24。源码许可证为 MIT，目前仅本地打包，`private: true` 保留公共发布门禁。

## 本地构建和安装

在仓库按开发入门准备 Node/pnpm 工作区后：

```powershell
pnpm --filter @fullnet/integration-sdk build
pnpm --dir packages/integration-sdk pack --pack-destination C:\FullNetPackages
```

在独立消费项目安装生成的 `.tgz`，例如 `npm install C:\FullNetPackages\fullnet-integration-sdk-0.1.0.tgz`。包包含 ESM JavaScript、TypeScript 声明、README 和 LICENSE，不要求消费项目引用仓库源码或安装其他 Full.NET 包。生成输入是原模块 OpenAPI 与事件 Schema；`pnpm --filter @fullnet/integration-sdk check:generated` 核对生成漂移。

## 授权调用

```ts
import { createIntegrationClient, webhooksListSubscriptions } from '@fullnet/integration-sdk';

const tenantId = process.env.FULLNET_TENANT_ID!;
const client = createIntegrationClient({
  baseUrl: 'https://api.example.com',
  tenantId,
  locale: 'zh-CN',
  getCredentials: async signal => {
    // 从同一次身份结果读取完整快照；调用方负责凭据的保护、刷新和撤回。
    const current = await credentialsProvider.read(signal);
    return { accessToken: current.accessToken, tenantId: current.tenantId };
  }
});
const subscriptions = await webhooksListSubscriptions(client, {});
```

`credentialsProvider` 是消费项目自己的身份来源。每次实际请求重新获取一份令牌/企业快照，企业已变化时在发送前拒绝；异步提供器和读取、退避共用请求期限。`tenantId` 用于本地作用域和结果一致性核对，不作为 HTTP 企业选择参数或服务端授权。API 按可信 Bearer 上下文核证企业和精确权限；调用方应在新的身份作用域中建立新 client，不能把分别读取的新令牌、旧企业拼成快照。包不保存凭据，不附带 Cookie，不自动刷新令牌，跨源重定向拒绝。

默认总体超时 15 秒、响应预算 4 MiB；可通过 `timeoutMilliseconds`、`maxResponseBytes` 设置有界预算。生产基址使用 HTTPS 原点，不携带账号、密码、查询、片段或路径前缀。本机测试可显式 `allowHttpLocalhost: true`；该选项仅允许 localhost/回环地址。

## 错误、重试与重放

```ts
import { IntegrationHttpError, webhooksReplayDelivery } from '@fullnet/integration-sdk';

// 在操作者确认原失败投递与修订后生成；未知结果须持久保留同一 operationId。
const operation = { operationId: crypto.randomUUID(), version: failedDelivery.version };
try {
  const receipt = await webhooksReplayDelivery(client, {
    subscriptionId: failedDelivery.subscriptionId,
    deliveryId: failedDelivery.id,
    body: operation
  });
  // receipt 是原排队事实，后续手动读取投递以核对最终状态。
} catch (error) {
  if (error instanceof IntegrationHttpError) {
    report(error.problem.status, error.problem.code);
  }
  // 读取原回执，或由操作者明确沿用 operation 重试，不自动换键再发送。
}
```

生成操作还包括创建/列表、暂停恢复、历史分页/详情、逐次尝试和原回执查询。重放仍要求平台交互会话及独立重放权限，SDK 不提供授权旁路。完整步骤和权限见仓库开发入门 §15。

GET 对网络故障及 429/502/503/504 默认最多重试两次，可设 `maxGetRetries: 0..3`；遵守有界 Retry-After/退避，取消或总体超时立即停止。所有 POST（含创建、状态变更及重放）均不自动重试，401/403/404 不重试；凭据撤销须经所属身份入口处理，调用方重新授权后再明确发起请求。`IntegrationHttpError.problem` 保留有界状态、机器码和排错字段，`IntegrationProtocolError` 表示作用域、正文或协议不符。SDK 不自动记录正文或凭据。

## 接收事件

```ts
import { verifyWorkflowCompletedWebhook } from '@fullnet/integration-sdk';

const event = await verifyWorkflowCompletedWebhook({
  body: rawRequestBytes, // 原始 UTF-8 字节，不能解析后重新序列化。
  signature: headers.get('X-FullNet-Signature')!,
  version: headers.get('X-FullNet-Signature-Version')!,
  keyId: headers.get('X-FullNet-Key-Id')!,
  timestamp: headers.get('X-FullNet-Timestamp')!,
  eventId: headers.get('X-FullNet-Event-Id')!,
  deliveryId: headers.get('X-FullNet-Delivery-Id')!,
  eventType: headers.get('X-FullNet-Event-Type')!,
  expectedTenantId: tenantId,
  resolveSigningSecret: async (keyId, signal) => protectedKeys.readAllowedKey(keyId, signal)
});
// 在接收方事务内以 event.EventId 建立 Inbox，并与业务写入同提交。
```

验签入口最多接受 256 KiB，按仓库 [签名 v1](../../docs/development/webhook-protocol.md) 验证原文字节摘要、KeyId、投递时间及事件元数据，再核对 PascalCase 模型与企业。`protectedKeys` 是接收方自己的受保护密钥来源，只解析预先允许的 KeyId，未知键返回 null；不得使用请求头拼接远程地址。默认过去/未来容差均为 300 秒，可用 `maxAgeSeconds: 1..3600` 配置；`clock` 返回可信 Unix 毫秒。读取秘密与加密操作共用默认 5 秒期限（`timeoutMilliseconds: 1..30000`），支持 `signal` 取消，等待后再次核对时间。密钥来源故障或超时不确认消费。`OccurredAtUtc` 保留业务发生时间，延迟重试使用新的投递时间。接收方仍须以原 EventId 去重并与业务同事务；密钥轮换尚待后续批次交付。

## 定向验证

`pnpm test:integration-sdk` 构建并运行包自身的定向协议测试。`pnpm test:integration-sdk:package` 在完成开发入门的后端构建、Docker/SQL Server 准备后，定向编译测试程序集、本地 pack、离线安装到系统临时独立项目并严格编译外部 TypeScript；随后通过真实 Kestrel 验证授权创建/查询、跨企业写入拒绝、生产事件验签和正式会话撤销。测试使用自己的临时账本/包目录，成功后清理，不公开发布包或访问客户地址。
