# Full.NET AI 与 Agentic Web 架构对齐开发计划

> 执行约定：按任务逐项实施并记录实际验证证据。遵守根目录 AGENTS.md；不自动创建工作树、派生代理或切换独立执行流程，不自动提交或推送。

**Goal:** 将现有 AI 管理与聊天模块演进为供应商中立、工具执行受控、可持久化恢复并支持 MCP/AG-UI 互操作的官方能力。

**Architecture:** 保留 `Full.NET.Modules.Ai` 作为 AI 管理业务和 `fn_ai_*` 数据的唯一所有者；通过反向实现存储及策略 Port，为独立 AI、Agent 和协议适配项目提供能力。供应商和 Agent Framework 的具体类型不进入普通业务模块核心或 Full.NET 稳定公开契约。先修正已有行为，再按可独立验收的纵向切片迁移。

**Tech Stack:** .NET 10、Microsoft.Extensions.AI、Microsoft Agent Framework、MCP C# SDK、AG-UI、Dapper、自有 SQL/事务执行器、SQL Server/MySQL、System.Text.Json 源生成、Vue 管理端、Microsoft Testing Platform。

## 0. 状态、授权与代码基线

- 日期：2026-09-08。
- 状态：实施中。T01 本地缺陷修复已落地；T02 已接入中立文本聊天和网关流式用量能力声明；T03 已实现聊天/连通性适配、连接时网络政策、请求内凭据引用和 Provider 写入保护。T00 原生验证、T02 双库验收、T03 长期凭据/轮换及部署验收仍未关闭；T04 已实现预检/传输分离与独立有界清理，本地验证通过、双库 HTTP 与原生运行验收待执行；T05 已实现统一只读工具执行器、当前会话权威授权与意图/回执审计，本地验证通过、双库验收待 CI；T06 已落地统一预算账本、价格快照、聊天接入和未知计量补录，本地验证与双库验收状态见执行记录；硬费用认证和 Agent/Embedding 实际消费者仍有后续门禁；T07–T15 尚未实施。
- 授权依据：用户要求“审查完善方案（现在代码如果有问题，可以重新设计并实现），制定开发计划”。随后用户明确要求“按计划执行”；允许在所列范围重设计，不要求保留存在缺陷的内部实现。
- 设计依据：[总体规格 §18](../specs/2026-07-17-fullnet-architecture-design.md#18-ai-与-agentic-web)、[路线图](../../roadmap/adminnet-feature-parity.md)、[模块交付](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。本计划不修改强化型模块化单体、双库、许可与 AOT 基线。
- 审查基线：`main`，`f74515d1b9e4544e4c75eeb623892970790b4d0c`；开始时工作区干净。此前咨询使用的 `020ba27a` 已不是当前 HEAD，实施时必须重新记录基线。
- 本轮验证方法：源码/依赖/调用者检索、官方协议资料核对、两个不访问外部服务的算法复现实验；没有运行 .NET 测试、真实模型调用、双库或浏览器。
- 唯一活动实施计划为本文。安全/AOT 细则在 T00 收敛为该主题的一份专项 Spec，任务状态和实施顺序只维护在本文，避免多份竞争计划。

## 1. 审查结论与需要纠正的方案

### 1.1 已有资产与处理决定

| 资产 | 处理 | 约束 |
| --- | --- | --- |
| 模型配置、租户配额、会话/消息、Vue 页面和精确权限 | 保留业务能力及稳定 API，调整内部调用路径 | 不清库、不改既有 ID、不静默更名 ProviderKey/权限码 |
| `AiChatQuotaGuard`、204 配额迁移、205 生成租约迁移 | 保留现有约束和回归测试，扩展为统一预算及恢复基础 | 不把现有实现直接认定为 Agent 运行时；不修改已执行迁移 |
| `AiChatCompletionStreamer`、`AiModelConnectivityTester` | 允许替换；供应商代码迁入 Provider | 行为测试跟随能力，不能仅移动文件后宣布分层完成 |
| `AiAgentToolCatalog`、工具审计表与页面 | 目录/展示可复用；增加静态 Handler 和统一执行器 | 目录元数据不是执行能力；未实现 Handler 不得对模型/MCP 宣告可调用 |
| `AiApiKeySecretProtector` | 保留密文兼容性，改变明文使用位置 | 保留 Data Protection purpose 和旧密文读取，Provider 负责解密与使用 |
| 自定义聊天 SSE | 保留为兼容端点；新增标准 AG-UI 适配 | 不直接把 `delta/done/error` 政名后称为协议兼容 |

### 1.2 现有问题、证据与首批验证

| 编号 | 结论与源码位置（仓库相对路径） | 根因/证据 | 处理任务 |
| --- | --- | --- | --- |
| F01 | `src/Modules/Full.NET.Modules.Ai/Streaming/AiChatCompletionStreamer.cs` Ollama 文本损失 | 按上一片段前缀扣减当前片段；官方 chunk 为增量。等价算法输入 `哈`、`哈`、`哈哈`，结果为 `哈哈`，预期 `哈哈哈哈` | T01、T03 |
| F02 | `Domain/AiChatContentPolicy.cs` 的 `SanitizeExternalError` 不能脱敏 | 代码只截断 512 字符；上游错误体及异常消息会进入 SSE。属于已确认的错误映射缺口，不声称已发生真实泄露 | T01、T03 |
| F03 | `Domain/AiAgentToolAuditPolicy.cs` 的正则不能覆盖 JSON 凭据字段 | 等价正则处理 `{"password":"sample-value","token":"sample-token"}` 仍保留敏感值；审计 Writer 目前未找到生产调用者 | T01、T05 |
| F04 | `Connectivity/AiModelConnectivityTester.cs` 全量读取外部响应 | `ReadAsStringAsync` 位于长度截断之前；限制最终消息长度不限制接收内存 | T01、T03 |
| F05 | `Domain/AiModelConfigFieldValidator.cs` 的安全 URL 校验只检查 scheme、userinfo 和 host | 未在该链路找到目的地址策略；并非所有内网访问都是漏洞，Ollama 本就有内网场景。需要在 Provider 建立显式网络策略并验证重定向/DNS | T03 |
| F06 | 生成流 EOF 与有效终态未明确区分 | 两种解析器均可在 EOF 后返回结果；需要回归测试证实断流、错误帧和终态缺失时的行为 | T01、T03 |
| F07 | `AiChatStreamService` 将预检失败写入 SSE 错误，且直接解密模型密钥 | 应在响应启动前返回标准 HTTP/ProblemDetails；模型协议和凭据边界应移出应用编排 | T03、T04 |

除 F01/F03 的等价算法实验外，上述结论来自静态审查；实施时必须先在真实被测 C# 类上建立失败测试。不得把算法实验写成已有 C# 回归通过。

### 1.3 对上一轮建议的修订

1. 先建立统一模型调用和安全执行边界，再接协议；引入 `IChatClient` 本身不提供租户授权、预算和可靠执行。
2. 运行数据统一归 Ai 模块，避免运行时与管理模块各持一套会话、预算、审批表，并避免通用 BuildingBlock 反向依赖业务模块。
3. 租约负责执行所有权，Checkpoint 负责可恢复状态，业务回执负责副作用去重；三者必须分别实现。
4. 人工确认由服务端绑定执行意图；模型输出的“已获批准”或客户端布尔字段无效。
5. 恢复语义为“从已提交边界继续”。不承诺重建断流时的精确模型输出，也不承诺外部调用恰好一次。
6. Agent Framework 工作流仅负责编排 Agent；既有 Workflow/DataApproval 继续拥有业务审批规则。首个工具审批为 AI 执行许可，不重做通用审批平台。
7. 当前没有第三方版本/AOT 的运行证据，不能承诺任意 SDK 可直接接入 Native AOT；将依赖实验前置为门禁。

## 2. 全局约束与设计边界

### 2.1 项目、真实消费者与依赖方向

新项目按对应任务首次产生真实调用时创建，不提前生成空壳。

| 项目路径 | 职责 | 真实消费者/隔离收益 |
| --- | --- | --- |
| `src/AI/Full.NET.AI.Abstractions/Full.NET.AI.Abstractions.csproj` | 中立模型选择、预算、执行主体与工具描述契约，直接使用微软 AI 接口 | Ai、两个 Provider、Agents、MCP；隔离供应商与业务持久化类型 |
| `src/AI/Full.NET.AI.Providers.OpenAI/Full.NET.AI.Providers.OpenAI.csproj` | OpenAI/兼容协议、凭据操作、能力差异 | 当前 Ai 聊天和后续 Agents；隔离 SDK/HTTP/认证逻辑 |
| `src/AI/Full.NET.AI.Providers.Ollama/Full.NET.AI.Providers.Ollama.csproj` | Ollama Chat/Embedding 与增量协议 | 当前 Ollama 聊天和后续 Agents |
| `src/AI/Full.NET.AI.Providers.AzureOpenAI/Full.NET.AI.Providers.AzureOpenAI.csproj` | Azure deployment、endpoint 和认证适配 | T13 的真实 Azure 配置/调用切片；不得先创建空项目 |
| `src/AI/Full.NET.Agents/Full.NET.Agents.csproj` | 自有运行契约、内部 Framework 适配、统一工具执行、租约/检查点协调 | Ai 管理 API、后台执行、MCP、AG-UI；公开类型不泄漏 Framework DTO |
| `src/AI/Full.NET.AgenticWeb.Mcp/Full.NET.AgenticWeb.Mcp.csproj` | MCP Client/Server 与授权协议 | MCP 外部标准客户端、Agents 远端只读工具 |
| `src/AI/Full.NET.AgenticWeb.AgUi/Full.NET.AgenticWeb.AgUi.csproj` | AG-UI 请求/事件映射与 SSE | Vue 工作台及标准 AG-UI 客户端 |
| 既有 `src/Modules/Full.NET.Modules.Ai` | 所有 `fn_ai_*` SQL、配置/配额/运行/审批/审计管理及运行 Port 实现 | 对外管理 API；为上述运行时提供受控存储与策略 |

依赖约束：Providers → AI.Abstractions；Agents → AI.Abstractions；MCP/AG-UI → AI.Abstractions + Agents；Ai → AI.Abstractions + Agents 的自有契约；Composition 装配全部具体实现。Ai 是专用管理模块，其他普通业务模块不引用 Agents/MCP/AG-UI/Provider 实现或 Agent Framework SDK。

Agents/Providers/协议项目均不得引用 `Full.NET.Modules.*`，也不得查询 `fn_ai_*`。Ai 的存储适配实现 Agents 定义的 Port。新增 `src/AI` 目录必须进入架构扫描，不能利用现有扫描只覆盖 BuildingBlocks 的盲区。

不新增无消费者的 `.Contracts/.Http/.Worker` 项目。若 T00 证明单个 Agents 程序集导致不可接受的 AOT/打包闭包，先形成最小隔离证据及批准的 Spec/ADR 变更，不用降低现有 AOT 门禁掩盖问题。

### 2.2 模型、凭据与数据政策

- 直接消费 `IChatClient`、`IEmbeddingGenerator<string, Embedding<float>>`，不重新定义同义 Chat/Embedding API。
- 模型选择按可信租户、用户、模型策略和版本解析；Provider 接收不可变模型配置和凭据引用，不接收 `AiModelConfigRecord`、HttpContext 或数据库连接。
- API 输入边界必然接触用户提交的密钥，立即交给 Provider 的凭据写入能力保护；核心编排、工具参数、Trace、审计和模型上下文不携带明文。既有保护 purpose `Full.NET.Ai.ModelConfigApiKey.v1` 保持兼容。
- Provider 的凭据读取 Port 只返回受保护内容或秘密存储引用，解保护只在 Provider 内发生；轮换按凭据版本隔离，旧客户端必须可释放，禁止全局 DefaultRequestHeaders 承载租户密钥。
- 外发政策区分用户显式聊天内容、工具业务数据与系统上下文；生产业务数据默认不外发。记录允许的模型/供应商/区域、数据类别、保留期和训练使用限制；配置声明不等于已验证供应商实际履约。
- 外部错误映射为稳定机器码和安全文案；原始错误正文、API Key、JWT、连接串不进入客户端或普通日志。审计采用字段白名单摘要，不能仅依赖正则替换任意 JSON。
- Provider 目的地策略区分公开 HTTPS 与管理员明确批准的 Ollama 内网地址；校验重定向、解析地址和实际连接目的地，阻止云元数据地址、非批准回环和 DNS 变更绕过；内部 HTTP 只能由显式内网策略允许。

### 2.3 工具执行与身份

唯一生产执行链：解析可信主体 → 查静态目录 → 校验工具版本/Schema → 重新确认账号、租户、会话或持久委托 → 权限和数据范围 → 预算 → 审批/策略豁免 → 记录执行意图 → 调用 Handler → 记录回执和审计。

- 工具只允许显式静态注册。未知工具、无 Handler、Schema 不匹配、工具禁用或版本漂移均失败关闭。目录按主体过滤，但执行时仍复核。
- 工具元数据来自模型或 MCP 时均不可信；远端声明 `readOnlyHint` 不能自动授予只读豁免。
- `TenantId`、`ActorUserId`、审批结果不能由模型参数覆盖。用户修改过滤条件也不能扩大 SQL 数据范围。
- 每次工具执行、审批恢复及长任务重启均查询权威身份状态；不把请求期间捕获的 ClaimsPrincipal 或访问令牌长期持久化。
- 默认 Run 绑定发起会话；会话撤销或过期后停止后续执行并进入 `authorization_required`。需要跨会话长任务时，显式创建有期限、可撤销、限定工具/数据范围的委托，重新检查用户和租户仍有效。
- 副作用类别为 None、Read、Write、Delete、Payment、ExternalMessage。所有写入默认审批；例外为服务端管理的有版本、有范围、有期限、有审计策略，不接受请求中的 `skipApproval`。
- 首个写工具为“重命名当前用户自己的聊天会话”；付款、删除和外发消息只覆盖拒绝与模拟故障测试，未完成各业务所有者集成前不对生产启用。
- 业务 Handler 再检查领域规则。跨模块立即读取使用公开最小 Contract；跨模块可靠写入使用事务 Outbox、业务所有者 Inbox/幂等、回执与对账，不让 Agent 开跨模块本地事务。

### 2.4 运行、预算与恢复

运行状态：`queued → running → completed`；running 可进入 `awaiting_approval`、`authorization_required`、`retry_scheduled`、`reconciliation_required`、`failed`、`cancelled`、`expired`。只有明确恢复操作和重新验证后，暂停状态才能回到 queued；已完成终态不可普通重开。

- Run 固化 Agent/工作流定义版本、模型配置版本、价格版本、工具版本、主体/授权方式和预算；不持久化 SDK CLR 类型名、任意程序集或非受控多态。只记录可公开消息、动作与执行摘要，不以审计为由收集模型隐藏推理；受保护 Checkpoint 也必须接受保留期和访问控制。
- API 创建 Run 后返回 202 和 RunId；Worker 在独立可信作用域内执行。浏览器断开只结束订阅，独立取消端点负责停止持久 Run；旧聊天 SSE 保持断开取消当前生成的兼容语义。
- Worker 以持久租约和递增 fencing token 获取执行权，所有状态写入带版本与租约条件。失去租约后立即停止模型/工具派发，不能靠本地字典证明跨实例唯一执行。
- Checkpoint 在已提交步骤边界保存；执行意图和回执另行持久化。恢复先核对回执再恢复 Framework 检查点；禁止在已经提交的副作用前重放。
- 预算包括总时长、单模型调用时长、步骤数、模型调用次数、工具次数、Token、金额与并发槽位。子 Agent 共用根 Run 总预算，不能通过拆子任务增加额度。
- 每次模型调用以稳定 OperationId 在 Ai 本地事务预留请求/Token/费用；真实模型调用在事务外。实际用量幂等结算，缺失用量保留保守预留并记录 unknown，禁止记零。
- 金额使用 `decimal` 和币种，输入/输出/缓存 Token 单价按价格版本固化；未知价格模型不能进入要求硬费用上限的生产运行。限额无法约束供应商已产生的账单，硬预算只能建立在已验证用量上界与有界派发之上。
- 明确 Host 预算；保留旧“未配置租户月配额=不限月额度”兼容语义，但新的 Run 必须有有限的单次执行预算。预算策略禁用不等于禁用 AI。
- 模型重试仅在未产生对用户可见输出且结果可判定时有界进行；流已输出后不透明拼接重试。工具超时、断网或进程崩溃导致外部结果未知时进入 reconciliation_required，禁止盲目重试。
- 超时、取消、预算耗尽、授权失效分别记录错误码。所有清理/结算使用独立且有界的超时，不能依赖已断开的 RequestAborted，也不能无限等待 CancellationToken.None。
- 运行预算截止时间包括等待审批；审批同时有独立 ExpiresAtUtc。业务补偿不是取消的隐式动作，必须有显式授权与业务契约。

### 2.5 数据模型与迁移

数据所有者统一为 Ai；表内使用 TenantId、ActorUserId、ScopeKey，并以可信范围查询。Host 的唯一约束不能依赖 MySQL nullable TenantId 自动去重；ScopeKey 显式区分 Host 与 Tenant，不采用 Guid.Empty 伪造租户。

| 表/变更 | 主要字段与唯一性 | 首次任务 |
| --- | --- | --- |
| `fn_ai_agent_run` | Id、ScopeKey、TenantId、ActorUserId、SessionId、DefinitionKey/Version、AuthorizationBindingId、StatusKey、BudgetJson、DeadlineAtUtc、Version、LeaseOwner、LeaseEpoch、LeaseExpiresAtUtc；唯一 ScopeKey+ActorUserId+ClientRequestId，保存 RequestHash | T07 |
| `fn_ai_agent_step` | RunId、StepKey、Attempt、OperationId、StatusKey、Model/Tool/PriceVersion、Tokens、Cost、Currency、TraceId、参数摘要、错误码；唯一 RunId+StepKey+Attempt 和 OperationId | T07 |
| `fn_ai_agent_checkpoint` | RunId、Sequence、FormatVersion、FrameworkVersion、DefinitionVersion、受保护 Payload、Checksum、CreatedAtUtc；唯一 RunId+Sequence | T07 |
| `fn_ai_agent_event` | RunId、Sequence、EventType、PayloadVersion、受控 Payload、CreatedAtUtc；唯一 RunId+Sequence，用于重连与诊断 | T07 |
| `fn_ai_agent_approval` | RunId、OperationId、ToolVersion、ArgumentsHash、PolicyVersion、RequestedBy、ApproverId、Decision、ExpiresAtUtc、ConsumedAtUtc、Version；一次性消费 | T09 |
| `fn_ai_agent_delegation` | 主体、限定权限/工具/数据范围、期限、撤销状态、版本；不存 Access Token | T09 |
| 现有 `fn_ai_agent_tool_call` 扩展 | RunId/StepId/OperationId、ArgumentsHash、审批引用和业务回执引用；保留旧审计行 | T05/T09 |
| 现有配额预留扩展及 `fn_ai_model_price` | ScopeKey、OperationId、预留/实际 Token 和金额、币种、计量状态、价格版本；结算唯一性 | T06 |

迁移分别为 `AiProviderPolicy`、`AiOperationBudget`、`AiAgentRuntime`、`AiAgentApproval` 语义批次。在对应任务开始时读取双库最大迁移序号后分配相同的新序号；本计划不提前抢占 210+ 序号。路径为 `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/` 与 `MySql/`，不得改写 184–189、204–205 历史脚本。

采用 expand → 分批幂等回填 → 校验 → 收紧约束。旧消息保留原样，不伪造历史 Run/Step/费用或审批结果；历史不可得数据明确标记 unknown。Checkpoint/事件正文独立保留策略，删除会话不级联清除法定或策略要求保留的审计。Production 只播种安全 Baseline，示例 Agent/模型凭据仅在测试或显式环境 Overlay。

### 2.6 宿主与协议

- AiModule 增加公开最小后台注册入口；Composition 的共享目录按 Api/Worker/Migrator Profile 装配。Worker 不装完整 HTTP 模块，Migrator 不创建模型客户端或执行 Agent。
- MCP Server 默认使用所选 SDK 支持的标准 Streamable HTTP；不把 AG-UI SSE 当作 MCP。建立独立 audience/resource、认证 challenge、受保护资源元数据与外部授权服务器发现；重用现有身份基础但不宣称普通 JWT 端点即完整 MCP OAuth。
- MCP 的 list/read/get/call 都逐次授权；resources 只允许自有 URI 模板和受限数据，prompts 只允许静态注册及参数 Schema；禁止任意文件/SQL/Controller 映射。
- MCP Client 的服务器、能力版本及 Schema 摘要需管理员显式批准。远端能力更新先禁用变化项，不能因服务重新返回工具就扩大权限。默认只读允许集，不向远端透传用户访问令牌或 Cookie。
- AG-UI 采用标准 run/message/tool/state 生命周期事件与 HTTP+SSE；先持久化可重放事件再发送。重连使用协议支持的方式或明确文档化的扩展；游标过期返回需要重新同步，不能把自定义扩展宣称标准必需行为。
- 管理查询使用真实 HTTP 状态和 ProblemDetails。协议流启动后的失败使用协议错误事件；协议 DTO 不进入 Admin.NET 包络。SignalR 是可选未来适配，不是本计划完成条件。

## 3. 公共契约草案与文件责任

以下签名为本计划锁定的自有契约方向；T00 通过编译/AOT 实验确定依赖 API 后，T02/T05/T07 建立实际类型。新增命名按项目 Naming Profile 检查，不把示意签名直接当作已经存在的源码。

```csharp
// AI.Abstractions/Models/ModelBinding.cs
public sealed record ModelBinding(Guid ConfigId, int Version, string ProviderKey,
    string ModelId, Uri Endpoint, Guid? CredentialReference);

// AI.Abstractions/Models/IAiModelClientFactory.cs
public interface IAiModelClientFactory
{
    ValueTask<Microsoft.Extensions.AI.IChatClient> CreateChatClientAsync(
        ModelBinding binding, CancellationToken cancellationToken);
    ValueTask<Microsoft.Extensions.AI.IEmbeddingGenerator<string,
        Microsoft.Extensions.AI.Embedding<float>>> CreateEmbeddingGeneratorAsync(
        ModelBinding binding, CancellationToken cancellationToken);
}

// AI.Abstractions/Tools/ToolInvocation.cs；身份由执行上下文服务解析，不来自此参数。
public sealed record ToolInvocation(Guid OperationId, Guid? RunId, string ToolName,
    int ToolVersion, System.Text.Json.JsonElement Arguments);
public sealed record ToolExecutionResult(string StatusKey,
    System.Text.Json.JsonElement? Value, string? ErrorCode, Guid? ApprovalId);

// Agents/Tools/IAgentToolExecutor.cs；唯一生产执行入口。
public interface IAgentToolExecutor
{
    ValueTask<ToolExecutionResult> ExecuteAsync(
        ToolInvocation invocation, CancellationToken cancellationToken);
}

// Agents/Runtime/AgentRunLease.cs 与 Persistence/IAgentRunStore.cs
public sealed record AgentRunLease(Guid RunId, string WorkerId, long Epoch,
    long Version, DateTimeOffset ExpiresAtUtc);
public interface IAgentRunStore
{
    ValueTask<AgentRunLease?> TryAcquireAsync(Guid runId, string workerId,
        DateTimeOffset now, CancellationToken cancellationToken);
    ValueTask<bool> RenewAsync(AgentRunLease lease, DateTimeOffset now,
        CancellationToken cancellationToken);
}
```

客户端工厂返回实例的释放责任归调用者；若内部池化必须返回有独立生命周期的包装，不让释放租户 A 客户端破坏租户 B。模型调用前授权由 Ai 的模型策略适配完成；Factory 不是公开 HTTP/工具入口。

`IAgentRunStore` 的原子创建/步骤提交/终态提交方法在 T07 与具体状态模型一起定义，必须把“状态+Checkpoint+事件+相关本地预留/回执更新”作为单一原子操作；禁止提供任意 SQL 或通用 Update 回调。工具静态目录、身份解析、审批、预算 Port 各有独立接口文件，由 Ai 实现策略和存储，Agents 只协调调用。

## 4. 分阶段任务

每个任务遵循：建立可失败验证 → 确认失败原因 → 实现 → 聚焦验证 → 审查差异。复用既有测试项目和 fixture；不为每个功能新建测试项目。下列文件均为仓库相对路径，Create 表示计划新增而非当前存在。

### T00：固化安全/AOT 设计和可用依赖组合

**依赖：** 无。**交付：** 一份与本文一致的专项设计和受测依赖组合；若 SDK 不满足约束，问题在大规模重构前暴露。

**文件：** Create `docs/superpowers/specs/2026-09-08-ai-agentic-web-security-runtime-design.md`；Modify 总体规格 §18（仅补链接及实际批准的澄清）、`Directory.Packages.props`、`tests/Full.NET.ArchitectureTests/AiDependencyBoundaryTests.cs`（新增）、`eng/testing/test-matrix.json`。临时依赖实验使用任务专属 `artifacts/ai-agentic-dependency-probe/`，不纳入正式 solution 或发布物；有效测试随后迁入对应纵向切片。T00 不依赖 T02 项目存在。

- [ ] 将本文 §2 的所有权、审批、身份、外发、MCP OAuth、协议版本、保留策略和迁移决定整理为专项 Spec，记录当前用户授权、适用范围与未验证限制。
- [ ] 核对微软 AI/Agent Framework、MCP SDK、AG-UI 的具体 NuGet 版本、稳定/预览、维护/许可与传递依赖，集中锁定并更新实际第三方许可清单；不复制不明版本代码。
- [ ] 在临时最小 net10.0 宿主中用现有聊天的流式请求样本与一个静态工具验证 JSON 源生成、DI、FunctionCall 内容和 Checkpoint 格式的静态闭包。实验结束时记录准确包版本、告警与运行结果；这只是依赖候选证据，T02/T07/T10/T11 接入真实宿主后仍验证实际可达闭包。任何无法验证的供应商能力保持未验证。
- [ ] 为后续真实测试新增 `ai-provider`、`ai-runtime`、`ai-tools`、`ai-protocols`、`ai-agentic-boundaries` 的矩阵聚焦集；只在对应测试已存在时登记，最低发现数按真实测试维护，不统一写 1。
- [ ] 执行候选依赖 AOT 分析与已有架构测试；Linux publish/原生测试按 §5 交给 Actions。远端未授权或不可用时允许继续本地实现，但不得关闭该原生验证门禁或声明兼容。若必须改变 Host.Api/Worker 运行边界，先列出替代方案和 ADR 决策，不默默引入 JIT 网关或关闭原有能力。

**拒绝条件：** 抽象依赖具体业务模块、Full.NET 公共 API 泄漏 SDK 对象、依赖只能依靠无界反射或通配 linker root 运行。

### T01：先用回归测试修正现有聊天与数据暴露问题

**依赖：** 无，可先于依赖选型完成。**交付：** 现有聊天行为修正，后续迁移有可复用基线。

**文件：** Modify `src/Modules/Full.NET.Modules.Ai/Streaming/AiChatCompletionStreamer.cs`、`Connectivity/AiModelConnectivityTester.cs`、`Domain/AiChatContentPolicy.cs`、`Domain/AiAgentToolAuditPolicy.cs`；Modify `tests/Full.NET.UnitTests/Ai/AiChatStreamingBudgetTests.cs`、`AiModelConnectivityTesterTests.cs`、`AiChatContentPolicyTests.cs`、`AiAgentToolAuditPolicyTests.cs`。

- [x] 在既有 `AiChatStreamingBudgetTests`（复用其 ResponseHandler）新增下面的真实解析测试并确认当前实现因文本损失失败：

```csharp
[TestMethod]
public async Task Ollama_repeated_chunks_are_not_deduplicated_async()
{
    const string body = "{\"message\":{\"content\":\"哈\"},\"done\":false}\n"
        + "{\"message\":{\"content\":\"哈\"},\"done\":false}\n"
        + "{\"message\":{\"content\":\"哈哈\"},\"done\":false}\n"
        + "{\"message\":{\"content\":\"\"},\"done\":true,\"prompt_eval_count\":1,\"eval_count\":4}\n";
    using var client = new HttpClient(new ResponseHandler(body));
    var factory = Substitute.For<IHttpClientFactory>();
    factory.CreateClient(Arg.Any<string>()).Returns(client);
    var result = await new AiChatCompletionStreamer(factory).StreamAsync(
        new AiModelConfigRecord { ProviderKey = "ollama",
            EndpointBaseUrl = "http://ollama.test", ModelId = "test" },
        null, [], _ => Task.CompletedTask);
    Assert.AreEqual("哈哈哈哈", result.Content);
}
```

- [x] 增加用例：OpenAI/Ollama 终态缺失、错误帧、取消、流中断、重复 chunk、usage 缺失；预期不能把中断标记 completed，仍保留受限部分正文。
- [x] 为 JSON 嵌套 password/token、Bearer、连接串、提供程序返回任意正文建立失败断言；预期客户端及摘要不含原值。仅允许固定安全摘要字段，禁止通过新增大量正则假装通用脱敏。
- [x] 使用可计数的响应 Stream 验证连通性探测在达到响应预算后停止读取；取消必须保持取消语义，不能被通用 catch 变成连通成功/普通失败。
- [x] 实现增量直接追加、明确有效终态、有界探测读取与安全错误映射；保持现有 Token/租约回归。测试进入 `ai-provider` 聚焦集。

**预期：** 修复前新增场景失败，修复后新增和既有流预算场景通过；不要求调用真实付费模型。

### T02：建立中立模型契约与真实工厂接入

**2026-09-08 网关能力切片：** `OpenAiGatewayPolicy` 从可信宿主配置读取精确端点与模型的 `SupportsStreamingUsage` 声明；未知绑定与显式 false 都不发送 `stream_options.include_usage`。字段拼写、必填值、HTTPS 端点和重复绑定在装配时校验。策略冻结到宿主生命周期，不根据官方/兼容域名猜测能力。实际 usage 仍可读取，缺失保持 unknown；不通过重试推断能力，现有工具/多模态/调用选项拒绝语义保持。配置方法见 [Provider 操作说明](../../operations/ai-provider-network.md)。下方 T02 初始切片中的密文过渡已由 T03 短期引用替代。

**2026-09-08 实施收敛：** 当前消费者仅为文本聊天，实际 `IAiModelClientFactory` 按 `ProviderKey` 显式注册并返回 `IChatClient`；Embedding 工厂随 T13 的真实消费者定义，不预建无实现接口。OpenAI 与 Ollama 同时迁移，以保留两个既有 ProviderKey。`ModelBinding` 暂传历史受保护密文，不传明文；不透明凭据引用、轮换及连通性解密迁移由 T03 完成。这是过渡实现，不是凭据边界最终验收。

已验证中立文本/usage、禁用/未知模型拒绝、取消、消费失败释放，以及两种 Provider 对非文本内容和非空 `ChatOptions` 在网络调用前拒绝。调用选项、官方/兼容网关能力策略和错租户双库验证尚未完成。现有固定输出上限保持 4096；未宣称支持工具调用或多模态。新的架构测试覆盖 Abstractions 依赖限制和 Module/Provider 引用方向；连通性协议仍在模块内，因此未勾选全面供应商隔离。

**依赖：** T00、T01。**交付：** 当前聊天通过微软接口调用至少一个 Provider。

**文件：** Create §2.1 中 Abstractions、OpenAI 项目；Create `Models/ModelBinding.cs`、`Models/IAiModelClientFactory.cs`、`Full.NET.AI.Providers.OpenAI/OpenAiModelClientFactory.cs`；Modify `Full.NET.slnx`、`src/Composition/Full.NET.Composition/Full.NET.Composition.csproj`、`src/Modules/Full.NET.Modules.Ai/Full.NET.Modules.Ai.csproj`、`AiModule.cs`、`Features/ManageChatSessions/AiChatStreamService.cs`；Create `tests/Full.NET.UnitTests/Ai/AiModelClientFactoryTests.cs`。

- [ ] 以假 `IChatClient` 证明现有聊天只依赖中立响应/用量类型；测试禁用模型、未知 Provider、错租户模型、客户端取消和释放。
- [ ] 实现 §3 工厂签名，模型选择在 Ai 中产生已授权 ModelBinding；不让业务模块传任意 Endpoint 或密钥给工具。
- [x] OpenAI 官方与兼容网关按能力配置区分，未知能力失败关闭，不默认每个兼容服务都支持 usage、tools 和 embeddings。当前真实开放能力仅为文本与显式流式用量；其他能力不宣告可用。
- [ ] 将现有 ProviderKey、配置 ID 和聊天路由保持不变；核心聊天代码不再 switch ProviderKey。架构测试拒绝 Modules.Ai 中出现供应商 SDK 调用或供应商 URL 拼装。
- [ ] 运行 `ai-provider` 与 `ai-agentic-boundaries`；同一源码下重复套件使用 `--no-build`。

### T03：迁移 Ollama、凭据和网络/错误边界

**2026-09-08 凭据切片：** 以实际 `Security/AiModelBindingScope` 实现 `IProtectedModelCredentialStore`，在已授权查询后签发请求内引用；其完整绑定与冻结选项共同约束凭据用途，其他作用域、篡改的绑定与已释放作用域拒绝读取。`ModelBinding` 不再携密文；配置管理改为调用 Provider 的 `IAiModelCredentialProtector`，删除旧模块保护实现，保留原 purpose 与数据库列。Provider 和两个调用器调整为 Scoped；Composition 仅在 Ai 被启用时注册对应依赖。长期存储引用与跨请求即时撤销尚未实现，持久 Agent 恢复不能复用当前引用。未新增 SQL、表或迁移。

**2026-09-08 网络切片：** 新增 `Full.NET.AI.Providers.Http`，集中维护两个 Provider 共享的地址校验和连接生命周期，由 Composition 配置四个隔离的聊天/目录客户端。默认公共 HTTPS；只有可信宿主的精确 Ollama origin 列表允许内网/环回及 HTTP。元数据、链路本地、未指定、多播、保留地址与特殊 IPv6 转换范围不得豁免。DNS 结果整组校验后按已校验 IP 连接，HTTP 解析到公网也拒绝；代理、自动重定向和 Cookie 禁用，协议固定到经过 TCP 回调的 HTTP/1.1。配置方法和升级兼容性见 [部署说明](../../operations/ai-provider-network.md)。这只关闭本地实现/测试步骤，真实部署 DNS、双库 Host 与 Linux 原生运行验收仍开放。

**2026-09-08 连通性切片：** 新增 `IAiModelConnectivityProbe`，两个已有 Provider 工厂显式实现与注册。`AiModelConfigOperationsService` 只传已授权查询得到的配置，`AiModelConnectivityTester` 只派发中立绑定；供应商 URL、目录协议与解密迁入 Provider，删除模块解密入口。目录只接受当前协议的结构化精确模型标识，拒绝畸形或其他协议正文，不再用字符串包含关系兜底。保留 15 秒总预算和 64 KiB 响应预算，并释放 HTTP 客户端及响应流。

历史语义保留：有效 OpenAI 目录未列出模型时提示人工核对，Ollama 必须列出模型；探测允许尚未启用的配置，且不代表推理成功。Ollama 保持本地无认证协议，网关认证能力尚未开放。配置密钥写入保护仍在 API 输入边界，`ModelBinding` 仍暂携受保护密文；不透明凭据引用、持久化换钥/双租户验证、目的地址/DNS/重定向策略和完整网络故障矩阵未完成。并发单测仅证明 Provider 不串请求头，不宣称已证明数据库租户授权。

**依赖：** T02。**交付：** 两个现有供应商走新路径，旧凭据和数据可继续使用。

**文件：** Create Ollama 项目及 `OllamaModelClientFactory.cs`、`OllamaChatClient.cs`；Create `src/AI/Full.NET.AI.Abstractions/Credentials/IProtectedModelCredentialStore.cs`、`src/Modules/Full.NET.Modules.Ai/Providers/AiProtectedModelCredentialStore.cs`、`Providers/AiModelAccessPolicy.cs`；Modify 既有 `Security/AiApiKeySecretProtector.cs`、模型配置与连通性服务；Create `tests/Full.NET.UnitTests/Ai/AiProviderCredentialIsolationTests.cs`、`AiProviderEndpointPolicyTests.cs`。响应读取/错误映射实现随 Provider 归属，确有共享消费者再抽共享辅助类。

- [ ] 为旧 purpose 密文、密钥轮换、两个租户并发、客户端释放、没有凭据和解密失败建立测试；断言密钥只出现在目标 Provider 请求，模型/审计/日志均不携带。
- [ ] 将 T01 的协议测试迁至 Provider；Ollama 按 NDJSON 增量实现中立接口，错误、结束标志和 usage 做明确映射。
- [ ] 用受控 HTTP handler/解析器测试允许地址、拒绝元数据地址、重定向到禁区、DNS 变化、超大响应、挂起流和取消；生产连接策略必须约束实际连接而非仅第一次 DNS 查询。
- [ ] 将凭据保护/解保护行为移到 Provider 边界；配置入口保留原格式兼容。新增网络与外发策略使用 Ai 强类型自有配置；必要持久化使用 AiProviderPolicy 双库新增迁移。
- [ ] 切换新 Provider 后删除旧重复协议代码及无调用者 DI，保留兼容适配直到聚焦测试通过。未知外部错误统一映射机器码，不向前端回显原始正文。

### T04：拆分聊天预检、执行和传输

**依赖：** T03。**交付：** HTTP/SSE 与模型执行分离，错误状态符合契约。

**文件：** Modify `Features/ManageChatSessions/Endpoint.cs`、`AiChatStreamService.cs`、`Streaming/AiChatSseWriter.cs`、`Streaming/AiChatGenerationLeaseMonitor.cs`、`src/Modules/Full.NET.Modules.Ai.Contracts/AiErrorCodes.cs`、`ui/admin/src/api/ai-chat.ts`；Create `tests/Full.NET.IntegrationTests/Ai/AiChatApiAssertions.cs`、`AiChatApiSqlServerTests.cs`、`AiChatApiMySqlTests.cs`；Modify `ui/admin/src/views/AiChatView.test.ts`。

- [x] 增加测试（双库 API 用例已编译，真实执行待 CI）：响应开始前无权限 403、无会话 404、重复生成 409、输入错误 422；配额拒绝保留既有策略状态并使用稳定错误码。响应开始后用 SSE 错误事件，不修改已发送状态码。
- [x] 将预检/获取执行槽位置于 StartAsync 前，模型执行不持有 HttpContext；现有 JSON/SSE DTO 与 OpenAPI 必要变更同步生成。
- [ ] 测试跨实例取消、租约丢失、HTTP 断开、最终写入失败和结算失败；独立清理必须有截止时间，不能覆盖新代次。当前单元测试已覆盖租约丢失、断开、写入零行/异常、结算/释放异常与忽略取消的驱动；真实跨实例/双库执行仍待验收。
- [x] Vue 同时识别 ProblemDetails 与流错误，不把 403/409 当流解析失败；同步类型与聚焦组件测试。

### T05：交付统一工具执行器与真实只读工具

**依赖：** T02；与 T03/T04 不共享修改时可独立实施，默认仍逐项执行。**交付：** 三个目录项都有受控 Handler 和审计。

**状态：** 已实现并完成本地验证；跨租户/所有权双库执行待 CI，不标记为完整验收。

**文件：** Create Agents 项目及 `Tools/IAgentToolExecutor.cs`、`Tools/AgentToolExecutor.cs`、`Tools/AgentToolRegistry.cs`；Create `src/AI/Full.NET.AI.Abstractions/Tools/ToolInvocation.cs`；Modify Ai 的 `Domain/AiAgentToolCatalog.cs`、`Features/ManageAgentTools/AiAgentToolCallAuditWriter.cs`、`AiModule.cs`；Create `Features/ManageAgentTools/Handlers/PingToolHandler.cs`、`ListModelsToolHandler.cs`、`ListChatSessionsToolHandler.cs`；Create `tests/Full.NET.UnitTests/Ai/AiToolExecutionSecurityTests.cs`、`tests/Full.NET.IntegrationTests/Ai/AiToolExecutionAssertions.cs` 及同名 SqlServer/MySql 测试包装。

- [ ] 建立调用计数测试：未知/禁用工具、无 Handler、额外参数、跨租户、非本人、无权限、身份失效时 Handler 调用次数必须为 0，拒绝原因有安全审计。
- [x] 实现 §3 ExecuteAsync；三种 Handler 使用 Ai 既有查询/所有权能力，所有参数使用静态 JsonTypeInfo；目录中的 planned 不进入可调用列表。
- [x] 所有调用绑定 OperationId，设置耗时、次数、输出尺寸限制；返回值标为不可信数据，不能被拼接为系统授权指令。
- [x] 审计写入失败时不得继续未派发的调用；已执行调用回执写入失败须明确失败状态，不伪造 succeeded。T09 前只允许只读工具。
- [ ] 双库验证当前用户只能看自己的会话；人工绕过前端或从模型直接构造参数也不能读取他人数据。

### T06：统一模型调用预算、价格与审计

**依赖：** T03、T05。**交付：** Chat、Agent 和 Embedding 共享同一预算入口。

**状态：** 统一预算/价格记账核心已实现；双库与原生运行待 CI，尚不关闭 M2。当前 Provider 不具备硬费用认证，要求硬上限时拒绝；不把费用账本当成硬账单保证。

**文件：** Create `src/AI/Full.NET.AI.Abstractions/Budgets/AiExecutionBudget.cs`、`Budgets/IAiOperationBudgetStore.cs`；Modify `src/Modules/Full.NET.Modules.Ai/Streaming/AiChatQuotaGuard.cs`、`Persistence/AiQuotaReservationSql.cs`、`Features/ManageTenantQuotas/`；Create `Persistence/AiOperationBudgetSql.cs`、`Persistence/AiModelPriceSql.cs`；AiOperationBudget 双库迁移；Create `tests/Full.NET.UnitTests/Ai/AiOperationBudgetTests.cs`、`tests/Full.NET.IntegrationTests/Ai/AiOperationBudgetAssertions.cs` 及 SqlServer/MySql 包装。

- [ ] 测试同一 OperationId 重复预留只扣一次；请求摘要不同返回冲突；跨月结算回到原预留月份，Host/Tenant 不串账。
- [ ] 原子检查并预留 Token、请求数和金额；费用使用价格版本及币种，结算重复/乱序不得重复调整。取消/缺失 usage 保守保留并可对账。
- [x] 绑定单 Run、单租户和 Host 默认上限；没有硬费用计量条件时拒绝要求硬费用上限的运行，而不是当作免费。
- [ ] 测试两实例竞争最后一个预算名额、数据库中断、结算失败重放、负数/溢出用量；错误帧不能释放可能已消耗的全部预留。
- [x] 增加低基数模型调用量、失败、拒绝和费用指标；用户/租户/RunId 仅进受控 Trace/审计，不能作为无界 Metrics label。

### T07：持久化 Run/Step/Checkpoint/事件与 Worker

**依赖：** T00、T06。**交付：** 单 Agent 独立于 HTTP 连接执行，可取消并在已提交步骤边界恢复。

**文件：** Create `src/AI/Full.NET.Agents/Runtime/AgentRunCoordinator.cs`、`AgentRunLease.cs`、`Persistence/IAgentRunStore.cs`、`Framework/AgentFrameworkAdapter.cs`；Create `src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentRunStore.cs`、`AiAgentRunWorker.cs`、`Persistence/AiAgentRunSql.cs`、`AiAgentStepSql.cs`、`AiAgentCheckpointSql.cs`、`AiAgentEventSql.cs`、`Serialization/AiAgentJsonSerializerContext.cs`；Modify AiModule、Composition/Host Profile；AiAgentRuntime 双库迁移；Create `tests/Full.NET.UnitTests/Ai/AiAgentRunStateTests.cs`、`tests/Full.NET.IntegrationTests/Ai/AiAgentRecoveryAssertions.cs` 及双库包装。

- [ ] 为 §2.4 全部状态转换建立表驱动测试；终态不可重开，旧租约不能提交，新 Worker 不重做已提交步骤。
- [ ] 实现受控版本化 DTO、Dapper 双库存储和源生成物化；原子提交状态、步骤、Checkpoint 与有序事件，外部模型/工具不进入事务。
- [ ] Agent Framework 类型仅在 Framework 目录内部使用；以框架支持的 C# 持久化扩展接入自有 Store，检查点写入失败不能报告步骤已可恢复。
- [ ] API 创建 Run 幂等返回 RunId；Worker claim/renew/release、取消、重启恢复使用可信新作用域，不持有请求级数据库连接或令牌。
- [ ] 在“预算预留后、模型完成后、步骤提交前、事件发送前”注入进程/数据库失败，逐个验证费用、回执和事件状态；两 Worker 对同一 Run 只有一个有效执行者。
- [ ] Worker 健康状态必须反映租约/数据库及所需依赖；运行能力禁用或不可用时拒绝新 Run，不接受后永久排队。

### T08：单 Agent 工具循环与版本化恢复

**依赖：** T05、T07。**交付：** 模型 → 只读工具 → 模型回复的真实运行闭环。

**文件：** Create `src/AI/Full.NET.Agents/Definitions/AgentDefinitionRegistry.cs`、`Runtime/AgentToolLoop.cs`、`Runtime/AgentCheckpointCompatibility.cs`；Create `src/Modules/Full.NET.Modules.Ai/Features/ManageAgentRuns/Endpoint.cs` 与管理服务；Create `tests/Full.NET.UnitTests/Ai/AiAgentToolLoopTests.cs`、`AiCheckpointCompatibilityTests.cs`。

- [ ] 用脚本化 `IChatClient` 响应产生工具调用，断言只经过统一执行器、工具 CallId 和 OperationId 稳定、输出按工具消息传回，未注册工具不会执行。
- [ ] 覆盖无限工具循环、并行工具总次数、步骤预算、Token/费用耗尽、模型改写 TenantId、工具输出伪造系统指令；服务端权限结果不受模型文本改变。
- [ ] 固化 Definition/Tool/Model/Framework/Checkpoint 格式版本；不兼容版本停止恢复并返回明确错误，不自动用新工作流解释旧状态。
- [ ] 模型流中断只保留实际收到的部分结果和失败 Attempt；再次生成作为新的、可审计 Attempt，不能伪装为原流续传。

### T09：人工审批、委托和副作用恢复

**依赖：** T08。**交付：** 一个真正经过人审、可防重复执行的写工具。

**文件：** Create `src/AI/Full.NET.Agents/Approvals/AgentApprovalGate.cs`；Create Ai 的 `Features/ManageAgentApprovals/Endpoint.cs`、审批/委托服务、`Features/ManageAgentTools/Handlers/RenameChatSessionToolHandler.cs`、`Persistence/AiAgentApprovalSql.cs`、`AiAgentDelegationSql.cs`；AiAgentApproval 双库迁移；Modify `AiAuthorizationContributor.cs` 和对应权限/序列化；Create `tests/Full.NET.UnitTests/Ai/AiApprovalBindingTests.cs`、`tests/Full.NET.IntegrationTests/Ai/AiApprovalRecoveryAssertions.cs` 及双库包装。

- [ ] 请求审批时保存完整受保护执行参数和规范化 ArgumentsHash；审批端返回人能理解的动作、目标、变更、范围、费用上界和有效期，不只显示不可读哈希。
- [ ] 审批绑定 Run/Operation/主体/租户/工具版本/参数哈希/策略版本/期限；决定重复提交幂等，参数或版本变化重新申请。未授权审批人、过期、已消费和被撤销授权必须拒绝。
- [ ] 首个写工具在 Ai 同一事务中消费审批、记录操作回执并重命名会话；审批同意不能绕过会话所有权。持久委托创建/撤销有独立权限和审计。
- [ ] 覆盖两实例同时消费审批、执行成功后响应丢失、批准后权限撤销、请求会话过期、取消与审批竞争；相同操作最多产生一次本地业务变更。
- [ ] 为跨模块写工具建立 Outbox/幂等回执的模拟接收方测试：结果未知进入 reconciliation_required；付款/外发等尚无生产 Handler 的工具保持拒绝，不创建虚假的通用付款工具。

### T10：标准 AG-UI 与 Vue 工作台

**依赖：** T08、T09。**交付：** 可展示运行、工具、审批和恢复的标准协议工作台。

**文件：** Create AG-UI 项目及 `AgUiEndpoint.cs`、`AgUiEventMapper.cs`；Create `ui/admin/src/api/ai-agent-runs.ts`、`ui/admin/src/views/AiAgentRunsView.vue`、`AiAgentRunsView.test.ts`；Modify AiChatView/工具页按实际入口复用；Create `tests/Full.NET.UnitTests/Ai/AgUiEventContractTests.cs`、`tests/Full.NET.IntegrationTests/Ai/AgUiProtocolTests.cs`。

- [ ] 使用官方事件契约测试 run/message/tool 生命周期顺序、错误、取消、审批等待/恢复；不把自定义 event 名称当作标准。
- [ ] 映射 T07 的持久事件，确保重连不触发重跑工具；重复事件去重、有界缓冲、慢客户端和过期游标均有明确行为。
- [ ] 请求身份只来自认证边界；AG-UI 输入中的 messages、tools、state 和审批信息均不扩大服务端注册目录或授权。
- [ ] Vue 页面显示 Run 状态、步骤摘要、Token/费用及 unknown、审批操作和稳定错误；逐按钮权限来自同一权威目录。无权限时直接 API 仍拒绝。
- [ ] `pnpm --filter @fullnet/admin typecheck` 与聚焦组件测试通过；真实 AG-UI 客户端互操作进入 Actions，页面集中验收前不升级 Verified。

### T11：MCP Server 的工具、资源、提示和标准授权

**依赖：** T05、T09；T00 协议授权/AOT 门禁必须关闭。**交付：** 外部标准 MCP 客户端可安全使用已实现能力。

**文件：** Create MCP 项目及 `Server/McpServerRegistration.cs`、`McpToolAdapter.cs`、`McpResourceAdapter.cs`、`McpPromptAdapter.cs`、`McpAuthorizationOptions.cs`；Modify Composition 的 API 装配；Create `tests/Full.NET.UnitTests/Ai/McpExposurePolicyTests.cs`、`tests/Full.NET.IntegrationTests/Ai/McpServerAuthorizationTests.cs`、`McpServerInteropTests.cs`。

- [ ] 核对 SDK 支持的协议版本与 Streamable HTTP，配置正确 audience/resource、metadata/challenge、授权服务器发现、Origin/会话边界；不转发用户 Token 到本机 HTTP 或外部服务。
- [ ] 映射 T05 的三个工具，list/call 独立授权；写工具只有在其异步审批结果能用明确协议契约表达后才暴露，不能长时间挂住 tools/call 假装已执行。
- [ ] 首个资源为当前主体可访问的会话摘要 URI；首个提示为参数受限的静态摘要模板。resources/read、prompts/get 同样复核权限，非法 URI/参数失败关闭。
- [ ] 标准客户端执行 initialize、能力协商、list、call、资源读取和提示读取；覆盖错误 audience、过期令牌、另一租户 session id、权限撤销、畸形 JSON-RPC 和超限请求。
- [ ] 确认 MCP 错误/结果格式未被管理 API ProblemDetails 或 Admin.NET 包络破坏。协议能力只能声明真正实现且验证的集合。

### T12：MCP Client 与外部能力治理

**依赖：** T11、T06。**交付：** Agent 可消费一个显式批准的外部只读 MCP 能力。

**文件：** Create MCP 的 `Client/McpClientConnectionManager.cs`、`McpRemoteToolAdapter.cs`、`McpRemoteCapabilityPolicy.cs`；Create Ai 的远端连接管理服务/强类型策略；Create `tests/Full.NET.UnitTests/Ai/McpRemoteCapabilityPolicyTests.cs`、`tests/Full.NET.IntegrationTests/Ai/McpClientInteropTests.cs`。

- [ ] 外部服务器地址、凭据引用、OAuth scopes 和允许能力分别配置；沿用 Provider 网络策略，不允许模型任意指定 URL、stdio 命令或程序参数。
- [ ] 将批准的远端 Schema/version/hash 固化为本地注册项，远端只读声明不足以获得豁免。变化项自动不可执行并要求管理员重新批准。
- [ ] 用独立测试 MCP Server 验证重连、令牌刷新/撤销、过大响应、Schema 漂移、工具提示注入、超时和断流；不需要生产外部凭据。
- [ ] 远端调用同样经过统一身份、预算、审批和审计入口；严格禁止用户 access token/Cookie 透传。外部结果未知的写工具继续保持禁用。

### T13：Embedding 与 Azure Provider 完整切片

**依赖：** T03、T06。**交付：** 规格中非聊天中立接口和 Azure 适配有真实消费者。

**文件：** Create AzureOpenAI 项目及 `AzureOpenAiModelClientFactory.cs`；扩展现有两个 Provider 的 Embedding factory；Create `src/Modules/Full.NET.Modules.Ai/Features/TestEmbeddings/Endpoint.cs` 与测试服务；Create `tests/Full.NET.UnitTests/Ai/AiEmbeddingProviderContractTests.cs`、`AzureOpenAiProviderTests.cs`；模型配置/精确权限/Vue 能力测试入口同步。

- [ ] 对一个输入和一个批量输入测试中立 Embedding 返回、维度一致性、输入规模限制、取消、计量和配额；不支持 Embedding 的模型显式拒绝，不返回假向量。
- [ ] 提供受权的管理员能力测试用例作为真实消费者；不借此新增向量库或 RAG 平台。输出大小与访问权限受限。
- [ ] Azure 区分 deployment 与 model、认证方式、endpoint 和兼容能力；凭据生命周期测试沿用隔离原则。没有真实 Azure 环境时只报告合同/模拟验证，不声明生产 Provider Verified。
- [ ] OpenAI/Ollama/Azure 适配使用同一预算/外发/Trace 管道，普通业务模块不因新增供应商改变引用。

### T14：显式工作流与多 Agent 的有界示例

**依赖：** T08、T09、T12。**交付：** 规格中的显式工作流和多 Agent 具备可复现的受控运行路径。

**文件：** Create `src/AI/Full.NET.Agents/Workflows/AgentWorkflowDefinition.cs`、`AgentWorkflowRegistry.cs`、`AgentWorkflowRunner.cs`；Create `tests/Full.NET.UnitTests/Ai/AiMultiAgentBudgetTests.cs`、`tests/Full.NET.IntegrationTests/Ai/AiWorkflowCheckpointTests.cs`；示例在测试/环境 Overlay 静态注册，不加入 Production Baseline。

- [ ] 定义“读取当前会话 → 摘要 Agent → 校验 Agent → 人工确认重命名”的版本化显式图；节点与边静态白名单，不开放任意代码或无限动态子 Agent。
- [ ] 所有子节点共享根预算与原主体的权限交集，禁止逐跳权限放大；并行节点汇总按持久状态推进，失败节点不能触发后继写操作。
- [ ] 在节点边界保存/恢复 Framework Checkpoint；验证服务重启、版本不兼容、部分并行完成、重复审批恢复、根取消及预算耗尽传播。
- [ ] 与 Workflow/DataApproval 保持职责分离；需要业务审批时调用其 Contract/可靠事件，不直接 JOIN 审批表或复制其状态机。

### T15：迁移演练、协议验收与旧实现退役

**依赖：** T04–T14。**交付：** 按已验证范围关闭能力，旧路径有明确退役条件。

**文件：** Modify `tests/Full.NET.IntegrationTests/NativeAot/NativeApiE2EAssertions.cs`、对应 Worker 原生测试、`.github/workflows/ci.yml`、`api-native-aot-linux.yml`、`worker-native-aot-linux.yml`（仅补实际影响路径）、`eng/testing/test-matrix.json`、`docs/roadmap/adminnet-feature-parity.md`、`docs/roadmap/client-delivery-roadmap.md`、OpenAPI/客户端生成物及配置说明。

- [ ] 双库演练历史数据升级、DDL 部分完成重跑、回填中断、旧版本读兼容、密钥轮换/旧密文读取、Checkpoint 版本拒绝与备份恢复；不通过 down migration 删除运行历史来回滚。
- [ ] Linux 原生进程验证真实依赖闭包下的 Chat、Tool、Run、审批恢复、MCP、AG-UI；Worker 运行路径同时遵守当前 Worker AOT 基线，不能外推 API 证据。
- [ ] 使用标准客户端而非仅自写客户端进行 MCP/AG-UI 验收；模型协议自动测试用确定性受控服务，真实付费供应商验收另行按环境证据标注。
- [ ] 集中验收 Vue 模型配置、聊天、工具审计、运行与审批页面，检查精确权限、错误、取消、重连、移动布局与可访问性；冻结 Layui 不改动。
- [ ] 只有旧 Provider 路径无调用者、兼容测试与回滚演练通过后删除重复实现；旧 SSE 端点的公开退役另有版本公告，不随内部迁移直接删除。
- [ ] 路线图分别记录 AI Chat、Agent Runtime、MCP Server、MCP Client、AG-UI、Embedding、Azure、多 Agent 状态；目录存在、编译成功或测试计划勾选均不能直接升级 Verified。

## 5. 验证命令、执行位置与停止条件

唯一执行策略源为 [开发质量 §11](../../../rules/development-quality.md#11-测试与验证)。以下命令是实施步骤的对应入口，不要求在本轮文档任务运行。

```powershell
# 每次真正开始代码任务时读取实际基线；脏工作区或跨窗口时创建快照。
git rev-parse HEAD
git branch --show-current
git status --short
pnpm test:task:start -- ai-agentic-web-alignment
pnpm test:integration:affected:plan -- --snapshot ai-agentic-web-alignment --phase inner

# T00 登记真实测试后才使用这些新聚焦集；未登记时不得假称命令已经可用。
pnpm test:dotnet:unit -- --selection ai-provider
pnpm test:dotnet:unit -- --selection ai-tools --no-build
pnpm test:dotnet:unit -- --selection ai-runtime --no-build
pnpm test:dotnet:unit -- --selection ai-protocols --no-build
pnpm test:dotnet:architecture -- --selection ai-agentic-boundaries
pnpm test:dotnet:architecture -- --selection api-native-aot --no-build
pnpm test:aot:analyzers

# 新增测试/迁移/契约时选择对应检查，不盲目全量构建。
pnpm test:integration:partitions
pnpm test:governance
pnpm test:naming
pnpm test:sql-safety
pnpm test:openapi
pnpm test:openapi:breaking
pnpm audit:dotnet
pnpm --filter @fullnet/admin typecheck
pnpm --filter @fullnet/admin test src/views/AiChatView.test.ts src/views/AiAgentToolsView.test.ts src/views/AiAgentRunsView.test.ts

# 关闭切片前规划；重型执行由取得提交/推送授权后的 Actions 承担。
pnpm test:integration:affected:plan -- --snapshot ai-agentic-web-alignment --phase slice
git diff --check
git status --short
git branch --show-current
```

- `--no-build` 只用于同一最终源码已构建后的套件；每次新源码的第一个对应套件负责 Release build。
- 现有 Unit/Architecture 矩阵当前没有上述 AI 聚焦集；T00/T01 随真实测试逐项登记，最低发现数只维护在矩阵。
- 双库测试使用现有共享数据库/Host fixture。Unit 无法证明 SQL 并发、事务回滚、Host/Tenant 唯一性和跨实例租约，因此这些场景进入 Integration；外部 MCP/Ollama 测试使用受控测试服务，不携带真实密钥。
- 本地只跑受影响的无容器快速验证；Docker、双库、真实浏览器、Linux publish/原生进程优先 Actions。未授权推送时完成本地代码与快速验证，列出远端未验证项，不擅自提交或推送。
- 共享 Composition/授权变化补双库 Smoke；新增迁移补恢复分片；SDK 接入补相应 API/Worker AOT 分析、publish 和原生调用证据。禁止改过滤器漏选 `src/AI` 或以 skip 换绿。
- 每个切片保留执行命令、commit SHA、发现数、退出码、跳过、CI 终态和未验证范围；普通证据在 CI/TRX，T15 的安全/恢复演练才按文档规则建立独立 Verification。
- 必须立即处理：越权、串租户、重复副作用、费用错误、密钥泄露、数据恢复失败、公共协议不兼容及 AOT 实际运行回归。页面布局/定位器问题可按项目规则集中验收，但不能标记 Verified。

## 6. 发布、回滚与实施顺序

推荐顺序：`T00/T01 → T02 → T03 → T04 → T05 → T06 → T07 → T08 → T09 → T10 → T11 → T12 → T13 → T14 → T15`。斜线仅表达无先后依赖，不授权自动派生代理。

| 里程碑 | 范围 | 可交付结果 | 退出条件 |
| --- | --- | --- | --- |
| M1 | T00–T04 | 现有聊天正确、中立 Provider、凭据隔离与标准错误 | 现有用户流程不退化，协议/安全回归和受影响 AOT 证据成立 |
| M2 | T05–T06 | 真正可执行的只读工具与统一预算 | 工具权限/所有权、审计、并发预算双库证明 |
| M3 | T07–T09 | 单 Agent、持久恢复与人工审批 | 重启不重复副作用、权限撤销不继续执行、审批不可重放 |
| M4 | T10–T12 | AG-UI 工作台、MCP 双向互操作 | 标准客户端、授权与重连故障矩阵通过 |
| M5 | T13–T15 | Embedding、Azure、有界多 Agent、整体迁移验收 | 逐能力证据齐全，未验证 Provider 明确保留状态 |

不以单个“大重构提交”交付。每个任务若预计超过两个工作日，按“可独立验证的成功路径/恢复路径”拆为同一任务下的子切片，任务依赖与状态仍在本文维护。SDK/AOT 风险解除前不承诺固定工期。

回滚策略：先停止接受新 Run，保持运行查询/审批审计可读；持久记录取消或排空存量执行，确认租约和外部回执收敛后再切回应用版本。数据库采用向前兼容扩展，旧代码不理解的新状态不可分发给旧 Worker。安全修复后的旧聊天路径如需临时保留，必须明确开关、到期和删除门禁，禁止自动降级回已知存在数据损失或错误泄露的实现。

## 7. 需求覆盖自审

| 总体规格要求 | 对应任务 | 必须证明的行为 |
| --- | --- | --- |
| IChatClient / IEmbeddingGenerator 与供应商隔离 | T02、T03、T13 | 普通业务不改实现即可切供应商，计量/取消一致 |
| 单/多 Agent、显式工作流 | T07、T08、T14 | 静态定义、父子预算、受控工具闭环 |
| 会话、步骤、检查点、长任务 | T07–T09 | 脱离 HTTP 执行，重启恢复，身份/版本重新确认 |
| 人审与策略豁免 | T05、T09 | 精确意图绑定、一次性消费、服务端策略，写操作不默认放行 |
| MCP Server 工具/资源/提示 | T11 | 标准客户端和逐次授权 |
| MCP Client | T12 | 远端能力批准、无 Token 透传、输出不可信 |
| 模型、Token、费用、Trace、摘要/审批审计 | T05–T09 | 实际用量与 unknown 区分，价格/模型版本可追溯 |
| 时长/步骤/工具/Token/费用预算与取消 | T04、T06–T09、T14 | 并发原子预留、父子共享预算、跨实例取消 |
| 幂等、重试、断点恢复 | T06–T09、T14、T15 | 已提交步骤不重放，外部未知结果必须对账 |
| 提示注入与数据外发 | T03、T05、T08、T12 | 文本不改变服务端授权，白名单上下文与受限目标 |
| AG-UI 与可替换预览适配 | T00、T10 | 标准事件、协议 DTO 隔离、重连不重跑 |
| 双库、Native AOT、Vue、许可 | T00、各切片、T15 | 同场景双库/原生证据与逐页面验收，不以文档替代 |

## 8. 参考来源

以下官方资料于 2026-09-08 查阅，仅支撑协议/接口方向，不能替代 T00 对锁定 NuGet 版本的编译和运行验证：

- [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai)：中立 Chat/Embedding 接口。
- [Ollama Streaming](https://docs.ollama.com/capabilities/streaming)：partial content 按片段累加，支持 F01 的协议判断。
- [Agent Framework Checkpoints](https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints)：检查点保存执行状态并支持从保存边界恢复；C# 实现按其对应语言 API 验证，不能套用 Python 存储接口。
- [MCP Authorization 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization)：HTTP 授权与资源标识参考；实际采用协议版本在 T00 固化。
- [Agent Framework AG-UI](https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/ui/ag-ui/)：Agent 与 Web 客户端协议映射参考。

## 9. 执行记录：2026-09-08 首批修复

- 快照：`ai-agentic-web-alignment`，基线仍为 `f74515d1b9e4544e4c75eeb623892970790b4d0c`。未提交、未推送。
- T00：专项 Spec 已写入并链接总体规格。NuGet 查得 AI 10.9.0、Agent Framework 1.20.0、MCP ASP.NET Core 2.2.0、AG-UI Hosting 1.20.0-preview.260831.1；仅在被忽略的临时探针锁定候选组合，尚未写入生产依赖。
- 探针：使用中立假 ChatClient 实际调用 ChatClientAgent，静态 AIFunction 注册 MCP，并映射 AG-UI 端点。启用 RequestDelegateGenerator 后 Release 编译零告警；自检退出 0。没有执行 Linux native publish、标准外部客户端或持久 Checkpoint 恢复，不将此结果外推为 T00 完成。
- T01 RED：`pnpm test:dotnet:unit -- --selection ai-provider --no-build` 发现 24 项，10 项新增回归失败、14 项已有测试通过；失败覆盖文本损失、意外 EOF、错误帧、错误正文回显、JSON 摘要、取消和响应读取预算。
- T01 GREEN：`pnpm test:dotnet:unit -- --selection ai-provider` 24/24 通过，Release 编译零告警/错误。`pnpm test:dotnet:unit -- --selection ai-module --no-build` 45/45 通过。
- 安全变化：自由文本审计摘要只保留 `[redacted]`，不再尝试用正则覆盖所有 JSON；结构化字段白名单随 T05 工具 Handler 交付。外部错误显示固定安全信息。修正了旧摘要测试上限/实际值的参数顺序。
- `pnpm test:aot:analyzers` 退出 0；`pnpm test:dotnet:architecture -- --selection api-native-aot` 73/73 通过；`pnpm test:governance` 52/52 通过。
- `pnpm test:integration:partitions` 失败：新鲜 Release Integration 程序集发现 810 项，而当前 canonical 登记 792。分片实际/登记分别为 api-sqlserver 74/74、api-mysql 74/75、migrations 442/424、infrastructure 163/162、messaging-heavy 57/57。本轮没有修改 Integration 源码或缩小过滤器，也没有降低矩阵门槛；此差异尚未修复，不能关闭 slice 验证。
- 双库实际执行、Linux 原生运行与页面验收未执行；T01 标记只表示步骤实现和本地验证，不表示整套计划或生产验收完成。
- 下一切片：继续 T00 依赖/授权闭包审查和 T02 中立客户端接入；先解决矩阵发现差异的来源，再关闭后续双库切片。

### 2026-09-08：T02 中立文本聊天切片

- 快照 `ai-agentic-web-t02`；分支/HEAD 仍为 `main` / `f74515d1b9e4544e4c75eeb623892970790b4d0c`，未提交、未推送。
- 新增 Abstractions、OpenAI、Ollama 三个真实项目，生产依赖仅新增 MIT 的 `Microsoft.Extensions.AI.Abstractions` 10.9.0，并登记第三方许可。聊天编排消费 `IChatClient`，Composition 显式注册两个工厂；Provider 不引用业务模块，业务模块不引用具体 Provider。
- RED：旧构造函数的供应商传输依赖守卫失败；绑定诊断文本泄露密文测试失败；两个 Provider 对未支持调用选项和非文本内容的拒绝测试失败。随后修复并确认 GREEN。
- `pnpm test:dotnet:unit -- --selection ai-module`：56/56，通过，无跳过，Release 编译零警告/错误。覆盖文本/usage、旧解析安全回归、取消/释放、禁用/未知模型及能力拒绝。
- `pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot' --minimum-expected-tests 39`：73/73，通过，无跳过；`pnpm test:dotnet:architecture -- --selection ai-agentic-boundaries --no-build`：3/3，通过。
- `pnpm test:aot:analyzers`：退出 0，零警告/错误；`pnpm test:governance`：52/52，通过；`pnpm audit:dotnet`：退出 0，未发现未经审查的高危或严重安全公告；`git diff --check` 通过。
- 没有执行双库、真实模型调用、Linux 原生发布/运行或页面验收；原有 Integration 810/792 发现数差异仍未关闭，未调整其门槛。矩阵新增 AI 单元/架构聚焦入口及实际新增测试最低数。
- 下一步收敛 T02 能力配置与 T03 的凭据引用、连通性迁移和目的地址策略；新增字段/策略与两租户并发验证按双库要求实施。M1 未完成，Agent、MCP、AG-UI 仍未实现。

### 2026-09-08：T03 连通性与解密迁移切片

- 快照 `ai-agentic-web-t03-connectivity`；仍在 `main`，基线 `f74515d1b9e4544e4c75eeb623892970790b4d0c`，没有提交或推送。
- RED：`pnpm test:dotnet:unit -- --selection ai-module` 发现 60 项，其中新增的三个目录误判用例与 HTTP 传输依赖守卫失败，原有 56 项通过。确认根因为目录解析回退至任意正文子串匹配，以及探测器直接依赖 HTTP 工厂。
- 实现：新增中立探测扩展点并由两个已有 Provider 实现；业务配置操作不再解密，移除模块的 `Unprotect` 方法。探测按协议解析目录，拒绝不合法响应，保持安全错误信息、有界接收和取消语义。未新增第三方依赖、数据库对象或 SQL。
- GREEN：`pnpm test:dotnet:unit -- --selection ai-module` 72/72，通过，无跳过，Release 编译零警告/错误。覆盖精确目录、畸形/跨协议响应、缺失/损坏凭据、并发配置与换钥后的逐请求头隔离、取消及客户端释放。
- `pnpm test:dotnet:architecture -- --selection ai-agentic-boundaries` 4/4；`pnpm test:dotnet:architecture -- --selection api-native-aot --no-build` 73/73；`pnpm test:governance` 52/52，均通过。`pnpm test:aot:analyzers` 退出 0，零警告/错误；`git diff --check` 通过。
- `pnpm test:integration:affected:plan -- --snapshot ai-agentic-web-t03-connectivity --phase inner` 识别 Ai、integration-matrix 与 Smoke，未漏掉新目录。`pnpm test:integration:partitions` 仍以 810/792 发现数差异失败，本次复用已有 Integration 程序集检查，未将其作为新鲜构建或双库执行证据。
- 未执行真实双库 Smoke、Linux 原生运行或外部模型调用；连通性切片实现与本地验证完成，T03 与 M1 验收仍未关闭。后续优先处理目的地址/DNS/重定向策略，然后收敛凭据引用/写入边界与网关能力配置。

### 2026-09-08：T03 目的地址、DNS 与重定向切片

- 快照 `ai-agentic-web-t03-network`；基线仍为 `main` / `f74515d1b9e4544e4c75eeb623892970790b4d0c`，未提交、未推送。
- RED：原有命名客户端允许自动重定向，新增守卫失败（其余 72 项通过）；后续新增“获批 HTTP 域名解析至公网必须拒绝”用例再次失败（其余 112 项通过），确认需要限制明文例外的实际连接地址。
- 实现：共享 Provider HTTP 项目集中静态网络规则；两个 Provider 的聊天/连通性都切至受控命名客户端，移除业务模块的旧 HTTP 注册。网络规则、精确批准配置、DNS 单次解析与按 IP 连接、代理/重定向/Cookie 禁用均纳入测试；HTTP 公网重绑定已修正。
- `pnpm test:dotnet:unit -- --selection ai-module`：最终 113/113，通过，无跳过，Release 编译零警告/错误。包含真实本机套接字的获批连接/302 不跟随验证，不访问真实供应商或元数据服务。
- `pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol' --minimum-expected-tests 40`：最终 77/77；`pnpm test:governance`：52/52，通过。`pnpm test:aot:analyzers`：最终退出 0，零警告/错误。独立安全代码审查及 HTTP 公网重绑定修正复核未发现阻断问题，仅为静态审查。
- `pnpm test:integration:affected:plan -- --snapshot ai-agentic-web-t03-network --phase inner` 包含 Ai、integration-matrix 和 Smoke。`pnpm test:integration:partitions` 仍因已有程序集发现 810/792 差异失败；未改 Integration 源码或降低门槛。`git diff --check` 通过。
- 部署兼容变化已记录在 [网络操作说明](../../operations/ai-provider-network.md)：默认不再允许任意内网/HTTP；Ollama 需可信宿主精确批准，内网 OpenAI 网关与系统代理目前不支持豁免。真实部署网络、双库 Smoke、Linux 原生发布/运行仍未验证，不将本地门禁外推为 M1 完成。
- 下一切片：凭据引用与配置写入保护边界、网关能力配置；同时继续保留双库/原生验证及 Integration 发现差异为未关闭项。

### 2026-09-08：T03 请求凭据引用与写入保护切片

- 快照 `ai-agentic-web-t03-credentials`；仍在 `main`，HEAD `f74515d1b9e4544e4c75eeb623892970790b4d0c`，未提交或推送。
- `ModelBinding` 移除密文字段，改用请求作用域的随机引用；作用域保存完整绑定与冻结选项，Provider 经读取 Port 获取密文。跨作用域、配置/版本/模型/Provider/端点/选项篡改以及已释放作用域失败关闭。取消保持取消语义，释放时清空引用。
- 配置输入通过 Provider 的 `IAiModelCredentialProtector` 保护，保留历史 Data Protection purpose 与原表字段，删除模块内旧保护实现。没有新增 SQL、迁移或第三方包。Factory、探测器与聊天调用器为 Scoped；只有实际启用 Ai 时才装配对应 Provider。
- RED 证据：密文字段守卫新增 1 项失败；独立审查发现 Ai 禁用后的悬空 DI 依赖，新增回归失败并修正；自审新增 URI 用户信息/片段篡改 2 项失败，确认 record 的 URI 值比较不够严格，额外加入完整 `AbsoluteUri` 的 Ordinal 比较后通过。
- 最终 `pnpm test:dotnet:unit -- --selection ai-module`：128/128，通过，无跳过，Release 编译零警告/错误。包括生产装配的作用域验证、模块禁用、旧保护格式和真实工厂在篡改端点后不创建 HTTP 客户端的验证。
- 最终 `pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol' --minimum-expected-tests 40`：77/77；`pnpm test:governance`：52/52；`pnpm test:aot:analyzers`：退出 0，零警告/错误。独立静态审查复核 DI 和完整 URI 修正后无阻断问题。`git diff --check` 通过。
- 影响集计划包含 Ai、integration-matrix、Smoke。`pnpm test:integration:partitions` 仍因已有程序集发现 810/792 差异失败，未修改 Integration 源码或降低门槛。真实双库、Linux 原生运行和外部模型未验证。
- 当前引用只服务同步请求，不可放入 Checkpoint、日志或跨请求复用；不宣称已实现持久密钥版本库、跨请求即时撤销或长期运行授权。后续收敛网关能力配置与 T04 聊天预检/传输边界，长期凭据及恢复验证随运行时切片完成；T03/M1 验收仍开放。

### 2026-09-08：T02 网关流式用量能力配置切片

- 快照 `ai-agentic-web-gateway-capabilities`；分支仍为 `main`，HEAD `f74515d1b9e4544e4c75eeb623892970790b4d0c`，未提交或推送。
- 新增宿主配置 `FullNet:Ai:OpenAiGateways`，按规范化完整端点与大小写敏感模型名匹配 `SupportsStreamingUsage`。未知绑定默认不发送可选 `stream_options`；显式开启才请求流式用量。无效、重复或未知能力配置在启动时拒绝，不增加网络、凭据或工具权限。
- RED：`pnpm test:dotnet:unit -- --selection ai-module` 发现 129 项，新增“未知网关不发送可选字段”测试失败，其余 128 项通过。修正后扩展为 17 项能力配置测试，覆盖精确匹配、显式关闭、非法配置和重复声明。
- 最终 `pnpm test:dotnet:unit -- --selection ai-module`：145/145，通过，无跳过，Release 编译零警告/错误。未知用量仍为 null，既有配额结算保留保守预留；不在字段被拒绝后自动重试模型请求。
- `pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol' --minimum-expected-tests 40`：77/77；`pnpm test:governance`：52/52；`pnpm test:aot:analyzers`：退出 0，零警告/错误。独立静态审查未发现阻断问题。
- `pnpm test:integration:affected:plan -- --snapshot ai-agentic-web-gateway-capabilities --phase inner` 识别 integration-matrix 与 Smoke，新 Provider 路径由 Smoke 兜底。`pnpm test:integration:partitions` 仍因已有程序集发现 810/792 差异失败，未降低门槛。真实双库 Smoke、Linux 原生运行与外部模型调用未执行。
- [网络与能力配置说明](../../operations/ai-provider-network.md) 已同步精确配置、重启生效与未知用量行为。没有新增 SQL、迁移或第三方依赖。下一切片为 T04 聊天预检与传输边界；T00、T03 长期凭据及 M1 验收继续开放。

### T04 当前实施顺序

1. 为输入、会话、模型与重复生成拒绝建立响应未启动的失败测试，再覆盖配额拒绝与标准状态映射。
2. `AiChatStreamService` 返回应用 Result 并消费模块内部输出接口；HTTP 适配器独占响应状态、SSE 编码与启动，Endpoint 用现有 Result Mapper 返回 ProblemDetails。历史读取、配额预留与发起前续租都先于响应启动。
3. 将失败消息收尾、配额结算和租约释放放入独立有界清理，终态通知在持久化与清理成功后发送；故障测试验证清理失败不会跳过释放，旧代次 SQL 条件保持不变。
4. 复用既有双库 API fixture 增加 HTTP 契约验证，补 Vue HTTP 拒绝和流错误回归，同步 OpenAPI 状态声明；执行本地聚焦验证并记录远端未验证门禁。


### 2026-09-08：T04 聊天预检、传输和清理切片

- 快照 `ai-agentic-web-t04`，仍为 `main` / `f74515d1b9e4544e4c75eeb623892970790b4d0c`，未提交或推送。
- 预检返回 Result，由 Endpoint 使用既有 Mapper 生成 ProblemDetails：正文不合法/模型不可用 422，会话不存在或不属于用户 404，重复生成 409，配额拒绝保留 403 与稳定错误码。历史读取、配额预留和发起前续租先于输出启动。输入校验前的认证/权限策略继续由宿主执行。
- 新增模块内部 `IAiChatOutput` 与 HTTP/SSE 适配器，编排不再持有 HttpContext。消息保存、配额结算与租约释放完成后才通知 done；启动后错误只发送 error。新增安全错误码 `ai.chat_generation.failed`。最终消息更新零行不再被当作成功。
- 每项失败收尾/结算/释放独立使用五秒期限与独立数据作用域；超时不提前释放尚未退出的驱动作用域，也不与后续清理共用连接。收尾仅使用原请求捕获的可信租户快照，租户停用不阻止本代清理；新推理与续租仍检查活动租户。原有 SQL 代次条件保持不变，没有新增迁移。
- RED：四项预检拒绝原先均写出 SSE；最终写入零行仍发送 done；停用租户阻止清理；已取消 SSE 事件仍写出 38 字节。均通过新增测试复现后修正。独立审查发现的停用租户问题已复核，无新增重要问题。
- `pnpm test:dotnet:unit -- --selection ai-module` 最终 161/161，通过，无跳过，编译零告警/错误。`pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol' --minimum-expected-tests 41`：78/78，通过。
- `pnpm --filter @fullnet/admin test -- src/api/ai-chat.test.ts src/views/AiChatView.test.ts`：9/9，通过；`pnpm --filter @fullnet/admin typecheck`：退出 0。客户端既有 ProblemDetails 分支可复用，无需改变页面/API 生产逻辑。
- `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --configuration Release --nologo -clp:ErrorsOnly`：退出 0，零告警/错误。新增两库 HTTP 用例验证权限、所有权、422/404/409 与持久生成槽位；编译不能替代执行，真实双库及原生运行仍待 CI。
- OpenAPI 快照按新增状态声明同步并重新运行 `pnpm openapi:client:generate`，客户端类型无额外差异；`pnpm test:openapi` 166/166，`pnpm test:governance` 52/52，通过。尚未从双库运行宿主重新导出核对快照。
- 影响集识别 Ai 与 integration-matrix。`pnpm test:integration:partitions` 使用本次新鲜 Release 程序集发现 812 项，canonical 随本次新增 2 项升至 794；原有 18 项差异仍未关闭，没有降低门槛。T04 标记为实现与本地验证，M1 不关闭；下一开发任务为 T05 统一工具执行器与显式只读工具。
- 最终 `pnpm test:aot:analyzers`：退出 0，零告警/错误；`pnpm openapi:client:snapshot -- --check --offline` 通过。最终 `git diff --check` 通过，工作区保留各轮未提交改动，分支与 HEAD 未变化。

### T05 当前实施顺序

1. 建立工具调用的中立契约、静态注册表和执行器测试；默认只允许 none/read，未知或无 Handler、版本/参数错误和身份拒绝均不派发。
2. 由 Identity 提供当前会话权威授权 Port，Ai 适配可信租户/主体；显式注册 Ping、模型列表和本人会话列表 Handler，参数源生成并拒绝未知字段。
3. 复用调用审计主键作为 OperationId：先插入执行意图，后条件更新回执；重复主键失败关闭、不重放。只保留参数哈希与输出字节数摘要，不存原始工具数据。长期 RunId 与审批仍按后续任务交付。
4. 覆盖拒绝、预算、审计失败、输出边界和双库所有权；运行本地 Unit/Architecture/AOT，双库真实执行按 CI 门禁保留。


### 2026-09-08：T05 统一只读工具执行切片

- 快照 `ai-agentic-web-t05`；分支 `main`，HEAD `f74515d1b9e4544e4c75eeb623892970790b4d0c`，未提交或推送。
- 新建 `Full.NET.Agents` 请求作用域执行器与中立 ToolInvocation/授权/审计 Port；本切片不引入 Agent Framework SDK，持久运行留在 T07。三个 Handler 显式注册：Ping、最小模型列表、本人会话列表；无自动方法发现，也尚未开放模型工具循环或 MCP 调用入口。
- Identity 新增最小 `ICurrentSessionAuthorization`，每次校验有效 JWT 会话、用户/安全戳、活动租户及权威权限；意图写入后、派发前再次确认主体/租户/会话未变化。当前切片拒绝 API Key、非空 RunId 和副作用工具；长期委托、人工审批与策略豁免尚未实现。工具目录也按当前权限和实际 Handler 过滤。
- 参数源生成、拒绝未知/重复字段；租户与用户只来自可信上下文。模型查询采用最小列，会话查询保留本人和租户条件。输出使用专用 camelCase JSON context，全部标记为不可信。
- 每个执行器作用域最多 8 次尝试、1 个在途调用；参数 16 KiB、输出 64 KiB，执行取消预算 10 秒，回执独立 5 秒。超时依赖 Handler/驱动合作取消，不提前释放仍在运行的数据库作用域，不宣称能强制终止忽略取消的驱动；跨请求/跨实例共享运行预算留给后续任务。
- 复用审计 Id 作为 OperationId：意图先插入、回执按主体/租户/started 条件更新且必须影响 1 行。重复 OperationId 由主键失败关闭，不执行第二次；原始参数/输出不落审计，仅参数 SHA-256、版本、输出字节数与 Trace 等受控摘要。回执失败不返回 succeeded。进程崩溃或回执失败可能保留 started，当前不自动重放或对账，随 T07/T09 持久运行补齐。
- 发现既有 188 的状态 CHECK 不接受执行意图，新增双库 `210_AiToolExecutionStatus.sql`，在保留旧状态/历史数据的基础上允许 started/cancelled。发布必须先运行 Migrator，再启用新工具执行；回退应用时停用新入口、保留扩展 CHECK 和历史行，不自动执行收窄约束的降级。固定条件 DDL/PREPARE 经静态复核，按既有模式登记两项精确文件级命名解析债务。
- RED 证据：执行器缺失导致新测试编译失败；真实 Handler 测试复现分页默认值反序列化为 0，改为有默认值的 positional record；独立审查指出 Items 与声明的 items 不一致，以专用源生成 context 修正并验证实际输出。
- `pnpm test:dotnet:unit -- --selection ai-module`：198/198，零跳过；`pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol|FullyQualifiedName~GlobalSql|FullyQualifiedName~IdentityContractsHub' --minimum-expected-tests 43`：81/81，零跳过。两次 Release 编译均零警告/错误。
- `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --configuration Release --nologo -clp:ErrorsOnly`：退出 0，零警告/错误。新增两库工具所有权/重复 OperationId/伪造参数用例及 210 升级、约束删除后恢复、重跑与历史行保留用例，共 4 项；只完成编译，真实执行待 CI。
- `pnpm test:naming` 31/31、`pnpm test:governance` 52/52、`pnpm test:integration:tooling` 46/46，通过。独立静态复核修正后的执行链和双库 210，无剩余阻断问题，不将静态审查当成数据库执行证明。
- `pnpm test:integration:affected:plan -- --snapshot ai-agentic-web-t05 --phase inner` 识别 Ai、Identity、210 与矩阵影响。`pnpm test:integration:partitions` 新鲜程序集发现 816、canonical 798，仍有既有 18 项差异；矩阵仅增加本任务 37 项 Unit 和 4 项 Integration，没有降低门槛。
- `pnpm test:sql-safety` 未通过：未改动的 SQL Server 011/093 存在 7 项 DROP COLUMN/TABLE 安全登记缺口，相关脚本与豁免表的 git diff 均为空；未伪造备份或审查豁免。真实双库与 Linux 原生运行仍按 CI 门禁待验证。下一开发任务为 T06 统一模型预算、价格与审计；M1/M2 均不关闭。
- 最终 `pnpm test:aot:analyzers`：退出 0，零警告/错误；`git diff --check` 通过。分支与 HEAD 未变化，保留此前及本任务所有未提交改动。

### T06 当前实施顺序

1. 建立中立预算请求/回执与价格计算测试，覆盖缺失价格、负数/溢出、重复和未知用量。请求摘要只保存 SHA-256，不保存提示。
2. 新增 211 双库 scope 锁行、操作账本和版本价格表；同一可信 scope 的预留/结算使用同一本地事务串行化，操作按原月份/Run 聚合，稳定 OperationId 摘要冲突不重扣。现有租户配额作为同事务兼容约束保留。
3. 聊天预检接入新预算入口（含 Host），独立清理结算；源生成参数/静态 SQL，单 Run 与月度默认上限来自服务端配置。当前 Provider 没有硬费用计量认证，要求硬费用的调用失败关闭。
4. 增加双库竞争、跨月、幂等、未知后补录与故障原子性测试；本地 Unit/Architecture/AOT/命名验证，真实双库及原生执行按 CI 门禁保留。


### 2026-09-08：T06 统一预算、价格与操作账本切片

- 快照 `ai-agentic-web-t06`；`main` / `f74515d1b9e4544e4c75eeb623892970790b4d0c`，保留此前改动，未提交或推送。
- 新建中立 `AiOperationRequest`、用量/价格/回执契约与 `IAiOperationBudgetStore`，Ai 实现持久预算；聊天成为首个实际消费者，Host 和 Tenant 都在启动 SSE/模型派发前预留，在独立清理作用域结算。Agent/Embedding 的实际调用分别随 T07/T08/T13 接入，不以 Kind 枚举存在宣称已有消费者。
- 新增双库 211：可信 scope 锁行、操作预算账本、追加式版本价格。scope 仅从当前可信 Host/租户编码；每次预留/结算在短事务内锁定同一范围，按原 UTC 月份和 Run 聚合，模型调用在事务外。旧租户配额作为同事务额外约束，不清空历史计数或改写旧迁移。
- OperationId 是预算幂等主键；相同完整摘要返回原凭据且 `IsNew=false`，不授权重派发；不同摘要冲突。聊天摘要绑定完整历史及配置版本/端点/组织，账本固化模型名称/ProviderKey、价格版本/币种/费率和 TraceId，不保存提示、密钥或输出。聊天 OperationId 沿用助手消息 Id，可关联所属会话；持久 Run 的主体关联在后续运行时完善。
- 服务端默认每 scope 月度 1000 请求/1000 万 Token、每 Run 100 请求/100 万 Token，可通过 `FullNet:Ai:Budgets` 配置。未知计量保留预留，允许 unknown→known 后补录；完整回执重复无操作、冲突拒绝，旧 unknown 不覆盖 known。迟到结算留在原月份，新月份不被扣减。
- 费用计算使用 decimal 并向上舍入八位小数，缓存输入单价不得高于普通输入；缺少缓存计量时按普通输入保守记账，未知价格保持 NULL。当前 Provider 没有硬费用上界认证，要求硬费用上限或配置费用上限时拒绝派发；价格维护目前为受控运维追加版本，没有开放价格 CRUD API、自动价格抓取或虚构市场费率。硬费用认证/管理面仍需后续收敛，M2 不关闭。
- Metrics 仅使用固定状态与配置币种，不使用用户、租户、模型名称或 Run 标签。指标属于事件观测，不提供持久化恰好一次保证；记账事实以账本为准。部署默认、价格语义、暂停切换与保留账本回退见 [预算运维说明](../../operations/ai-operation-budgets.md)。
- RED：新增预算能力缺失；Host 原路径跳过预留；锁等待跨月导致新旧账本月份不同；端点/组织/版本变化未改变摘要；静态账本读取器缺失。逐项修正后 `pnpm test:dotnet:unit -- --selection ai-module` 最终 223/223，零跳过、Release 编译零警告/错误。
- 独立审查另外发现 SQL Server 新价格列与既有模型列排序规则冲突，双库价格标识已对齐 BIN2/utf8mb4_bin；预留在锁后捕获一次 UTC 时间并传给兼容 Guard。摘要和行读取注册经再次静态审查，无剩余确定阻断问题。静态审查不替代数据库或原生执行。
- AOT 查询路径新增 operation/totals/price/tenant quota 的显式静态读取注册，Unit 验证 decimal 聚合与 NULL 回执。其他 AI 原生可达路径仍须由 T00 和真实原生门禁闭合，不因本轮分析通过而升级整个 AI 模块状态。
- 新增 4 项 Integration：双库预算数据路径及 211 部分迁移恢复。覆盖独立连接竞争最后一个名额、重复/冲突、跨月/Run 额度、未知后补录、Host/租户隔离、价格大小写匹配、预留/回执事务回滚和迁移重跑。真实双库尚未执行，当前只验证编译。
- `pnpm test:naming` 31/31、`pnpm test:governance` 52/52、`pnpm test:integration:tooling` 46/46。同步数据库对象注释目录与生成词典，不新增命名豁免。矩阵增加本任务 25 项 Unit 与 4 项 Integration。
- 影响集计划识别 Ai、integration-matrix、211 与 Smoke；`pnpm test:integration:partitions` 发现 820、canonical 802，既有 18 项差异保持。`pnpm test:sql-safety` 仍因未改动的 SQL Server 011/093 的 7 项 DROP 安全登记缺口失败；没有降低门槛或伪造备份豁免。
- 下一开发任务为 T07 持久化 Run/Step/Checkpoint 与 Worker；T00 原生、前序双库以及 T06 硬费用认证/价格管理面和 M1/M2 验收继续开放。
- `pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol|FullyQualifiedName~GlobalSql' --minimum-expected-tests 41`：80/80，通过。静态读取修正后再次执行；预算 Meter 已接入模块 OpenTelemetry，223 项 Unit 覆盖最终装配。
- 最终 `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --configuration Release --nologo -clp:ErrorsOnly` 与 `pnpm test:aot:analyzers` 均退出 0、零警告/错误。`git diff --check` 通过，分支和 HEAD 未变化。真实双库与 Linux 原生运行仍未执行，未提交或推送。

### T07 当前实施顺序

1. 先建立运行状态、租约代次、检查点版本和未知执行意图的失败验证；已完成步骤只读回执恢复，未确认模型调用进入 reconciliation_required，不自动重放。
2. 固定 Microsoft.Agents.AI 1.20.0（ME.AI 10.9.0），以内部适配器执行文本单 Agent 和序列化完整会话；编译/AOT 不通过时不开放运行创建。
3. Identity 提供无 HTTP 的会话绑定重验 Port；API 只保存主体/会话/授权到期与安全戳摘要，Worker 每次派发前和续租时重验。
4. 212 双库持久化 Run/步骤/检查点/事件与 Worker 心跳，短事务内原子提交；API 创建幂等、读取本人及取消，后台独立 claim/renew，租约失效/取消/过期停止后续派发。
5. 聚焦 Unit/Architecture/双库恢复测试与 API/Worker AOT 编译；真实双库和原生运行仍按 CI 门禁，默认关闭新 Run，只有匹配版本 Worker 心跳新鲜才接受。

### T07 子切片与 T07a 实施记录

按本文“大任务分为可独立验证子切片”的交付规则拆分，T07 整体仍进行中：

| 子切片 | 验收范围 | 状态 |
| --- | --- | --- |
| T07a | 真实 Agent Framework 文本执行、完整会话保存/恢复、状态规则、依赖边界 | 本轮实现，验证记录见下 |
| T07b | Identity 后台会话绑定重验，双库 Run/Step/Checkpoint/Event、租约 fencing 与原子提交 | 下一任务；迁移序号实施前重新确认 |
| T07c | Worker 领取/续租/取消/恢复、预算衔接、心跳准入与创建/读取/取消 API | 待 T07b |
| T07d | 双 Worker 与故障注入、双库/原生 CI、运行就绪门禁收口 | 待 T07c |

- 使用 Microsoft.Agents.AI 1.20.0 的实际 ChatClientAgent；第三方 SDK 类型仅出现在内部 Framework 适配器，公共接口使用 IChatClient、JsonElement 和 Full.NET 自有结果类型。UseProvidedChatClientAsIs 禁止 SDK 默认自动工具循环。
- SDK 完整序列化会话并通过新适配器反序列化；测试确认输入/输出均在会话中，恢复验证不再次访问模型。该验证仅证明 SDK 可恢复性，尚不替代持久化层的主体绑定、加密、校验和及版本门禁。
- 保留实际 Input/Output 用量及未知空值；客户端由调用方释放。已取消调用在进入 SDK 前拒绝，结果诊断文本不携带正文或会话。
- RED 证据：缺失适配器编译失败；随后安全测试发现取消仍可传递到客户端和 record 默认诊断载荷泄漏，已通过入口检查和 ToString 脱敏修复。真实 OpenAI 兼容/Ollama Provider 配合脚本 HTTP 验证单次执行与无网络恢复。
- 固定 SDK 导致 DI.Abstractions / Logging.Abstractions 从 10.0.10 升至 SDK 要求的 10.0.11；其余中心版本保持原值。NuGet nuspec 已确认 SDK、Tokenizer、AI Evaluation、Compliance 和 VectorData 传递依赖为 MIT，通知补充 Agent Framework 与 Tokenizer。
- 本轮没有注册后台运行服务或新增 Run HTTP 入口，没有创建双库运行表；不得据此宣称长任务、持久恢复、授权撤销后停止或原生运行已验证。T07b 继续按既定安全设计实现，不重用交互式 JWT。
- 独立只读审查未发现 T07a 范围内阻断问题；审查没有替代执行证据。

- 扩大验证发现并修复前序 AI 漏项：预算原生行读取器改为仅 AOT 编译，Unit 条件链接同一源文件保持实际读取验证，普通模块恢复不依赖 ADO.NET；租户写入精确边界登记已审查的 AiChatCleanupScope / AiToolAuditPort；Identity 注册预期补齐已存在的 ICurrentSessionAuthorization Scoped 映射。未放宽依赖门禁或改成模糊注册断言。
- `pnpm test:dotnet:unit --selection ai-module`：243 项通过。随后 `pnpm test:dotnet:unit` 完整复跑：2646 成功、0 失败、1 项 Linux FIFO 场景在 Windows 明确跳过；构建零警告/错误。
- `pnpm test:governance`：52 项通过；影响集 `pnpm test:integration:affected:plan -- --snapshot ai-agentic-web-t07` 成功，识别 Ai / integration-matrix / smoke。只生成计划，真实双库和 Linux 发布/原生执行仍待 GitHub Actions，不因本地测试通过升级状态。
- 最终 `pnpm test:dotnet:architecture`：219/219 通过，包含 AI SDK 依赖、原生可达规则、业务数据边界和租户上下文写入边界；未降低失败门槛。日志 `artifacts/ai-t07-architecture-verified.log`。
- 最终 `pnpm test:aot:analyzers` 与 `pnpm test:aot:worker:analyzers` 均退出 0，零警告/错误；这是编译分析证据，SDK 的 Linux 原生发布和真实调用仍待 CI。日志分别为 `artifacts/ai-t07-aot-final.log`、`artifacts/ai-t07-worker-aot.log`。
- T07a 代码切片与本地门禁完成；T07 整体未完成，下一任务明确为 T07b。最终 `git diff --check` 通过，仍在 main、HEAD `f74515d1b9e4544e4c75eeb623892970790b4d0c`，保护既有脏改，未提交或推送。

### 2026-09-12：T14 显式工作流切片

- 交付 `AgentWorkflowDefinition/Registry/Runner`、`fullnet-chat-rename-workflow-v1` 示例图、检查点读写与协调器集成；`AiAgentRunManagementService` 接受工作流 DefinitionKey。
- `pnpm test:dotnet:unit --selection ai-module`：318/318 通过；`ai-runtime` 聚焦集登记 `AgentWorkflowRunner`/`AiMultiAgentBudget`。
- 双库 `AiWorkflowCheckpoint*` 集成测试已编写，本地未跑（需 Docker）。未提交、未推送。

### 2026-09-12：T15 首批验收切片（进行中）

- 迁移：`Migration212` checkpoint、`Migration214` delegation、`Migration215` tool_approval 部分 DDL 重跑演练（+6 双库用例）；修复 `Migration214` 依赖 `fn_ai_agent_tool_call` 桩表与 MySQL 用户变量；矩阵 `migrations` 最低 460、`full` 856。
- Native AOT：`NativeApiE2EAssertions` 增补 AI 工具目录与 MCP 受保护资源元数据探针；`NativeWorkerE2EAssertions` 增补 `fn_ai_agent_worker_instance` 心跳探针。
- 路线图：`adminnet-feature-parity.md` §4.3 与 `client-delivery-roadmap.md` 登记 AI 能力 **Build-verified** 边界。
- Vue：`AiModelConfigsView`/`AiChatView`/`AiAgentToolsView`/`AiAgentRunsView`/`AiMcpRemoteConnectionsView` 组件测试 9/9 通过。
- OpenAPI/客户端契约：手工补齐 `fullnet-client-v1.openapi.json` 中 Agent Run、MCP 远端连接与 `test-embeddings` 路径（+16 operation，manifest 544）；`packages/client-contracts` 增补 `ai-mcp-remote-connections.ts` 与 embedding 测试契约；`vue-client-coverage-v1.json` 登记 `ai-agent-runs`/`ai-mcp-remote-connections` 消费方；`AiAgentToolCallListItem` 响应字段演进登记为允许的 additive evolution。
- 验证：`pnpm test:integration:partitions` 通过（full 856）；`pnpm test:openapi` 166/166 通过；`pnpm test:dotnet:unit --selection ai-module` 318/318 通过；`generated/models` 同步 `AiAgentToolCallListItem` 审计扩展字段。
- 2026-09-12（续）：SqlServer/MySQL 全链迁移 001–215 通过（`mysql-migrate-through.ps1`、Migration208/212/214/215 双库）；修复授权目录 MQTT/Cryptography 页面作用域、`RegionsErrorCodes.Prefix` 尾点号；`pnpm openapi:client:snapshot --update/--check` 双库通过；`OpenApi_documentation_follows_contract` SqlServer+MySQL 2/2 通过；manifest 登记 `tenancyGet*Branding*` 为 `publicOperationIds`；`openapi-contract-compatibility.mjs` 增补整数参数/Schema 元数据、ProblemDetails 响应与公开 security 降级豁免；`AiModule.AddServices` 注册 `AiAgentWorkerHeartbeatService` 以修复 Agent Run API DI。
- 2026-09-12（续 2）：`AiModule.AddServices` 注册 `AiAgentWorkerHeartbeatService`；`AiAgentRunApiAssertions` 改为先断言无心跳时 422、再种子心跳（移除 `CreateIsolatedFactory` 跨实例 JWT 密钥不一致导致的 401）。
- 2026-09-13（续）：`AiAgentRunManagementService.CreateAsync` 在预算预留前加载模型配置并传入 `ProviderKey`/`ModelId`（修复 `ai.budget.invalid_request`）；本轮验证通过：`Migration212` SqlServer、`Migration213` SqlServer、`AiOperationBudgetApiSqlServerTests`、`AiChatApiSqlServerTests`（4/4 约 25s）。`FullyQualifiedName~AiAgentRunApi` / `AgUiProtocolSqlServerTests` 等在 `POST /api/v1/ai/agent/runs` 路径仍挂起 >5min（需单独排查 HTTP 处理链/预算行锁，运行前须 `taskkill` 残留 `dotnet`/`testhost`）。
- 2026-09-13（续 2）：修复 Agent 工具 DI 环（`IAgentToolRegistrySource`/`AiAgentApprovalConsumption`/`AiAgentRunApprovalGate`）；审批后工具执行幂等（`TransitionDeniedApprovalToStarted`/`TouchExistingCall`）；集成测试心跳与身份种子（`AiAgentRuntimeTestSettings`、`CreateHostIdentityAsync` 角色权限）；委托审批可读（`GetOwnedAsync` 允许代行读取版本）；SqlServer 租约领取改为 UPDATE+`SelectLease` 统一路径。本地顺序验证通过（双库各 2 项）：`Approval_api_preserves`、`Tool_execution_preserves`、`Delegation_api_preserves`、`Agent_run_lease_preserves`、`Stream_replays`、`AiAgentRunApi`、`AgUiProtocol`；`Full.NET.UnitTests.Ai` 323/323。
- 2026-09-13（续 3）：`pnpm openapi:client:snapshot` 与 `--check` 通过（SqlServer/MySQL 运行时快照）；MCP 集成专测修复：`McpLoopbackHttpClientTestSupport` 将 `McpClientConnectionManager` 出站 HTTP 绑定到 `WebApplicationFactory` 测试宿主；`McpServerAuthorization` 补 `application/json` Content-Type；`McpServerInterop` 资源摘要改为 JSON 字段断言。
- 2026-09-13（续 4）：`b116dd1d` 修复 `generate-fullnet-client.mjs`（SSE/ webhook / PDF / Stream 误标 JSON）；`pnpm test:openapi` 166/166 PASS。`Production_global_sql` 1/1、`Full.NET.UnitTests.Ai` 323/323、`Migration210`–`215` 18/18 PASS。AI 集成顺序验证（双库各 2）：Approval、Tool、Delegation、Lease、Stream、AiAgentRunApi、AgUiProtocol、McpServerAuthorization、McpServerInterop PASS；`McpClientInterop`/`McpRemoteConnectionApi` MySQL 因 `215` 误用 `char(36)` 与 Binary16 不一致失败，已改 `binary(16)`。Vue AI 视图单测 8/8 PASS。
- 2026-09-13（续 5）：`490c995d` Native AOT 分析器门禁 `pnpm test:aot:analyzers` PASS。`AgentToolLoop`/`AgentToolLoopJson`（源生成 JSON）、`AgentWorkflowRunner`（`WorkflowRenameSessionArguments`）、`Full.NET.AgenticWeb.Mcp`（`McpJsonSerializerContext`、远端参数手工解析）、`AiAgentRunCoordinator`（`AiWorkflowSessionSnapshot`）消除 IL2026/IL3050。`pnpm test:integration:partitions` PASS（full 856）；`Full.NET.UnitTests.Ai` 323/323 PASS。旧 Provider 退役审计：模块内已无 `AiApiKeySecretProtector` 与独立供应商 switch 路径；聊天/Agent 统一经 `IAiModelClientFactory` + `AiChatCompletionStreamer`；`AiChatQuotaGuard` 仅作预算账本兼容层保留。未推送。
- 2026-09-13（续 6）：工作区无新代码待提交（`490c995d`/`31bb3967` 已落地）。复跑 `pnpm test:aot:analyzers`、`pnpm test:integration:partitions`（856）、`pnpm test:openapi`（166/166）PASS。本地 Docker Desktop 未运行（`npipe://./pipe/docker_engine` 超时），`Migration210`–`215`、`McpClientInterop*`、`openapi:client:snapshot --check` 集成依赖项阻塞；需启动 Docker 或推送本地提交触发 Actions 完成双库/Linux native 验收。
- 2026-09-13（续 7）：启动 Docker 后本地顺序验证 PASS：`Migration210`–`215` 14/14（双库恢复演练）；核心 AI API 12/12（Approval、Tool、Delegation、Lease、AiAgentRunApi、AgUiProtocol）；MCP 8/8（Authorization、Interop、ClientInterop、RemoteConnectionApi）；`pnpm openapi:client:snapshot --check` SqlServer+MySQL；Vue AI 视图 9/9；`Full.NET.UnitTests.Ai` 323/323；`pnpm test:aot:analyzers` PASS。未推送。
- 2026-09-13（续 8）：`FullyQualifiedName~Full.NET.IntegrationTests.Ai` 全量顺序验证 **34/34** PASS（含 Chat、Budget、Quota、WorkflowCheckpoint、ApprovalRecovery 与上述 MCP/Run/AG-UI 切片）。T15 双库集成与协议验收本地门禁已关闭；仍缺 Linux native 进程 E2E（Actions）与 Vue a11y/移动端人工验收。
- 2026-09-13（续 9）：推送 `28cdddcd` 触发 Actions；`api-native-aot-linux` publish 因未登记 `Serilog.Sinks.Elasticsearch`/`Elasticsearch.Net`/`System.Linq.Expressions` ILC 告警失败。补登记 `api-native-aot-publish-warnings.mjs` 与 governance 测试后重推。
- 2026-09-13（续 10）：`bf83589c` Actions publish 已通过；E2E 4 失败为 SerialNumbers/Workflow 60s 超时（未执行到 `VerifyAiModuleNativeClosureAsync`）。新增 `NativeApiAiE2ETests` 双库独立探针（agent-tools + MCP metadata），`nativeAotIntegration.minimum` 7→9。
- 2026-09-13（续 11）：`b43bc875` 补充 `AiMcpRemoteToolRecord` Native AOT 行物化器（预防性）；CI `34755218231` 日志显示 agent-tools 500 实为 `IReadOnlyList<AiAgentToolCatalogItem>` 未纳入 `AiJsonSerializerContext`。
- 2026-09-13（续 12）：`6d43085a` 在 `AiJsonSerializerContext` 注册 `IReadOnlyList<AiAgentToolCatalogItem>`；CI `34756767453` **AI 独立探针双库 Passed**（`NativeApiAiE2ETests`）。workflow 仍 4 失败（SerialNumbers/Workflow 60s 超时，与 AI 无关）。
- 未执行/未关闭：共享 Native E2E 超时、Worker 原生 DI 启动失败、Vue 集中 a11y/移动端人工验收。T15 AI Native 闭包验收已绿，整体未关闭。
