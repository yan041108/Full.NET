# Full.NET TypeScript 接入包

当前交付企业 Webhook 的十三个管理操作、生成模型/响应守卫及工作流完成事件验签，包含准备、读取、明确激活和取消密钥轮换。包使用标准 fetch/Web Crypto，零运行时依赖，独立于 Vue 管理端和内部实现；支持 Node.js 24。源码许可证为 MIT，目前仅本地打包，`private: true` 保留公共发布门禁。

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

生成操作还包括创建/列表、暂停恢复、历史分页/详情、逐次尝试和原回执查询。重放仍要求平台交互会话及独立重放权限，SDK 不提供授权旁路。密钥轮换使用 `webhooksPrepareSigningKeyRotation`、`webhooksGetPendingSigningKeyRotation`、`webhooksGetSigningKeyRotation`、`webhooksActivateSigningKeyRotation`、`webhooksCancelSigningKeyRotation`：先固定准备操作及原修订，接收端配置返回的新 KeyId/秘密与旧键截止时间，再由操作者明确激活。所有 POST 均不重试；未知激活结果须保留原操作、KeyId 和确认修订后读取或明确重试，不能取后来修订当作原确认。取消清除准备秘密，原活动密钥不变。完整步骤和权限见仓库开发入门 §15 及签名协议。

恢复期间经权威回执确认接收方已提交的待发送投递，可由有独立对账权限的租户操作员调用 `webhooksConfirmRecoveredDelivery`。固定原版本、事件标识、载荷与证据摘要，并保留原操作标识以便明确重试。证据核对及 Worker 隔离步骤见仓库恢复手册。

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

验签入口最多接受 256 KiB，按仓库 [签名 v1](../../docs/development/webhook-protocol.md) 验证原文字节摘要、KeyId、投递时间及事件元数据，再核对 PascalCase 模型与企业。`protectedKeys` 是接收方自己的受保护密钥来源，只解析预先允许的 KeyId，未知键返回 null；不得使用请求头拼接远程地址。默认过去/未来容差均为 300 秒，可用 `maxAgeSeconds: 1..3600` 配置；`clock` 返回可信 Unix 毫秒。读取秘密与加密操作共用默认 5 秒期限（`timeoutMilliseconds: 1..30000`），支持 `signal` 取消，等待后再次核对时间。密钥来源故障或超时不确认消费。`OccurredAtUtc` 保留业务发生时间，延迟重试使用新的投递时间。接收方仍须以原 EventId 去重并与业务同事务；密钥来源须按受信旧键截止时间停止返回其秘密，期限不能来自请求；原事件去重事实不随换键删除。

## 定向验证

`pnpm test:integration-sdk` 构建并运行包自身的定向协议测试。`pnpm test:integration-sdk:package` 在完成开发入门的后端构建、Node.js 24、Docker/SQL Server/MySQL/Redis 准备后，定向编译本场景 Worker 与测试程序集，本地 pack、离线安装到系统临时独立项目并严格编译外部 TypeScript。两个提供程序分别通过真实 Kestrel 验证授权创建、全部轮换操作、跨企业拒绝及会话撤销；业务旅程实际发布企业申请审批定义、创建/提交并办理，由独立 Worker 的正式 Outbox/签名/投递循环发送至 Node 接收端，安装包核对所捕获原始报文与业务身份，再验证持久回执去重。

验收仅在测试夹具中把命名 HTTP 传输转发到本机真实接收端，保留原公开目标检查、队列、签名与落账；没有放行生产内网目标或宣称公网交付。入队提交后故意丢失一次确认，真实 Outbox 重试仍只产生一条投递/消费结果。接收程序仅复制样例的三个有限源文件，消费 SDK 不依赖仓库内部运行路径；各提供程序独立目录，共享各自 API/Worker 密钥环。临时账本/包目录成功后正常清理，不公开发布包或访问客户地址。
