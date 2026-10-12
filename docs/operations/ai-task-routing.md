# AI 受控任务路由

首版使用调用方明确选择的封闭意图，不执行模型意图分类。每次请求只进入一个现有执行边界，没有并行派发或失败后自动切换任务。

| POST 入口 | `IntentKey` | 必需载荷 | 权限与执行 |
| --- | --- | --- | --- |
| `/api/v1/ai/tasks/knowledge-ask` | `knowledge_ask` | `SessionId`、`Knowledge`，省略 `ToolTask` | `ai.chat.messages.send` 及既有知识读取权限集合；复用公开 Ask 的会话归属、完整来源复核、槽位、三分钟总时限和收费预算 |
| `/api/v1/ai/tasks/authorized-tool-task` | `authorized_tool_task` | `ToolTask`，省略 `SessionId`、`Knowledge` | `ai.agent_runs.create`；仅交互会话，权威用户/会话/租户必须与冻结绑定一致；返回既有 `202` 运行标识和 Location |

`Knowledge` 使用 `AskAiKnowledgeRequest`，生成模型固定为会话绑定。`ToolTask` 使用 `CreateAiAgentRunRequest`，本版仅支持 `fullnet-chat-rename-workflow-v1` 的结构化校验版本 2，不能指定 Prompt 版本、纯文本定义、只读远程工具循环或其他定义。该工作流按顺序执行，工具集合仅为 `ai.chat.sessions.list` 和 `ai.chat.sessions.rename`；知识问答路由不获得工具权限。

未知意图、非规范大小写、混合载荷和意图与入口不一致返回 `400 ProblemDetails`，机器码 `ai.routing.invalid`，没有模型、预算预留或工具派发。受保护请求仍先通过入口权限检查。路由没有独立预算池，工具任务沿用同一运行的总预算、截止时间、逐工具授权、人工审批、幂等和恢复检查。`202` 仅表示创建运行，不表示工具写入已审批或已完成。

原有问答与 Agent 入口保留。路由不扩大 Provider 网络资格或来源模型审批，也不宣称完整数据分类、实际输入 Token 硬限、跨重启 Trace 关联或容量已验收。原生运行和生成应用/页面验收在 AI 模块批次集中执行。

工具路由复用 `ClientRequestId` 时，已有运行的定义、版本、模型、提示和输入/输出预算必须完全一致；直接重放与创建竞争落败都检查完整持久快照，不匹配返回 `409 ai.routing.conflict`。竞争落败请求未调用模型，沿用原边界释放已知零用量预留。其他定义或旧工作流版本不能借原入口创建的请求键进入新路由。
