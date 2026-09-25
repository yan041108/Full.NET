# 执行清单 72 — AI 模型配置 / 连通性 / 额度 API 与管理页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **72**（`AiProviderKeys` 白名单；`AiApiKeyProtector` 保护密钥；列表端点脱敏；`POST …/test` 连通性；`tenant-quotas` CRUD；`AiModelConfigsView`；**无**聊天流式 **73**、无虚假通用供应商）。

## 选定切片

| 项 | 值 |
|----|-----|
| 提供程序 | `openai_compatible`、`ollama`、`azure_openai` 等已注册键（非任意字符串） |
| 模型 API | `/api/v1/ai/model-configs` CRUD + `disable` + `test`（`test-embeddings` 另路径） |
| 配额 API | `GET/PUT /api/v1/ai/tenant-quotas`（Host 管理租户月度 token/request 上限） |
| 脱敏 | `AiModelConfigMasking.MaskEndpoint`；`HasApiKey`；详情无密钥回显 |
| 校验 | `AiModelConfigFieldValidator`（端点禁止内嵌凭据）；`AiProviderEndpointPolicy` |
| Vue | `AiModelConfigsView`（模型表 + 配额表、测试连接、创建/编辑） |
| 权限 | `ai.model_configs.*`；`ai.tenant_quotas.read` / `.update` |

## 交付锚点

| 层 | 位置 |
|----|------|
| 服务 | `AiModelConfigManagementService`；`AiModelConfigQueryService`；`AiModelConnectivityTester` |
| 配额 | `AiTenantQuotaManagementService`；`AiTenantQuotaQueryService` |
| 安全 | `AiApiKeyProtector`；`AiCredentialBoundaryTests` |
| 契约 | `AiModelConfigContracts.cs`；OpenAPI `AiModelConfigs` / `AiTenantQuotas` |
| 单元 | `AiModelConfigMaskingTests`、`AiModelConfigFieldValidatorTests`、`AiModelConnectivityTesterTests`、`AiTenantQuotaTransactionTests` 等 |
| Vitest | `AiModelConfigsView.test.ts` |

## 清单 72 验收结论

- **已有**：Host/租户作用域模型行；测试写回 `LastTestStatusKey`；配额乐观并发 `version`。
- **本槽**：`phase-c-72-ai-model-configs.spec.mjs`；`ai-real-stack.mjs`。
- **未验**：real-stack 对外部 LLM 的成功 `test`（需运维密钥与网络）；聊天/Agent/MCP 见 **73+** 与独立编号。

## 停止边界

- **73**：`AiChatView` 流式会话、取消、历史；用量计费与敏感信息策略。
- Agent 工具、MCP 远程连接、Agent Runs 不在本槽。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`AiModelConfig|AiTenantQuota|AiModelConnectivity|AiCredential` | **40/40**（`d40de4e6`） |
| `pnpm exec vitest run` `AiModelConfigsView.test.ts` | **1/1** |
| OpenAPI | `OpenApiOperationIdentityRulesTests` 登记 model-configs / tenant-quotas |
| real-stack | `phase-c-72-ai-model-configs.spec.mjs`（需 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
