# 执行清单 75 — Payments 商户/渠道配置与支付单创建/查询页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **75**（独立资金安全设计；**首切片 `wechat_native`** 下单与查询；`PaymentMerchantSecretProtector`；商户列表脱敏；`PaymentMerchantConfigsView` + `PaymentOrdersView`；**无**回调幂等/退款/对账 UI **76**、禁止真实自动扣款测试）。

## 选定切片

| 项 | 值 |
|----|-----|
| 渠道 | `PaymentChannelKeys.WeChatNative`（清单首选；代码亦含 `alipay_page` 白名单，本槽证据以微信 Native 为主） |
| 商户 API | `GET/POST/PUT /api/v1/payments/merchant-configs`；`…/disable` |
| 订单 API | `GET/POST /api/v1/payments/orders`（租户上下文 + 金额 `AmountMinor` / `CNY`） |
| 凭据 | 创建必填 `ApiV3Key` + `PrivateKeyPem`（微信）；更新可空表示保留 |
| 脱敏 | `PaymentMerchantMasking`；`HasApiV3Key` / `HasPrivateKey` |
| Vue | `PaymentMerchantConfigsView`；`PaymentOrdersView`（创建对话框、列表查询） |
| 权限 | `payments.merchant_configs.*`；`payments.orders.read` / `.create` |

## 交付锚点

| 层 | 位置 |
|----|------|
| 服务 | `PaymentMerchantConfigManagementService`；`PaymentOrderManagementService` |
| 校验 | `PaymentMerchantFieldValidator`；`PaymentBusinessNumberGenerator` |
| 微信签名 | `WeChatPaySigner` / `WeChatPaySignatureVerifier`（单元，非本槽 E2E 扣款） |
| 契约 | `PaymentMerchantConfigContracts`；`PaymentOrderContracts` |
| 单元 | `PaymentMerchantMaskingTests`、`PaymentMerchantFieldValidatorTests`、`PaymentsAuthorizationContributorTests` 等 **41** 项 |
| Vitest | `PaymentMerchantConfigsView.test.ts`；`PaymentOrdersView.test.ts` |

## 清单 75 验收结论

- **已有**：商户默认作用域（Host/租户）；订单创建绑定活跃租户与可用商户配置。
- **本槽**：`phase-c-75-payments-merchant-orders.spec.mjs`；`payments-real-stack.mjs`。
- **未验**：微信预下单真实 `code_url`、回调 notify、对账/退款（**76**）；支付宝页渠道仅代码白名单，非本槽验收面。

## 停止边界

- **76**：`wechat-native/notify` 幂等、对账、`PaymentRefundsView`、单笔退款。
- 禁止在 CI/real-stack 触发真实资金扣款。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~Payments` | **41/41**（`d40de4e6`） |
| `pnpm exec vitest run` `PaymentMerchantConfigsView` + `PaymentOrdersView` | **2/2** |
| OpenAPI | `paymentsListMerchantConfigs` / `paymentsCreateOrder` 等 |
| real-stack | `phase-c-75-payments-merchant-orders.spec.mjs`（需 Host + 租户） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
