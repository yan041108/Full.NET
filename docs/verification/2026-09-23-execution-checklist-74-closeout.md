# 执行清单 74 — 只读 Agent Tool / MCP 静态目录与工具调用审计页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **74**（依赖 **72** 与安全边界；`GET /api/v1/ai/agent-tools` 静态目录 + `mcpExposureKey`；`GET /api/v1/ai/agent-tool-calls` 审计；`AiAgentToolsView` 目录/审计页签；`McpExposurePolicy`；**不**含写工具人审、**不**声称 MCP 远端连接配置为本槽主交付（`AiMcpRemoteConnectionsView` 另面））。

## 选定切片

| 项 | 值 |
|----|-----|
| 目录 API | `aiListAgentTools` / `aiGetAgentTool`（`AiAgentToolCatalog` + `AiAgentToolCatalogService`） |
| 审计 API | `aiListAgentToolCalls`（`AiAgentToolCallQueryService`，租户/工具/状态筛选） |
| MCP 元数据 | 目录项 `mcpExposureKey`（`exposed` / `internal` / `planned`）；`McpExposurePolicyTests` |
| 安全 | `AiToolExecutionSecurityTests`（预算/输出上限、取消不派发）；不转发调用者 Header 回环（独立安全 Spec + 单测约束） |
| 代表工具 | `ai.chat.sessions.list`（`read`）；`ai.chat.sessions.rename`（`write` + internal MCP） |
| Vue | `AiAgentToolsView`（目录表 + 审计表 + 筛选） |
| 权限 | `ai.tools.catalog.read`；`ai.tools.calls.read` |

## 交付锚点

| 层 | 位置 |
|----|------|
| 端点 | `Features/ManageAgentTools/Endpoint.cs` |
| 目录 | `Domain/AiAgentToolCatalog.cs`；`AgentToolRegistryFactory` |
| 审计持久化 | `AiToolAuditPersistenceTests`；`AiAgentToolAuditPolicyTests` |
| MCP 策略 | `McpExposurePolicy`；`McpRemoteCapabilityPolicyTests`（暴露边界） |
| 只读连接列表 | `GET /api/v1/ai/mcp/remote-connections`（E2E 契约探测，非本槽 UI 主证据） |
| Vitest | `AiAgentToolsView.test.ts` |

## 清单 74 验收结论

- **已有**：静态目录不可客户端扩展；调用审计分页；写副作用工具默认不 MCP 对外暴露。
- **本槽**：`phase-c-74-ai-agent-tools-audit.spec.mjs`；`ai-real-stack.mjs` Agent Tool 辅助。
- **未验**：Agent Run 执行写工具、MCP discover/approve 全流程（`AiAgentRunsView` / `AiMcpRemoteConnectionsView` 另编号）。

## 停止边界

- **75**：Payments 商户/渠道（与 AI 无耦合）。
- 写工具人工审批、Agent Run 恢复、MCP 连接 CRUD UI 不在本槽。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`AiAgentTool|AiToolAudit|McpExposure|AiToolVisible` | **22/22**（`d40de4e6`） |
| `pnpm exec vitest run` `AiAgentToolsView.test.ts` | **1/1** |
| OpenAPI | `aiListAgentTools` / `aiListAgentToolCalls` 登记 |
| real-stack | `phase-c-74-ai-agent-tools-audit.spec.mjs`（需 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
