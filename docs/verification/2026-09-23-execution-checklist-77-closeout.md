# 执行清单 77 — 支付宝单一交易模式与支付/查询页面 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **77**（依赖 **75** 通用订单边界）；`alipay_page` 电脑网站支付（`AlipayPagePayClient`：`alipay.trade.page.pay` / `alipay.trade.query`）；商户 `returnUrl` 元数据；订单创建与对账查询复用 **75/76** 端点；`PaymentMerchantConfigsView` / `PaymentOrdersView` 渠道选项；**无**支付宝异步通知入口、**无**转账（`alipay.fund.trans`）权限或实现。

## 选定切片

| 项 | 值 |
|----|-----|
| 渠道键 | `PaymentChannelKeys.AlipayPage`（`alipay_page`） |
| 下单 | `PaymentOrderManagementService.InvokeProviderAsync` → `CreatePagePayUrlAsync`（本地签名拼网关 URL；`CodeUrl` 字段承载跳转链接） |
| 查询 | `PaymentOrderReconciliationService` → `QueryTradeByOutTradeNoAsync`（对账 API 与微信共用） |
| 商户校验 | `ValidateAlipayMetadata`；可选商户号/证书序列号占位 `-`；`returnUrl` 必填 https |
| 凭据 | 创建仅需 RSA 私钥 PEM（无 API v3 密钥）；`AlipaySigner` |
| Vue | 商户编辑：支付宝隐藏微信专有字段、展示同步跳转；订单创建渠道含「支付宝网页支付」 |
| 转账 | 代码库无 Payments 转账 Provider；不随本槽授权 |

## 交付锚点

| 层 | 位置 |
|----|------|
| 客户端 | `Connectivity/AlipayPagePayClient.cs`；`IAlipayPagePayClient` |
| 签名 | `Domain/AlipaySigner.cs` |
| 校验 | `PaymentMerchantFieldValidator`（`ValidateMetadata_accepts_alipay_page_*`） |
| 单元 | `AlipaySignerTests`；`PaymentMerchantFieldValidatorTests`；Payments 合计仍 **41/41** |
| Vitest | `PaymentMerchantConfigsView.test.ts`；`PaymentOrdersView.test.ts`（渠道选项由视图绑定，无新增用例） |

## 清单 77 验收结论

- **已有**：Page Pay URL 构建与 trade.query 解析；双渠道白名单与 UI 分渠道表单。
- **本槽**：`phase-c-77-payments-alipay-page.spec.mjs`；`payments-real-stack.mjs`（`paymentAlipayPageChannelKey` / `buildAlipayPageMerchantConfigBody`）。
- **未验**：真实 `openapi.alipay.com` 联调、支付宝 notify 验签回调；GoView 见 **78** closeout。

## 停止边界

- **78**：GoView 大屏项目保存/发布/预览（独立 Client）。
- 禁止 real-stack 打开 live 网关 URL 完成真实支付；转账另立资金安全切片（清单 PY02）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~Payments` | **41/41**（`d40de4e6`） |
| `dotnet test` …`AlipaySigner\|PaymentMerchantFieldValidator` | **8/8** |
| `pnpm exec vitest run` `PaymentMerchantConfigsView` + `PaymentOrdersView` | **2/2**（沿用 75） |
| OpenAPI | `paymentsCreateOrder` + 商户 CRUD（`alipay_page` 由校验层约束） |
| real-stack | `phase-c-77-payments-alipay-page.spec.mjs`（需 Host + 租户） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
