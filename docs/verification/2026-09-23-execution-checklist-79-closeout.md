# 执行清单 79 — K3Cloud 首个固定单据认证/提交适配与同步状态页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **79**；Host 域连接配置 + 单据同步；`ValidateUser` 连通性（`POST …/test`）；首切片单据 `k3cloud.sal_sale_order` → `SAL_SaleOrder` 的 **Save + Submit**（`IK3CloudWebApiClient` 受控方法，**无** Audit、**无**万能远程调用）；`K3CloudConnectionConfigsView` / `K3CloudDocumentSyncsView`；real-stack **不**对接真实金蝶环境。

## 选定切片

| 项 | 值 |
|----|-----|
| 单据 | `K3CloudDocumentTypeKeys.SalSaleOrder`；`K3CloudDocumentTypeCatalog` 白名单 |
| 同步状态 | `pending` → `save_succeeded` / `save_failed` / `submitted` / `submit_failed` / `provider_unknown` |
| 连接 API | `GET/POST/PUT /api/v1/k3cloud/connection-configs`；`POST …/test` |
| 同步 API | `GET/POST /api/v1/k3cloud/document-syncs`；`POST …/retry` |
| 事务边界 | 意图先提交本地，再事务外 `SaveDocumentAsync` / `SubmitDocumentAsync` |
| 凭据 | `K3CloudPasswordProtector`；响应 `HasPassword`，不回显密码 |
| 权限 | `k3cloud.connections.*`；`k3cloud.document_syncs.read` / `.create` / `.retry` |

## 交付锚点

| 层 | 位置 |
|----|------|
| 客户端 | `K3CloudWebApiClient`；`IK3CloudWebApiClient` |
| 服务 | `K3CloudConnectionOperationsService`；`K3CloudDocumentSyncService` |
| 解析 | `K3CloudResponseParser` |
| 单元 | `K3CloudDocumentTypeCatalogTests`；`K3CloudDocumentSyncSideEffectTests`；`K3CloudResponseParserTests` 等 **17/17** |
| Vitest | `K3CloudConnectionConfigsView.test.ts`；`K3CloudDocumentSyncsView.test.ts` |

## 清单 79 验收结论

- **已有**：销售订单 Save/Submit 流水线；同步列表与重试；连接测试入口。
- **本槽**：`phase-c-79-k3cloud-connection-document-sync.spec.mjs`；`k3cloud-real-stack.mjs`。
- **未验**：真实金蝶 Save/Submit 绿路径、Audit 步骤、第二单据类型；OCR 见 **80** closeout。

## 停止边界

- **80**：OCR 身份证 Provider + 受控上传/人工确认。
- 禁止暴露任意 FormId/方法名的通用 K3 代理 Endpoint。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~K3Cloud` | **17/17**（`d40de4e6`） |
| `pnpm exec vitest run` `K3CloudConnectionConfigsView` + `K3CloudDocumentSyncsView` | **2/2** |
| OpenAPI | `k3cloudTestConnectionConfig` / `k3cloudCreateDocumentSync` / `k3cloudRetryDocumentSync` |
| real-stack | `phase-c-79-k3cloud-connection-document-sync.spec.mjs`（需 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
