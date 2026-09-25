# 执行清单 76 — 微信支付回调幂等/对账、单笔退款与状态管理页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **76**（依赖 **75**）；`wechat-native/notify` 匿名入口与签名字段门禁；`PaymentNotifyReceiptSql` / `PaymentWeChatNotifyService` 金额与商户绑定；订单 `reconcile`；`POST …/orders/{id}/refunds` + 退款列表/详情；`PaymentOrdersView` 对账/退款操作 + `PaymentRefundsView`；**禁止** real-stack 真实扣款或带有效签名的 live notify。

## 选定切片

| 项 | 值 |
|----|-----|
| 回调 | `POST /api/v1/payments/wechat-native/notify/{merchantConfigId}`（`AllowAnonymous`；缺 `Wechatpay-*` 头 → `FAIL` + HTTP 500 保留重试语义） |
| 幂等/绑定 | `PaymentWeChatNotifyService` + `PaymentNotifyReceiptSql`；单元 `PaymentNotifyBindingTests` |
| 对账 | `POST /api/v1/payments/orders/{orderId}/reconcile`；`PaymentOrderReconciliationService`；`payments.orders.reconcile` |
| 退款 | `POST …/orders/{orderId}/refunds`；`GET /api/v1/payments/refunds`；`payments.refunds.read` / `.create`；UI 创建入口在订单行（`payments.orders.refund`） |
| Vue | `PaymentOrdersView`（`payment-order-reconcile-*` / `payment-order-refund-*`）；`PaymentRefundsView`（列表） |
| 微信客户端 | `WeChatNativePayClient` 退款/查询路径由服务层调用；E2E 不对接生产微信 |

## 交付锚点

| 层 | 位置 |
|----|------|
| Notify | `Features/ReceiveWeChatNotify/Endpoint.cs`；`PaymentWeChatNotifyService` |
| 对账 | `PaymentOrderReconciliationService` |
| 退款 | `PaymentRefundManagementService`；`ManageRefunds/Endpoint.cs` |
| 签名/应答 | `WeChatPaySignatureVerifier`；`WeChatNotifyHttpAcknowledgementTests` |
| 单元 | `PaymentNotifyBindingTests`；Payments 过滤器合计 **41/41**（含 notify/签名子集 **20/20** 专项过滤器） |
| Vitest | `PaymentRefundsView.test.ts` |

## 清单 76 验收结论

- **已有**：回调收据表与重复通知语义；对账与退款状态机；管理端列表与订单行操作壳。
- **本槽**：`phase-c-76-payments-notify-reconcile-refunds.spec.mjs`；扩展 `payments-real-stack.mjs`（notify / reconcile / refunds API）。
- **未验**：带真实商户密钥的 signed notify 端到端、微信侧退款 API 联调；支付宝见 **77** closeout。

## 停止边界

- **77**：`alipay_page` 下单/查询与页面渠道验收（仍依赖 **75** 订单边界）。
- 禁止在 CI/real-stack 触发真实资金扣款或成功退款。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~Payments` | **41/41**（`d40de4e6`） |
| `dotnet test` …`PaymentNotify\|WeChatNotify\|WeChatPay` | **20/20** |
| `pnpm exec vitest run` `PaymentRefundsView.test.ts` | **1/1** |
| OpenAPI | `paymentsWeChatNativeNotify` / `paymentsReconcileOrder` / `paymentsCreateRefund` / `paymentsListRefunds` |
| real-stack | `phase-c-76-payments-notify-reconcile-refunds.spec.mjs`（需 Host + 租户） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
