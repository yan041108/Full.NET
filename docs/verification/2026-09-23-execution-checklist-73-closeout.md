# 执行清单 73 — AI 单一聊天流程（流式 / 取消 / 历史）与 Vue 对话页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **73**（依赖 **72**；`GET/POST/PUT/DELETE /api/v1/ai/chat/sessions`；`POST …/messages/stream`（SSE）；`POST …/cancel`；`AiChatView` + `streamAiChatMessage`；`AiChatScope` Host/租户隔离；配额预留在流式路径消费 **72** 配额；**无** Agent Tool 目录 **74**）。

## 选定切片

| 项 | 值 |
|----|-----|
| 会话 API | 列表/详情（含 `messages` 历史）、创建、重命名、删除 |
| 流式 | `AiChatStreamService` → `text/event-stream`（`AiChatHttpOutput` / `AiChatSseWriter`） |
| 取消 | `AiChatGenerationRegistry` + `RequestCancellation` SQL |
| 策略 | `AiChatContentPolicy`（标题/内容边界）；失败恢复与 ProblemDetails |
| 预算 | `AiOperationBudgetStore` / `AiChatQuotaReservationTests`（与租户配额联动） |
| Vue | `AiChatView`（会话侧栏、composer、流式追加、取消、错误展示） |
| 权限 | `ai.chat.sessions.read` / `.create` / `.update` / `.delete` / `.send` / `.cancel` |

## 交付锚点

| 层 | 位置 |
|----|------|
| 端点 | `Features/ManageChatSessions/Endpoint.cs` |
| 服务 | `AiChatSessionManagementService`；`AiChatSessionQueryService`；`AiChatStreamService` |
| 流 | `AiChatCompletionStreamer`；`AiChatGenerationLifecycleTests` |
| 前端 API | `api/ai-chat.ts` |
| 契约 | OpenAPI `aiStreamChatMessage` / `aiCancelChatGeneration` |
| 单元 | `AiChat*` 测试族（**38** 项 `FullyQualifiedName~AiChat`） |
| Vitest | `AiChatView.test.ts`（权限壳 + HTTP 403/409/422 错误恢复） |

## 清单 73 验收结论

- **已有**：单会话单活跃生成；取消协作式中止；Host 聊天 `TenantId=null`。
- **本槽**：`phase-c-73-ai-chat.spec.mjs`；`ai-real-stack.mjs` 聊天辅助。
- **未验**：real-stack 完整 SSE 绿路径（需 **72** 可用模型与外部推理）；租户聊天需 `enterDevelopmentTenant` 另测。

## 停止边界

- **74**：静态 Agent Tool 目录、`agent-tool-calls` 审计、`AiAgentToolsView`；MCP 远程连接另编号。
- 多 Agent 运行、AG-UI、审批不在本槽。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~AiChat` | **38/38**（`d40de4e6`） |
| `pnpm exec vitest run` `AiChatView.test.ts` | **4/4** |
| OpenAPI | `OpenApiOperationIdentityRulesTests` 登记 chat sessions 路径 |
| real-stack | `phase-c-73-ai-chat.spec.mjs`（需 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
