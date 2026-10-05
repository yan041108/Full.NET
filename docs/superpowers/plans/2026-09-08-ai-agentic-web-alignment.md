# Full.NET AI 与 Agentic Web 架构对齐开发计划

> 执行约定：按任务逐项实施并记录实际验证证据。遵守根目录 AGENTS.md；不自动创建工作树、派生代理或切换独立执行流程。2026-10-02 用户授权每项开发完成并通过验证后自动提交；只提交本任务改动，不自动推送。

**Goal:** 将现有 AI 管理与聊天模块演进为供应商中立、工具执行受控、可持久化恢复并支持 MCP/AG-UI 互操作的官方能力；下一阶段补齐带权限和引用的知识库问答、质量评估、版本治理与受控路由。

**Architecture:** 保留 `Full.NET.Modules.Ai` 作为 AI 管理业务和 `fn_ai_*` 数据的唯一所有者；通过反向实现存储及策略 Port，为独立 AI、Agent 和协议适配项目提供能力。供应商和 Agent Framework 的具体类型不进入普通业务模块核心或 Full.NET 稳定公开契约。先修正已有行为，再按可独立验收的纵向切片迁移。

**Tech Stack:** .NET 10、Microsoft.Extensions.AI、Microsoft Agent Framework、MCP C# SDK、AG-UI、Dapper、自有 SQL/事务执行器、SQL Server/MySQL、System.Text.Json 源生成、Vue 管理端、Microsoft Testing Platform。

## 0. 状态、授权与代码基线

### 2026-10-02 开发交付摘要

- 当前用户明确要求“根据 AI 开发计划，进入开发交付”，随后要求“提交代码（以后每一项完成开发就提交）并继续下一项执行”；按任务完成验证后提交，再顺序推进，不自动推送或派生代理。
- 本轮交付 R02：冻结中文合成语料、测试项目内离线评分器、人工参考结果、可重放报告入口及对应失败回归。R02 离线基线已本地验证；真实检索/模型端到端基线明确为未测，随 R06 接入。
- R02 已提交 `4a748409af6fcddd23b441a182ee3e7e8fe2b47f`。随后交付 R03 独立选型实验及 [ADR-0013](../../architecture/adr/ADR-0013-ai-retrieval-provider.md)：首版采用双库存派生向量、SQL 先过滤、有界精确余弦；Qdrant 保留候选，PdfPig 0.1.16 作为 R05 文本 PDF 接入候选。没有新增生产依赖或功能开关。
- 实施基线：分支 `codex/foundation-acceptance-20260926`，HEAD `a7a68776818731703f1ebc11bc529f2746df59e1`，任务快照 `ai-rag-evaluation-r02`。保留原有日志文档、code-wiki 与语言资源改动。
- R04 的目录、模型版本审批、用户/角色/机构两层授权、文件版本与导入恢复已按 R04a、R04b1–R04b6 分项本地验收，并于 2026-10-04 完成收尾核对。R05a 有界 TXT/Markdown 解析基础、R05b 有界 Token 分块核心、R05c 标题/段落/表格上下文分块基础、R05d 固定真实词表/精确模型配置绑定与 R05e1 PDF 分页输出验收/页内模型分块已完成内部切片本地验收。R05e2 真实 PDF 独立工具、本地 Docker 硬隔离与 Linux Native AOT 发布/运行已验收，尚未接入业务导入或 Worker。R05d 的完整 Host Linux 发布尝试因内存不足/资源压力未完成，分块器原生运行仍未验证；R05f1 索引任务持久化、双库恢复和代次/租约隔离，以及 R05f2a 派发前权威快照已完成内部切片本地验收；后续继续 Worker 执行、派发前权威复核、预算账本及索引写入。R05–R12 未完成，完整知识库/RAG 保持 Planned，Qdrant 未生产选用。R01 既有未关闭门禁不因这些切片自动关闭；当前验收执行位置遵守开发质量 §11 的本地验收规则，替代下方历史记录中的 Actions 前置。

### 2026-09-27 当前摘要（替代下方历史状态中的未实施判断）

- 本文继续作为 AI 主题唯一活动计划；不另建竞争计划。新增任务 R01–R12 见 §7.1；R01 已实现并通过本地单元验证，原生/双库切片门禁尚未关闭，R02–R12 保持 Planned。
- 授权：当前用户要求“按你的建议完善我们的系统，你更新相关文档和安排开发计划”。本次交付为设计、计划和能力状态修订；不自动实施代码、提交、推送或派生代理。
- 源码已具备 MAF 适配、统一模型 Provider、Embedding 调用、工具执行、预算、持久运行/审批和协议相关实现。旧摘要“T07–T15 尚未实施”已失效；历史证据留在原执行记录，未逐项重验的任务不批量改为 Verified。
- 当前缺口：完整知识库入库/检索/引用闭环、系统化质量评估、Prompt 发布治理和业务路由；现有静态会话重命名工作流不代表完整多智能体平台。
- R01 已修正 `NOT VALID` 子串误判和恢复路径跳过校验的问题，使用封闭 JSON 与写入前复核；此项不表示知识库或其他多智能体任务已经实施。
- 设计权威：[专项设计 §3](../specs/2026-09-08-ai-agentic-web-security-runtime-design.md#3-知识库与受控协作增量设计2026-09-27)。保留模块化单体、双库、AOT 与工具安全基线；Qdrant 尚未选定。
- 本次依据源码只读核对和用户提供的文章；未运行模型、双库、浏览器或原生性能实验。旧任务未关闭的真实门禁继续保留，不被新增任务覆盖。

### 2026-09-08 初始基线与历史状态

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

## 7.1 知识库与受控协作开发队列（2026-09-27）

### 排期、依赖与完成定义

以交付里程碑安排顺序，不预先承诺固定周数。每个任务由实施者负责功能和证据，涉及权限、数据与公共架构的切片关闭前进行独立审查；不因本文自动启动多代理。所有任务初始为 Planned；开始时登记基线和实际影响集，完成时记录精确提交/CI 与尚未验收项。

| 里程碑 | 顺序 | 可交付结果 | 前置条件 |
| --- | --- | --- | --- |
| M1 安全与质量基线 | R01 → R02 → R03 | 严格工作流结果、可重复评估、明确检索 Provider 决策 | 复核旧 T00–T15 未关闭门禁；不重做已有能力 |
| M2 知识库首个闭环 | R04 → R05 → R06 → R07 | 可导入、可查状态、按权限问答、可查看真实来源 | R03 决策完成；R02 评估集贯穿实现 |
| M3 运营与质量完善 | R08 → R09 | 版本切换/删除恢复、受控 Prompt 发布、反馈和检索优化 | M2 闭环可用 |
| M4 业务协作与验收 | R10 → R11 → R12 | 节点恢复、受控路由、全链路观测及原生/页面验收 | R01/R09；路由前节点恢复语义已明确 |

开发优先：每项同步完成必要失败用例、类型/契约与安全验证；页面视觉、全链路容量和集中人工验收收敛到 R12。SQL/租户/授权/真实运行回归不延后。R03 的证据实验只决定选型，不代表正式能力已经 Verified。R05/R06 的新增解析、Embedding 与检索可达路径必须在各自切片关闭时验证受影响 AOT 分析和原生产物，按 §11 使用获授权的 Actions；不能等到 R12 才发现依赖不兼容。无原生证据时明确保留切片门禁未关闭状态。

文件表中的新文件为拟新增责任位置，目录不要求预先建空文件；沿实际消费者决定最小 Port。以下 DTO 名称为内部设计名称，公开 API 与 Schema 在对应切片先定稿并生成客户端，不能跨任务各自定义同名不同义契约。

### R01：严格校验工作流结果（P1，首先执行）

**修改：** `src/AI/Full.NET.Agents/Workflows/AgentWorkflowRunner.cs`、`AgentWorkflowRegistry.cs`；`tests/Full.NET.UnitTests/Ai/AgentWorkflowRunnerTests.cs`。在 Workflows 目录新增 `AgentValidationResult.cs`，并接入该运行时已有源生成 JSON 上下文。

**契约：** 校验节点只接受 `{"decision":"approved"}` 或 `{"decision":"rejected"}`；缺字段、未知枚举、重复字段、多余非 JSON 文本与解析失败均停止后续写工具。模型输出与程序的标题长度/空白规则独立校验；旧检查点按定义版本兼容或明确拒绝，不能静默改变其含义。

- [x] 建立失败回归：自由文本、重复字段/额外字段、未知 decision、畸形 JSON 和恢复到写节点时的无效结果均不得调用 rename；原实现复现误放行后再修正。
- [x] 更新 Prompt、源生成结果解析及定义版本；写入前复核结果与标题，保留人工审批，拒绝旧版恢复。
- [x] 执行 `pnpm test:dotnet:unit -- --selection ai-runtime` 与同一 Release 产物的 `pnpm test:dotnet:unit -- --no-build --selection ai-module`，均通过，无跳过。

R01 实施记录（2026-09-27）：任务基线 `4c171501b3d5ab9ff6d13a57bc8a149cb046160a`，快照 `ai-workflow-validation-r01`。稳定 DefinitionKey 不变，DefinitionVersion 从 1 升为 2；创建、请求摘要和检查点使用同一新版。旧运行返回 `ai.agent_run.definition_incompatible`，不自动迁移审批或重新执行工具。发布时关闭新派发、停止旧 Worker 后同步部署 API/Worker，再重新开放；禁止旧 Worker 与新版混跑。旧运行保留审计/账本，由操作者检查既有副作用后使用新的 ClientRequestId 创建新运行，不能自动重试旧写操作。反向回滚也须先停派发与新版 Worker，版本 2 的存量运行不得交给旧 Worker。

本地治理与 Integration 分片检查通过；`pnpm test:aot:analyzers`、`pnpm test:aot:worker:analyzers` 均成功且零警告/错误。静态复核发现的双库检查点夹具版本已同步。实际双库 Integration、Linux Native 发布/原生运行仍待获授权的 Actions，不能标记整体 Verified；后续 R02–R12 未执行。

### R02：建立质量评估基线（P1）

**新增：** `tests/Full.NET.UnitTests/Ai/RagEvaluationScoringTests.cs`、`tests/ai-evaluation/rag-cases.json`、`tests/ai-evaluation/README.md`。实现时把离线评分器放入测试项目；真实模型运行独立配置，不进入默认联网单元测试。

**输入/输出：** 样例包含 caseId、问题、文档版本/允许来源、预期证据片段和是否应拒答；结果记录数据/模型/Prompt/索引版本、候选及引用、回答、用量和耗时。安全样例单独计分，不能被平均分掩盖。

- [x] 建立覆盖中文术语、编号、表格、跨段落、冲突版本、无答案、跨租户/撤权、注入的合成语料；本轮全部原创合成，不含真实敏感信息，采用仓库 MIT 许可。
- [x] 对 Recall@K、引用来源有效性、拒答与泄漏评分编写固定输入输出测试；语义支持情况由标注/受控评审单独记录，不能用“有引用”自动判定正确。
- [x] 记录初始离线参考基线与冻结质量门槛；安全失败要求为零，候选必须使用相同版本、摘要、策略和完整样例。真实端到端状态为 `not_measured`。
- [ ] R06 接入后在相同冻结语料上生成首份真实检索/模型端到端基线；本项是后续集成门禁，不将人工参考答案当作 RAG 验收。

R02 实施记录（2026-10-02）：实现与契约见 [`tests/ai-evaluation/README.md`](../../../tests/ai-evaluation/README.md)。评分器和严格 JSON 加载均在单元测试项目中；未新增生产依赖或模型调用。报告独立显示安全失败与安全子集结果，完整生成依赖也需授权；重复候选不能提高召回，缺失/重复样例直接拒绝，未知用量不记零，不同币种不直接求和。人工参考结果的质量指标为 1、安全失败为零，属于离线评分器基线。

本地 Windows、.NET SDK `10.0.401`、Node `v24.12.0`、pnpm `10.26.0`：`pnpm test:dotnet:unit -- --selection ai-evaluation` 完成当前源码 Release build，零警告/错误，聚焦测试全部通过、无跳过；同产物 `pnpm test:dotnet:unit -- --selection ai-module --no-build` 全部通过、无跳过。最低发现数只维护在测试矩阵。实际发现数与退出码保存在执行输出，参考报告位于本地 `artifacts/ai-evaluation/r02-reference-report.json`；不提交运行工件。`pnpm test:governance`、`pnpm test:integration:tooling`、`pnpm test:integration:partitions` 及矩阵契约检查均通过。本轮仅修改离线评估与测试数据，不涉及生产 SQL、宿主、Provider 或 AOT 可达路径；slice 影响规划因矩阵修改选择 `integration-matrix` 工具验证，未选择数据库 Integration。未运行真实模型、双库、浏览器或 Native AOT，不升级这些能力状态。

### R03：检索 Provider 与解析器选型证据（P1，基础设施决策门）

**检查：** `Directory.Packages.props`、`src/AI/`、`rules/native-aot.md`、`rules/naming-conventions.md`、现有 Files Contract。证据形成后更新本专项 Spec；新增 ADR 文件编号在实施时从 `docs/architecture/adr/` 分配，名称使用 `ai-retrieval-provider` 主题。

- [x] 对照当前实际可用的 R02 冻结合成语料（12 问题/13 片段），比较双库可用路径与 Qdrant 的过滤、中文/编号探针、重建及运维成本；同一 SQL 预过滤和受控精确余弦在双库得到一致业务语义。没有生产语料/规模，实际模型与目标规模门禁由 R05/R06 关闭，不外推容量。
- [x] 锁定 NuGet/镜像版本，完成写入、租户/来源过滤、删除、超时、取消及 Linux Native AOT 原生调用；核对文本 PDF/MD/TXT 范围、许可与预算。解析同步调用的硬内存/解压/时限隔离是 R05 正式接入门禁，不由文件/输出限制替代。
- [x] 在 ADR-0013 记录权威源、派生恢复、Files 最小契约、凭据/网络边界、部署关闭及回退，选择保持现有数据库的有界首版路径；未扩大长期基础设施授权。
- [x] 候选依赖仅在独立测试项目，未进入生产中央依赖/解决方案/宿主；未启用 Qdrant，未生成后续任务空实现。

R03 实施记录（2026-10-02）：分支 `codex/foundation-acceptance-20260926`，基线 `4a748409af6fcddd23b441a182ee3e7e8fe2b47f`，任务快照 `ai-retrieval-provider-r03`。实现、复现及精确环境见 [R03 实验说明](../../../tests/ai-evaluation/RETRIEVAL-PROBE.md)。`node tests/ai-evaluation/run-retrieval-probe.mjs` 最终退出 0，独立原生程序 PDF/文本 11、SQL Server 22、MySQL 21、Qdrant 23 项观察全部通过；manifest 为 passed/resourcesRemoved，源码摘要前后相同。复用 ADR-0008 的四条已登记程序集级告警，不抑制新告警。报告明确真实 Embedding 为未测；小允许集的 Recall 不证明排名，另有 12 候选的合成编号 Top-1 一致性探针，不代表真实语义质量。

验证：候选边界先观察 7 项 RED，再实现；`pnpm test:dotnet:unit -- --selection ai-retrieval-candidates` Release 构建零警告/错误，7/7 通过；同产物 `pnpm test:dotnet:unit -- --selection ai-module --no-build` 394/394 通过。`pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol' --minimum-expected-tests 42` 79/79 通过。`pnpm test:governance` 57/57、`pnpm test:integration:tooling` 54/54；`pnpm test:integration:partitions` 验证 1106 项无遗漏/重复，均退出 0。实验依赖图漏洞审计未报告已知漏洞。正式能力、真实模型、公开权限/文件引用竞态、目标规模和 Host.Api/Worker 生产闭包均未因此验收。

独立只读审查指出匿名数据卷清理及正文取消同步问题，修正后复核通过；同时补明确小候选 Recall 的限制与宽范围合成排序探针。审查核对最终 manifest 与源码摘要一致，不替代原生运行证据。`pnpm test:integration:affected:plan -- --snapshot ai-retrieval-provider-r03 --phase slice` 仅选择已通过的 integration-matrix 工具验证；无生产 SQL/可达路径变更，本次双库证据来自独立实验而非正式业务 Integration。提交前执行本任务 `git diff --check` 并核对分支/status；保留任务开始前的日志文档、code-wiki 和语言资源改动。

### R04：知识库目录、权限与文件引用（P1）

**R04a 本地验收（2026-10-02）：** 实施基线 `37fef643c91f639927359dd41d6232f61b985baa`，任务快照 `ai-knowledge-management-r04`；保留原有日志文档、code-wiki 和语言资源改动。新增目录及模型配置依赖的 AOT 静态物化；原生模型创建先复现 500，再修复并通过双库原生产物验证。操作契约见[私有目录与审批指南](../../operations/ai-knowledge-bases.md)。

- `pnpm test:dotnet:unit -- --filter FullyQualifiedName~Full.NET.UnitTests.Ai --minimum-expected-tests 406`：406/406；受影响 Architecture 过滤 AI、NativeAot、MemoryPack、SQL Scope 和模块数据访问：79/79。`pnpm test:aot:analyzers`：0 告警、0 错误。
- `pnpm test:integration:affected -- --snapshot ai-knowledge-management-r04 --phase slice`：Windows 38 项通过、27 原生项跳过，原命令退出非零，不记为通过。使用矩阵原生过滤器在隔离 Linux SDK 容器运行正式 API/Worker 产物：25 项通过，2 个 Production Kafka 用例因副本位于 `/tmp` 触发 Key Ring 安全校验；移到 `/work/Full.NET` 后同两用例 2/2 通过。按测试身份核对最终影响集 65 项均有实际通过证据，无遗漏和跳过；保留原失败 TRX 与重试 TRX，未修改安全校验。
- `pnpm test:aot:publish:linux` 及 `pnpm test:aot:publish:linux -- --host worker`：正式 linux-x64 链接成功，产物分别 133691920/86214280 字节，告警门禁分别接受已批准的 17/15 项。Linux SDK 10.0.400、Docker Desktop/WSL2；运行测试限制 2 个处理器，Docker 内存上限约 31.22 GiB。此为功能/原生运行验收，不代表容量或生产 SLO。
- `pnpm test:sql-safety` 5/5、`pnpm test:naming` 33/33、`pnpm test:governance` 57/57、测试工具链 54/54；Integration 分片无遗漏/重复。全量 Architecture 为 231/232，既有 `auditing.operations.details.read` 命名检查失败，已对照基线确认，本切片未修改或宣称其通过。只读代码复核未发现剩余实现问题；本任务 diff 检查通过后提交。

2026-10-02 按大任务规则拆为可独立验收的两个切片，R04 总体保持进行中：

- [x] R04a：私有知识库目录及模型处理审批。新增 `AiKnowledgeContracts.cs`、`AiKnowledgePermissions.cs`、`Domain/AiKnowledgePolicy.cs`、`Features/ManageKnowledgeBases/` 与 `Persistence/AiKnowledgeSql.cs`；240 双库迁移保存所有者/可信租户、目录状态、分类、Embedding/生成模型配置及批准版本。创建只允许目录字段，模型批准初始为空；更新审批须独立精确权限。每个读取/写入均校验可信范围和所有者，版本不符 409，不返回跨资源信息。单元先测未知分类、缺少/漂移批准及严格 JSON；双库 HTTP 测精确权限、Host/租户/所有者隔离、乐观并发与默认拒绝，迁移测半完成与重复执行。运行受影响 Unit/Architecture、双库 Integration、命名/注释、AOT 分析和正式原生路径后提交。
- [x] R04b：文档/版本/成员与文档授权、上传、Host Claim/Probe、Tenant 资源所有权、导入任务及权威删除/撤权。已复用既有目录交付授权成员访问、角色/机构范围及共享生成客户端；文件失败/恢复、双库与原生证据和收尾核对见 R04b1–R04b6。R04 的目录、权限与文件引用范围于 2026-10-04 关闭；解析、检索索引及问答仍未实现，不能以文件生命周期完成或预留接口冒充完整 RAG。

R04b 执行顺序：先交付知识库成员授权与立即撤权（所有者写入、受授权成员读取，可信范围及精确端点权限独立生效）；再交付文档版本、文档级授权与文件生命周期；最后生成客户端并核对角色/组织范围及全部文件故障场景。每个独立切片验证后提交，R04b 总勾选须等待所有子项完成。

- [x] R04b1：新增 `fn_ai_knowledge_member` 及成对恢复迁移，公开有界成员读取/整量替换请求；成员仅由所有者管理，写入独立精确权限，按知识库 `Version` 防止覆盖。用户候选经 Identity 最小 Host/可信 Tenant 活动成员目录核对，不查询 Identity 表。目录读取使用本模块参数化 SQL 联合知识库/成员记录；成员只能读取启用目录，目录编辑和模型审批仍限定所有者。撤权直接提交权威成员状态，不等待缓存或索引失效。Unit 先覆盖名单上限、重复/空标识、所有者排除和版本边界；双库 HTTP 再覆盖权限、禁用与租户隔离、授权后可读、撤销后立即不可读、跨租户候选拒绝、越权改成员及乐观并发。角色/组织与文档级授权留给后续 R04b 子项，不以此关闭全部共享授权。

**R04b1 本地验收（2026-10-02）：** 基线 `8d577621fe781b4e9189ff5031166a6e6894eae6`，快照 `ai-knowledge-members-r04b1`，分支 `codex/foundation-acceptance-20260926`。本切片只交付明确用户成员；角色/组织、文档授权与文件生命周期继续开放。

已提交 `b00fbf1cb163f1d915d7477cf7ab4e612a7ece96`（`feat(ai): add scoped knowledge members and immediate revocation`），未推送；随后沿本计划进入 R04b2a。

- 成对新增 241 成员迁移；所有者整量替换最多 100 个唯一用户，共享知识库版本并在 AI 本地短事务中原子提交。候选经 Identity Host 批量目录或可信 Tenant 活动成员目录核对，跨模块读取在写事务外；成员读取启用目录，目录编辑、模型审批和成员管理仍限定所有者。撤权以本模块权威 SQL 立即生效，不依赖缓存或索引。
- 最终 `pnpm test:dotnet:unit -- --filter 'FullyQualifiedName~Full.NET.UnitTests.Ai|FullyQualifiedName~Full.NET.UnitTests.Identity' --minimum-expected-tests 837`：837/837，零跳过。新增成员验证曾先复现失败；双库 HTTP 覆盖独立权限、禁用/未知候选与无部分写入、跨范围、并发版本、目录停用与空名单立即撤权，241 覆盖索引/注释恢复、重跑、授权保留及重复/悬空关系拒绝。
- 最终 `pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol|FullyQualifiedName~SqlStatement|FullyQualifiedName~ModuleDataAccess|FullyQualifiedName~IdentityOidcBoundaryTests' --minimum-expected-tests 86`：86/86，零跳过。`pnpm test:aot:analyzers`：退出 0，零警告/错误。未复跑全量 Architecture；前序记录的 Auditing 权限命名失败未由本任务修复，不宣称全量通过。
- 真实原生 API 先在角色授权复现 500，补齐 `IdentityRolePermission` / `IdentityUserRole` 静态参数绑定；真实首次改密再复现 500，补齐共用的 `ConsumeRefreshSessionUpdate` 四字段绑定。测试通过现有 Cookie/CSRF 登录辅助履行首次改密；保留安全日志脱敏、强制改密和静态闭包规则。两轮失败各 4/10，原始 TRX 单独保留，没有改写为通过。
- 最终 `pnpm test:aot:publish:linux` 与 `pnpm test:aot:publish:linux -- --host worker` 均退出 0；正式产物分别 133822032 / 86251944 字节，告警门禁接受既有 17 / 15 项。Linux SDK 10.0.400、Windows SDK 10.0.401、Docker 内存约 31.22 GiB，运行限制 2 个处理器；这里只验收功能与原生路径，不代表容量或生产 SLO。
- 最终 `pnpm test:integration:affected -- --snapshot ai-knowledge-members-r04b1 --phase slice` 含 Ai、Identity、241 与完整原生过滤器，发现 232 个去重测试身份。Windows 205 项通过、27 项原生跳过，原命令退出非零，不记为通过；隔离 Linux SDK 容器直接运行最终正式产物，API 10/10、Worker 17/17 通过，零跳过。逐测试身份核对最终 232/232 实际通过，无遗漏，任务源码哈希未变化。`artifacts/native-aot/linux-x64/r04b1/after-final-fix/` 保存最终发布清单、源码哈希、TRX 与 `verification-summary.json`；原失败记录保留在父目录及 `after-fix/`。
- `pnpm test:sql-safety` 5/5、`pnpm test:naming` 33/33、`pnpm test:governance` 57/57；测试工具链 54/54、Integration 分片 1114 项无遗漏/重复。只读安全与原生闭包审查未发现剩余确定问题；成员关系、首密登录和三处参数绑定均由真实原生链路验证。本任务 diff 检查后独立提交，保留既有无关改动；下一项为下方 R04b2。

R04b2 下一项实施边界（复用本计划，不新增平行计划）：

- [x] R04b2a：先交付可独立使用的文档草稿目录与明确用户文档授权。新增 AI 自有文档/成员表及成对恢复迁移；所有者用独立创建、编辑、删除、成员管理权限维护草稿。读取同时核对启用知识库、可信范围、知识库成员和文档成员，标题及描述也受文档授权保护；软删除和空名单撤权立即禁止读取。整量授权名单仅允许当前知识库明确成员，文档 `Version` 防止覆盖；目录分页有界。不接受正文、任意 FileId、模型或索引状态输入，无文件版本时返回固定 `draft`。Unit、真实双库 HTTP、恢复及原生证据通过后单独提交；R04b2b 再接入下面的版本/上传/导入故障链路，R04b2 和 R04b 保持未完成。

**R04b2a 本地验收（2026-10-02）：** 基线 `b00fbf1cb163f1d915d7477cf7ab4e612a7ece96`，快照 `ai-knowledge-documents-r04b2`，分支 `codex/foundation-acceptance-20260926`。范围仅为草稿元数据及明确用户文档授权，不包含内容版本、文件或索引。

- 元数据及权限目录先建立失败 Unit，MySQL HTTP 创建入口先复现 404。双库首轮在列表处复现空 UUID 参数绑定 500；移除未使用参数并拒绝空路径标识后，双库 HTTP 与 242 恢复 4/4 通过。授权同时保护标题、描述、分页与详情，覆盖成员不足、精确权限、错误范围、无效名单整量回滚、并发版本、目录禁用、即时撤权、软删除及重复删除；租户切回后的断言使用新会话令牌。
- `pnpm test:dotnet:unit -- --selection ai-module` 实际 413/413；`pnpm test:dotnet:architecture -- --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol|FullyQualifiedName~SqlStatement|FullyQualifiedName~ModuleDataAccess' --minimum-expected-tests 81` 实际 81/81，`pnpm test:aot:analyzers` 零警告、零错误。全架构与全 Unit 未在本项重跑；不改变 Auditing 既有命名失败的基线状态。
- `pnpm test:naming` 33/33、`pnpm test:sql-safety` 5/5；影响集工具检查 65/65，分片发现 1118 项无遗漏/重复，治理 57/57。`pnpm test:integration:affected -- --snapshot ai-knowledge-documents-r04b2 --phase slice` 在 Windows 发现 69 个 UID，实际 42 通过、27 原生跳过，MTP 原始退出码 9、包装器退出码 1，不能称该单次命令通过。
- `pnpm test:aot:publish:linux` 与 `pnpm test:aot:publish:linux -- --host worker` 均退出 0，API/Worker 分别为 134233312/86373944 字节，分别通过 17/15 个已批准警告门禁。使用本次正式产物在隔离 Linux 环境新建测试程序集，API 10/10、Worker 17/17，均零失败、零跳过；按完整测试身份替换上述 Windows 跳过项后，精确影响集合并 69/69 通过。
- 最终只读安全及 Native AOT 静态闭包复核未发现剩余确定问题。24 个本项代码/测试文件冻结摘要复核一致；本地证据位于忽略目录 `artifacts/native-aot/linux-x64/r04b2a/`，包含失败复现、TRX、正式发布清单及 `verification-summary.json`。环境为 Windows SDK 10.0.401、Docker Linux SDK 10.0.400、运行时 `DOTNET_PROCESSOR_COUNT=2`、Docker 内存 31.22 GiB；结论限功能和 AOT，不代表模型质量或容量验收。完成本项后按用户要求独立提交，下一项为 R04b2b，整体 R04b/RAG 保持未完成。

- [x] R04b2b：在已交付草稿及文档授权上接入上传、不可变文件版本与有界导入操作恢复。复用 Files 权威契约，覆盖以下失败与恢复链路；未实现解析或索引前不返回就绪状态。

R04b2b 当前实施契约（2026-10-03）：在 `main` 基线 `79da3f96b21cb50b1370d1a4e551df12c066c88a` 上执行，快照 `ai-knowledge-file-versions-r04b2b`。保持既有草稿 API，追加版本上传、版本分页、受授权来源下载及所有者导入查询/重试。上传为当前请求的有界原始流，最多 8 MiB，仅允许匹配扩展名的 PDF、UTF-8 Markdown/纯文本；请求只能提供文件名、当前文档版本和重试键，不能提供 FileId、范围或处理状态。

- 在 `Features/ManageKnowledgeVersions/` 实现上传协调、恢复及来源授权；`Persistence/AiKnowledgeVersionSql.cs` 与 `AiKnowledgeImportSql.cs` 只访问 AI 自有表。243 成对迁移新增不可变文件版本和持久化导入操作；版本标识由服务端 UUID v7 预先生成，操作保存内容摘要、大小、预期文档版本、阶段及有界租约，同一文档重试键的不同内容/元数据返回 409。
- 消费 Files 的 `IHostFileUploadWriter`、`IHostFileReferenceClaimService`、`IHostFileContentReader`、`ITenantResourceFileStore`，新增 Ai Claim 消费者/稳定版本键。API 与 Worker 最小后台入口注册精确文件引用 Probe 和 Tenant Owner，只有真实持久化版本或未终止导入意图可保留文件；Probe 不把禁用目录误当成文件引用已消失。
- 首轮 RED 为双库真实 HTTP 上传入口缺失；随后用双库 HTTP、迁移重放与可控文件故障验证重复上传、租约竞争、未知提交不释放、确认失败后重试、已知回滚补偿及释放失败恢复。文档删除先在同一 AI 事务标记清理，再在事务外释放；来源读取每次复核目录/文档授权，下载强制附件响应。未实现 R05 解析/索引时版本只能报告 `pending_processing`。
- 验证命令沿用本计划 R04 与测试矩阵；先运行聚焦 RED/Green，再选择快照影响集、AI Unit、相关 Architecture、命名/SQL/AOT 门禁和本次正式 Linux 原生产物。通过安全审查、摘要复核与 Git 检查后在 `main` 独立提交，不推送。

R04b2b 本地验收记录（2026-10-03）：

- 双库文件版本 HTTP/故障恢复和 243 迁移恢复聚焦 8/8 通过。Host 未知提交保留引用、确认失败恢复及删除竞争均已覆盖；Tenant 上传确认未知结果通过真实 Worker 恢复，文件字节额度只记一次。Files 发布顺序改为先确认额度再晋升，独立清理契约保留失败意图；Worker 注册真实额度 Port，确认冲突不再误判为不存在。
- `pnpm test:integration:affected -- --snapshot ai-knowledge-file-versions-r04b2b --phase slice` 选择 132 个精确身份，Windows 原始结果为 104 成功、1 失败、27 原生跳过。既有 Files 列表测试使用原始 Dapper 构造时间类型失败，改为同一生产 SQL 的 Guid 投影后双库 2/2 通过；27 个精确原生身份由本次 Linux API 10/10、Worker 17/17 全部补齐。保留原始失败/跳过日志，按身份合并为 132/132，没有缩小选择范围。
- 最终完整 Unit 在 Windows 为 3894 成功、0 失败、1 个 Linux FIFO 跳过；最低发现门槛因此退出 9。Linux 同一 FIFO 身份 1/1 通过，合并 3895/3895。最终受影响 Architecture 87/87，通过；此前全量 Architecture 在十分钟超时前发现 SQL 声明工厂不符合静态门禁，已改为直接静态声明并复跑相关 87 项，不宣称全量 232 项通过。
- `pnpm test:sql-safety` 5/5、`pnpm test:naming` 33/33；Integration tooling 65/65，分区精确发现 1126 项。API/Worker AOT 分析退出 0、零警告/错误。正式 Linux API publish 134772512 字节、17 条已允许警告；Worker publish 86515384 字节、15 条已允许警告，均退出 0。本次原生测试所用二进制与正式产物 SHA256 一致。
- 独立只读复核无剩余确定阻断问题；52 个本任务代码/契约/测试文件摘要未变化。忽略目录 `artifacts/native-aot/linux-x64/r04b2b/` 保存原始日志、TRX、发布清单、二进制摘要及精确身份汇总。环境：i7-12700H（14 核/20 线程），Windows SDK 10.0.401、Docker Linux SDK 10.0.400，运行时处理器上限 2、Docker 内存 31.22 GiB；功能及原生闭包证据不代表模型质量或容量验收。

- [x] R04b2c：知识库、文档及文件版本 HTTP 的 OpenAPI/共享客户端契约已交付。从真实双库导出快照；上传表达原始二进制请求体、来源读取表达二进制响应；生成客户端覆盖授权目录、成员、版本和导入恢复入口，保持服务端可信范围与精确权限。角色/组织授权、解析、索引、问答及 Vue 页面仍待后续 R04/R05/R06；R04b 整体保持未完成。

- [x] R04b2c1：先交付可独立验证的生成器原始二进制请求支持。生成 Operation 接受 Blob，多个受支持媒体类型使用显式联合类型 Content-Type，单一类型直接固定；不经过 JSON/multipart 编码，可选正文缺省不发送 Content-Type，响应继续以 unknown 进入守卫。RED 为原生成器拒绝 raw requestBody；执行生成代码验证原始字节、路径/查询编码、取消及 RequestOptions 透传、非法响应拒绝和 Stream 引用。原有生成产物零漂移，OpenAPI 全套 205/205、共享客户端 194/194 及 TypeScript 构建通过，相对 `e46cff04` 的 94 个契约兼容检查通过。独立提交后继续 R04b2c2 的真实双库契约接入；本切片未改动 .NET 代码或 HTTP 行为，不升级 R04b2c 状态。
- [x] R04b2c2：补齐上传 Endpoint 原始二进制元数据及来源 Stream 成功响应 Schema，真实双库快照登记知识库/文档/版本 20 个 Operation；共享包提供生成 Operation/模型/守卫入口，覆盖来源 Blob、删除 204 与导入恢复。真实导出、生成零漂移、共享客户端运行时及受影响服务端验证通过后独立提交。

R04b2c2 本地验收（2026-10-03）：`main` 基线 `fdfab53423bf83e8c91bf2599b5098db051827cf`，快照 `ai-knowledge-client-contract-r04b2c2`。

- RED：离线清单缺少知识库 20 个 Operation；真实 MySQL 文档上传缺少 requestBody，补齐后 SQL Server 来源下载缺少 content Schema。按既有 Files 的 Stream 元数据模式修正，SQL Server/MySQL 真实导出各 1/1 通过且规范化内容完全一致，现有 561 个 Operation 加本项 20 个后为 581。
- `pnpm test:integration:affected -- --snapshot ai-knowledge-client-contract-r04b2c2 --phase slice`：精确选择 Ai 与 client-openapi-snapshot，最终 48/48 成功、零跳过，14 分 26 秒。首轮 --no-restore 遇到缺少 JIT Agent Framework/MemoryPack 还原资产；强制 JIT 还原后并行 MSBuild 仍遇到内部内存异常。降低处理器及 Copy Task 并行度至 1 后重新构建零警告/错误并执行完整 48 项，没有减少影响集或更改测试门槛。
- AI Unit 429/429，零跳过；本切片未改动 Unit 覆盖的业务/契约源码，复用前项已验证的 Unit 程序集完成新鲜执行，初次隔离 Unit 构建的 MSBuild 内存异常保留在日志。52 个前项文件摘要对比仅文件版本 Endpoint 元数据变化；本项 12 个代码/契约/测试文件冻结核对一致。
- 当前源码在独立 ArtifactsPath 下完成 API AOT 分析与 Architecture 构建，均零警告/错误；NativeAot/MemoryPack 相关架构 73/73，零跳过。本切片没有修改处理、授权、持久化、JSON DTO 或发布依赖；本轮未重跑正式原生 publish/E2E，原生业务证据仍引用 R04b2b，不升级其范围。
- `node --test tests/openapi/*.test.mjs` 206/206；共享客户端 198/198，包含原始 Blob 认证刷新重放、语言/取消、撤权错误、来源 Blob、204、导入响应拒绝和分页整数精度保护。共享包 TypeScript 及 Vue 生产构建通过；标准快照离线检查、生成零漂移和相对 `fdfab534` 的 94 个契约兼容检查通过。治理 57/57。
- 本地原始日志、TRX 与源码摘要保留在忽略目录 `artifacts/ai-knowledge-client-contract/` 及同前缀日志；环境沿用 R04b2b。本项完成后在 `main` 独立提交，不推送；后续角色/组织 Port、授权及客户端增量仍须独立验收。

- [x] R04b3：角色/组织范围授权及共享生成客户端已在 `main` 分项交付，包含最小权威目录、独立授予维护和知识库/文档两层读取；各子项的实际验证范围如下，R04b 收尾见 R04b4–R04b6，完整 RAG 仍未完成：
  - [x] R04b3a：所有者的权威角色候选目录（基线 `dca199b8`，快照 `ai-knowledge-role-candidates-r04b3a`）。新增 `Identity.Contracts/IActiveRoleDirectory.cs`、Identity `Roles/ActiveRoleDirectory.cs` 与 `Persistence/ActiveRoleDirectorySql.cs`，AI `Features/ManageKnowledgeRoleCandidates/` 服务/Endpoint 作为真实消费者。Identity 仅查询自己拥有的活动普通角色；Host 使用 HostOnly，Tenant 使用 TenantRequired/CurrentTenantId，不使用 Workflow Port、JWT 角色声明或跨模块 SQL。
    - API：知识库及其文档分别增加 `GET .../role-candidates`，使用 `ai.knowledge_bases.role_candidates_read` 与 `ai.knowledge_documents.role_candidates_read` 独立权限；响应只含角色 Id/Code/Name 和分页。先查当前范围资源所有权，再调用目录；成员和陌生人均返回 404。禁用知识库仍允许所有者读取知识库候选；文档候选沿用启用目录和未删除文档的维护边界。
    - 分页：page ≥ 1、pageSize 1..100、offset ≤ 100000，排序 Code/Id。不接受请求 TenantId，不创建授权缓存、迁移或角色授予。新增 JSON 源生成与静态行物化闭包。
    - 权限每次通过 `ICurrentSessionAuthorization` 核对活动会话和权威精确权限，再使用返回的当前用户标识核对资源所有权。初始 JWT 策略只作入口门禁；数据库撤销权限后旧令牌不得继续读取。仅支持交互会话，API Key 不具备所有者维护资格。
    - RED：`tests/Full.NET.IntegrationTests/Ai/AiKnowledgeRoleCandidatesApiSqlServerTests.cs` / `AiKnowledgeRoleCandidatesApiMySqlTests.cs` 复用真实 API 夹具，验证缺失路由；随后验证普通活动/禁用/超级管理员角色、Host/租户隔离、所有权/精确权限、分页边界、禁用及已删除资源、目录禁用和权限撤销立即生效。Unit 无法证明真实 SQL 过滤及授权管线，故使用双库 HTTP；不属于 messaging-heavy。
    - 验收：affected slice 双库集、AI Unit、受影响 Architecture/Native AOT 分析及原生路径、命名/SQL safety/分片/治理。通过后仅提交本切片；后续 R04b3b 才接入角色授予及双层读取授权，组织范围仍待实现。R04b3 与 RAG 保持未完成。
    - 已取得的新鲜证据：双库专用 HTTP 2/2、真实 OpenAPI 导出各 1/1 且规范化内容一致；`pnpm test:dotnet:unit -- --no-build --selection ai-module` 429/429，受影响 Architecture 91/91，均零跳过。Architecture 覆盖原生闭包、受控协议、AI 依赖、Identity 契约消费者、模块表/事务边界、数据作用域、Global SQL 目录及 Operation 标识，没有宣称完整 Unit 或 Architecture 全集通过。
    - 契约与客户端：`node --test --test-concurrency=1 tests/openapi/*.test.mjs` 206/206，`pnpm --filter @fullnet/client-contracts exec vitest run --maxWorkers=1` 200/200；共享包 TypeScript 与 Vue 生产构建通过。标准快照离线检查、生成零漂移及相对基线的 94 个契约兼容检查通过；本项新增两个 Operation，知识库共享客户端合计 22 个，标准快照合计 583 个。
    - 本地静态门禁：命名 33/33、SQL safety 5/5、affected 工具 65/65、治理 57/57；全量分片发现 1128 个集成身份，无遗漏或重复。API AOT 分析构建零警告/错误；正式 `pnpm test:aot:publish:linux` 和 `pnpm test:aot:worker:publish:linux` 均成功，产物分别为 134944752/86561672 bytes，17/15 条第三方许可警告符合既有白名单。正式产物的 API 10/10、Worker 17/17 原生影响集全部通过且零跳过；Worker 未另行执行独立分析构建。
    - `pnpm test:integration:affected -- --snapshot ai-knowledge-role-candidates-r04b3a --phase slice` 发现 AI、Identity、强认证及 Native AOT 合计 240 个唯一测试身份。Windows 合并执行因内存耗尽中断，没有产生新鲜 TRX，排除旧的 15:14 结果；后续并行 Windows 批次也出现内存/栈异常，失败日志保留。保留真实完成的 SQL Server 40/40 与 AI/强认证 51/51，其余 JIT 在隔离 Linux SDK 容器串行完成 SQL Server 41/41、MySQL 40/40 和 41/41。合并这些新鲜 TRX 与正式原生 API 10/10、Worker 17/17，按原始发现的完整 FQN 集逐一核对，最终 240/240（JIT 213、Native 27）通过，零跳过、遗漏、重复或范围外身份；冻结源码/契约摘要不变。修正运行器后未缩小影响集，也未把环境失败计为通过。
    - 失败与修正记录：RED 首先证明缺失路由 404；随后旧 JWT 在权限撤销后仍返回 200，新增每请求权威权限核对后双库用例通过。原生夹具通过角色管理 API 撤权会同时使会话失效，正确返回 401；为单独证明权限核对，改为测试侧只删除两个权限关系、保留会话与安全戳，旧令牌在两个候选 Endpoint 均精确返回 403。曾出现 Linux Migrator 缓存文件失去执行位、测试辅助镜像无 python3 等运行器问题，修正仅限忽略目录脚本/缓存，未放宽生产授权或断言。
    - 环境与证据位置：本地 Windows i7-12700H（14 核/20 线程）、约 64 GiB 内存，Docker VM 31.22 GiB，Windows/Linux SDK 10.0.401/10.0.400；资源受压的长批次使用单 CPU、两个测试 worker 和 1 GiB GC 限制。原始日志、TRX、冻结源码/契约摘要及正式发布 manifest 留在忽略目录 `artifacts/ai-knowledge-role-candidates/`；这些证据只认证所述功能范围，不认证模型质量、容量或生产 SLO。
  - [x] R04b3b：角色授予与双层读取授权。知识库列表/读取、文档列表/读取、版本列表及来源打开后的二次复核已完成本地完整影响集验收；组织仍独立留待后续切片。
    - 本次基线 `c2eff12b`，快照 `ai-knowledge-role-grants-r04b3b`。新增 `Features/ManageKnowledgeRoles/AiKnowledgeRoleGrantService.cs`、`Endpoint.cs`、`Features/AiKnowledgeReadAccess.cs`、`Persistence/AiKnowledgeRoleGrantSql.cs` 和角色授予请求/响应 DTO；扩展 `IActiveRoleDirectory` 及其 Identity 实现，真实消费者分别是授予校验和 AI 读取上下文。
    - 知识库与文档分别提供 `GET/PUT .../roles`，精确权限为对应资源的 `roles_read`/`roles_update`；响应为资源标识、排序后的角色标识和资源 Version。244 成对迁移新建 `fn_ai_knowledge_role`、`fn_ai_knowledge_document_role`，只向本模块父资源建立外键，并登记恢复影响集；MySQL 受控索引恢复沿现有迁移模式精确登记命名债务。
    - 一次读取完整权威角色集合，以源生成 `Guid[]` 序列化的单个 `ActiveRoleIdsJson` 参数绑定 SQL Server OPENJSON / MySQL JSON_TABLE，避免 SQL Server 2100 参数限制且不截断角色。各层采用所有者 OR 明确用户 OR 活动角色，层间采用 AND；有界列表和总数共同使用该条件。写响应改用所有者专用 AI 查询，不能通过读取方法间接在 AI 事务内调用 Identity。来源打开后重新获取权威角色参数再查，失败或取消释放流。
    - 读取 Endpoint 每请求复核交互会话或当前 API Key 的权威精确权限；新增 `ICurrentApiKeyAuthorization` 与静态范围查询检查活动 Key/账号、有效期、安全戳及当前权限，来源打开后再次复核。该 Port 不重复计费或消费签名重放；当前 AI 入口保持 Signature 未开放，真实签名请求仍返回 401。
    - 原生 RED 暴露既有 API Key 创建/轮换写入缺少 `ApiKeyRecord` 静态参数绑定：SQL Server/MySQL 的真实创建入口均返回 500。已在 `IdentityDapperAotMaterializerContributor` 为 INSERT 的 13 个参数增加显式注册与绑定，仅作用于 `FULLNET_AOT_COMPILE`；JIT 路径保持原样。修复后重新执行分析、正式 API/Worker 发布及原生验证，不复用修复前产物宣称通过。
    - RED：新增 `AiKnowledgeRoleGrantsApiAssertions.cs` 与双 Provider 测试类，先在真实 HTTP 证明角色授予路由缺失，再覆盖单层拒绝、双层允许、用户/角色交叉匹配、同令牌角色移除/禁用立即拒绝、稳定分页、旧名单更新保留角色授予和版本冲突。迁移恢复测试冻结精确脚本集合，模拟父表已建、授予表或索引/注释部分完成后重放。
    - Identity 扩展当前最小角色 Port：单次批量验证最多 100 个授予角色，并按可信 Host/Tenant 范围读取当前用户的权威活动普通角色关系；不枚举角色的全部用户，不读取 JWT 角色声明。活动账号、租户成员、角色及作用域必须同时有效。失败或取消不得回退到声明或缓存。
    - AI 保存知识库/文档各自拥有的角色授予，SQL Server/MySQL 成对迁移及恢复测试；角色标识不建立跨模块外键。独立 GET/PUT 角色授予接口与精确权限，写入复用对应资源 Version，旧明确用户名单接口只替换用户授予，不能清空角色授予。
    - 一次权威角色读取服务当前操作的 AI 本地 SQL 过滤，知识库与文档分别判断所有者、明确用户或活动角色匹配，最终仍同时满足两层。总数与列表使用相同过滤，不能先分页再逐条跨模块查询；角色读取及授予校验必须位于 AI 本地事务之外。下载打开后的复核重新读取权威状态。
    - RED 先覆盖仅知识库角色/仅文档角色拒读、两层匹配可读、明确用户与角色交叉匹配、角色禁用/移除后旧令牌立即拒读、Host/Tenant 角色混用拒绝、旧用户名单更新保留角色授予、并发版本和失败原子回滚。通过双库恢复、实际影响集与原生闭包后独立提交，再生成共享客户端。
    - 最终验收：AI Unit 429/429，完整 Architecture 232/232；`pnpm test:aot:analyzers` 与 `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release -m:1 -nodeReuse:false` 零警告/零错误。影响集按 UID/FQN 冻结为 244 项并全部通过、无失败/跳过/遗漏/额外项：AI 双库 25 + 25，Identity 165，244 迁移双库恢复 2，Native 27。最终程序集及 46 个本任务代码/契约文件哈希已核对；修改原生参数绑定后重新编译并重跑全部 217 项 JIT，不复用旧程序集的 AI 结果。
    - 正式 Linux Native AOT API 为 135257744 字节、Worker 为 86595224 字节，17/15 条警告均在既有精确清单内。API 原生 2 + 8 项通过。Worker 首批因 45 分钟总时限中止，原始汇总为 12 成功、0 失败；通过当前测试平台的 `TrxTestResultSerializer.ReadAll` 解码 12 个 Passed 原始流记录，逐个核对预期 UID/FQN 并保存原始流及读取器哈希，其余 5 个 UID 在 120 分钟时限内补跑全部通过。原始中止日志保留，未改写为完整 TRX。最终 27 个原生节点无遗漏/重复/跳过，均使用同一正式二进制；API SHA256 `cf216a9be655fd51d0d76e8445544979c4d462060646022b9e38c47e80ebc740`，Worker SHA256 `e932dc52c297f3871c0a20684649d9c345da74d2b57dd81a1570e964720b374c`。
    - 实际执行使用 `dotnet <IntegrationTests.dll> --filter <冻结 FQN 集> --minimum-expected-tests <批次节点数> --report-trx`；AI/Identity/迁移按 Provider 分批，Native 同时保留原始结果流与补跑 TRX。命名/UUID/SQL safety 38/38，OpenAPI 206/206，测试工具 54/54，治理/本地化 64/64。分片发现核对 1132 个方法、无遗漏/重复，此为发现校验，不表示运行全部 1132 项。
    - 硬件为 i7-12700H（14 核/20 线程）、64 GB Windows；Windows SDK 10.0.401、Linux SDK 10.0.400。正式 API/Worker 全项目 Release `linux-x64`、SelfContained、`FullNetPublishMode=NativeAot` 发布，SDK 容器 4 CPU/10 GB；测试为 2 CPU、3..4 GB SDK 容器，进程 GC 上限 1.5 GB。一次 Windows Event 2004 证实宿主提交内存耗尽，后续临时限制 WSL 16 GB；原 `.wslconfig` 不存在的状态及测试 SQL 的原内存/高级选项已恢复，保留容器当时的停止状态。环境中断不是业务通过证据；本切片不认证容量、模型质量、生产 SLO 或完整 RAG。
  - [x] R04b3c：四个角色授予 Operation 的共享生成客户端。独立于 R04b3b 后端提交，知识库客户端从 22 个 Operation 扩展至 26 个；组织授权和 Vue 页面保持后续范围。
    - 后端基线 `7bcc285e`；快照 `ai-knowledge-role-grant-client-r04b3c` 在后端待验收时记录工作区，客户端代码相对该快照独立修改。清单缺少四个 Operation 的 RED，以及运行时调用未导出的 8 个 RED 场景均已保存；生成文件只由仓库生成器输出。
    - 从真实 SQL Server/MySQL Host 导出并核对相同规范化 OpenAPI，登记精确 manifest，运行仓库生成器并增加共享薄入口。
    - 验证四条资源路径、并发版本、清空角色、认证单次刷新、语言/取消、403/409 ProblemDetails 和损坏响应守卫；再执行完整 OpenAPI/共享包测试、TypeScript/Vue 构建、离线快照、生成零漂移及契约兼容检查，通过后独立提交。
    - 真实双库 `OpenApiDocumentationApiSqlServerTests` / `OpenApiDocumentationApiMySqlTests` 各 1 项通过，导出同一最终程序集的运行时协议；规范化快照逐字节一致，受控 Operation 合计 587。新增 `AiKnowledgeRoleGrantsResponse` / `SetAiKnowledgeRoleGrantsRequest`，四个操作和参数类型从共享薄入口及根入口导出，不接受请求租户或主体字段。
    - `node --test --test-concurrency=1 tests/openapi/*.test.mjs` 206/206；`pnpm --filter @fullnet/client-contracts exec vitest run --maxWorkers=1` 209/209，包括 9 个新增角色授予场景。共享包 TypeScript 和 Vue 生产构建通过；`snapshot-client-openapi.mjs --check --offline`、`generate-fullnet-client.mjs --check` 通过，相对 `7bcc285e` 的 94 个契约兼容检查通过。治理 57/57。本项未再修改 .NET/SQL 行为，沿用 R04b3b 的 244 项后端证据；不重复发布相同原生代码。
  - [x] R04b3d：组织授权候选目录（基线 `96c8d787`，快照 `ai-knowledge-organization-candidates-r04b3d`）。先交付可独立验收的两个候选 Endpoint，组织授予和双层读取留给 R04b3e。
    - Organization.Contracts 新增 `ICurrentTenantOrganizationUnitDirectory.ListActiveUnitsAsync(page, pageSize, token)`，复用最小机构投影；实现和双库分页 SQL 均位于 Organization，固定 `TenantRequired/CurrentTenantId`，不接受租户覆盖，Host 或上下文缺失直接拒绝 Port 调用。按 Code、Id 排序，页大小 1..100，偏移最多 100000，不缓存组织状态。
    - AI `ManageKnowledgeOrganizationCandidates` 作为真实消费者，先校验资源所有权，再验证分页并调用 Port。Host 资源返回空候选；文档必须在启用知识库内且未删除。知识库和文档使用各自 `organization_candidates_read` 权限，当前会话权威复核独立于资源所有权。AI 只引用 Organization.Contracts。
    - RED 使用真实 MySQL HTTP 确认缺失端点返回 404；GREEN 双库覆盖当前/其他租户、Host、活动/禁用机构、精确权限、旧令牌权限撤销、非所有者、父/子机构独立启用状态、分页及资源版本不变。Unit/Architecture、运行时 OpenAPI、影响集、AOT 分析和原生候选路径完成后，经独立只读复核再提交。
    - 后续 R04b3e 追加有界组织授予及活动机构/活动成员权威读取，验证明确机构匹配、调离撤权与来源打开后二次复核；组织候选不能被当作已有资源授权，R04b3、R04b 与 RAG 继续保持未完成。
    - 新鲜验收：双库专用 HTTP 2/2；AI/Organization 与相关注册 Unit 480/480、完整 Architecture 232/232，均零跳过。`pnpm test:aot:analyzers` 与 Release Integration build 通过；命名/UUID 33/33、SQL safety 5/5、工具 54/54、治理/本地化 64/64、OpenAPI 207/207。`pnpm test:integration:partitions` 核对 1134 个发现身份，此为发现校验，不代表运行完整 1134 项。
    - `pnpm test:integration:affected -- --snapshot ai-knowledge-organization-candidates-r04b3d --phase slice` 冻结 AI 52、Organization 12、Native AOT 27，合计 91 个 UID/FQN。`dotnet <IntegrationTests.dll> --filter <冻结 FQN 集> --minimum-expected-tests <批次节点数> --report-trx` 实际执行 JIT 64、原生 API 2 + 8、Worker 17；最终四份正式 TRX 逐一匹配 91/91，零失败、跳过、遗漏、重复或额外身份。23 个后端代码/契约文件及原生产物 SHA256 前后一致，聚合证据位于 `artifacts/ai-knowledge-organization-candidates/final-acceptance.json`。
    - 正式 API/Worker 全项目 Release `linux-x64`、SelfContained、`FullNetPublishMode=NativeAot` 发布使用仓库 SDK 与警告校验契约，产物为 135413216/86641512 bytes，17/15 条第三方告警均在已有清单内。API SHA256 `d81209db932251014e5a70e598bc7ed5d2187bb257bdbbbf2a676af56feef392`；Worker SHA256 `cc6f4142e5d1a6ae80aa5d1c8f77a0881ce61dd074199dc605725317cb841b3c`。原生双库候选探针创建真实活动机构，核对非空最小投影，覆盖新 Port 的参数绑定、行物化与 JSON 闭包。
    - RED 为缺失路由返回 404。首轮 GREEN 的 422 来自测试读取主体未加入租户，补齐夹具活动成员后双库通过；初次 Architecture 无还原构建与 AOT 分析还原发生冲突，待分析结束后重新正常构建并运行全部 232 项通过；模块依赖图通过仓库生成器同步。独立只读安全复核未发现 P1/P2 问题，没有放宽权限、范围、断言或影响集。
    - 本地 i7-12700H（14 核/20 线程）、64 GB Windows，Windows/Linux SDK 10.0.401/10.0.400；发布 SDK 容器 4 CPU/10 GB，原生测试 SDK 容器 2 CPU/3500 MB、进程 GC 上限 1.5 GB，Docker VM 16 GB。本项没有迁移或容量测试，不认证完整 RAG、模型质量或生产 SLO。客户端随后独立提交，组织授予继续留给 R04b3e。
  - [x] R04b3d-client：两个组织候选 Operation 的共享生成客户端，知识库薄入口与根入口合计 28 个 Operation。组织授予和 Vue 页面仍待后续切片。
    - 后端基线 `33b46df1`，快照 `ai-knowledge-organization-candidate-client-r04b3d`；客户端行为 RED 为 7 个缺失导出失败，15 个原有知识库场景通过。清单和生成文件由双库运行时协议及仓库生成器产生，未手写生成路径或模型。
    - SQL Server/MySQL 真实 Host OpenAPI 导出各 1/1，规范化快照逐字节一致，共 589 个 Operation，SHA256 `2e36140cbcbff8bbd2b8a8b7cdd040c405a7c4a385f2cd1b4b0bbbd42aa71a69`。模块图增加 Organization → Ai 依赖后快照标签顺序由运行时自然生成；候选 DTO 仅含 Id/Code/Name 与分页，不接受请求租户或主体覆盖。
    - `pnpm --filter @fullnet/client-contracts exec vitest run --maxWorkers=1` 216/216，包含 7 个新增候选场景：两条资源路径、分页、认证/语言/取消、Host 空页、UUID 守卫、400/403/404 ProblemDetails 和 401 单次刷新。`node --test --test-concurrency=1 tests/openapi/*.test.mjs` 207/207；共享包 TypeScript 与 Vue 生产构建通过。
    - `snapshot-client-openapi.mjs --check --offline`、`generate-fullnet-client.mjs --check` 及契约兼容检查通过（相对开发基线 `96c8d787` 为 94 个既有契约、95 个当前契约；后端提交后相对 `33b46df1` 再核对 95/95 个契约兼容）；治理 57/57。客户端 9 个代码/契约文件哈希无漂移，slice 选择器为 none，未追加 .NET/SQL 行为；沿用 R04b3d 的 91 项后端证据，不重复发布相同原生源码。
  - [x] R04b3e：组织授予与权威成员读取，基线 `bd5c20ac`，快照 `ai-knowledge-organization-grants-r04b3e`。
    - [x] RED：新增 `AiKnowledgeOrganizationGrantsApiAssertions.cs` 及双库测试，真实 HTTP 首先断言缺失 `GET .../organizations`；覆盖两层匹配、组织与明确用户/角色混合匹配、仅授予父机构不包含子机构、活动机构/隶属撤销及来源流打开后撤权释放。
    - [x] 扩展 `ICurrentTenantOrganizationUnitDirectory`：`FindActiveUnitsAsync(IReadOnlyCollection<Guid>, CancellationToken)` 验证最多 100 个唯一非空标识；`ListActiveUnitIdsForUserAsync(Guid, CancellationToken)` 读取完整活动关系。SQL 只 JOIN Organization 自有 unit/user_unit，机构和隶属均活动且各自 TenantId 等于可信当前租户；Host 不调用该 Port，禁止缓存、截断关系或借用 Global 目录。
    - [x] 新建 AI 自有 `fn_ai_knowledge_org` / `fn_ai_knowledge_document_org`，列 Id、父资源标识、OrganizationUnitId、CreatedAtUtc。245 成对迁移及 `Migration245AiKnowledgeOrganizationGrantRecoveryTests.cs` 检验部分完成重放、唯一约束、索引/注释收敛和既有授予/版本保留。只向 AI 父资源建外键，不向 Organization 建外键。
    - [x] 新增 `ManageKnowledgeOrganizations/`、`AiKnowledgeOrganizationGrantSql.cs`、请求/响应和 JSON 源生成。知识库/文档分别 GET/PUT `.../organizations`，Operation 为 `aiGetKnowledgeOrganizations` / `aiSetKnowledgeOrganizations` / `aiGetKnowledgeDocumentOrganizations` / `aiSetKnowledgeDocumentOrganizations`，独立 `organizations_read` / `organizations_update` 权限。当前权威交互会话及资源所有权先核对，Host 只接受空授予；Tenant 校验在事务外，AI 事务中按资源 Version CAS 替换，仅回读本模块。
    - [x] 扩展 `AiKnowledgeReadAccess`、知识库与两层 SQL：用完整活动组织 Guid 数组的单个 JSON 参数，SQL Server OPENJSON / MySQL JSON_TABLE 成对过滤。每层所有者 OR 明确用户 OR 活动角色 OR 明确活动机构，层间 AND；名单/角色更新不清空组织，删除文档同步清理组织授予。来源二次复核重新读取组织状态，失败或取消释放流。
    - [x] 新鲜验收：相关 Unit、完整 Architecture、双库授权/245 恢复及 selector 完整影响集、AOT 分析和正式 API/Worker 原生门禁、运行时 OpenAPI/契约、命名/SQL/治理；独立只读安全复核及本任务 diff/status 后在 main 单独提交。共享生成客户端随后单独交付；R04b/RAG 仍不由组织授权单项推导为完成。
    - 最终验收（2026-10-04）：Release Unit/Architecture/Integration 编译与 `pnpm test:aot:analyzers` 通过；相关 Unit 506/506、完整 Architecture 232/232，均零跳过。slice 冻结的 95 个 UID/FQN（AI 54、Organization 12、245 恢复 2、Native 27）逐一匹配：JIT 68/68、API 原生 2 + 8、Worker 原生 17，四份正式 TRX 全部 Passed，零失败、跳过、遗漏、重复或额外项。JIT 27m03s，原生各批 3m50s / 16m24s / 32m16s；35 个源码及 Windows/Linux 测试程序集 8 个哈希核对一致，最终证据在 `artifacts/ai-knowledge-organization-grants/final-acceptance.json`。
    - 双库授权与恢复通过，包含超过 2100 条机构关系的尾部匹配、跨租户残留关系拒绝、机构/用户及机构/角色两向组合、旧令牌撤权、来源打开后撤权及权威依赖失败/取消异常释放、并发 CAS 与插入失败回滚。取消用例验证依赖返回已取消 Task 时释放，不代表真实客户端 RequestAborted 的端到端验证。独立只读安全审查及补充测试复核没有确认的 P1/P2 问题。
    - SQL Server/MySQL 真实 OpenAPI 导出各 1/1，新增四个操作的规范化协议一致，共 593 个 Operation；共享清单仍待下一项更新。OpenAPI 208/208、命名/UUID 33/33、SQL safety 5/5、工具 54/54、治理/本地化 64/64；文档更新后治理再次 57/57。分片发现核对 1138 项，无遗漏/重复，此为发现而非完整执行。
    - 扩展完整 Unit 首轮为 3898 项：3895 成功、2 失败、1 跳过；相关实现与测试共 357 个文件核对与起始提交一致。独立提交 `b010c374` 补齐固定注册预期中已有的当前 API Key 授权及活动角色目录两个 Scoped 服务，两个原失败测试重跑 2/2、零跳过、退出 0。完整 Unit 重跑 3897 成功、0 失败、1 项 Linux FIFO 在 Windows 跳过；最低执行数 3898 未满足，脚本退出 9，不报告全量通过。该测试预期修复不改变生产闭包，35 个源码及 8 个测试程序集指纹仍一致。最初相关 Unit 命令的最低预期数误设 473，实际 470 全通过但退出 9；最终扩大相关注册范围后 506/506、退出 0，保留原记录。
    - 正式全项目 Release `linux-x64`、SelfContained、`FullNetPublishMode=NativeAot` API/Worker 发布均退出 0，产物 135655664/86670952 bytes，17/15 条第三方告警均通过既有清单。API SHA256 `3e811788bdef61d178f55a75624116027dd2beb79b02cea6320dab048eb89796`；Worker SHA256 `25dd18b6e7464c12bbed71dd6f34e840005b010a70050f5f073196a13027386a`。原生双库探针真实建立活动机构和成员关系，非空替换/读取知识库及文档机构授予，并由目录读取覆盖完整机构 JSON 参数；非所有者授权、二次复核失败/取消的细分场景来自 JIT 双库，不扩大原生证据范围。
    - 本地硬件为 i7-12700H（14 核/20 线程）、64 GB Windows，Windows/Linux SDK 10.0.401/10.0.400。编译 SDK 容器 4 CPU/12 GB、swap 总上限 16 GB、GC 上限 10 GB、HeapCount 1；原生测试 SDK 容器 2 CPU/3500 MB、进程 GC 上限 1.5 GB，复用数据库实例但每项创建隔离业务库。WSL 临时 16 GB/swap 8 GB；验证后已恢复原 16 GB/swap 4 GB 运行预算及 .wslconfig 不存在状态，五个原容器均 Running、无启动错误，控制平面仍映射 127.0.0.1:61490。临时构建卷核对任务标签后已删除。恢复时 Docker CLI 启动遇到自身 config.json 重命名拒绝访问；只停止本任务的挂起启动进程，重新启动 Desktop 后恢复，未修改配置内容或 ACL。本项未执行容量或真实模型实验，R04b/RAG 与 Capacity-not-verified 继续保持开放。
    - 环境失败原始记录保留：Docker 500/no route to host；3 GB、4.5 GB、7 GB 编译器堆先后在对象写入阶段耗尽；32 GB 默认 WSL 下 Windows Event 2004 记录 vmmem 约 28 GB、dmsvc 约 25 GB导致还原中断。重启 WSL/WinNAT 恢复端口冲突，20 GB WSL 下宿主提交内存余量约 3 GB，主动停止后收紧至 16 GB，最终完整发布与运行通过。中断/失败没有改写为通过，未停止或调整原有 Windows 数据库服务，临时构建卷清理前核对任务标签。
  - [x] R04b3e-client：独立交付四个机构授予生成 Operation，知识库薄入口和根入口由 28 扩展至 32 个操作；后端提交 `23a3a5b3`。
    - RED：缺失导出导致新增 10 个用例失败，既有 22 个知识库用例通过。生成后覆盖知识库/文档 GET/PUT（使用不同父资源及文档标识）、Host 空名单、完整名单与 Version 保留、403/409 ProblemDetails、401 单次刷新保留正文、取消/语言和损坏响应守卫。路径、DTO 和响应守卫均来自仓库生成器。
    - 使用 R04b3e 当前程序集的真实双库 OpenAPI，一致规范化快照及精确 manifest 共 593 个 Operation。完整共享包 Vitest 226/226、OpenAPI 208/208，均零失败和跳过；共享包 TypeScript 及 Vue 生产构建通过。离线快照、生成零漂移及相对后端 `23a3a5b3` 的 96/96 契约兼容检查通过；9 个客户端代码/契约/测试文件哈希核对一致，客户端快照影响集为空。本项未再修改 .NET/SQL，沿用后端 95/95 及正式原生发布证据。治理 57/57，日志在 `artifacts/ai-knowledge-organization-grants/client-*.log`。
  1. 先建立权威读取边界。Identity 当前角色目录是 Workflow 专用契约，新增真实 Ai 消费所需的通用最小活动角色/成员关系 Port；不得依赖 JWT 中旧角色声明或扫描 Workflow 办理人全量用户。Organization 既有成员 Port 使用 Global SQL，且只检查成员隶属活动状态；新知识库读取需按可信当前租户同时核对活动机构和活动隶属，不把跨租户列表直接当作授权。
  2. 在 AI 自有表保存有界角色/机构授予，成对迁移及恢复测试；不向 Identity/Organization 建外键，不跨模块 JOIN。新增独立范围读取/修改 Endpoint 与精确权限，保持现有明确用户成员 API 的兼容语义，避免旧客户端的用户名单更新清空新范围。知识库与文档版本继续提供并发保护，所有者/租户只能来自可信上下文。
  3. 每层授权为所有者、明确用户、权威活动角色或当前租户活动机构的匹配；读取仍需同时满足知识库和文档两层。机构首切片只匹配明确机构，不能从父机构推导未授予的子机构。禁用目录、删除文档、角色禁用/移除、机构禁用/成员调离及端点权限撤销均需拒读；来源流打开后继续再次复核。
  4. 行为修改前建立 RED：双库覆盖 Host/租户角色隔离、无独立权限、旧角色令牌、组织跨租户/禁用/调离、未授予子机构、单层可见但另一层拒绝及读取/下载之间撤权。按真实影响集验证 Unit、Architecture、双库 HTTP/恢复、AOT 分析与原生路径，独立只读安全复核后提交；再同步生成客户端。本任务完成前 R04b 和 RAG 保持未关闭。

- [x] R04b4：补齐 Host Claim 前后中断的同场景双库验收。基线 `211e2322`，快照 `ai-knowledge-claim-interruption-r04b4`；只扩展 `AiKnowledgeImportFaults.cs` 的真实端口故障注入及现有双库恢复用例，不新增生产抽象或改变文件契约。
  - [x] 在调用 Claim 前、真实 Pending 提交后各注入一次 IOException，检查 HTTP 失败、AI 版本尚未发布、文档版本未推进，且没有补偿释放；直接向真实 Files 服务查询开放 Claim 证明中断时状态。
  - [x] 重放原始上传键与内容，核对同一 FileId/版本标识/幂等键，Pending Claim 复用同一 ClaimId 后确认 Active；来源恢复可读，完成后重复上传不再调用 Claim，AI 只保存一个版本。
  - [x] 用例运行前审查快照 slice 影响集，聚焦双库恢复再完成所选集合；本项只有测试变化，生产源码/依赖未变化且正式原生产物指纹一致，复用 R04b3e 的正式发布与原生证据。治理及 diff/status 核对后在 main 独立提交。不能以本项通过推导尚未核对的文件所有权/共享权限场景或完整 RAG 完成。
  - 新鲜验收（2026-10-04）：`dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --configuration Release --no-restore --nologo -m:1 -p:UseSharedCompilation=false` 退出 0，零警告/错误，6m47s。聚焦两个双库恢复用例 2/2、零跳过，2m04s；既有用例中追加场景，没有增删 MTP 测试身份或修改矩阵门槛。
  - `pnpm test:integration:affected:plan -- --snapshot ai-knowledge-claim-interruption-r04b4 --phase slice` 精确选择 Ai；`node artifacts/r04b4/run-affected.mjs` 使用仓库选择器、发现器及标准聚焦参数执行完整集合，54/54、零失败/跳过、退出 0，17m42s。正式 TRX 与发现的 UID/FQN 逐项匹配，无遗漏、额外或重复身份；测试源码与程序集哈希前后一致，API/Worker 正式原生产物仍匹配 R04b3e SHA256。汇总及原始日志在忽略目录 `artifacts/r04b4/` 与 `artifacts/r04b4-*.log`。一个用例的捕获文本超过 TRX 字段上限被截断，测试身份与 Passed 结果完整，未声称所有捕获文本无截断。
  - `node --test tests/governance/*.test.mjs` 57/57、零跳过；本地环境沿用 R04b3e，Windows SDK 10.0.401、两个测试 worker、原有五个容器保持运行。本项验证可控异常与新 HTTP 作用域恢复，finally 仍释放租约，未执行杀进程/断电实验；未重跑全量 Unit/Architecture 或相同生产源码的原生发布。下一项继续核对 AI 来源入口的底层文件缺失/删除与租户资源归属错配，R04b/RAG 与 Capacity-not-verified 保持开放。

- [x] R04b5：AI 来源入口的文件依赖与租户资源归属边界。基线 `16a16fff`，快照 `ai-knowledge-source-boundaries-r04b5`；在两个既有双库来源用例中调用 `AiKnowledgeSourcePersistenceAssertions.cs`，不新增 MTP 测试身份或修改生产契约。
  - [x] 在隔离数据库中模拟不存在的文件引用、Host 软删除、Tenant released 状态，以及 Tenant 的 ResourceId/OwnerModuleKey 错配；每次真实 HTTP 返回 `source_unavailable` 404，finally 恢复后重新读取原始正文。
  - [x] 核对 Local Provider 与测试工厂临时根目录后，模拟 ready 元数据对应的实际对象丢失；HTTP 返回统一脱敏的 unexpected 500 ProblemDetails，不包含正文、对象路径或内部异常，finally 按原键恢复相同字节。此项不把存储异常改写成资源不存在。
  - [x] 单元替身不能证明双库作用域、Files 元数据与真实对象读取的共同约束，复用现有双库 HTTP fixture；本项不属于 messaging-heavy。聚焦两个来源用例后运行选择器完整 AI slice，核对 UID/FQN 与源码/程序集指纹，再运行治理、diff/status，在 main 独立提交。
  - 新鲜验收（2026-10-04）：`DOTNET_PROCESSOR_COUNT=2`，`dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --configuration Release --no-restore --nologo -m:1 -p:BuildProjectReferences=false -p:UseSharedCompilation=false` 退出 0，零警告/错误，43.48s；本项只改测试及文档，复用 R04b4 已构建且未变化的生产依赖，重新编译当前测试程序集。首次聚焦因新夹具 SQL 拼接缺少空格生成 `ANDknowledge`，双库分别报 102/1064、2 项失败；仅修正两处拼接分隔后，最终聚焦 2/2、零跳过、退出 0，2m05s。此失败是测试夹具错误，不作为生产缺陷 RED 证据，原始失败日志保留。
  - `pnpm test:integration:affected:plan -- --snapshot ai-knowledge-source-boundaries-r04b5 --phase slice` 核对四个变更文件，只选择 Ai。`node artifacts/r04b5/run-affected.mjs` 使用仓库选择器、发现器及标准聚焦参数执行完整 AI slice，54/54、零失败/跳过、退出 0，21m35s；每个 Provider 验证 Host 三类、Tenant 五类来源依赖故障，共 16 个场景，仍为两个既有 MTP 用例。
  - `node artifacts/r04b5/verify.mjs` 核对正式 TRX 的全部 UID/FQN，与发现集合精确一致，无遗漏、重复或额外身份；两份测试源码及程序集摘要保持冻结，API/Worker 原生产物仍匹配 R04b3e SHA256。一个用例的捕获文本达到 TRX 字段上限后截断，身份及 Passed 结果完整。原始构建、失败/成功聚焦、正式 TRX、冻结清单与 `acceptance.json` 保留在忽略目录 `artifacts/r04b5/`、`artifacts/r04b5-*.log`。
  - `node --test tests/governance/*.test.mjs` 57/57、零失败/跳过；本地 i7-12700H（14 核/20 线程）、64 GB、Windows SDK 10.0.401，两个测试 worker，原有五个容器保持运行。未重跑全量 Unit/Architecture 或无变化生产源码的 Native publish/E2E；本项不认证模型质量、完整 RAG 或容量，Capacity-not-verified 保持。
  - 收尾核对发现：R04b4 的可控中断会在 finally 成功释放导入租约，既有租约物化用例也不能证明遗留租约接管。下一项 R04b6 在隔离双库夹具中保留已提交租约，验证到期前拒绝并发、到期后唯一接管及同一文件/版本恢复；不以本项推导进程终止实验已完成，R04b 继续保持开放。

- [x] R04b6：遗留导入租约到期后的唯一接管与恢复。基线 `dd7fec55`，快照 `ai-knowledge-abandoned-lease-r04b6`；复用 `AiKnowledgeImportFaults.cs` 的两个双库恢复测试，新建 `AiKnowledgeAbandonedImportLease.cs` 负责租约故障和断言，不新增 MTP 身份、生产接口、迁移或 SQL。
  - [x] 复用真实 ICommandExecutor，仅在隔离夹具中让一次租约清理失败；Host 在 Claim 调用前、Pending 提交后中断，Tenant 在 FileId 已保存后丢失响应。查询真实导入日志确认 FileId、版本、租约及五分钟到期时间已持久化，AI 版本尚未发布，不释放文件。
  - [x] 到期前的新 HTTP retry 和原上传重放返回 409 `ai.knowledge.import_busy`，原文件、版本及租约不变。推进夹具 IClock 六分钟后暂停已成功领取租约的请求，另一个请求仍返回 409；旧 LeaseToken 的状态写入和租约释放均影响零行，不能覆盖接管者。
  - [x] 放行接管者后完成同一操作、文件与版本，来源正文恢复，重复请求不创建第二个版本；Tenant 仍只有同一 ready 文件。finally 释放测试等待门并恢复时钟，故障只作用于本次目标操作。此验证模拟遗留持久化状态，不执行真实杀进程或断电。
  - [x] 使用 Release 当前测试程序集先运行双库恢复聚焦，审查 selector 的完整 Ai slice 并逐项核对正式 TRX UID/FQN 与冻结摘要；测试及文档变化复用未变化生产依赖和正式原生产物证据，最终治理、diff/status 通过后在 main 独立提交。按已批准 R04 文件要求完成关闭条件核对。
  - 新鲜验收（2026-10-04）：`DOTNET_PROCESSOR_COUNT=2`，`dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --configuration Release --no-restore --nologo -m:1 -p:BuildProjectReferences=false -p:UseSharedCompilation=false` 最终退出 0、零警告/错误，1m28s。只重建变化的测试程序集，复用未变化生产依赖。首轮聚焦 2 项均因新夹具提前切换 Tenant 导致旧 Host 会话失效而返回 401；按真实会话绑定将切换移到 Tenant 场景开始处，未修改生产认证。原始失败 TRX 保留，不将其作为生产缺陷 RED；最终同一聚焦 2/2、零失败/跳过、退出 0，2m42s，覆盖三类中断的六个 Provider/场景组合。
  - `pnpm test:integration:affected:plan -- --snapshot ai-knowledge-abandoned-lease-r04b6 --phase slice` 核对四个变更文件，只选择 Ai。`node artifacts/r04b6/run-affected.mjs` 使用仓库选择器、发现器与标准参数运行完整 AI slice，54/54、零失败/跳过、退出 0，22m13s。`node artifacts/r04b6/verify.mjs` 核对正式 TRX 的全部 UID/FQN 精确匹配、无遗漏/重复/额外身份，两份测试源码和程序集摘要保持冻结，API/Worker 正式原生产物继续匹配 R04b3e SHA256。
  - 只读审查未发现确定的 P1/P2：真实 acquire 返回 1 后才等待，遗留租约故障限定目标文档，旧令牌不能破坏接管者；既有文件依赖拒读、授权下载者拒写、任意 FileId 拒绝、Confirm/未知提交/补偿/删除及撤权证据覆盖已批准 R04 范围。`node --test tests/governance/*.test.mjs` 57/57、零失败/跳过。原始日志、正式 TRX、冻结清单、审查摘要及 `acceptance.json` 位于忽略目录 `artifacts/r04b6/` 和 `artifacts/r04b6-*.log`；一个用例的捕获文本达到 TRX 字段上限而截断，身份及 Passed 结果完整。
  - 本地 i7-12700H（14 核/20 线程）、64 GB Windows、SDK 10.0.401、两个测试 worker，原有五个容器保持运行；从 R04b3e 提交到本项生产源码及依赖无变化，复用对应正式 Native publish/E2E、迁移及客户端证据，未重跑全量 Unit/Architecture 或正式原生发布。至此关闭 R04 的目录、权限、文件引用与导入恢复范围，下一项进入 R05；完整 RAG、模型质量、杀进程/断电与容量仍不能由本项推导，Capacity-not-verified 保持。

1. 先建立文档/版本与导入操作的失败用例：只有知识库所有者及独立上传权限可写，明确成员的目录读取资格不能授予文档/来源访问；跨范围、禁用目录、删除文件、重复请求与旧版本覆盖均拒绝。正文上传只从当前操作的流进入 Files，不提供按任意 FileId 导入其他模块附件的入口。首版状态只能表达待处理/失败，R05 前不能标记索引就绪。
2. 在 `Features/ManageKnowledgeDocuments/`、`Persistence/` 及双库成对迁移扩展既有草稿与文档授权，持久化不可变文件版本和有界导入操作状态。所有 UUID 在应用端生成；版本变更、删除与撤权先写权威状态，事务只覆盖 AI 自有表。具体迁移编号在实现时取仓库下一空号；按实际新增测试更新矩阵，不预填发现数。
3. Host 上传复用 `IHostFileUploadWriter`，给 Files 追加 Ai Claim 消费者及稳定版本幂等键，注册精确版本/文件 Probe。按 Claim → AI 提交 → Confirm 顺序执行；确认失败与未知提交保留待对账引用，已知回滚才补偿释放。Tenant 上传复用 `ITenantResourceFileStore` 的 Ai 模块键及服务端版本资源 ID，注册 `ITenantResourceFileOwner`，通过 `ListReadyAsync` 恢复已上传对象；禁止跨模块 JOIN、外部存储操作嵌套本地事务和按客户端声明确认引用。
4. 双库 HTTP/恢复用例验证文档授权、并发版本与立即撤权；可控故障验证 Claim 前后中断、Confirm 失败、释放失败重试、资源归属不匹配、只有下载权而无共享权。删除/撤权后来源端点即刻不可读，后续 R05 清理/索引消费者必须复核权威状态。操作指南只更新真实实现，OpenAPI/共享客户端与角色/组织范围在 R04b 收尾验证。
5. 每个可独立交付的切片依次运行 Unit、受影响 Architecture、双库 Integration、命名/注释/SQL 门禁及 AOT 分析、正式原生影响集；只读安全审查和新鲜 `git diff --check` 后提交。未完成文件故障场景、客户端和范围授权前保持 R04b 未勾选。

**新增责任位置：** `src/Modules/Full.NET.Modules.Ai/Features/ManageKnowledgeBases/`、`Features/ManageKnowledgeDocuments/`、`Persistence/AiKnowledgeSql.cs`；双库迁移位于 `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/` 与 `MySql/`。测试位于 `tests/Full.NET.IntegrationTests/Ai/`，复用现有双库 fixture。

**产出：** 知识库、文档版本、文档授权与导入任务的内部模型；文件关联只携带 Files 权威标识/版本，通过最小契约读文件与维护引用。知识库管理、导入、查询、问答、来源读取分别声明精确权限，权限码按 Naming Profile 定稿。

**文件接入边界：** 检查 `src/Modules/Full.NET.Modules.Files.Contracts/HostFileReferenceClaimContracts.cs`、`ITenantResourceFileStore.cs` 与 `ITenantResourceFileOwner.cs`；Host 必须登记 Ai Claim 消费者及 Probe，租户采用资源所有权合同。首期只支持向授权知识库上传；不开放按任意 FileId 导入其他模块附件，后续共享必须由原数据所有者批准。

- [x] 先覆盖跨租户、无文档授权、文件不存在/已删除、重复导入、引用失败补偿、并发版本更新与迁移部分执行恢复。对应 R04b1/R04b2a/b 的授权、导入及 240–243 恢复，R04b3 的角色/组织 CAS 及 244–245 恢复，R04b4 的 Claim 中断恢复，R04b5 的文件依赖故障；各项双库证据见上文，不代表全部导入崩溃恢复边界已关闭。
- [x] 覆盖 Host Claim 提交前/后崩溃、Confirm 失败、租户资源归属不匹配和“有下载权但无共享权”；实现最小文档删除/撤权权威状态，立即禁止检索与来源读取，再由 R05 清理索引/释放引用。对应 R04b2b、R04b3、R04b4–R04b6：可控中断及遗留租约模拟覆盖持久化恢复，未执行真实杀进程/断电；授权下载者不能写入，任意 FileId 导入仍拒绝。当前尚无索引或检索实现，R05 派发与检索必须重新检查删除/撤权权威状态，不能从本项推导已经完成索引清理。
- [x] 实现范围约束、UUID v7、参数化双库 SQL、乐观并发、标准 ProblemDetails 与源生成 DTO；文件关联不得跨模块 JOIN/事务。对应 R04a、R04b1/R04b2b 与 R04b3 的分项双库/原生证据。
- [x] R04a 已建立知识库数据分类与批准的 Embedding/生成模型配置及版本，默认批准为空；双库验证未知分类、缺失/漂移批准和不可用模型拒绝。实际派发尚未实现，R05/R06 必须重新核对此策略与最新权威状态，R11 继续扩展重排、路由和遥测覆盖。
- [x] OpenAPI 与共享生成客户端由 R04b2c、R04b3c/d-client/e-client 分项交付；知识库入口包含 32 个 Operation，真实双库协议一致、生成产物无漂移，各项实际测试及矩阵证据见上文。底层文件异常和整体关闭条件仍由前两项核对。

### R05：有界解析、分块与索引 Worker（P1，依赖 R03/R04）

**新增责任位置：** `src/Modules/Full.NET.Modules.Ai/Knowledge/`、`Runtime/AiKnowledgeIndexWorker.cs`、`tests/Full.NET.UnitTests/Ai/AiKnowledgeChunkingTests.cs`；Provider 放入 R03 决策的适配边界，复用现有 Embedding 工厂与预算 Port。

**输入/输出：** 消费已授权文档版本及内容摘要；生成包含片段 ID、页码/位置、分块版本、模型/维度和索引代次的记录。向量索引键由版本/代次/片段确定，状态由版本条件推进。

- [x] R05a：交付有界 TXT/Markdown 解析基础，基线 `42bee22e`、快照 `ai-knowledge-text-parser-r05a`。新增 `Knowledge/AiKnowledgeTextParser.cs` 和 `tests/Full.NET.UnitTests/Ai/AiKnowledgeTextParserTests.cs`，不新增公开 API、DI 注册、迁移、模型调用或第三方依赖。此基础供后续 R05 的分块与 Worker 使用，本项不能把现有 `pending_processing` 推进为 ready。
  - [x] 先用 Unit 证明解析能力缺失，再建立实际流、摘要绑定、严格 UTF-8、中文/emoji、BOM、标题/表格/代码原文及稳定 UTF-16 位置的断言。输入上限沿用 R04 的 8 MiB；解析输出最多 2 Mi UTF-16 单元，超限拒绝且不截断。
  - [x] 实现 `ParseAsync(Stream, originalFileName, contentType, expectedContentHash, CancellationToken)`；复核安全名称及匹配 TXT/MD 类型、真实字节摘要，拒绝空白、非法 UTF-8、NUL、输入/输出超限及摘要漂移，使用内部强类型失败分类。只移除文件开头 UTF-8 BOM，其余文本及换行不改写；不执行脚本、Markdown、外链或文件名路径。
  - [x] 流归调用者所有；循环有界读取并传播取消、I/O 异常，不把基础设施异常当作空文档。PDF 直接拒绝，由后续独立进程解析切片实现 ADR-0013 的强制终止、网络/凭据隔离及预算验证；不以 Task.Run 超时冒充 PDF 硬限制。
  - [x] 根据实际新增 Unit 身份维护矩阵，运行 Release、聚焦/完整 Ai Unit、受影响 Architecture/AOT 分析、选择器完整 Ai slice、治理与分片核对；交付只声明内部解析基础验证范围，保留 R05/RAG 与容量开放，最终 diff/status 后在 main 独立提交。

  R05a 本地证据（2026-10-04）：
  - RED 先证实内部解析器缺失（1 项失败），再用只有类型和拒绝结果的桩运行真实行为断言：33 项中 23 失败、10 通过，退出码 2；实现后同一 33 项全部通过。最终 `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter 'FullyQualifiedName~AiKnowledgeTextParserTests' --minimum-expected-tests 33 --timeout 2m --no-ansi --progress off --report-trx --report-trx-filename text-parser-focused-final.trx --results-directory artifacts/r05a/focused-final` 为 33/33、零失败/跳过、退出码 0。
  - `pnpm test:aot:analyzers` 通过，零警告/错误；随后完整 JIT 图 `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo` 通过，Unit/Architecture 的 Release 构建复用已恢复的依赖，均零警告/错误。`pnpm test:dotnet:unit -- --selection ai-module --no-build` 462/462、`pnpm test:dotnet:architecture -- --no-build` 232/232，均零失败/跳过、退出码 0。Unit 实际发现 3931 项，新增 33 个解析身份已对应维护矩阵；此为全量发现，不代表运行了全量 Unit。
  - `pnpm test:integration:affected:plan -- --snapshot ai-knowledge-text-parser-r05a --phase slice` 选择完整 Ai 与 integration-matrix。`node artifacts/r05a/run-affected.mjs` 通过仓库选择器合并完整 Ai 过滤器，54/54、零失败/跳过、退出码 0，23m 00s；`node artifacts/r05a/verify.mjs` 核对正式 TRX 的全部 UID/FQN 与冻结集合一致，解析器/测试/矩阵及最终程序集摘要一致，Unit/Integration 的 Ai 模块副本与生产 DLL 摘要相同。TRX 有 1 项超长捕获文本被平台按字段上限截断，不影响身份和通过结果；原始日志、TRX、摘要与验收清单保存在忽略目录 `artifacts/r05a/` 及同前缀日志。
  - `pnpm test:integration:tooling` 54/54、`pnpm test:governance` 57/57、`pnpm test:naming` 33/33，均零失败/跳过、退出码 0；`pnpm test:integration:partitions` 实际发现 1138 项，无遗漏或重复，此为发现而非全量 Integration 执行。只读安全审查未发现 P1/P2；本项新类型仅由 Unit 消费，未改 Host.Api/Worker 的运行时注册和可达路径。
  - 本地 i7-12700H（14 核/20 线程）、64 GB Windows、SDK 10.0.401、两个测试 worker，原有五个容器保持运行。尚未接入 Worker 或执行新解析器的正式 Linux Native AOT 运行，本项 AOT 证据仅为分析器与架构检查；PDF 进程隔离、模型 Token 预算、分块/Embedding/索引及 ready 推进仍待后续 R05。无新增第三方分发资源；完整 RAG 与 `Capacity-not-verified` 保持开放。

- [x] R05b：有界 Token 分块核心，基线 `8943215f`、快照 `ai-knowledge-token-chunking-r05b`。新增 `Knowledge/AiKnowledgeTextChunker.cs` 与 `tests/Full.NET.UnitTests/Ai/AiKnowledgeChunkingTests.cs`，复用现有依赖树中的 `Microsoft.ML.Tokenizers 2.0.0`，不引入词表资源、模型默认映射、第三方版本或生产入口。内部方法显式接受已核准的 `TiktokenTokenizer` 及其版本，后续 Worker 必须绑定真实模型/词表并完成匹配验收；不能用字符数替代 Token 或把夹具词表用于生产。
  - [x] 用真实 BPE 夹具先建立失败行为断言，再实现 `Chunk(AiKnowledgeParsedText, TiktokenTokenizer, tokenizerVersion, AiKnowledgeChunkOptions, CancellationToken)`。每块记录原文 UTF-16 区间、精确正文 Token 数、序号；集合绑定来源摘要、解析/Tokenizer/分块版本和预算策略。结果仅供模块内部，失败不返回部分分块。
  - [x] 约束每块 1–4096 Token、重叠 0–256 且不超过每块预算的四分之一、最多 4096 块及最多 4 Mi 总 Token；单次 Tokenizer 输入最多 32768 UTF-16 单元，正文累计最多 6 Mi UTF-16 单元。优先窗口内后半段的换行边界，保护 surrogate pair 与 CRLF，逐块重新计数；整窗索引失效时最多实测 128 个完整前缀，不根据首字符计数推断更长前缀计数，搜索预算耗尽单独失败。拒绝无合法有界前缀、块数/总 Token/总正文超限；重叠只取不超过预算的原文后缀，保证覆盖和前进。
  - [x] 保留中文、emoji、组合字符、Markdown 标题/表格/代码和原始换行；取消在 Tokenizer 调用前后及迭代间传播。Tokenization 不归一化原文，拒绝带 Normalizer 或特殊控制 Token 映射的 Tokenizer，正文不得解释为控制 Token。Tokenizer 不响应调用中取消的事实不能被报告为可强制终止；可信词表与有界窗口是此基础的调用约束。
  - [x] 运行 RED/GREEN、完整 Ai Unit、Ai/AOT 架构与分析器、选择器完整 Ai slice、工具/治理及矩阵发现核对，按实际身份维护矩阵。正式模型词表匹配、标题/段落结构策略和长表格表头续接、PDF 隔离及 Worker/索引/ready 均保持开放；核对 diff/status 后在 main 独立提交。

  R05b 本地证据（2026-10-04）：
  - RED 先证明分块器缺失（1 项失败），类型桩上的 41 个行为身份为 33 失败、8 通过，退出码 2；初版实现 41/41 通过。只读审查再发现 BPE 整窗索引可跨 UTF-8 字节，且独立前缀 Token 数不单调；`aé` 回归 RED 为 1 项失败，`éaéb` 与超出 128 次完整前缀探测的回归 RED 为 2 项失败，均退出码 2。按有界逐前缀实测修正后，最终 `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter 'FullyQualifiedName~AiKnowledgeChunkingTests' --minimum-expected-tests 44 --timeout 3m --no-ansi --progress off --report-trx --report-trx-filename token-chunker-focused-final.trx --results-directory artifacts/r05b/focused-final` 为 44/44、零失败/跳过、退出码 0；搜索未覆盖完整窗口时明确返回 `BoundarySearchLimitExceeded`，不误报无合法分块。
  - `pnpm test:aot:analyzers` 通过，零警告/错误；随后 `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo` 恢复并构建完整 JIT 图，Unit/Architecture 用 `dotnet build <project> -c Release --nologo -p:BuildProjectReferences=false` 复用已恢复依赖，三项均零警告/错误、退出码 0。`pnpm test:dotnet:unit -- --selection ai-module --no-build` 506/506；`pnpm test:dotnet:architecture -- --selection ai-agentic-boundaries --no-build` 6/6，`pnpm test:dotnet:architecture -- --selection api-native-aot --no-build` 实际 73/73，均零失败/跳过、退出码 0。未运行全量 Architecture 或全量 Unit；Unit JSON 实际发现 3975 项，其中 Ai 506、新增分块 44，矩阵按实际身份增加 44，不把发现当作执行通过。
  - `pnpm test:integration:tooling` 54/54、`pnpm test:governance` 57/57、`pnpm test:naming` 33/33，均零失败/跳过、退出码 0；`pnpm test:integration:partitions` 实际发现 1138 项，无遗漏或重复，属于发现核对而非全量 Integration 执行。
  - `pnpm test:integration:affected:plan -- --snapshot ai-knowledge-token-chunking-r05b --phase slice` 选择完整 Ai 与 integration-matrix；`node artifacts/r05b/run-affected.mjs` 使用仓库选择器发现并冻结完整 Ai 的 54 个 UID/FQN，54/54、零失败/跳过、退出码 0，21m 45s。`node artifacts/r05b/verify.mjs` 核对正式 TRX 全部身份和七个源码/测试/矩阵/最终 JIT 程序集摘要一致；聚焦正式 TRX 的 44 个身份也与当前发现集合一致，Unit/Integration 的 Ai 模块副本与生产 DLL 摘要相同。平台按字段上限截断了 1 项超长捕获文本，身份和通过结果完整；日志、TRX、冻结摘要及验收清单在忽略目录 `artifacts/r05b/` 及同前缀日志。只读审查的三个 BPE 边界问题均由可失败回归关闭，最终未剩余已确认 P1/P2；审查自身只读，执行证据来自本地命令。
  - 本地 i7-12700H（14 核/20 线程）、64 GB Windows、SDK 10.0.401、`DOTNET_PROCESSOR_COUNT=2`，原有五个容器保持运行。词表由测试自行构造字节与合并项，无外部词表下载/复制或依赖版本变化；夹具不证明真实 Embedding 模型匹配。本项尚无生产调用者或运行时注册，未执行新分块器的 Linux Native AOT 运行，AOT 证据限于分析器及架构检查。正式模型/词表映射、标题/段落/表格语义策略、PDF 独立进程与 Worker/Embedding/索引/ready 仍开放，完整 RAG 保持 Planned、容量保持 `Capacity-not-verified`。

- [x] R05c：标题、段落与表格上下文分块基础，基线 `8a4327c2`、快照 `ai-knowledge-structured-chunking-r05c`。新增 `Knowledge/AiKnowledgeTextStructure.cs`、`Knowledge/AiKnowledgeStructuredChunker.cs` 和 `AiKnowledgeStructuredChunkingTests.cs`；复用 R05b 的验证、精确 Token 边界和重叠逻辑，既有 `Chunk` 原文窗口语义与版本不变。内部入口显式选择纯文本或 Markdown，不自动从正文猜测来源格式，不新增依赖、HTTP、DI、SQL 或 Worker。
  - [x] 先建立失败验证：短段落保持完整，ATX 标题与首段结合，层级标题按原文偏移记录；代码围栏内的假标题/表格不得成为上下文，TXT 不解释 Markdown。Markdown 只识别有界顶层 ATX、围栏/缩进代码和带管道的表格子集，不声称完整 GFM；保留原文、空白、换行及 Unicode，不渲染或执行内容。
  - [x] 结构扫描最多 65536 行、8192 块；每块标题与表头上下文最多 4096 UTF-16 单元。表头和分隔行的列数必须相等，转义管道不作为列界；续块重复权威原文表头，优先完整行，超长行按 R05b 完整 Unicode 边界回退。正文区间与重复上下文位置分开保存，不让拼接文本冒充单一原文区间。
  - [x] 对包含重复标题/表头的完整 Embedding 文本逐块计数，不能把上下文 Token 数减去后假定可加；Tokenizer 单次输入仍最多 32768 UTF-16 单元，包含重复上下文的总 Token、全部保留输出正文分配累计、块数及取消均有界。超限或搜索耗尽拒绝全部输出，不截断上下文、不返回部分结果，不授权模型外发。
  - [x] 运行新增 RED/GREEN、R05b 回归、完整 Ai Unit、Ai/AOT 架构与分析器、选择器完整 Ai slice、治理与发现核对，实际维护矩阵并只读复核后在 main 提交。正式模型词表绑定、PDF 隔离、Worker/Embedding/索引/ready 继续开放；新分块仅内部基础，不升级 R05 或 RAG 完成状态。

  R05c 本地证据（2026-10-04）：
  - 能力缺失 RED 为 1 项失败；类型桩上的 32 项真实 BPE 行为断言全部失败、退出码 2。初次编译的四个 CS1501 源于错误的 Span `Trim` 重载，核对本机 .NET 10 参考 XML 后改为字符集合重载；编译错误不算功能 RED。只读审查发现行末反斜杠跳过最后一列、连续同级标题的最终层级快照错误归属较早正文，两个问题对应的 3 个回归全部 RED，再修正 EOF 列结算与逐标题转换快照。复核未剩余已确认 P1/P2；审查自身没有执行构建或测试。
  - 首轮 76 项聚焦包含 2 Mi 字符低 Token 单段与长标题的放大预算夹具，触发 4m 时限后停在取消；只终止精确匹配本次报告名的测试进程，退出 -1，没有完整 TRX，不报告通过。排除这一夹具后 75/75、零失败/跳过、退出 0，10.796s；仅将该夹具改为同一长标题下 2000 个短段落，继续断言实际保留输出累计超限且没有部分输出。补齐审查、标题路径累计预算、全局块数与精确行数边界后，完整 82 项聚焦 82/82、零失败/跳过、退出 0，67.690s。原始超时日志保留；本项不宣称原单段夹具的吞吐或整体处理时限通过，真实词表及 Worker deadline 仍待验收。
  - `pnpm test:aot:analyzers` 通过，零警告/错误；随后 `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo` 完整恢复 JIT 图，Unit/Architecture 的 Release 构建用 `-p:BuildProjectReferences=false` 复用已恢复依赖，三项零警告/错误、退出 0。`pnpm test:dotnet:unit -- --selection ai-module --no-build` 544/544、零失败/跳过、退出 0；`pnpm test:dotnet:architecture -- --selection ai-agentic-boundaries --no-build` 6/6，`pnpm test:dotnet:architecture -- --selection api-native-aot --no-build` 实际 73/73，均零失败/跳过、退出 0。AOT 后程序集摘要发生变化，最终验收只使用恢复后的 JIT 程序集，不能混用之前编译结果。最终 `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter 'FullyQualifiedName~AiKnowledgeStructuredChunkingTests|FullyQualifiedName~AiKnowledgeChunkingTests' --minimum-expected-tests 82 --timeout 4m --no-ansi --progress off --report-trx --report-trx-filename structured-focused-final.trx --results-directory artifacts/r05c/focused-final` 为 82/82、零失败/跳过、退出 0，77.286s；`node artifacts/r05c/verify-focused.mjs` 确认正式 TRX 的全部 UID/FQN 与最终发现的 38 个结构分块及 44 个既有 Token 分块身份逐一一致。
  - 最终 Unit JSON 发现 4013 项，其中 Ai 544、结构分块 38；矩阵按新增 38 个真实身份维护。`pnpm test:integration:partitions` 发现 1138 项，无遗漏或重复，均属于发现而非全量执行；未运行全量 Unit 或全量 Architecture。`pnpm test:integration:tooling` 54/54、`pnpm test:governance` 57/57、`pnpm test:naming` 33/33，均零失败/跳过、退出 0。
  - `pnpm test:integration:affected:plan -- --snapshot ai-knowledge-structured-chunking-r05c --phase slice` 选择完整 Ai 与 integration-matrix；`node artifacts/r05c/run-affected.mjs` 按仓库选择器发现并冻结完整 Ai 的 54 个 UID/FQN，正式运行 54/54、零失败/跳过、退出 0，26m 23.771s。`node artifacts/r05c/verify.mjs` 核对正式 TRX 全部身份，以及 10 项源码、测试、矩阵和最终 JIT 程序集摘要一致；生产、Unit、Integration 的 Ai 模块 DLL 副本 SHA-256 均为 `9484C220D98E48CD7020F6957CD9DFBB68CD0F74C2F613C2093C16515ABC7020`。平台按字段上限截断了 1 项超长捕获文本，身份与通过结果完整；原始失败/超时与最终通过日志、TRX、冻结摘要及验收清单保存在忽略目录 `artifacts/r05c/` 及同前缀日志。
  - 扫描规则参考 [GFM 标题、代码与表格规范](https://github.github.com/gfm/)，实现仅为已列明的顶层子集：不解析 Setext、嵌套容器、HTML 或 inline AST，表格数据行需含未转义管道；不插入、忽略或规范化原表格单元格。来源与重复上下文各自记录 UTF-16 区间，完整 Embedding 文本重新计费。无词表下载/复制或依赖版本变化，合成 BPE 不代表真实模型；未新增生产调用者、运行时注册、SQL、HTTP 或索引状态推进，AOT 证据只覆盖分析器及架构，未执行新分块器的 Linux Native 运行。本地 i7-12700H（14 核/20 线程）、64 GB Windows、SDK 10.0.401、`DOTNET_PROCESSOR_COUNT=2`，原有五个容器保持运行；真实模型/词表、PDF 隔离和 Worker/索引/ready 仍开放，RAG 保持 Planned、容量保持 `Capacity-not-verified`。

- [x] R05d：真实 Embedding 词表与精确模型配置绑定，基线 `d97b8aad`、快照 `ai-knowledge-model-tokenizer-r05d`。新增 `Knowledge/AiKnowledgeCl100kTokenizer.cs`、`Knowledge/AiKnowledgeModelChunker.cs`、固定 `Knowledge/Tokenizers/cl100k_base.tiktoken.deflate` 及完整许可通知，扩展模块资源声明、`AiKnowledgeModelChunkingTests.cs` 和测试矩阵；不新增 NuGet 依赖、SQL、HTTP、DI、模型调用或 Worker。仅声明内部离线切片的本地验收，追加 Linux 发布未通过，原生分块运行仍未验证。
  - [x] 先验证内部模型分块入口缺失，再建立行为 RED：精确配置 ID/版本、启用状态、Host/租户范围、仅官方 OpenAI 目的地及精确 `text-embedding-3-small`/`text-embedding-3-large` 标识。拒绝未知模型、兼容网关、Azure 部署名、Ollama 和未经审批配置；来源读取及模型外发授权仍由后续 Worker 权威复核。
  - [x] 词表来自 Microsoft.ML.Tokenizers.Data.Cl100kBase 2.0.0 的固定资源，压缩/解压字节长度及 SHA-256 均冻结；与独立 tiktoken 0.12.0 的完整 100256 项普通词表逐项核对。使用静态源生成 cl100k 正则及普通文本计数，无 Normalizer/特殊控制 Token；运行时只读取嵌入资源，拒绝缺失、漂移、超限及取消，不加载类型名、路径或网络。
  - [x] 用独立参考 Token ID/计数覆盖中英、多语言、emoji、组合字符、大小写缩写、数字、空白/换行及字面特殊 Token；绑定后的结构分块保留实际模型配置、Provider/模型和词表版本，完整上下文计费及失败无部分输出沿用 R05c。同步 SDK 初始化/计数只在调用前后观察取消，不声明硬中断或真实模型用量一致性。
  - [x] 运行 RED/GREEN、既有解析/分块回归、完整 Ai Unit、Ai/AOT 架构与分析器、选择器双库影响集、许可/治理及发现核对；只读审查、最终摘要/diff/status 后在 main 提交。PDF 隔离、实际 Embedding 账本及 Worker/索引/ready、模型端到端与容量均保持开放。

  R05d 本地证据（2026-10-04，内部离线切片；Linux 发布及原生分块运行未通过/未验证）：
  - 入口缺失 RED 为 1 项失败；类型桩上的 52 项行为断言全部失败、退出 2。初次实现的两个编译错误分别是 `RegexPreTokenizer` 缺少明确的 `specialTokens` 参数和 Provider 常量命名空间错误，核对本机 SDK XML 及现有 Contracts 后修正；编译错误不算功能 RED。首次 167 项聚焦为 166 通过、1 失败：`Uri` 会将 `%76%31` 规范化为 `v1`。补齐点段及反斜杠后，12 项目的地回归为 2 失败、10 通过（编码与点段失败，反斜杠已拒绝）；随后改为在规范化之前精确约束原始目的地形式，保留主机/协议大小写及默认 443 端口等价。完整解析/分块/模型聚焦 169/169、零失败/跳过、退出 0，66.176s。只读审查最终无已确认 P1/P2，审查自身没有构建或执行测试。
  - `artifacts/r05d/reference-venv/Scripts/python.exe artifacts/r05d/reference.py` 用独立 tiktoken 0.12.0 核对全部 100256 项词条及 rank，并冻结 16 组普通文本 Token ID 参考值。压缩资源 522929 字节、SHA-256 `05dd7477a7feeda8b71eb826b7e4c702e5df6f44c35d6ce4d1ef3f7bfc8a0f36`；解压资源 1190718 字节、SHA-256 `91423dd5a6f4f288c81534b7608e52cc6d54bc7a4bb522b31e9ed9a9b87061be`。资源逐字节来自 Microsoft.ML.Tokenizers.Data.Cl100kBase 2.0.0（固定提交 `efefa92f4486a43047c5b47618885a71bf7f0967`）；没有生产 Data 包依赖、动态模型工厂、Normalizer 或控制 Token 映射，完整 Microsoft/OpenAI MIT 通知与资源一并嵌入并登记在 `THIRD-PARTY-NOTICES`。仅本地参考实验下载词表，生产只读固定嵌入资源。
  - `pnpm test:aot:analyzers` 退出 0；随后 `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo` 完整恢复 JIT 图，Unit/Architecture Release 构建以 `-p:BuildProjectReferences=false` 复用依赖，三项均零警告/错误、退出 0。`node artifacts/r05d/run-unit.mjs` 保留仓库完整 `ai-module` 选择、最低发现数及 10m 时限，只追加正式 TRX：598/598、零失败/跳过、退出 0，1m 17.065s。`node artifacts/r05d/verify-unit.mjs` 核对最终 JSON 发现的 4067 个 Unit、598 个 Ai、54 个新增模型测试与正式 TRX 的完整 UID/FQN；其中四类解析/分块测试 169 项全部包含在最终通过集合。`pnpm test:dotnet:architecture -- --selection ai-agentic-boundaries --no-build` 6/6，`--selection api-native-aot --no-build` 73/73，均零失败/跳过、退出 0；首次误写 `ai-agentic` 被入口拒绝，未执行测试，不计通过。
  - `pnpm test:integration:tooling` 54/54、`pnpm test:governance` 57/57、`pnpm test:naming` 33/33，均零失败/跳过、退出 0。`pnpm test:integration:partitions` 发现 1138 项，无遗漏或重复，仅为发现而非全量 Integration 执行；本轮未执行全量 Unit 或全量 Architecture。
  - 固定模型映射依据 [OpenAI Embeddings 文档](https://developers.openai.com/api/docs/guides/embeddings)，只接受官方目的地及精确的第三代两个 Embedding 模型名，不由兼容协议名推断供应商词表。映射支持不表示外发获准或实际模型用量已测；未新增 Worker、Embedding 调用、HTTP、SQL、索引状态推进或 ready。PDF 隔离、实际模型账本及完整 RAG 仍开放，容量保持 `Capacity-not-verified`。
  - 首次 `pnpm test:aot:publish:linux` 在 WSL 可用内存不足 500 MB、4 GB swap 耗尽且内存压力超过 80% 时，Docker 状态查询返回 500，发布随后以 unexpected EOF、退出 125 结束，没有新发布清单。原有服务自动恢复，两个数据库容器曾退出，随后启动恢复；尝试终止本轮 ILC 时其 PID 已消失，归属检查拒绝操作，未执行终止、WSL/Docker 重启或配置修改。保留失败日志后，`node artifacts/r05d/run-api-aot-publish-bounded.mjs` 使用官方入口的同一发布属性、隔离输出、告警及产物门禁，仅限制本轮临时 SDK 容器为 2 CPU、6 GB 内存/总内存和 `DOTNET_PROCESSOR_COUNT=2`。原始发布失败不计为通过。
  - 首次双库影响集在数据库恢复后启动，首个 SQL Server 用例于模板初始化的 `ALTER DATABASE fullnet_it_schema_sql` 失败，尚未进入 AI 业务断言；模板库随后不存在且全部数据库 ONLINE。第二次完整重跑于 `CREATE DATABASE` 失败，明确报告同名 `.mdf` 已存在。`DB_ID` 为 NULL 且 `sys.master_files` 对这两个固定模板文件无引用，确认目录残留与数据库注册状态不一致；不把这些初始化失败当作词表功能 RED。两次仅终止精确匹配本轮完整 Ai 过滤器、报告目录及最低 54 身份的测试进程，均退出 -1，各保留一项失败流式记录和日志，没有完整正式 TRX，不计为通过。确认测试容器、模板名、绝对路径、无符号链接及目标不存在后，原生容器内将孤立的 `.mdf`/`.ldf` 改名保留为 `.r05d-recovery-20261004` 备份，未删除文件或操作任何已注册数据库；随后完整重跑 54 项，源码、矩阵及最终 JIT 图不变。
  - 最终 `node artifacts/r05d/run-affected.mjs` 保留选择器的完整 Ai 54 身份、最低发现数及 30m 时限，正式运行 54/54、零失败/跳过、退出 0，26m 55.690s。`node artifacts/r05d/verify.mjs` 核对正式 TRX 的所有 UID/FQN 与冻结的完整发现集合逐一相符，并确认 17 项源码、资源、许可、测试、矩阵及最终 JIT 程序集摘要未变化；生产、Unit、Integration 的 Ai 模块 DLL 副本 SHA-256 均为 `AD7DFF532C0322921E05CA75BD2F4E98A014652C5E8C84636BB1EE0E99922361`。平台仅按字段上限截断 1 项捕获文本，测试身份与通过结果完整。失败/RED/最终日志、正式 TRX、独立参考、冻结摘要与验收清单保存在忽略目录 `artifacts/r05d/` 及同前缀日志。
  - 6 GB 重试仍在 ELF 对象的重定位生成阶段抛出 `System.OutOfMemoryException`，退出 1，没有成功发布清单；原有服务保持运行。保留日志及该入口副本后，只将本轮临时 SDK 容器的内存/总内存提高为 10 GB，仍为 2 CPU、`DOTNET_PROCESSOR_COUNT=2`；未更改发布属性、告警允许清单、产物阈值或 WSL 配置。10 GB 重试期间 WSL swap 再度耗尽，内存压力 some/full 达到 95%/85%，Docker 状态查询返回 500；确认单一本轮 ILC 的 PID、命令及容器 cgroup 后仅终止该编译进程，ILC 退出 143、入口退出 1。没有成功发布清单或新分块器原生运行证据，所有发布失败日志保留；不升级任何 Native AOT 能力状态，后续生产消费者接入仍需对应原生产物验证。
  - 最后暂存差异检查发现许可通知末尾一个多余空行，仅移除该换行；`node artifacts/r05d/verify-final.mjs` 确认许可正文逐字未变，另 16 项冻结的源码、词表、测试、矩阵与 JIT 程序集摘要仍一致。单元/双库与发布尝试中的嵌入许可包含该额外换行，未重建或将这次机械差异伪称为行为重测；按文字/机械变更使用直接结构检查。暂存词表与工作区固定压缩字节逐一一致、SHA-256 相同，`git diff --cached --check` 通过。
  - 本地 i7-12700H（14 核/20 线程）、64 GB Windows、SDK 10.0.401；Linux SDK 容器 10.0.400，WSL 为 16 GB 内存/4 GB swap。Unit/Architecture/Integration 使用 `DOTNET_PROCESSOR_COUNT=2`。未修改 WSL/Docker 配置或主动重启引擎，未停止原有 Worker/控制面服务；测试数据库首次引擎失稳后已启动恢复，原有五个容器保持存在。R05d 未接入 API/Worker 运行路径，仍不表示真实模型账本、生产解析/索引/ready、RAG 或容量已验证。

- [x] R05e1：PDF 分页输出验收与获批模型分块（基线 `30c0d58a`，快照 `ai-knowledge-pdf-pages-r05e1`）。依据 ADR-0013，先完成隔离解析输出的消费边界；不引入 PdfPig 依赖、运行进程或启用 PDF 导入。
  - 文件：新增 `Knowledge/AiKnowledgePdfOutputReader.cs`、`Knowledge/AiKnowledgePdfModelChunker.cs`、`tests/Full.NET.UnitTests/Ai/AiKnowledgePdfPagesTests.cs`；扩展 `AiKnowledgeParsedText` 的固定来源枚举及 Token 分块验证，更新矩阵真实发现数。
  - [x] RED：缺失入口断言后建立行为失败，覆盖严格 JSON、重复/未知属性、版本/摘要、连续页码、空白页、非法 Unicode/NUL、输入/输出/页数预算和取消；可信来源大小/摘要作为独立参数，不从解析输出选择文件、租户或模型。
  - [x] GREEN：`ReadAsync(Stream, string expectedContentHash, int sourceSizeBytes, CancellationToken)` 按固定线格式版本 1 和 `pdfpig-0.1.16-content-order-v1` 接收完整 JSON；逐页保留原始 Unicode/换行，原始来源最大 5 MiB（收紧 8 MiB 上传边界）、最大 128 页、总 2 Mi UTF-16 字符，线字节预算覆盖最坏 JSON 转义。来源凭据、URL 和任意版本不可进入结果；失败无部分页。
  - [x] GREEN：`Chunk(AiKnowledgeBaseRecord, AiModelConfigRecord, AiKnowledgeParsedPdf, AiKnowledgeChunkOptions, CancellationToken)` 复用 R05d 精确配置批准与真实词表，页内偏移以 UTF-16 表达，页码从 1、全局块序号从 0；禁止跨页重叠，Token/块数/字符预算按整份 PDF 计算，失败无部分块。
  - [x] 验证：先运行聚焦 RED/GREEN，再完整 Ai Unit、Ai/AOT Architecture、分析器、治理、命名、分片发现及选择器完整双库影响集；只读审查后在 main 提交。
  - 开放门禁：该接收器不证明解析进程身份、摘要正确计算或来源授权；后续执行器必须向沙箱传入已授权字节并复核输出。真实 PdfPig 进程仍须禁网、无凭据、文件签名复核、硬内存/解压/时间预算、取消强杀及恶意压缩/损坏/原生产物实验；验证前 PDF 导入、Worker/索引/ready、RAG 和容量均不得升级。

  R05e1 本地证据（2026-10-04，仅内部消费/分块切片）：
  - 缺失入口 RED 为 1 项失败；44 项类型桩行为测试为 31 失败、13 通过、退出 1，通过项仅命中统一拒绝桩。编译阶段的内部枚举公开参数和 `long` 上限关系模式错误已核对修正，不计为行为 RED。首次 GREEN 为 42/44：来源错误复用了 8 MiB 上传限制，以及取消断言错误排除了 `TaskCanceledException`。独立收紧 PDF 来源为 5 MiB，并接纳合法取消子类；补齐边界、碎片读、转义重复字段、JSON 文本内非法 UTF-8 和伪造记录后，最终 `AiKnowledgePdfPagesTests` 48/48、零失败/跳过、退出 0，2.204s。
  - `pnpm test:aot:analyzers` 退出 0；随后恢复最终 JIT 图的 Integration Release 构建、Unit/Architecture 的 `-p:BuildProjectReferences=false` Release 构建均零警告/错误、退出 0。`node artifacts/r05e1/run-unit.mjs` 保留官方 `ai-module` 选择、最低发现数与时限，仅增加正式 TRX，646/646、零失败/跳过、退出 0，1m 23.708s。`node artifacts/r05e1/verify-unit.mjs` 核对全部 UID/FQN 与最终 JSON 发现相符，完整知识解析/分块子集 217 项包含新增分页 48 项。最终 Unit 发现 4115 项；未执行全量 Unit。
  - `pnpm test:dotnet:architecture -- --selection ai-agentic-boundaries --no-build` 6/6，`--selection api-native-aot --no-build` 73/73，均零失败/跳过、退出 0。`pnpm test:integration:tooling` 54/54、`pnpm test:governance` 57/57、`pnpm test:naming` 33/33 均通过；`pnpm test:integration:partitions` 发现 1138 项，无遗漏/重复，属于发现而非全量执行。最终计划同步后再运行治理，核对暂存差异及冻结摘要。
  - `pnpm test:integration:affected:plan -- --snapshot ai-knowledge-pdf-pages-r05e1 --phase slice` 选择完整 Ai 与 integration-matrix；`node artifacts/r05e1/run-affected.mjs` 复用官方过滤器与预算，冻结 SQL Server/MySQL 各 27 个身份，54/54、零失败/跳过、退出 0，23m 54.365s。`node artifacts/r05e1/verify.mjs` 核对正式 TRX 的全部 UID/FQN 及 20 项源码/测试/矩阵/最终 JIT 输入摘要；生产、Unit、Integration 的 Ai DLL 副本 SHA-256 均为 `074A97CF24D78FED208BF229E87F256FDB8C7B0C0A39339744971E30320A4925`。平台仅截断 1 项超限捕获文本字段，身份及通过结果完整。原始构建、行为 RED/GREEN、最终日志、TRX、冻结摘要与验收清单保存在忽略目录 `artifacts/r05e1/` 及同前缀日志。
  - 最终只读审查无确认 P1/P2；审查未运行测试。固定解析器标识只是接收条件，不证明真实 PdfPig 已执行；摘要由权威来源独立传入，输出声明不代替来源授权或进程身份。本轮没有解析库、进程、HTTP、SQL、DI、模型调用或索引状态推进；未执行本切片 Linux Native 发布/运行。PDF 隔离、Worker/Embedding 账本、ready 与完整 RAG 仍开放，容量保持 `Capacity-not-verified`。本地 i7-12700H（14 核/20 线程）、64 GB Windows、SDK 10.0.401、`DOTNET_PROCESSOR_COUNT=2`，双库使用既有本地 Docker 容器；原有五个容器保持运行。

- [x] R05e2：真实文本 PDF 独立工具及本地硬隔离验收（基线 `7b0dd28b`，快照 `ai-knowledge-pdf-sandbox-r05e2`），执行 ADR-0013；不注册 Worker 或启用业务 PDF 导入。
  - 项目拆分证据：`src/Tools/Full.NET.Ai.PdfParser/` 只依赖中央锁定的 PdfPig 0.1.16 与 BCL，不能引用模块/Host/数据/模型 SDK。真实线格式消费者为 R05e1 的 `AiKnowledgePdfOutputReader` 与分页模型分块，Unit 串联真实生产代码验证；本地沙箱驱动实际启动原生工具。模块/Host 无编译期解析库引用，独立可强杀/禁网/无凭据与依赖闭包隔离是拆分收益；Architecture 固化方向及 Host 排除。新增项目进入解决方案，生产许可完整保留包的 Apache-2.0 及外部组件通知。
  - 文件：新增工具项目、`Program.cs`、有界过滤器/输出组件、许可、固定基镜像 Dockerfile；新增 `scripts/ai/pdf-sandbox.mjs` 及 `tests/ai-evaluation/pdf-sandbox/` 原生验收驱动/恶意子进程探针；扩展 Ai Unit/Architecture、测试矩阵、中央版本和活动计划。
  - [x] RED：独立项目缺失；类型桩上的真实中文/压缩 PDF、损坏/空白/扫描/签名、输入/分页/输出/累计解压预算、未知编码/预测器及固定失败码；实际输出经 R05e1 消费，未完整成功不得返回部分页或块。
  - [x] GREEN：工具仅 stdin 原始 PDF → stdout 完整版本 1 JSON，stderr 固定失败码，无路径/模型/凭据参数；严格签名，来源 5 MiB、128 页、2 Mi UTF-16 字符、最大嵌套 64。原创 Flate/ASCIIHex 有界解码，累计解码 32 MiB，拒绝其他编码及非 1 预测器；不调用默认无界 Flate，不忽略过滤器错误。
  - [x] 沙箱：仅可信固定镜像 ID，独立唯一容器，禁网/只读/非 root/禁提权/去全部能力/无挂载和凭据；512 MiB 内存及总内存、2 CPU、32 PID、30s 硬时限，有界 stdout/stderr，禁用容器日志。容器内固定 watchdog 不依赖解析 SDK 取消；外部取消/超时必须终止本次已核对归属的容器并确认停止。清理失败关闭，不操作既有资源。
  - [x] 验收：锁定依赖并审计，JIT RED/GREEN；linux-x64 Native AOT 工具独立发布与启动，真实成功/失败/压缩炸弹/网络与凭据隔离/硬内存/超时/取消/洪泛输出实验；Ai Unit/Architecture、AOT 分析与边界、治理/命名/分片发现及选择器双库影响集。安全只读审查、最终摘要/diff/status 后在 main 提交。
  - 停止条件与开放范围：发布告警、新许可风险、硬边界或恶意文件实验失败不能启用 PDF；实测预算只声明本地环境及支持的编码子集。文件来源授权、派发前模型/文档权威复核、Worker 租约/索引/预算账本/ready、业务端到端与容量继续开放；本地 Docker 驱动不证明 Kubernetes 调度及生产部署已接入。

  R05e2 本地证据（2026-10-04，仅独立工具和本地隔离边界）：
  - 缺失项目 Architecture RED 为 1 项失败；类型桩 Unit RED 为 27 项中 26 失败、1 通过。修复常量恒真断言的编译分析器错误后才记录行为 RED；首次 GREEN 27/27。只读安全审查复现间接 DecodeParms 被忽略、未结束 DEFLATE 伪造正确校验和、重复关联 ID 误清理旧任务三处 P2。新增回归 RED 为 36 项中 4 失败、32 通过，真实重复 ID 实验失败；显式拒绝间接参数、原创 RFC 1951 最终帧/长度/距离预算校验、内部独立所有权 nonce 后，解析 Unit 36/36，二次只读审查无确认 P1/P2。
  - `node tests/ai-evaluation/pdf-sandbox/build-native.mjs` 固定 SDK/运行时 digest 与 linux-x64 锁文件，工具及独立对抗探针 Native AOT 发布退出 0、零告警；编译容器限定 2 CPU/2 GiB，不构建 Host.Api 完整原生闭包。正式原生工具 SHA-256 为 `5579f68c1ba04b8ad9cd0339ce417b3acf412db3405dbc24a540bf401eaec67d`，正式镜像 ID 为 `sha256:f227466641fd3ace82e0a1baded526d1769402d7f6ce5681d73a3e20a016eb56`；完整 PdfPig Apache/PDFBox/FontBox/Adobe 许可随工具发布。`dotnet list src/Tools/Full.NET.Ai.PdfParser/Full.NET.Ai.PdfParser.csproj package --vulnerable --include-transitive --format json` 退出 0，无已知漏洞报告。
  - `node tests/ai-evaluation/pdf-sandbox/run-native.mjs` 最终 23 项观测全部通过，退出 0；覆盖真实中文/压缩页、128/129 页、扫描/空白/坏签名/坏文件、未知编码、直接及间接预测器、伪造校验和的未结束流、单流炸弹及跨流解码预算、嵌入动作、真实资源隔离、超时/取消/内存强杀、stdout/stderr 洪泛、重复关联 ID，以及协调进程被杀后 watchdog 仍终止解析器。探针实测只有 loopback、外部连接不可达、无宿主凭据、只读/非 root/零能力/禁提权，cgroup memory=536870912、swap=0、CPU=2、PID=32；OOMKilled=true 与强杀 exit=137 为实际退出证据。本次所有运行容器已移除，原有五个容器保持运行。
  - `node --test tests/ai-evaluation/pdf-sandbox/*.test.mjs` 7/7、零失败/跳过、退出 0；其中控制层模拟确定性覆盖启动中取消、强杀拒绝、删除失败和 create 响应丢失，模拟不充当原生隔离证据。驱动只有完整成功、EOF、exit=0、停止和清理均确认后才返回输出；未知控制状态保持失败。
  - `node scripts/testing/run-api-aot-analyzers.mjs` 退出 0；恢复最终 JIT 的 Integration Release 构建、独立工具及 Unit/Architecture 构建均零警告/错误。`node artifacts/r05e2/run-unit.mjs` 复用官方 Ai 选择/最低数/时限，682/682、零失败/跳过、退出 0，1m 19.525s；Unit 总发现 4151、Architecture 总发现 233，不声称全量执行。官方 Architecture 的 `ai-agentic-boundaries` 7/7、`api-native-aot` 73/73；命名 33/33，均通过。
  - `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-sandbox-r05e2 --phase slice` 选择 integration-matrix 与双库 Smoke；工具链及反馈契约 65/65、治理 57/57、分片发现 1138 项无遗漏/重复，完整 Smoke 8/8、零失败/跳过、退出 0，5m 13.742s。`node artifacts/r05e2/verify.mjs` 核对 Ai 与 Smoke 正式 TRX 的全部 UID/FQN、34 项未变输入，并明确保留 1 项 README 追加控制故障测试说明的纯文档前后摘要。正式和 Unit 中工具 DLL 摘要同为 `25315a1eb225e37f0570661b2f5e3ba3a457a039422b300001525ac13c3d2659`；原生清单另保留最终 24 项源码/许可/锁文件摘要。原始日志、TRX、冻结输入与验收清单在忽略目录 `artifacts/r05e2/`、同前缀日志及 `artifacts/ai-evaluation/r05e2/`。
  - 本地 i7-12700H（14 核/20 线程）、64 GB Windows、SDK 10.0.401，Linux SDK 10.0.400；受测独立工具预算为 512 MiB/2 CPU/32 PID/30s。未验证完整分块器/Host 原生运行、真实 Worker 接入、模型/来源派发前复核、Embedding 账本、索引与 ready、Kubernetes 部署或容量；完整 RAG 保持 Planned，容量保持 `Capacity-not-verified`。

- [x] R05e3：独立 PDF 控制器的受限子进程协议（基线 `fd02da28a031b5a49489ad6d48485a9e4d5366b7`，快照 `ai-knowledge-pdf-cli-r05e3`）。
  - 文件：新增 `scripts/ai/pdf-sandbox-cli.mjs`、固定输出辅助进程 `pdf-sandbox-publish.mjs`、`tests/ai-evaluation/pdf-sandbox/cli.test.mjs`、`cli-transport.test.mjs`、`cli-process-publication.test.mjs`、`cli-publication-death.test.mjs`、`run-cli-native.mjs`；更新既有驱动的 watchdog/kill 竞态、生命周期回归及同目录 README。复用既有 `runPdfSandbox`，不复制 Docker 所有权与清理控制器。
  - 接口：CLI 只接受不可变 `--image-id`、权威 `--content-hash`、`--size-bytes`、可缩短的 `--timeout-ms` 和仅用于跟踪的可选 `--run-id`；stdin 为授权后的原始字节，stdout 成功时为原有 v1 PDF 输出。读取也纳入最长 30s，严格限制 5 MiB，并在启动容器前比较实际摘要、大小。错误只输出固定机器码，退出 2；成功必须确认停止、清理、策略、完整输入和 exit=0。
  - 步骤：先执行 CLI 拒绝/有界读取 RED；实现严格参数与有界传输；补模拟成功、清理失败、信号取消及输出门禁；通过真实原生镜像运行中文分页、扫描件拒绝、运行中取消、超时及输出洪泛，并复核本次资源清理；运行影响集规划、相关测试和治理，完成只读安全审查后在 main 提交。
  - 消费约束：后续 Worker 必须以参数数组和管道启动可信 Node/脚本，以 EOF、exit=0、无 stderr 加 `AiKnowledgePdfOutputReader` 复核摘要/分页后消费；控制器失败/死亡时停止接受并关闭管道，不能只等 EOF 或把杀掉控制器等同于停止容器。本切片交付本地进程协议及其实际验收消费者，不注册生产 Worker、不挂载 Docker socket；分页持久清单、业务来源/会话复核与 PDF 入队仍在后续切片，完整 RAG 保持 Planned。

  R05e3 本地证据（2026-10-06，仅本地子进程协议）：
  - CLI 首轮 RED 5/5 失败；其后补来源完整性、取消原因、终端关闭错误、最终取消及 watchdog/kill 竞态回归。只读审查和真实未读管道实验发现同步 stdout 阻塞计时器，以及异步文件写入虽可触发计时器仍阻塞 `process.exit`；最终将写入交给固定、空环境的辅助进程，由控制器按剩余预算强杀并等待其退出。未把失败实验计为通过，保留原始失败清单和最小进程复现于 `artifacts/ai-evaluation/r05e3/`。
  - `node --test tests/ai-evaluation/pdf-sandbox/*.test.mjs` 最终 30/30 通过，零失败/跳过、退出 0；真实进程覆盖受阻输出、取消、完整交付、空结果及控制器死亡后先关闭继承管道、辅助写入进程随之退出。复审未发现剩余确认 P1/P2。
  - `node tests/ai-evaluation/pdf-sandbox/run-cli-native.mjs` 11 项观测全部通过、退出 0；复用源码、原生二进制摘要与不可变镜像均未变的 R05e2 构建，重新验证中文/压缩页、扫描/坏文件拒绝、实际隔离、超时/取消、输出洪泛、控制器死亡后的解析器 watchdog。真实未读 stdout 实验先确认容器清理，再以 20s 剩余预算观测控制进程退出 2（20154 ms，含启动开销），错误仅固定超时码。本次容器均已清理，原有三个运行容器保持运行；运行时输入摘要与验收期间冻结清单一致。
  - `node tests/ai-evaluation/pdf-sandbox/run-native.mjs` 原隔离驱动 23 项观测重新通过、退出 0，覆盖解压预算、损坏流、128/129 页、重复关联 ID、真实 OOM、强杀、洪泛及 watchdog。新旧控制路径的运行时代码摘要均复核一致；原驱动运行后仅追加 README 的控制器死亡消费说明，不以该纯文档差异宣称运行证据失效或扩大验收。完整清单分别在 `artifacts/ai-evaluation/r05e3/acceptance.json` 与 `artifacts/ai-evaluation/r05e2/acceptance.json`，旧验收另保留到本次证据目录。
  - `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-cli-r05e3 --phase slice --plan` 判定无 Integration 影响；此次只改脚本、测试和说明，未修改 .NET/SQL/宿主闭包，不重跑双库或 Host AOT，不声称这些未执行套件通过。`pnpm test:governance` 57/57、`pnpm test:naming` 33/33、语法及 diff 检查均通过、退出 0。本机 Windows、Node 24.12.0，解析器运行于 Docker Linux Native AOT；未验证 Linux Node 控制器、生产 Worker、Kubernetes 调度、PDF 分页持久清单或容量。

- [x] R05f1：持久化索引任务与租约边界（本次切片，仅内部持久化，不注册 Worker 或宣布 ready）。
  - [x] 新增成对 `246_AiKnowledgeIndexTask.sql`：AI 自有任务以 UUID v7 为代次，唯一绑定来源版本、文档版本及知识库授权版本；保存精确模型配置/版本、有限尝试次数、租约及失败机器码。只允许 queued/running/failed/cancelled，不提供空索引 ready。
  - [x] `Persistence/AiKnowledgeIndexTaskSql.cs` 与 `Knowledge/AiKnowledgeIndexTaskStore.cs` 经既有执行器访问 AI 表；Host/可信租户分离。入队、领取、续租和失败写回均复核未删除、启用、版本、最新已确认文件及获批模型；取消允许在删除/禁用后终止工作。外部 Files/模型调用不进入事务。
  - [x] 真实双连接 RED/GREEN 验证唯一入队、争抢、到期接替、旧 token 隔离、取消、重试上限、来源/批准失效和跨租户拒绝；冻结迁移允许集合，验证缺索引/注释的未记账恢复与已有行保留。
  - [x] 命名/SQL/治理、矩阵发现、Ai Unit/Architecture、AOT 分析及选择器的对应双库恢复与 Ai 影响集；只读安全审查后在 main 提交。本切片不证明真实后台调度、Embedding 预算/索引写入、业务 PDF 或 RAG 已可用。

  R05f1 本地证据（2026-10-04，仅内部持久化边界）：
  - 实施基线 `main`、`4a4fef308fa0b10b4a2a8597136959604c4170f1`，任务快照 `ai-knowledge-index-lease-r05f1`。迁移缺失 RED 为 4/4 失败，存储桩 RED 为 10/10 失败；补充错误来源版本领取回归后，双库 RED 为 2 失败/2 通过，再通过静态 SQL 显式绑定请求 VersionId/DocumentVersion/KnowledgeBaseVersion 修正。生产入口未注册该内部存储。
  - 每库以 11 种独立权威失效分别验证领取、续租/失败和重试，共 33 个独立数据集；包含健康续租对照、禁用/删除、来源版本/导入状态、批准模型/版本/租户失效。另覆盖双连接争抢、到期接替、三次尝试上限、旧 token、取消后拒绝写回、Host 模型获批供租户使用、跨租户/所有者拒绝和缺注释/索引恢复。最终聚焦双库 4/4、零失败/跳过、退出 0，16.894s；只读审查修正续租断言缺乏有效对照后，复审无确认 P1/P2。SQL Server 到期边界夹具按整秒固定参数，避免 DateTime 测试绑定精度造成假失败；生产仍使用 IClock 的 DateTimeOffset。
  - `node artifacts/r05f1/run-unit.mjs` 复用官方选择、最低数和时限：Ai Unit 694/694、零失败/跳过、退出 0，1m 04.174s。`node scripts/testing/run-dotnet-test-suite.mjs architecture --selection ai-agentic-boundaries --no-build` 7/7；同入口 `api-native-aot` 73/73，均零失败/跳过。Unit 总发现 4163、Architecture 233；不声明全量执行。
  - `dotnet build src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj --configuration Release --nologo -p:FullNetAotAnalysis=true -clp:ErrorsOnly -m:4 -nr:false` 退出 0、零警告/错误，2m 33.98s；随后恢复 JIT，Integration/Unit/Architecture Release 构建均退出 0、零警告/错误。命名 33/33、SQL 安全 5/5、治理 57/57 均通过。
  - `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-index-lease-r05f1 --phase slice` 最终退出 0：工具链/反馈契约 65/65、治理 57/57、集成分片发现 1142 项无遗漏/重复；Ai 与 migration-246 合并影响集 58/58、零失败/跳过，21m 10.510s，完整执行规定的 30 分钟门槛及两个 Worker。先前临时目录 EBUSY、夹具精度失败和资源不足的运行不计作通过。
  - `node artifacts/r05f1/verify.mjs` 退出 0，核对 940 项最终源码/矩阵/运行输入未变及正式 Ai 694、双库 58 项的全部 UID/FQN；另核对精度夹具修正前后的 Unit 输入未变。`rag-evaluation-report.json` 是测试生成输出，明确排除在运行输入之外。AI 模块、Unit 与 Integration 的模块 DLL SHA-256 同为 `863B7B9E4928B1B48ACEE69E0B433031C4C218833BD25EC01103C3B07FEDEDF7`。日志、发现清单、TRX 和验收摘要保留在忽略目录 `artifacts/r05f1/`、同前缀日志及正式 TestResults。
  - 本地 Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401，Docker SQL Server 2022 CU14/MySQL 8.0。Windows 提交内存压力期间测试进程单独设置 `DOTNET_GCHeapHardLimit=0x80000000`（2 GiB）；未修改测试选择、最低数、并行数、时限或既有服务。未执行完整 Host/Worker Native 发布或新业务原生运行。下一项接入 Worker 执行：来源/模型派发前复核、稳定预算 OperationId、未知用量保留预留，再实现有代次约束的索引写入；完整 RAG 保持 Planned，容量保持 `Capacity-not-verified`。

- [x] R05f2a：派发前权威快照（仅内部执行前置，不启动 Worker 或外部模型）。
  - [x] 在 `AiKnowledgeIndexTaskSql` 新增 Host/Tenant 精确租约读取：同一语句复核任务代次、调用方来源/文档/目录版本、所有者、未过期 token、最新已确认来源及获批启用模型，返回文件标识/摘要/类型/大小和精确模型配置版本，不返回凭据或正文。
  - [x] `AiKnowledgeIndexTaskStore.ReadDispatchAsync` 复用可信租户及参数校验，读取结束再次检查时钟、取消和精确绑定；返回 null 表示禁止下一步。快照只证明判定点，文件读取/解析/预算等待后的模型调用需重新复核，不能充当预算预留或长期授权。
  - [x] 新增闭合快照行及 AOT 静态物化；Unit RED/GREEN 覆盖范围、取消、读取中到期和快照绑定，双库用既有固定迁移夹具覆盖健康来源、逐项撤权、旧 token、到期接替及取消。无新表或迁移。
  - [x] 按矩阵完成 Ai Unit、Architecture、AOT 分析、命名/SQL/治理、分片发现及选择器双库影响集，安全只读审查后提交 main；稳定批次 OperationId、Embedding 预算/执行、索引写入及 Worker 调度继续后续切片。

  R05f2a 本地证据（2026-10-04，仅内部派发前置读取）：
  - 实施基线 `main`、`43be0f33b3823bfd440ae0f47e48a0a82d87e113`，任务快照 `ai-knowledge-index-dispatch-r05f2a`。新增 Unit RED 17/17 失败（存储/物化桩缺失），双库 RED 2/2 失败（健康有效租约取不到快照）。实现后 Unit 17/17 通过；双库首轮 1 通过/1 失败，MySQL 裸 Dapper 将 datetime 解释成本地 +08:00。定位生产 `UtcDateTimeOffsetTypeHandler` 后，夹具仅通过生产 `AddFullNetDapper` 注册相同 Guid/UTC Handler，不构建/解析 ServiceProvider、不修改预期值；最终新增双库 2/2、零失败/跳过、退出 0，7.298s。
  - 每库分别以 Host/Tenant 健康租约对照验证 11 种权威变更，共 22 个独立数据集；包括禁用/版本/批准模型/模型租户/来源删除与导入失效/新来源。另覆盖 queued、错误任务/来源/目录/所有者/版本/token、跨范围/租户、精确到期、接替后的旧 token、取消，以及明确获批的 Host 模型供租户使用。字段断言验证文件标识/摘要/类型/大小、模型绑定及 UTC 租约一致；独立只读安全审查与 UTC 夹具补审均无确认 P1/P2。
  - `node artifacts/r05f2a/run-unit.mjs` 复用官方 Ai 选择、最低数和时限：711/711、零失败/跳过、退出 0，1m 01.148s，Unit 总发现 4180；不声明全量 Unit 执行。`node scripts/testing/run-dotnet-test-suite.mjs architecture --selection ai-agentic-boundaries --no-build` 7/7、19.234s；同入口 `api-native-aot` 73/73、8.767s，均零失败/跳过、退出 0，Architecture 总发现 233。
  - `node scripts/testing/run-api-aot-analyzers.mjs` 退出 0，AOT 分析构建零警告/错误、1m 15.55s，并恢复 JIT 还原图；最终 Integration/Unit/Architecture Release 构建分别 57.52s/12.77s/8.17s，退出 0、零警告/错误。命名 33/33、SQL 安全 5/5、治理 57/57 均通过。
  - `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-index-dispatch-r05f2a --phase slice` 退出 0：工具链/反馈契约 65/65、治理 57/57、集成分片发现 1144 项无遗漏/重复；Ai 与 migration-246 合并影响集 60/60、零失败/跳过，21m 31.716s，保留规定的 30 分钟时限及两个 Worker。没有手动缩小选择器范围。
  - `node artifacts/r05f2a/verify.mjs` 退出 0：942 项源码/矩阵/运行输入未变，正式 Unit 711 和双库 60 项全部 UID/FQN 与发现清单一致；测试生成的 `rag-evaluation-report.json` 明确排除为输出。源码项目、Unit 和 Integration 的 AI 模块 DLL SHA-256 同为 `9C256BD655378507D7A251571B19E0F79B311199ABC853DFBD1AFD3DE90DBD77`。原始 RED/GREEN、最终日志、发现清单、TRX 与验收摘要保留在忽略目录 `artifacts/r05f2a/`、同前缀日志及正式 TestResults。
  - 本地 Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401，Docker SQL Server 2022 CU14/MySQL 8.0；测试进程沿用单独的 2 GiB GC Heap 限额，不修改选择/最低数/并行数/时限或服务配置。未新增 Worker DI/轮询、实际文件/模型派发、预算预留/结算、索引写入或 ready，也未执行完整 Host/Worker Native 发布/运行。下一项持久化稳定批次 OperationId 并接入 Embedding 账本：重复操作不得自动再次派发，未知用量保留预留，模型调用前再次使用本项边界；完整 RAG 仍为 Planned，容量仍为 `Capacity-not-verified`。

- [x] R05f2b：稳定批次操作标识与预算准入（仅内部账本消费者，不进行模型调用或启用 Worker）。
  - [x] 成对新增 `247_AiKnowledgeIndexBatch.sql`：AI 自有批次以 UUID v7 主键作为 OperationId，唯一绑定 IndexTaskId/BatchOrdinal，保存完整预算请求指纹与输入数量；不保存正文/凭据/向量。SQL Server 表/列注释可独立恢复，MySQL 原子建表，重复未记账执行保留原行。
  - [x] `AiKnowledgeIndexBatchStore` 经既有执行器及 Host/可信租户静态 SQL 建立/读取批次；写入和唯一冲突恢复均复用有效租约及当前来源/批准门禁，相同指纹复用 ID，不同输入/模型绑定拒绝。批次号 0–4095、输入数量 1–16，固定 SHA-256 指纹。
  - [x] `AiKnowledgeIndexBudgetService` 先读取权威快照和当前真实模型，冻结有界输入并构造既有 Embedding 请求，持久化操作 ID 后调用统一预算 Port，再复核派发前权威。重复预留只返回对账信息、不返回可执行来源；新预留后撤权/取消/到期，以明确未调用的零用量取消隔离结算，未知预留/既有 unknown 不自动结算或再次派发。消费者须在无外层长事务的作用域接入，仍负责输入来源、执行者/数据去向授权及真实调用后的独立用量结算。
  - [x] Unit RED/GREEN、AOT 静态物化及真实双库覆盖争抢/恢复/跨范围/旧 token/指纹冲突，生产账本新预留、重复、未知用量与重新领取；按矩阵完成影响集、审查和 main 提交。Worker 调度、真实 Provider 执行、批次结果持久化、索引写入/ready 及人工对账后重新执行策略保持后续范围。
  R05f2b 本地证据（2026-10-04，仅内部批次身份与预算准入）：
  - 基线 main/b494811c6afd4b08132a0bda34c0fd6a70160200，任务快照 ai-knowledge-index-budget-r05f2b。18 项 Unit RED 均因未实现失败；双库恢复 RED 2/2 因缺少 247 失败。实现后新增 Unit 18/18 通过。新增 Integration 首轮 5 通过/1 失败：MySQL 用联合唯一索引支撑外键，夹具直接删索引被拒绝；夹具先保留普通外键支撑索引后，247 恢复/门禁 4/4、零跳过、退出 0，11.586s，不修改生产约束。
  - 每库 Host/Tenant 健康对照覆盖 11 种权威失效、所有者/租户/token/精确到期隔离；生产执行器和账本覆盖同批次并发唯一 ID/一次新预留、批准 Host 模型供租户使用、unknown 计量保留、租约接替复用、旧 token 拒绝、不同指纹拒绝与下一批次独立预留。没有模型调用。只读预算/安全审查无确认 Critical/Important；消费者禁止外层长事务的约束已写入代码，未来 I/O 仍需输入来源、执行者、去向和最终权威复核。
  - node artifacts/r05f2b/run-unit.mjs 复用官方 AI 选择/最低数/时限：729/729、零失败/跳过、退出 0，1m 03.915s；Unit 总发现 4198，并未执行全量。官方 architecture --selection ai-agentic-boundaries --no-build 7/7、11.572s，api-native-aot 73/73、5.222s；总发现 233。node scripts/testing/run-api-aot-analyzers.mjs 退出 0，零警告/错误、1m 12.99s，恢复 JIT 还原图；最终 Release Integration/Unit/Architecture 构建 50.69s/12.82s/7.94s，均退出 0、零警告/错误。
  - node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-index-budget-r05f2b --phase slice 退出 0：工具链/反馈契约 65/65、治理 57/57；1150 项分片发现无遗漏/重复；AI 与 migration-246/247 合并 66/66、零失败/跳过，19m 07s 110ms，保留两个 Worker 和规定 30 分钟时限。另命名 33/33、SQL 安全 5/5 通过，成对迁移注释目录及固定 MySQL 索引恢复债务已同步。
  - node artifacts/r05f2b/verify.mjs 退出 0：953 项源码/矩阵/运行输入未变，729 项 Unit 与 66 项双库结果的 UID/FQN 均与发现清单精确一致；生成输出 rag-evaluation-report.json 单独排除。源码项目/Unit/Integration AI DLL SHA-256 同为 7F5820D72B0249BE7830E41A6D83A3D0DC2100AD069E2B0BF1B30517C6DC0907。原始日志/TRX、发现清单和验收摘要保留于忽略目录 artifacts/r05f2b/、同前缀日志及正式 TestResults。
  - Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401，Docker SQL Server 2022 CU14/MySQL 8.0；每测试进程 2 GiB GC Heap 限额。未注册 Worker、调用 Files/Provider、保存向量/索引或 ready，未执行完整 Host/Worker Native 发布/运行。下一切片接入真实 Provider 前的输入来源/执行者/数据去向与最终复核、已知或未知计量回执；人工对账后的重新执行策略仍待交付。完整 RAG 保持 Planned，容量保持 Capacity-not-verified。


- [x] R05f2c：受会话绑定的文本批次 Embedding 执行（内部执行消费者，未注册 Worker/HTTP，结果不代表索引 ready）。
  - [x] 新增 Knowledge/AiKnowledgeIndexEmbeddingService.cs：ExecuteTextBatchAsync 接受任务/有效租约、批次序号和可信 SessionBindingSnapshot，不接受正文或模型配置；复核所有者、可信范围及 Identity 后台会话的知识库/文档读取、来源读取和上传精确权限；通过 IDataTransactionState 在读取及外部 I/O 前拒绝外层事务。
  - [x] 复用 Files 窄读取契约，Host 同时核对就绪引用摘要/大小，Tenant 固定 ai/版本/文件绑定；打开流前后复核，负责释放流。仅 TXT/Markdown，摘要及字节数校验后用实际获批模型的固定词表、默认有界策略生成完整分块，再按 16 条/32000 字符确定批次；不接业务 PDF 进程，不裁剪正文。
  - [x] 复用 R05f2b 稳定 OperationId/预算 Port；只接受新预留及精确源/完整请求指纹。授权、分类/精确批准和目的地在凭据签发及 GenerateAsync 前复核；等待工厂期间失效不得派发。调用前失败按零结算，进入调用后无回执则保留 unknown，已知用量先保存到局部事实再校验向量/返回权限，结算隔离调用取消并传播失败。
  - [x] 复制并校验返回向量的计数、官方模型维度、有限/非零值；响应后及独立结算等待后再次复核取消、会话/租约/来源/模型，拒绝时不交付临时向量，但保留已提交的模型操作用量事实，不重写或再次结算。整个执行使用有界取消，不重试或切换 Provider。返回只包含来源/模型/分块与操作身份及临时向量，不改变任务或文档 ready。
  - [x] 先建 Unit RED/GREEN，覆盖 Host/Tenant、授权与来源拒绝、批准/工厂期间撤权、重复不派发、取消前后及异常/畸形计量/向量、过期响应拒绝与清理失败。真实双库使用生产 SQL 执行器/账本与受控 Files/会话/模型边界验证 ID 复用及已知/未知/零结算；按矩阵影响集、AOT/架构、只读安全审查及 main 提交。真实云模型计费、持久会话任务绑定、Worker 调度、PDF 接入、结果落库、索引 ready 和人工对账重执行仍属后续。

  R05f2c 本地证据（2026-10-04，仅内部文本批次执行消费者）：
  - 基线 main/d13d3307dcd818b34ae5cbc697eb600d7369ee78，任务快照 ai-knowledge-index-execution-r05f2c。未实现类型桩的 Unit RED 36/36 失败、零通过/跳过、退出 2，2.376s；生产双库入口 RED 2/2 因未实现失败、零跳过、退出 2，2m 13.510s。类型桩未使用参数、Integration 误引未安装的替身包及一次不支持的 CLI 并行参数属于编译/命令错误，不计为行为 RED；分别修正类型桩、使用手写受控 Port、沿用程序集规定的两个 Worker，未新增依赖。最终专项 Unit 41/41、退出 0，3s 474ms；双库专项 2/2、退出 0，2m 05s 193ms，均零失败/跳过。
  - 每库 Host/Tenant 覆盖 known/lost/zero/revoked/unmetered/ambient/settled_revoked/settled_expired 八类场景；真实生产 Dapper 执行器、批次身份和预算账本验证确定零、已知 7 Token、未知用量保守计费、租约接替与重复不派发，外层真实命令事务在查询/Files/模型 I/O 前拒绝。Files、后台会话授权及模型使用受控契约边界，并未访问真实云计费。Unit 另覆盖来源摘要/大小/MIME、四项权限、工厂等待后撤权、取消、畸形向量/计量、向量复制和结算失败传播。只读补审发现 P2：返回候选在 finally 结算等待期间可能丢失取消/权威。异步结算门闩的 5 项 RED 均失败、退出 2、零通过/跳过，1.156s；最终在独立结算后追加取消与权威复核，保留已提交操作计量、不重写或再次结算，5 项纳入最终 GREEN。先前 765 项 Unit 与被停止的首轮双库影响集不作为修改后验收，最终产物重新构建/冻结并完整重跑。补审确认 P2 已修复、无未修复 Critical/Important 或新 findings；审查未运行测试。
  - node scripts/testing/run-api-aot-analyzers.mjs 退出 0，零警告/错误、00:01:49.58，恢复 JIT 还原图。最终 Integration/Unit/Architecture Release 构建分别 00:01:05.37/00:00:24.95/00:00:14.39，均零警告/错误、退出 0。node artifacts/r05f2c/run-unit.mjs 复用官方 ai-module 过滤/最低数/时限：770/770、1m 16s 029ms、零失败/跳过、退出 0；完整 Unit 发现 4239，并未执行全量。官方 architecture --selection ai-agentic-boundaries --no-build 7/7、16s 017ms，api-native-aot 73/73、9s 620ms；完整 Architecture 发现 233，并未执行全量。
  - node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-index-execution-r05f2c --phase slice 退出 0：工具链/反馈契约 65/65、治理 57/57；1152 项分片发现无遗漏/重复。完整 AI 影响集双库合并 58/58、零失败/跳过，21m 21s 829ms，保留两个 Worker 和规定 30 分钟时限；平台仅截断 1 项超限捕获文本字段，完整测试身份及通过结果均已核对。另独立工具链 54/54、命名 33/33、SQL 安全 5/5、治理 57/57 通过，最终文档同步后复核治理。
  - node artifacts/r05f2c/verify.mjs 退出 0：958 项源码/矩阵/运行输入未变；770 项 Unit 与 58 项双库结果的 UID/FQN 与发现清单精确一致，生成输出 rag-evaluation-report.json 单独排除。源码项目/Unit/Integration AI DLL SHA-256 同为 D9EB97AD8EA42D45866E7C72FCD6F72400811D288F44022B8D8B08926D98260E。原始 RED/GREEN、构建/最终日志、TRX、发现/冻结清单及审查/验收记录保留于忽略目录 artifacts/r05f2c/、同前缀日志及正式 TestResults。
  - Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401，Docker SQL Server 2022 CU14/MySQL 8.0；每测试进程 2 GiB GC Heap 限额。执行器只处理完整 SHA-256/字节数校验后的 TXT/Markdown，以实际精确获批模型分块并调用 Embedding 契约；25 秒取消令牌覆盖来源读取/解析/模型及交付门禁，独立结算不受调用取消控制；16 条/32000 字符确定批次，不重试/切换模型。未注册 HTTP/Worker，未持久任务会话绑定或保存向量/索引，未接业务 PDF 或改变 pending_processing/ready。未执行本切片完整 Host/Worker Native 发布/运行，亦未验证真实 Files/Identity 联动或付费云计量。持久会话任务绑定、索引持久化/切换、Worker 调度及人工对账后重执行仍需后续交付；完整 RAG 保持 Planned，容量保持 Capacity-not-verified。


- [x] R05f2d：持久索引任务的不可替换会话绑定（内部恢复路径，不启用 Worker/HTTP 或向量落库）。
  - [x] 成对迁移 248 创建 AI 自有一对一会话表；冻结执行者、可信租户、会话种类/标识、安全戳及 Actor/Effective scope，不保存令牌，不引用 Identity 表。任务与绑定在短 AI 本地事务中原子创建；授权 Port 在事务外调用，部分写入异常回滚。
  - [x] 同一来源/批准的重复创建只复用完全相同绑定；不同登录、旧无绑定任务均拒绝接管。恢复执行从持久绑定读取，不接受调用方 SessionBindingSnapshot；每个异步门禁复核有效租约、当前资源及原会话。
  - [x] 先建立失败验证，覆盖双库恢复/唯一约束、原子回滚、重复/竞争、旧任务拒绝、租户/所有者隔离、撤权/到期及静态物化。执行完整规定影响集、AOT/架构与只读审查后提交 main；向量写入/ready、Worker 和真实云计费留在后续。

  R05f2d 本地证据（2026-10-04 至 2026-10-05，仅持久会话绑定与内部恢复消费者）：
  - 基线 main/9c907ada2a3f4d44277d3e3bc61342020e5dae81，任务快照 ai-knowledge-index-session-r05f2d。未实现绑定桩的 Unit RED 31/31 失败、零通过/跳过，13s 898ms；缺失迁移 248 的双库 RED 2/2 失败，11s 814ms；旧调用方会话恢复路径的 RED 3/3 失败，1s 289ms。最终会话/文本执行/静态物化专项 Unit 82/82、零失败/跳过、退出 0，2s 408ms。编译错误、错误权限字面量与环境失败不计为行为 RED。
  - 双库使用生产 Dapper、真实 AI 命令事务及 Identity 后台授权 Port，验证同一绑定竞争只生成一笔记录、旧无绑定任务拒绝、第二笔写入异常回滚后仍可正常创建、撤销与新登录不能接管，以及安全戳变化和会话到期后拒绝。新登录辅助会清除首次密码状态并旋转安全戳，故原会话回滚/撤销用例在该登录之前执行，并断言授权有效作为前置条件；SQL Server 精确到期夹具显式使用 DbType.DateTime2，同时断言实际存储到期值，避免参数精度引起误判。最终真实会话专项双库 2/2、零失败/跳过、退出 0，2m 17s 401ms；此前夹具失败及被停止的影响集不作为验收。
  - 只读审查发现 P2：末次绑定查询等待后需要再次授权原会话。真实异步门闩 RED 1/1 失败，1s 344ms；修正后纳入 82 项专项与 811 项完整 AI Unit，撤销时拒绝交付并保留既有一次已知用量结算。最终只读补审确认生产修复与夹具前置条件，无未修复 Critical/Important 或新 findings；审查未代替测试。
  - node scripts/testing/run-api-aot-analyzers.mjs 退出 0、零警告/错误，00:03:23.06，恢复 JIT 还原图。Integration/Unit/Architecture Release 构建分别 00:06:22.57/00:00:38.21/00:00:29.83，夹具最终 Integration 重建 00:00:33.95，均零警告/错误、退出 0。node artifacts/r05f2d/run-unit.mjs 复用官方 ai-module 过滤/最低数/时限：811/811、1m 00s 468ms、零失败/跳过、退出 0；完整 Unit 发现 4280，并未执行全量。官方 architecture --selection ai-agentic-boundaries --no-build 7/7、11s 022ms，api-native-aot 73/73、5s 263ms；完整 Architecture 发现 233，并未执行全量。
  - node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-index-session-r05f2d --phase slice 退出 0：工具链/反馈契约 65/65、治理 57/57；1158 项分片发现无遗漏/重复。AI 与 migration-248 规定影响集双库合并 64/64、零失败/跳过，19m 28s 814ms，两个 Worker、官方组合时限 32 分钟；平台截断 1 项超限捕获文本字段，完整测试身份及通过结果均已核对。另独立工具链 54/54、命名 33/33、SQL 安全 5/5、治理 57/57 通过；文档同步后复核治理与 diff。
  - node artifacts/r05f2d/verify.mjs 退出 0：971 项源码/矩阵/运行输入未变；811 项 Unit 与 64 项双库结果 UID/FQN 和发现清单精确一致，单独排除生成输出 rag-evaluation-report.json。源码项目/Unit/Integration AI DLL SHA-256 同为 226650E383E99B7D6D6CB68D447B67A2C272CA3D6517F998C17238F3E2412017。RED/GREEN、构建/最终日志、TRX、发现/冻结/验收清单保留于忽略目录 artifacts/r05f2d/、同前缀日志及正式 TestResults。
  - Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401，Docker SQL Server 2022 CU14/MySQL 8.0，每测试进程 2 GiB GC Heap 限额。旧共享数据库容器恢复曾失败，临时限额已恢复原值；最终使用 FULLNET_TESTCONTAINERS_REUSE=0 的新容器，未删除旧共享数据。绑定服务未注册 DI/HTTP/Worker；真实 Identity 验证限 Host refresh 会话，Tenant/OIDC 形状与隔离使用 Unit/双库 SQL 边界，尚未执行其真实端到端登录链路。Files/Embedding 仍为受控契约，仅 TXT/Markdown；未接业务 PDF、向量写入/索引切换或 pending_processing/ready 转换，未验证付费云计量、完整 Host/Worker Native 发布运行或容量。后续交付向量持久化、后台调度与失败恢复；完整 RAG 保持 Planned，容量保持 Capacity-not-verified。

- [x] R05f2e：持久化已结算的文本向量批次（内部写入与恢复，不切换 ready 或启用 Worker）。
  - [x] 新增 Knowledge/AiKnowledgeIndexVectorCodec.cs 与 Unit：固定版本、有界 UTF-8/IEEE754 小端载荷，保存原始/Embedding 文本、稳定位置/上下文、分块参数和有限非零向量；畸形长度、维度、格式、尾随字节与取消失败关闭。来源及模型配置版本沿既有任务绑定，实际 Provider/ModelId/维度/距离共同冻结。
  - [x] 成对迁移 249 新增 fn_ai_knowledge_index_vector，以既有批次 OperationId 为唯一主键和 AI 本地外键；一个批次一个不可替换载荷，避免部分片段提交。Knowledge/AiKnowledgeIndexVectorStore.cs 仅通过自有执行器/短事务写入；账本须与完整请求指纹、模型和可信 scope 相同且已结算 succeeded（known/unknown），原会话授权在事务外，SQL 同时守卫租约、来源/批准及冻结会话。重复只复用相同载荷，冲突拒绝，不 UPDATE 原向量。
  - [x] Knowledge/AiKnowledgeIndexPersistenceService.cs 消费既有内部文本执行器；先复核持久批次再决定是否派发，恢复已存批次不再次调用模型。Unit 覆盖权限/租约等待、载荷冻结、冲突/取消/异常；双库生产路径覆盖写入/恢复、旧租约/来源/账本/会话拒绝与未记账迁移恢复。更新静态物化/命名/测试矩阵，执行完整 AI+249 影响集、AOT/架构及只读审查后提交 main。只有本批次持久成功，不代表整代次完整，索引切换/ready 与后台调度另行交付。

  R05f2e 本地证据（2026-10-05，仅已结算文本向量批次的内部持久化与恢复）：
  - 基线 main/bb66b9a524fd71d0c62e8352f5c7c6892dd963a4，任务快照 ai-knowledge-index-persistence-r05f2e。固定 knowledge-vector-batch-v1 完整载荷，最大 512 KiB，维度仅 1536/3072；复制前限制集合/向量分配，拒绝无效 UTF-16/UTF-8、畸形长度、非有限或零向量及尾随数据。成对迁移 249 以既有批次 UUID v7 为主键，一批一次原子写入，FK 仅指向 AI 批次；没有覆写或部分片段提交。
  - 编解码桩 RED 28/28 失败、15s 147ms；持久化桩 RED 29/29 失败、1s 813ms；缺失迁移双库 RED 2/2 失败、30s 175ms，均零通过/跳过。额外集合上限/非法 UTF-16 RED 30 项中 2 失败、28 通过，662ms；修正后纳入完整 AI Unit。编译错误、无效过滤和夹具错误不作为行为 RED。
  - 只读审查发现写入等待后租约到期/取消仍可能提交的 P2。Unit RED 2/2 失败于 Commits=1；MySQL 生产 INSERT 返回一行后推进时钟，RED 失败于向量仍存在。修复在事务回调内观察取消并对到期抛出受控异常，完成回滚才返回 false，保留原已知/未知预算。首轮 SQL Server 另因 211 账本默认排序与 247 批次 BIN2 比较报 468；四个静态 SQL Server 语句显式采用 BIN2，MySQL 保持原精确比较，不修改旧迁移。最终只读补审没有剩余 Critical/Important；审查不替代执行。
  - 双库通过生产 Dapper/AI 事务验证同批竞争、不可替换载荷、原会话/来源/模型/账本/租户守卫；完整请求指纹绑定 scope、OperationId、模型配置版本及完整 Embedding 输入。known/unknown 且 succeeded 只表示已结算；unknown 保留保守收费。恢复已存批次不调用模型，缺失向量的既有付费操作也不能免费重发。迁移重复执行保留完整字节、恢复 SQL Server 注释且唯一/FK 约束有效。专项双库 4/4、零失败/跳过，2m 27s 944ms；向量专项 Unit 61/61、零失败/跳过，1s 686ms。
  - node scripts/testing/run-api-aot-analyzers.mjs 退出 0、零警告/错误，00:01:06.60，随后恢复 JIT 还原图。Integration/Unit/Architecture Release 最终构建分别 00:05:51.34/00:00:45.52/00:00:30.45，均退出 0、零警告/错误。node artifacts/r05f2e/run-unit.mjs 使用官方 ai-module 过滤/最低数/时限：873/873、零失败/跳过，59s 965ms；完整 Unit 发现 4342，未执行全量。官方 architecture --selection ai-agentic-boundaries --no-build 7/7、10s 857ms，api-native-aot 73/73、5s 115ms；完整 Architecture 发现 233，未执行全量。
  - node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-index-persistence-r05f2e --phase slice 退出 0：工具链/反馈契约 65/65、治理 57/57，1160 项分片发现无遗漏/重复。AI 与 migration-249 规定影响集双库合并 62/62、零失败/跳过，20m 10s 102ms，两个 Worker、官方组合时限 32 分钟；平台截断 1 项超限捕获文本字段，完整测试身份与通过结果均已核对。命名 33/33、SQL 安全 5/5 通过；文档同步后复核治理与 diff。
  - node artifacts/r05f2e/verify.mjs 核对 982 项源码/矩阵/运行输入未变；Unit 与双库结果 UID/FQN 和发现清单精确一致，单独排除生成输出 rag-evaluation-report.json。源码项目/Unit/Integration AI DLL SHA-256 同为 E8665B485835FC8C6D4FA8D2B72EC6D4E0DFDAC3FAF4EBC40A1946DCA172C5F2。RED/GREEN、构建/最终日志、TRX、发现/冻结/验收清单保留于忽略目录 artifacts/r05f2e/、同前缀日志及正式 TestResults。
  - Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401，Docker SQL Server 2022 CU14/MySQL 8.0，每测试进程 2 GiB GC Heap 限额，FULLNET_TESTCONTAINERS_REUSE=0 创建新容器。仅 TXT/Markdown；新路径的 Files/授权/模型为受控契约，真实 Identity Host refresh 证据沿用 R05f2d，未扩展到 Tenant/OIDC 真实登录。没有注册 DI/HTTP/Worker，没有实现整代次完整性、索引切换/ready、业务 PDF、检索/引用、真实云计量或本切片完整 Host/Worker Native 发布运行。完整 RAG 保持 Planned，容量保持 Capacity-not-verified；后续继续完整代次与后台调度交付。

- [x] R05f2f：整份文本的确定批次计划（整代次校验的输入边界，不推进 ready）。
  - [x] 新增 Knowledge/AiKnowledgeIndexTextPlan.cs 和对应 Unit。Create(AiKnowledgeModelChunks, CancellationToken) 只消费已有获批模型分块结果；先验证最多 2048 个连续零起始片段及全部格式/输入/上下文上限，再冻结集合。按现有 16 条/32000 UTF-16 字符规则覆盖全部片段，拒绝空计划或畸形末批，不返回部分结果。
  - [x] 固定 knowledge-text-plan-v1 摘要格式；绑定来源摘要/格式、知识库与模型配置/版本/可信租户、解析器/词表/分块参数，以及全部批次顺序、原始/Embedding 文本、位置/结构/上下文和 Token 数。摘要不是授权、外发或费用证明，不作为新的持久协议。
  - [x] 替换 AiKnowledgeIndexEmbeddingService 私有 SelectBatch，真实执行器按完整计划选批，返回不含正文的完整计划摘要及本批次摘要。已有完整请求指纹和预算账本保持原义，未引入第二次模型调用；VectorCodec v1/249 表不静默演进，摘要当前仅随内部临时结果交付，持久 Manifest、整代次校验/激活和 Worker 后续实现。
  - [x] 先建立失败验证，覆盖数量/字符双重边界、全部片段一次覆盖、冻结/确定性、摘要漂移、取消和真实消费者。运行完整 AI Unit、相关架构/AOT、选择器规定双库影响集，核对身份/输入与文档后提交 main。

  R05f2f 本地证据（2026-10-05，完整文本计划的内部输入边界）：
  - 基线 main/a46e074a0524ba89744e83a059cd8f1a7d4e7773，任务快照 ai-knowledge-text-plan-r05f2f。knowledge-text-plan-v1 覆盖全部连续零起始片段；先限制最多 2048 个片段、每个上下文最多 8 条，再复制成只读集合。按完整 EmbeddingText 的 16 条/32000 UTF-16 字符上限分批，严格 UTF-8 拒绝孤立代理项，任何末批无效时整份计划失败。
  - 固定摘要绑定 KB/版本、可信 tenant、精确模型配置/版本/模型名、来源格式与摘要、解析器/词表/分块版本及参数，和有序批次的全部原始/Embedding 文本、位置、结构、上下文与 Token 数。整数采用小端、字符串采用 int32 字节长度与严格 UTF-8，流式 SHA-256 不累积整份文档编码。独立 Node 参考给出固定 batch SHA-256 9C1B429C96ED088AF2481975BBB41887164862CEB533409FEE695CD2C174E9B8、plan SHA-256 9D183C962553ED990847F06BC9F9843BC01E8A4672F7941B68D17A6421C2E964，Unit 比对实际编码。
  - 计划桩 RED 45/45 失败、18s 031ms；真实消费者 RED 3/3 失败、4s 368ms；固定摘要参考 RED 1/1 失败、929ms，均零通过/跳过。最终计划与消费者专项 95/95、零失败/跳过、1s 788ms。新增 49 个计划用例和 1 个真实消费者用例，共 50 个；编译、脚本转义与补丁上下文错误不计行为 RED。
  - 真实 Embedding 执行器按完整计划选批；最后一批仍返回整份文档的总批次/片段/Token 摘要，保持一次模型调用及既有预算预留/已知/未知结算。生产双库账本断言核对返回摘要，Files/授权/外部模型保持受控契约。既有完整请求指纹、VectorCodec v1、249 表与原子写入恢复不变。独立只读审查未发现 Critical/Important 或确定 Minor，审查不代替执行。
  - node scripts/testing/run-api-aot-analyzers.mjs 退出 0、零警告/错误，00:01:31.14，随后恢复 JIT 还原图。Integration/Unit/Architecture Release 最终构建分别 00:07:26.54/00:01:00.62/00:00:24.53，均退出 0、零警告/错误。node artifacts/r05f2f/run-unit.mjs 使用官方 ai-module 过滤/最低数/时限，923/923、零失败/跳过，59s 559ms；完整 Unit 发现 4392，未执行全量。官方 architecture --selection ai-agentic-boundaries --no-build 7/7、15s 788ms，api-native-aot 73/73、5s 458ms；完整 Architecture 发现 233，未执行全量。
  - node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-text-plan-r05f2f --phase slice 退出 0：工具链/反馈契约 65/65、治理 57/57，1160 项分片发现无遗漏/重复。规定完整 Ai 影响集双库合并 60/60、零失败/跳过，19m 40s 620ms，两个 Worker、官方组合时限 30 分钟。单一 Ai 目标的正式结果为 Full.NET.IntegrationTests-affected-ai.trx，全部 UID/FQN 与冻结发现集合一致；平台截断 1 项超长捕获文本字段，不影响完整身份与通过结果，截断文本不作为完整运行日志。
  - node artifacts/r05f2f/verify.mjs 核对 984 项源码/矩阵/运行输入未变；Unit 与双库结果全部身份精确一致，单独排除生成输出 rag-evaluation-report.json。源码项目/Unit/Integration AI DLL SHA-256 同为 0B98EFB867F892862D93AAEBC63B81276041F1EAC897633969E88D9E6DD9407D。命名 node --test tests/naming/*.test.mjs tests/database/uuid-storage-contract.test.mjs 33/33、SQL 安全 node --test tests/sql/*.test.mjs 5/5，零失败/跳过。RED/GREEN、构建/最终日志、TRX、发现/冻结/验收清单保留于忽略目录 artifacts/r05f2f/、同前缀日志及正式 TestResults；文档同步后复核治理与 diff。
  - Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401，Docker SQL Server 2022 CU14/MySQL 8.0，每测试进程 2 GiB GC Heap 限额，FULLNET_TESTCONTAINERS_REUSE=0 创建新容器。摘要当前只随内部临时结果交付，不是授权、外发、费用或向量已存证明；未建立持久 Manifest、整代次数据库完整性证明、激活/ready 或 Worker。仅 TXT/Markdown，未接业务 PDF、真实云计量、检索/引用或本轮完整 Host/Worker Native 发布运行；Tenant/OIDC 真实登录范围未扩展。完整 RAG 保持 Planned，容量保持 Capacity-not-verified，后续继续持久完整计划与整代次校验/切换。


- [x] R05f2g：持久化完整文本计划及批次归属（内部边界，不推进 ready）。
  - [x] 固定不含正文/凭据的有界 knowledge-text-manifest-v1 清单，保存来源身份、完整计划摘要及每批顺序/首片段/片段数/Token 数/内容摘要；解码校验完整摘要、连续覆盖和总量，不演进 VectorCodec v1。
  - [x] 成对迁移 250 建立任务主键关联的不可替换清单；通过 AI Dapper/短事务写入与恢复，绑定可信作用域、来源、精确获批模型和原会话，等待后到期/取消在事务内回滚。并发仅复用完全相同载荷，旧任务不自动回填。
  - [x] 持久消费者在预算预留前冻结清单；恢复必须核对向量原文/Embedding 输入/位置/结构/上下文与清单批次摘要，同一付费操作不再次调用模型，清单存在不代表向量齐全。暂不注册 DI/HTTP/Worker、不切换代次/ready。
  - [x] 先运行编解码/Store 失败验证和缺失迁移双库恢复失败验证，再验证真实消费者、静态行读取、完整 AI Unit、双库影响集/250 恢复、架构/AOT、命名/SQL 安全及治理；更新证据后提交 main。

  R05f2g 本地证据（2026-10-05，完整文本清单与批次归属的内部边界）：
  - 基线 main/7d9c9abc31ce952de5701186ea3725ef4cf3dc1c，任务快照 ai-knowledge-text-manifest-r05f2g。knowledge-text-manifest-v1 固定严格 UTF-8/小端编码，最多 128 KiB；保存来源身份、完整计划摘要及有序批次的首片段/数量/Token/内容摘要，不保存正文、向量或凭据。解码先限制字节与批次数，再校验连续覆盖、总量与整份摘要，返回只读集合。旧 knowledge-text-plan-v1 的独立固定 batch/plan 摘要仍由 Unit 比对，VectorCodec v1 不演进。
  - 双库迁移 250 建立 fn_ai_knowledge_text_manifest，一任务一清单，以任务 UUID 为主键和同模块外键；SQL Server 元数据说明独立可重放，MySQL InnoDB 原子创建。没有自动旧任务回填。生产 Dapper Store 通过无外层事务的短事务写入，SQL 同时绑定当前 KB/文档版本、获批模型、来源文件/摘要/类型/大小、可信 Host/Tenant、有效租约和不可替换原会话；等待后取消或到期由事务回滚，唯一竞争只复用完全相同载荷，其他故障传播。
  - 持久消费者在预算预留前保存完整清单；已存向量必须精确匹配批次原文/Embedding 输入、位置、结构、上下文与 Token 内容摘要。清单缺失时只能可信 Files 重读并复核已有向量后恢复；文件不可读、批次来源偏移不符、已付费但向量缺失均不得再次调用模型或改变收费。清单存在不证明向量齐全，单批次成功不证明整个代次完成。
  - 编解码/Store 桩 RED 31 项中 12 失败/19 通过、22s 793ms；缺失迁移双库 RED 2/2 失败、36s 764ms；预算前回调与静态读取桩 RED 6/6 失败、3s 411ms；仅使用旧 IsStored 的批次归属 RED 7 项中 6 失败/1 通过、5s 967ms，均零跳过。最终内部专项 184/184、零失败/跳过、27s 483ms；真实双库执行/250 重放专项补齐后 4/4、零失败/跳过、2m 53s 947ms。新增 Unit 49、Integration 2，编译/命令失败不计行为 RED。
  - 双库专项从不存在行竞争首次同载荷/不同载荷清单，证明同载荷复用与不同载荷一胜一拒绝、胜者不可替换；直接 SQL 负例检查原会话/租户/来源/版本/租约，真实 INSERT 后推进时钟证明回滚。已知和未知计量、Host 和 Tenant 都覆盖缺失清单恢复，核对 Files 重读、无第二次模型调用及 ChargedTokens 不变。独立只读审查提出的两处验证缺口已补齐；最终复核未发现 Critical/Important 或确定 Minor，审查不代替实际测试。
  - node scripts/testing/run-api-aot-analyzers.mjs 退出 0、零警告/错误，00:01:45.43，随后恢复 JIT 图。Integration/Unit/Architecture Release 最终构建分别 00:06:08.84/00:00:54.56/00:00:36.82，均退出 0、零警告/错误。node artifacts/r05f2g/run-unit.mjs 使用官方 ai-module 过滤/最低数/时限，972/972、零失败/跳过、1m 01s 503ms；完整 Unit 发现 4441，未执行全量。官方 architecture --selection ai-agentic-boundaries --no-build 7/7、18s 479ms，api-native-aot 73/73、5s 374ms；完整 Architecture 发现 233，未执行全量。
  - node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-text-manifest-r05f2g --phase slice 退出 0：工具链/反馈契约 65/65、治理 57/57，1162 项分片发现无遗漏/重复。规定完整 Ai 影响集与迁移 250 合并去重后双库 62/62、零失败/跳过、21m 00s 665ms；两个 Worker、官方组合时限 31 分钟。正式结果 Full.NET.IntegrationTests-affected-combined.trx 的全部 UID/FQN 与冻结发现集合一致；平台截断 1 项超长捕获文本字段，不影响完整身份与通过结果，截断文本不作为完整运行日志。
  - node artifacts/r05f2g/verify.mjs 核对 994 项源码/矩阵/运行输入未变；Unit 与双库结果身份精确一致，单独排除生成输出 rag-evaluation-report.json。源码项目/Unit/Integration AI DLL SHA-256 同为 CF4FADBE591E420633F7F29C802DF9C611FD5C20882A56FEAF63F3357A95D111。命名 node --test tests/naming/*.test.mjs tests/database/uuid-storage-contract.test.mjs 33/33、SQL 安全 node --test tests/sql/*.test.mjs 5/5，零失败/跳过；SQL 注释校验退出 0。RED/GREEN、最终构建与运行日志、TRX、发现/冻结/验收清单保留于忽略目录 artifacts/r05f2g/、同前缀日志和正式 TestResults；文档同步后复核治理与 diff。
  - Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401，Docker SQL Server 2022 CU14/MySQL 8.0，每测试进程 2 GiB GC Heap 限额，FULLNET_TESTCONTAINERS_REUSE=0 创建新容器。仅 TXT/Markdown 内部边界，未注册 DI/HTTP/Worker，未建立整代次数据库完整性证明、激活/ready、业务 PDF、云计量或检索/引用，本轮不宣称完整 Host/Worker Native 发布运行；Tenant/OIDC 真实登录范围未扩展。完整 RAG 保持 Planned，容量保持 Capacity-not-verified，后续继续整代次完整性校验与激活。


- [x] R05f3a：整份文本代次的当前完整性校验（激活前边界）。
  - [x] 持久消费者读取可信完整清单，逐批验证已结算向量的完整输入、模型空间与来源；缺批、额外批次或损坏载荷均拒绝，不派发模型、不改变费用。
  - [x] 以双库有界轻量回执在校验前后复核完整批次集合；最终 SQL 读取绑定原会话、可信作用域、来源、批准和有效租约，拒绝校验期间的载荷/账本/集合漂移。逐批读取完整载荷，不累积整代次向量。
  - [x] 新增静态行读取和可失败 Unit，双库实际多批次执行覆盖部分写入、全部写入、额外批次、损坏及等待后失效；完整 AI、相关架构/AOT和规定影响集通过后提交 main。
  - [x] 返回值仅证明最终读取判定点，不是可缓存的激活授权；任务完成、活动指针、ready、DI/HTTP/Worker 后续由原子切换切片实现。

  - RED：整代次边界有效 Unit 19 项中 17 失败、2 通过；静态回执的行读取 20 项中仅新映射 1 项失败。消费者临时仅检查首批时，SQL Server/MySQL 两项均在“首批齐全不能代表整代次齐全”断言失败（2m 17s 770ms）。审查新增的五类首批后模型空间漂移 Unit 5/5 失败。早期动态代理夹具失败、一次专项最低数误填，以及审查双库 RED/GREEN 两次容器 OOM、历史库恢复导致的 Windows 提交内存不足/Unit 构建中断均排除出行为验收证据。
  - 修复与审查：逐批核对完整来源/上下文请求摘要、模型空间和已结算载荷；两次回执精确检查完整批次集合，原始总批次数守卫拒绝未付费/未保存的额外批次。回执比较包含 ProviderKey、ModelId、Dimensions、FormatKey、DistanceKey；SQL Server HASHBYTES 与 MySQL SHA2 在最终读取点复算实际载荷，拒绝只改字节而保留旧摘要。静态行读取与真实双库首批后篡改维度/字节回归已补齐；独立只读复审确认原 P2 已关闭，无剩余确定问题。总校验限时 25 秒、有界 2048 批次、逐批加载，不积累整代次向量，无外层事务或模型调用/收费副作用。
  - GREEN：新鲜专项 Unit 158/158；全新容器双库多批次方法 2/2（2m 21s 115ms），覆盖 Host/租户与已知/未知计量、缺批/额外未保存批次、完整输入偏移、损坏载荷/清单、等待后向量缺失/到期及首批读取后的维度/字节篡改。正式完整 Ai Unit 996/996、AI 边界 Architecture 7/7、Native AOT Architecture 73/73；Release Integration/Unit/Architecture 与 API AOT 分析均退出 0、零警告/错误。全量 Unit/Integration 仅发现数核对为 4465/1164，未宣称全量执行。
  - 规定影响集：node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-generation-check-r05f3a --phase slice 退出 0，完整 Ai 双库 62/62、零失败/跳过、19m 49s 123ms；工具链 65/65、治理 57/57、1164 项分片发现无遗漏/重复。正式 Full.NET.IntegrationTests-affected-ai.trx 与冻结发现的全部 UID/FQN 一致；996 个代码/矩阵/运行输入摘要复核未变。平台截断 1 项超长捕获文本字段，测试身份与结果保持完整，截断区域不作为完整运行日志。pnpm test:naming 33/33、pnpm test:sql-safety 5/5 与最终 git diff --check 通过。实际日志、发现清单和验收摘要位于忽略目录 artifacts/r05f3a/ 及同名前缀日志。
  - 环境与边界：Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401，Docker VM 31.22 GiB，SQL Server 2022 CU14/MySQL 8.0，两个测试 Worker、每进程 2 GiB GC Heap 限额，正式双库 FULLNET_TESTCONTAINERS_REUSE=0。历史复用数据库启动恢复反复 OOM；临时提高上限后触发 Windows 提交内存不足，已停止两库并恢复 RAM 4 GiB/1 GiB、交换上限设为 Docker 默认两倍，数据保留，原运行状态未恢复。原三个 Worker/控制平面容器继续运行，正式双库验收使用全新容器，未引用复用库中断结果。仅 TXT/Markdown 内部当前读取证明；任务仍 running，未注册 DI/HTTP/Worker，未建立完成态、活动指针或 ready；本轮未执行完整 Host/Worker Native 发布运行或容量测试。完整 RAG 保持 Planned，Capacity-not-verified 保持，下一项继续原子激活边界。

- [x] R05f3b：内部文本代次原子完成与活动指针，重试须复核当前状态。
  - [x] 在 `Knowledge/AiKnowledgeIndexVectorStore.cs` 将整代次检查保留为布尔兼容入口，并增加 `ReadWholePlanProofAsync`，返回已验证、无正文的冻结回执及原来源/会话；`AiKnowledgeIndexTextManifestStore.ReadSnapshotAsync` 同时返回已验证原清单摘要。先在 `AiKnowledgeIndexVectorStoreTests` 建立成功与拒绝的 RED，保留 R05f3a 全部拒绝语义。
  - [x] 双库 `251_AiKnowledgeActivation.sql` 先增加允许 completed 的 Lifecycle CHECK，再移除旧 State CHECK；追加 `fn_ai_knowledge_active_generation`，保存 UUID v7 Id、唯一 DocumentId、唯一 IndexTaskId、VersionId、PlanHash、ManifestHash、ReceiptHash、无正文 ReceiptJson、原 LeaseToken、ActivatedAtUtc。仅 AI 本地外键，不回填旧任务或默认 ready；恢复测试冻结至 251，覆盖旧约束、双约束、缺失旧约束、半完成表/注释与重复未记账重放。
  - [x] `Knowledge/AiKnowledgeIndexActivationStore.cs` 消费新鲜完整证明与清单快照，事务外复核原会话权限；固定双库 `Persistence/AiKnowledgeIndexActivationSql.cs` 在短事务中锁住任务、文档、知识库、模型、来源、导入、原会话与清单，再读取有界并持锁的完整回执集合，比较冻结回执和实际 SHA256，精确核对原始批次数及来源/批准/租约。先完成任务并清除运行租约，再更新/插入唯一活动指针；后续写失败、取消或等待后到期必须抛异常使整笔回滚，事务内无 Identity/Files/模型调用。`AiKnowledgeIndexPersistenceService.ActivateTextGenerationAsync` 为真实内部消费者，不接受缓存布尔结果。
  - [x] SQL Server 的批次、清单、向量写入增加锁定的当前任务读取，避免 RCSI 下旧运行快照在完成后落入新批次；MySQL 在 ReadCommitted 下为三类 INSERT SELECT 显式添加 FOR SHARE，阻止旧运行快照继续写入。活动重复读取核对原绑定、原完成 token、完整清单/回执摘要、当前模型及实际批次载荷，不再派发模型或收费。JSON 使用单独源生成上下文、最大 1 MiB；新增行类型覆盖静态 AOT 物化。
  - [x] Unit 覆盖不可变证明、原会话/权限、外层事务、取消/到期及原子回滚；双库真实多批次覆盖部分拒绝、原子完成/指针、幂等重试、旧 token/新来源/禁用模型拒绝、完整性校验后漂移、两笔写入失败与竞争。沿用现有 AI 夹具，Unit 无法证明实际锁、DDL 恢复或事务回滚，新增 Integration 不进入 messaging-heavy。按矩阵登记 251 与新增身份，完整 AI、规定双库影响集、相关 Architecture/AOT 分析、命名/SQL/治理通过，独立只读审查后在 main 提交。
  - [x] 本项不注册 DI/HTTP/Worker，不改变公开文档 pending_processing 展示或新增检索入口；完成只代表内部活动代次原子持久化，完整 RAG、后台调度与公开 ready 接入仍保持未完成。旧复用数据库维持既有停止状态，使用 FULLNET_TESTCONTAINERS_REUSE=0，全新测试容器验收。

  - RED：冻结完整证明的 Unit 2/2 因能力缺失失败；迁移/激活初始双库 4/4 因 251 或指针表缺失失败。SQL Server RCSI/MySQL ReadCommitted 迟到写入回归 2/2 均在完成后写入返回 1（预期 0）处失败；修正三类写入的显式任务锁。完成后来源大小漂移回归双库 2/2 在应拒绝但返回成功处失败，补上持锁的版本/原导入大小、摘要和类型等值守卫。畸形回执 JSON 中空摘要的有效 Unit RED 为 23 通过、1 失败，改为拒绝；动态代理夹具错误不作为行为 RED。
  - 实现与复审：使用新鲜完整证明和原始清单摘要，短事务锁住 AI 自有来源与完整批次/账本/载荷，复算实际 SHA256；先完成任务，再根据已持锁旧指针选择唯一 INSERT 或 UPDATE，不以更新零行回退插入。第二笔零行、异常、取消或等待后到期回滚，旧指针 Id 保留。原完成 token 仅用于内部幂等重试，仍须事务外原会话授权及事务内当前来源/批准/清单/回执复核，模型调用和收费不变。独立只读复审确认前次来源漂移问题关闭，无剩余确定缺陷；审查不替代执行证据。
  - GREEN：专项 Unit 24/24；第二轮双库专项 6/6、零跳过（3m 35s 702ms），覆盖两代指针替换及 INSERT/UPDATE 故障回滚、来源/模型/会话拒绝、真实载荷损坏、RCSI/ReadCommitted 迟到批次/清单/向量拒绝，以及 251 缺失唯一索引/注释恢复、约束中断与未记账重放。正式完整 AI Unit 1020/1020（55s 200ms）、AI Architecture 7/7、AOT Architecture 73/73，零失败/跳过；Unit/Integration/Architecture Release 构建与 API AOT 单节点分析均退出 0、零警告/错误。完整 Unit/Integration 只核对发现数 4489/1170，未宣称全量执行。
  - 规定影响集：node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-activation-r05f3b --phase slice 退出 0，Ai 64 项与迁移 247/251 各 4 项合并去重共 72/72，零失败/跳过（23m 20s 187ms）。正式 combined TRX 与冻结发现的全部 UID/FQN 一致；961 个代码/矩阵/运行输入摘要未变，三处 AI 程序集 SHA256 一致。pnpm test:integration:tooling 54/54、正式入口工具集 65/65、test:governance 57/57、test:naming 33/33、test:sql-safety 5/5，1170 项分片无遗漏/重复；最终 git diff --check 通过。平台截断 1 项超长捕获文本字段，测试身份和结果完整，截断区域不作为完整运行日志。证据位于忽略目录 artifacts/r05f3b/ 及同名前缀日志。
  - 环境与未验证项：Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401、Docker SQL Server 2022 CU14/MySQL 8.0；双库两个测试 Worker、每进程 2 GiB GC 限额，FULLNET_TESTCONTAINERS_REUSE=0。首轮 API AOT 脚本并行构建及同时运行的分片发现因 Windows 提交内存不足中断，排除出验收，单节点 AOT 与分片重跑通过；历史复用数据库仍停止，数据保留。仅完成 TXT/Markdown 内部原子激活，未注册 DI/HTTP/Worker，公开 pending_processing 不变，未执行完整 Host/Worker Native 发布运行、真实后台/云计量联动或容量测试。完整 RAG 保持 Planned，Capacity-not-verified 保持，下一项继续后台执行与公开状态接入。

- [x] R05f4a：持租约的整份文本执行消费者（后台执行前置）。
  - [x] 扩展 `Knowledge/AiKnowledgeIndexSessionService.cs` 的 `RenewHeldAsync`，只使用任务原会话，续租前后复核全部精确权限、可信作用域、原来源及当前有效租约；剩余时间超过单阶段 25 秒时复用当前租约，续租拒绝或等待后失效不进入下一阶段。Unit 在 `AiKnowledgeIndexSessionTests` 覆盖足够租约、实际续租、原身份/来源漂移、撤权、取消与外层事务。
  - [x] 在 `Knowledge/AiKnowledgeIndexPersistenceService.cs` 添加 `ExecuteTextGenerationAsync`，总派发限时 15 分钟，已进入计费的操作继续独立结算；先识别原 token 的已完成重试，否则捕获原会话，冻结首批产生的完整清单，按既定 ordinal 顺序调用实际持久批次消费者。每阶段续租并精确重验原清单摘要，部分已存批次恢复不重收费；任何拒绝停止，最后调用新鲜原子激活边界，不缓存完整性布尔结果。
  - [x] 扩展现有 `AiKnowledgeIndexEmbeddingAssertions` 与双库包装，每次模型阶段推进测试时钟 20 秒，令多批次总执行超过初始 30 秒租约；覆盖 Host/Tenant、已知/未知计量、从零执行、首批恢复、续租零行、错误 token、撤权、取消及完成重试。Unit 不证明真实续租 SQL 和完整账本/指针，双库复用现有独立数据库夹具，不进入 messaging-heavy。
  - [x] 新行为先取得有效 RED，再实现；完整 AI Unit、规定 Ai 双库影响集、相关 Architecture/AOT 分析、分片/命名/SQL/治理验收后提交 main。只增加内部实际消费者，不新增 SQL/迁移或公共 DTO；Hosted 轮询领取、生产 DI、公开 ready 与检索仍随后接入。

  R05f4a 本地证据（2026-10-05，持租约完整文本消费者的内部边界）：
  - RED：续租门禁专项 Unit 16 项中 12 失败、4 通过，失败对应缺失续租、等待后复核及异常传播；完整执行入口双库 2/2 在已取消 token 应抛异常但未抛处失败。构建均零警告/错误。初次 GREEN 的 SQL Server 断言把 DateTimeOffset 读成 DateTime，引发夹具类型转换异常，改为数据库内比较租期后重跑；此异常不作为业务 RED。
  - 实现与审查：原会话与全部精确权限在阶段门禁前后复核，续租确认失败、原身份/来源漂移、等待后过期或取消均停止。保留足够 25 秒的原租约，临界租约使用既有双库 SQL 延长；不并发共享 DbSession 心跳，不把 Identity/Files/模型调用放入本地事务。完整消费者固定清单 PayloadHash/身份/摘要/批次，依 ordinal 使用实际持久消费者，已存付费批次不再收费，最后重新进入原子激活。15 分钟限制新派发，已计费操作继续独立结算；已完成原 token 恢复仍重验最新授权且不再续租、读取正文或调用模型。独立只读审查未发现可确认缺陷，审查不替代运行验收。
  - GREEN：续租门禁 16/16、双库完整执行专项 2/2（2m 28s 431ms），零失败/跳过。每次模型阶段推进测试时钟 20 秒，真实多批执行超过初始 30 秒租约；覆盖 Host/Tenant、已知/未知用量、从零执行、首批恢复、实际续租写入确认零行、错误 token、撤权、取消以及完成重试，核对完整账本、任务完成和租约清理。最终完整 AI Unit 1036/1036、AI Architecture 7/7、AOT Architecture 73/73，零失败/跳过；最终三个 Release 构建与 API AOT 单节点分析零警告/错误。全 Unit/Integration 仅核对发现数 4505/1172，不宣称全项目测试全部执行。
  - 规定影响集：node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-generation-execution-r05f4a --phase slice 退出 0，Ai 双 Provider 66/66，零失败/跳过（22m 26s 140ms），无新增 SQL/迁移。正式 TRX 与冻结发现的全部 UID/FQN 一致；939 个代码/矩阵/运行输入摘要未变，生产/Unit/Integration 三处 AI 程序集 SHA256 一致。pnpm test:integration:tooling 54/54、正式入口工具集 65/65、test:governance 57/57、test:naming 33/33、test:sql-safety 5/5，1172 项分片无遗漏/重复；git diff --check 通过。平台截断 1 项超长捕获文本字段，测试身份和结果完整，截断区域不作为完整运行日志。证据位于忽略目录 artifacts/r05f4a/ 及同名前缀日志。
  - 环境与未验证项：Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401、Docker SQL Server 2022 CU14/MySQL 8.0，FULLNET_TESTCONTAINERS_REUSE=0。首次构建因 Windows 提交内存接近上限失败，降低本任务并发/内存后专项构建通过；512 MiB 不足以编译 Integration，768 MiB 单节点重跑通过。AOT 后 JIT 还原首次因 NuGet 漏洞服务索引不可达失败，HTTP 200 后保留审计重试成功；失败命令不计入验收。最终环境内存余量恢复，构建单节点、2 GiB GC 限额、DOTNET_PROCESSOR_COUNT=2；历史复用测试数据库保持停止且数据保留。只完成内部持租约消费者，尚未接入 Hosted 轮询领取/生产 DI、公开 ready、真实后台/云计量链路、检索、完整 Host/Worker Native 发布运行或容量实测。完整 RAG 保持 Planned，Capacity-not-verified 保持，下一项继续 Worker 领取与宿主接入。

- [x] R05f4h：文档 DELETE 的同版本重放（main，基线 0031b976；快照 ai-knowledge-delete-replay-r05f4h）。
  - 契约：当前可信范围内的当前所有者重放原删除版本返回 204；墓碑版本必须等于请求版本加一，其他正版本返回既有 409，非法版本 400，不可见资源 404，独立删除权限继续由 Endpoint 验证。目录禁用不阻止确认既有墓碑；存活文档仍使用原启用目录和 CAS 条件。
  - [x] RED：修改 AiKnowledgeDocumentsApiAssertions 的重复删除预期，由现有双库包装运行；真实事务竞争、租户隔离和 SQL 无法仅靠 Unit 证明，不新增 fixture 或 messaging-heavy 用例。
  - [x] GREEN：AiKnowledgeDocumentSql 增加仅 AI 表的最小墓碑版本读取，AiKnowledgeDocumentService 在资源不可见或 CAS 未命中后确认删除收据；不重复版本递增或补偿意图写入。Endpoint 复用提交后的有界清理，文件副作用保持事务外、最终一致。
  - [x] 回归：双库 Host/Tenant 的顺序与并发重放、错误版本、权限/所有权/范围拒绝、禁用目录、更新竞争及残留清理恢复。只增加断言，不改变既有测试发现数量。
  - [x] 验证与提交：完整 AI Unit、正式 affected slice 双库集、相关 Architecture 与 API AOT 分析；审查 SQL/授权及最终证据，更新运维与能力状态，提交 main。完整 Native、云模型与容量不由此推导。

  R05f4h 本地证据（2026-10-06，同版本 DELETE 重放）：
  - 基线 main / 0031b976a7c62b97843b7a0359b735a47d84ebe4，快照 ai-knowledge-delete-replay-r05f4h。RED 使用旧生产实现与新增重放断言，真实 MySQL 1/1 因 expected NoContent / actual NotFound 失败；修正后的首轮文档/清理双库聚焦集 4/4，通过后补齐审查要求的更新竞争及版本上界测试，最终影响集重新覆盖全部最新源码。
  - 仅新增 Host/Tenant 墓碑版本标量 SQL 与删除失败分支的权威确认；ReadCommitted 事务内核对当前所有者/范围，原请求版本加一必须精确匹配，使用 long 运算避免上界溢出。没有新表、迁移、端点、权限、DTO 或测试发现数量变化；存活文档维持既有启用目录与 CAS。
  - 双库 Host/Tenant 覆盖顺序重放、确定性 DELETE/DELETE 同读后竞争、PUT 先提交后 DELETE 返回 409 且保留更新、MaxValue-1 删除及重放、错误/非法版本、错误父目录、不存在/非所有者/跨范围和权限拒绝。重放前后墓碑版本/时间不变，禁用目录仍可确认删除。生产索引清理链路覆盖 SQL 失败、Files 失败、有效租约不抢占与到期恢复；原会话、批次、费用、模型调用均保持，Files 故障为受控边界。
  - 独立只读审查提出的竞争测试缺口与测试失败清理建议均已修正，追加复核无 Critical/Important。最终 AI Unit 1215/1215（35s408ms）、规定 AI 双库影响集 78/78（26m30s068ms），零失败/跳过；AI 架构 7/7、AOT 架构 73/73，API AOT 分析与 Release 构建零警告/错误。
  - 命令：pwsh -NoLogo -NoProfile -File artifacts/r05f4h/final.ps1（含 preflight.ps1、完整 AI Unit、ai-agentic-boundaries / api-native-aot 架构和 node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-delete-replay-r05f4h --phase slice）；另执行 pnpm test:naming、pnpm test:sql-safety、pnpm test:governance、pnpm test:integration:partitions、git diff --check。全 Unit 4684 与 Integration 裸发现 1239 仅作发现核对；规定主分片 1184 项无遗漏或重复，矩阵数量未变，未宣称执行全项目全量集。
  - 1117 个源码/矩阵/运行输入摘要未变，生产与测试 AI 程序集一致，Worker JIT 依赖闭包一致；TRX 与发现 UID/FQN 精确匹配。集成 TRX 有 3 项捕获文本因单字段上限截断，结果状态和标识完整，截断文本不作为完整诊断日志。原始结果在忽略目录 artifacts/r05f4h/ 与同名前缀日志。
  - 临时验收脚本首次在数据库测试通过后读取了上一切片的 affected-combined.trx（124 项），身份核对失败并退出 1。核对正式工具的按目标命名规则及文件时间后，修正为本次 affected-ai.trx（78 项），单独运行 node artifacts/r05f4h/verify.mjs 退出 0，确认全部标识和输入摘要一致；测试源码/产物未变，无需重复执行已通过的数据库测试。该脚本故障不计为测试通过，也不改写正式工具或门槛。
  - 环境 Windows i7-12700H、64 GB、SDK 10.0.401，Docker SQL Server 2022 CU14/MySQL 8.0；Testcontainers 复用关闭、2 GiB GC 限额、2 个处理器、单节点构建。完整 Host/Worker Native 发布运行、真实外部模型/云计量、容量、PDF 业务导入及检索问答仍未完成；完整 RAG 保持 Planned、Capacity-not-verified，R05 未整体完成。

- [x] R05f4g：Worker 自动恢复已删除文档的索引与文件清理（main，基线 91cdbd9d；快照 ai-knowledge-cleanup-worker-r05f4g）。
  - 范围：新增独立、默认关闭的 `FullNet:Ai:KnowledgeCleanupWorker`；不依赖索引生成开关、模型调用、原会话或目录启用状态。重复 DELETE 的 HTTP/CAS 语义另项处理。
  - 扫描：`Persistence/AiKnowledgeCleanupWorkerSql.cs` 成对静态 Global SQL 仅读取 AI 自有墓碑下的任务和待补偿导入标识；按 CreatedAtUtc/Kind/Id 有界 keyset 扫描。进程内 Singleton 游标跨 Scoped 保留并互斥，失败也推进，末尾回绕，重启从头可安全重放；每轮固定等待，避免租约竞争或持续故障忙循环。
  - 执行：`Knowledge/AiKnowledgeCleanupWorkerCoordinator.cs` 每项创建独立可信租户作用域，索引调用拆出的单任务清理短事务，导入调用既有 RetryAsync 和 Files 最小 Contract；无跨模块 SQL/事务，不新建迁移/端点/权限。仅完成已持久化的删除意图，文件副作用用既有五分钟租约和稳定版本键，保留账本/会话/批次。
  - [x] RED：Unit 证明 Worker 缺失 Scoped 清理依赖；补充扫描边界、游标公平性、取消、独立范围、Hosted 停止及静态 AOT 行读取测试。
  - [x] GREEN：在 AiModule.AddBackgroundServices 显式注册选项、游标、协调器、Hosted 和既有清理依赖；API 不启动循环。Global SQL 目录登记精确授权边界。
  - 真实 Worker 诊断发现租户 Files 服务无法解析 `IIdentityActiveTenantDirectory`：该既有端口只在 Tenancy API 注册。将 `ActiveTenantDirectory` 与该端口移入已由 API 复用的 AddBackgroundServices，保留 API 的活动读取别名；补充 Files Worker 装配断言，并将 Tenancy 双库影响集纳入交付。
  - [x] 双库：复用 FullNetApiFactory/现有生产索引生成夹具，验证墓碑提交后无需 HTTP 重试的恢复、Host/Tenant/禁用目录、活动导入租约、失败后推进/回绕、新作用域重启、并发与审计保留。真实扫描/事务/租约需要 SQL Server/MySQL，Unit 无法替代；非 messaging-heavy。
  - [x] 交付：只读安全审查、AI Unit、规定 affected 双库集、相关架构和 AOT 分析；更新矩阵/能力/运维，记录新鲜证据并提交 main。完整 Native、真实云模型和容量单独保留未验证。

  R05f4g 本地证据（2026-10-06，删除清理 Worker）：
  - 基线 main / 91cdbd9dfd94db9d9e08ddcc46e29d7c3e264da8，快照 ai-knowledge-cleanup-worker-r05f4g。有效 RED：仅 Worker 注册缺少 AiKnowledgeIndexCleanupStore，Unit 1/1 失败；首轮真实 MySQL 又暴露 Files 租户存储无法解析 IIdentityActiveTenantDirectory。直接在 Worker 启动前解析 ImportCoordinator 得到精确 DI 异常，随后把 Tenancy 既有活动目录注册移入 API 复用的后台入口，没有修改租户判断 SQL。修正后 MySQL 1/1 通过，失败日志不计入验收。
  - 实现：独立默认关闭的 KnowledgeCleanupWorker，双库固定 Global SQL 仅扫描 AI 墓碑下的任务/导入最小标识；CreatedAtUtc/Kind/Id keyset 游标跨轮保存、失败推进、末尾回绕。每项独立可信租户作用域、30 秒取消预算、每轮固定等待；索引逐任务短事务，文件补偿继续原租约和幂等键，保留会话/批次/收费，不调用模型，不要求目录启用或原会话存活。重复 DELETE 响应语义单独待办。
  - 双库新用例覆盖 Host/Tenant、存活资源不处理、墓碑提交后没有 HTTP 清理调用、索引事务故障回滚、Files 故障后游标推进和回绕、有效/过期导入租约、父作用域不污染、禁用目录及无会话历史任务。实际 Worker Profile 使用真实 Files Port 完成缺失文件的幂等补偿；两个实际 Hosted 实例处理同时间的跨批历史任务。生产生成链路的已知/未知费用、会话和批次保持不变；模型与前段 Files 故障使用受控边界，未做真实杀进程/断电或云模型实验。
  - 独立只读安全审查及 Tenancy 注册修正的追加复核均无 Critical/Important。最终 AI Unit 1215/1215（33s878ms），Tenancy 与 Files Worker Unit 136/136（1s594ms），规定 AI＋Tenancy 双库影响集 124/124（40m44s972ms），零失败/跳过；AI 架构 7/7、AOT 架构 73/73、全局 SQL/Worker/Host 架构 11/11，API/Worker AOT 分析及最终 Release 构建零警告/错误。集成 TRX 有 3 项的捕获文本超过单字段上限而截断，结果标识及通过状态完整，不把截断文本作为完整诊断日志。
  - 命令：pwsh -NoLogo -NoProfile -File artifacts/r05f4g/preflight.ps1；pwsh -NoLogo -NoProfile -File artifacts/r05f4g/close.ps1；后者调用 node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-cleanup-worker-r05f4g --phase slice，正式选择器保留扩大后的 Tenancy 集。1127 个源码/矩阵/运行输入摘要未变，AI/Tenancy 生产与测试程序集一致，Worker 项目依赖闭包一致；TRX 与发现 UID/FQN 精确匹配。原始结果保留在忽略目录 artifacts/r05f4g/ 与同名前缀日志。
  - 全 Unit/Integration 只核对发现数及主分片覆盖，未宣称全项目全量执行；命名、SQL 安全、治理、Integration tooling、主分片与本任务 diff 检查通过。环境 Windows i7-12700H、64 GB、SDK 10.0.401、Docker SQL Server 2022 CU14/MySQL 8.0，Testcontainers 复用关闭；单节点构建、2 GiB GC、2 个处理器。
  - 限制：持续新增积压超过吞吐时，旧失败项回绕重试延迟无上界；未验证容量或清理 SLA。完整 Native 发布运行、真实云模型/外部联动、PDF Worker、检索问答及 Vue 等后续能力保持未验证或待开发，完整 RAG 仍 Planned，Capacity-not-verified，R05 未整体完成。

- [x] R05f4f：删除后的派生索引有界清理与手动恢复（main，基线 26dd7afa；快照 ai-knowledge-index-cleanup-r05f4f）。
  - 交付边界：复用文档 DELETE 提交后的清理和 POST imports/cleanup；每次最多处理 25 个存在派生数据或可运行状态的索引任务。后台扫描、重复 DELETE 响应语义留待下一切片；本项重复清理幂等。不新增端点、权限、迁移或跨模块 SQL。
  - 数据归属与一致性：AI 墓碑先提交，读路径立即拒绝；新 Knowledge/AiKnowledgeIndexCleanupStore 与 Persistence/AiKnowledgeIndexCleanupSql 仅处理 AI 表。每个任务独立短事务，锁定查询核对已删除且当前所有者/可信租户的任务，撤销 queued/running 租约，再删除活动指针、向量和清单；保留任务、原会话、批次和预算账本。持有相同任务锁的旧写入先完成或被取消状态拒绝，不能复活清理后的载荷。异常回滚当前任务，已完成任务不重做。
  - 跨模块边界：ManageKnowledgeVersions/AiKnowledgeImportCoordinator 在 AI 清理事务全部退出后，继续既有 Files Contract 释放；复用持久 cleanup_pending 意图、五分钟租约及幂等版本键，释放失败或响应丢失可在新请求恢复。最终一致，无跨模块本地事务；不创建远程适配或新事件。
  - [x] RED：新增 AiKnowledgeIndexCleanupApiAssertions 和双库包装，真实生产代次生成后 DELETE，断言派生数据为零、账本不变；旧实现将因派生数据残留失败。真实 SQL/锁与 Files 边界无法由 Unit 证明，复用 FullNetApiFactory 和现有生产生成夹具，非 messaging-heavy。
  - [x] GREEN：实现成对静态 SQL、Scoped 注册及协调器接线；扩展覆盖 Host/Tenant、非所有者/跨范围/缺少权限、存活文档不清理、有界多次恢复、事务中断回滚、文件释放失败、残留租约、并发清理和旧 Worker 拒绝写入。现有 imports/cleanup 的整数仍只表示文件补偿完成数。
  - [x] 验证与提交：完整 AI Unit、规定 affected slice 双库集、相关架构、API AOT 分析、矩阵/命名/治理检查；只读安全审查。按实际结果更新本计划、能力状态与运维说明，逐项提交 main；完整 Native、云模型及容量仍不由本项推导。

  R05f4f 本地证据（2026-10-06，删除索引清理与手动恢复）：
  - 基线 main / 26dd7afa63c00f7a4cf613a9d5229512402465b8，快照 ai-knowledge-index-cleanup-r05f4f。有效 RED：真实 MySQL 生产索引生成后 DELETE 返回 204，派生数据预期 0、实际残留 3 类，1/1 失败；原始日志及 TRX 保留在 artifacts/r05f4f/red。首轮与事务交叠补充版 MySQL 各 1/1 通过，后续数据库锁等待观测纳入最终双库验收。
  - 实现：复用 DELETE 提交后及 imports/cleanup 的所有者恢复流程，每次最多 25 个索引任务；任务锁与现有写入锁互斥，撤销可运行租约后清除活动指针、向量、文本清单。仅 AI 短事务，终态任务/会话/批次/费用凭据保留；Files 释放继续使用既有持久意图和版本幂等键，事务外执行。没有新端点、权限或迁移；后台扫描与重复 DELETE 幂等语义仍属下一切片。
  - 最终双库专项 2/2（2m 42s 636ms），验证 Host/Tenant、逐入口权限/非所有者/同一所有者跨范围、存活文档不清理、禁用目录恢复、删除中断回滚及写入后取消回滚、外层事务拒绝、Files 失败后新请求恢复、有效/遗留过期导入租约、25 项上限及两请求同时读到同一候选后的竞争。实际生产生成链路创建已知/未知用量账本，清理前后完整费用与会话/批次不变；Files/模型使用受控边界，未调用真实云模型。
  - 并发证据：捕获生产执行器实际向量 SQL/参数，模拟持久运行态，分别在旧写入已持锁和清理已持锁时启动竞争。SQL Server dm_exec_requests 与 MySQL performance_schema.data_lock_waits 按隔离数据库确认真实锁等待后放行；旧写入先提交会被清除，清理先提交则旧 SQL 影响零行，最终无载荷且账本不变。该验证是受控持久态及事务竞争，不代表真实杀进程/断电实验。
  - 独立只读复核未发现 Critical/Important；补充候选双读屏障、真实锁等待观测、异常收尾、禁用目录及同一所有者反向范围对照后复核通过。最终 AI Unit 1196/1196（34s 021ms）、规定 AI 双库影响集 76/76（27m 03s 381ms），零失败/跳过；AI Architecture 7/7、AOT Architecture 73/73、SQL 目录/Worker/Host Profile 11/11。API/Worker AOT 分析与最终 Release 构建零警告/错误。
  - 命令入口：pwsh -NoLogo -NoProfile -File artifacts/r05f4f/preflight.ps1；pwsh -NoLogo -NoProfile -File artifacts/r05f4f/finish.ps1。最终官方入口为 node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-index-cleanup-r05f4f --phase slice；1119 个源码/矩阵/运行输入摘要保持不变，生产/Unit/Integration AI 程序集和 Worker 项目依赖闭包一致，TRX 与发现 UID/FQN 精确匹配。
  - 全 Unit/Integration 仅核对发现数 4665/1237；主分片 1182 项无遗漏或重复，未宣称全项目全量执行。test:naming、test:sql-safety、test:governance、test:integration:tooling、test:integration:partitions 与本任务 git diff --check 通过。原始证据位于忽略目录 artifacts/r05f4f/ 与同名前缀日志。最终 TRX 的 3 项捕获文本字段触发长度截断，测试身份和结果完整；截断字段不能作为完整过程日志证据。
  - 环境沿用 Windows i7-12700H、64 GB、SDK 10.0.401、Docker SQL Server 2022 CU14/MySQL 8.0，禁用 Testcontainers 复用；单节点构建、2 GiB GC 上限、2 个处理器。完整 Host/Worker Native 发布运行、真实云模型/外部联动与容量未验证；清理后台自动恢复、PDF Worker、检索问答及 Vue 等后续能力保持开放。R05 未整体完成，完整 RAG 保持 Planned、Capacity-not-verified。

- [x] R05f4e：原会话失败任务 HTTP 重试（依据当前继续开发授权，在 main 执行）。
  - [x] 在 `Features/ManageKnowledgeIndexing/Endpoint.cs` 与新 `AiKnowledgeIndexRetryService.cs` 实现 POST 精确版本 `/index/{taskId}/retry`；无请求体和查询参数，复用原会话 HTTP 绑定与 TaskId/VersionId 响应。新增 `AiKnowledgeDocumentPermissions.IndexRetry` 精确权限，叠加原入队五权限，202 仅确认已接受恢复。
  - [x] 服务器读取当前所有者、来源和模型批准；以既有静态会话投影核对完整原会话，旧无绑定任务/新登录/不同任务拒绝 409。新增 `Persistence/AiKnowledgeIndexRetrySql.cs` 双库条件更新，只有 failed 或第三次运行租约到期可回 queued；原会话行、当前来源及批准在同一 SQL 判定，不重置活动租约、完成态或取消态。重复 queued 请求返回 409。
  - [x] 重试只改任务状态/尝试/租约/安全失败码；清单、向量、批次、预算和原会话保持不变，Worker 沿既有消费与计费保护执行。权限 Port 在事务外，等待后复核取消/可信范围/来源，基础设施异常传播；HTTP 不读取 Files 或调用模型。
  - [x] 先运行真实 MySQL POST 缺失入口 RED，再添加 Unit 原会话/逐权限/等待后变化/失败传播及双库 HTTP 恢复状态、重放、活动租约、跨范围、删除/批准变化、账本保留断言。复用现有 API、身份、迁移夹具；实际 SQL 条件更新与授权需要双库，非 messaging-heavy。
  - [x] 使用任务快照 `ai-knowledge-index-retry-r05f4e` 执行规定 affected slice、完整 AI Unit、相关 Architecture 和 API/Worker AOT 分析；独立安全复核后同步运维与能力文档，核对 diff 并提交。派生索引删除、客户端/Vue 以及完整 Native/真实云模型仍分别待交付或验证。

  R05f4e 本地证据（2026-10-06，原会话失败索引重试）：
  - 基线 main / 9d98fb89fe00daed1974d1d981b4cfd7216811b3，快照 ai-knowledge-index-retry-r05f4e。有效 RED：真实 MySQL 原会话 POST 预期 202、实际 404（1/1 失败）。首轮专项 Unit 42/42；首轮 MySQL 因测试新登录后继续使用已撤销旧令牌，预期 409/实际 401，沿既有单会话策略定位并修正夹具、显式保留旧 token 401 断言，生产认证未放宽，此项不计业务 RED。
  - 实现：精确任务 POST retry，独立 index_retry 加原五权限；原会话逐字段匹配、服务器当前来源/模型批准及单 SQL 恢复条件，已排队/活动租约/完成/取消均拒绝，不重绑会话或模型、不触碰收费/批次/清单/向量。所有权限 Port 在 AI 事务外，等待后复核范围/取消/权限/来源；202 只确认接受恢复。
  - 双库专项 2/2（2m 49s 042ms），覆盖 Host/Tenant、逐权限、不同任务/新登录、并发只恢复一次、旧租约拒绝写回、已知/未知用量完整付费批次复用、已付费载荷缺失不免费补发、过期耗尽尝试、来源删除/批准漂移及格式拒绝。最终影响集额外包含缺失原会话行的独立健康对照。真实 Identity、AI SQL、预算和持久化链路，Files/模型受控；未使用真实云模型。
  - 独立只读安全复核未发现 Critical/Important，补齐两项测试建议：新增 SQL 并发恢复与旧 token 保护、缺失绑定独立对照；审查不替代执行。最终完整 AI Unit 1196/1196（35s 873ms），规定 Ai 双库影响集 74/74（24m 49s 178ms），零失败/跳过。AI Architecture 7/7、AOT Architecture 73/73、SQL 目录/Worker/Host Profile 11/11；API/Worker AOT 分析、最终 Release 构建及 Worker JIT 重建均零警告/错误。
  - 命令入口：pwsh -NoLogo -NoProfile -File artifacts/r05f4e/preflight.ps1；pwsh -NoLogo -NoProfile -File artifacts/r05f4e/close.ps1。后者运行完整 AI Unit、架构选择以及 node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-index-retry-r05f4e --phase slice，各入口退出 0。1122 个源码/矩阵/运行输入摘要未变，生产/Unit/Integration AI 程序集及 Worker 项目依赖闭包一致；TRX 与发现 UID/FQN 精确匹配。
  - 发现与门禁：全 Unit 4665、Integration 裸发现 1235，仅核对发现数；主分片 1180 项无遗漏/重复，不宣称全项目测试全部执行。正式入口工具集 65/65，test:integration:tooling 54/54、test:naming 33/33、test:sql-safety 5/5、test:governance 57/57、test:integration:partitions 与 git diff --check 通过；未新增迁移。TRX 对 3 项结果中的超长捕获文本字段作截断，用例身份与结果完整，截断区域不作为完整日志证据。原始证据保留在忽略目录 artifacts/r05f4e/ 及同名前缀日志。
  - 环境与边界：Windows i7-12700H、64 GB、SDK 10.0.401，Docker SQL Server 2022 CU14/MySQL 8.0，FULLNET_TESTCONTAINERS_REUSE=0，单节点构建、2 GiB GC 上限、2 个处理器。完整 Host/Worker Native 发布运行、真实云模型/外部联动、容量尚未执行；派生索引清理、PDF Worker、共享索引客户端/Vue 与检索问答仍待后续交付。完整 RAG 保持 Planned、Capacity-not-verified，R05 未整体完成。

- [x] R05f4d：公开索引状态与当前 ready 证明。
  - [x] 新增 `AiKnowledgeIndexStatusResponse` 与 `ai.knowledge_documents.index_read`，在 `Features/ManageKnowledgeIndexing` 增加 GET 精确版本 `/index`（OperationId `aiGetKnowledgeIndexStatus`），响应仅 versionId/taskId/state/failureCode；无请求体或查询覆盖，不返回执行会话、租约或正文。原版本列表 pending_processing 契约保持兼容。
  - [x] 新增 `Persistence/AiKnowledgeIndexStatusRecord.cs` 与 `AiKnowledgeIndexStatusSql.cs`：服务器读取当前范围、完整两层资源授权、最新任务及来源/模型状态。未入队为 pending_processing，排队/运行映射 queued/processing，失败仅返回稳定通用机器码；删除墓碑仅所有者可见，文件清理或派生索引未清除时为 deleting，全部清理后为 deleted。只读查询不新增迁移。
  - [x] 在 `AiKnowledgeIndexActivationStore` 保留原执行者恢复门禁，新增仅供已授权读取者使用的当前完整性确认：读取不要求原执行会话仍在线；短事务仍复用实际清单、来源、模型空间、完整回执与数据库载荷 SHA256 验证，并在事务内复核同一读取快照。Identity/Organization 权威关系在 AI 事务外取得，不新增跨模块 SQL、写入或模型/Files 调用。
  - [x] 新增状态服务，使用知识库/文档/索引读取三项权限、当前角色/机构关系及 Host/Tenant Scope；响应前复核权限、资源与取消。完成态只在当前证明成立时返回 ready，异常或失效失败关闭。源生成 JSON、静态 Dapper 物化与 API Scoped DI 同步接入。
  - [x] 先以真实 GET 预期 200/实际 405 和缺失权限目录取得 RED；Unit 覆盖状态映射、各权限、作用域、取消、事务与等待后变化，双库 HTTP 覆盖所有者/两层成员/角色/机构、跨范围、删除与真实完整代次 ready（含原会话失效后当前读者仍可读）、损坏载荷及来源漂移。复用既有数据库/身份和生产索引夹具；Unit 无法证明 SQL 授权、锁与实际哈希，因此必须双库，不属于 messaging-heavy。
  - [x] 运行完整 AI Unit、规定双库影响集、相关架构与 AOT 分析，独立安全复核；按真实证据更新矩阵、运维/能力状态并提交 main。真实模型、完整 Host/Worker Native 发布运行与容量未执行时继续明确保留限制；后续继续失败重试和清理 Worker。

  R05f4d 本地证据（2026-10-06，公开索引状态与当前完整性）：
  - 命令入口：`pwsh -NoLogo -NoProfile -File artifacts/r05f4d/preflight.ps1` 执行 API/Worker AOT 分析及最终 JIT 构建；`pwsh -NoLogo -NoProfile -File artifacts/r05f4d/close.ps1` 执行 Worker 重建、`node artifacts/r05f4d/run-unit.mjs`、三个架构选择、输入冻结与下述正式影响集命令。各入口退出 0，结果如下。
  - RED：权限目录 1 项预期 56/实际 55，MySQL 真实 GET 1 项预期 200/实际 405；均确认接口/权限缺失。首轮专项 Unit 的 7 项失败来自测试直接使用默认源生成上下文却断言 HTTP camelCase，按既有 Web 选项修正，未改变生产 JSON 契约；该夹具失败不计业务 RED。新增版本测试在 SQL Server 因匿名参数静态类型为 object 而被传为 sql_variant，显式 Guid 后重跑双库；该失败也不计业务 RED。
  - 实现：独立 index_read 与知识库/文档读取三权限，当前 Host/Tenant 两层用户/角色/机构权威读取；仅返回版本、最新任务、稳定状态及安全失败码。删除墓碑仅所有者可见，文件清理或活动指针/清单/向量残留时保持 deleting。ready 复用生产完整性事务、实际数据库载荷 SHA256 及同一读者资源快照；证明后再次读取当前权限与关系。原执行者离线不影响其他合法读者，原 IsActive 重试仍核对冻结会话，整个读取不调用 Files/模型或修改收费。
  - 审查与 GREEN：独立只读审查未发现 Critical/Important；按建议补齐真实 Refresh 撤销、更新来源版本与同版本较新任务选择，并复核增量。早期专项 Unit 74/74；最终包含新增等待后撤权回归的完整 AI Unit 1156/1156（32s 779ms）。最终双库 HTTP 专项 2/2（2m 35s 243ms），覆盖生产索引链路实际生成完整代次、向量字节损坏、来源/模型漂移、所有者/两层成员/角色/机构、跨范围、三个独立权限、原 Refresh 失效后机构成员仍可读及新登录所有者读取、删除残留清理状态；模型与 Files 使用受控边界，Identity/Organization 与数据库执行实际实现。
  - 最终验收：API/Worker AOT 分析、Integration/Unit/Architecture Release 构建与 Worker JIT 重建零警告/错误；AI Architecture 7/7、AOT Architecture 73/73、SQL 目录/Worker/Host Profile 11/11。node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-index-status-r05f4d --phase slice 退出 0，规定 Ai 双库影响集 72/72（27m 13s 138ms），零失败/跳过。全 Unit 4625、Integration 裸发现 1233 仅核对发现，主分片 1178 项无遗漏/重复，不宣称全项目全部执行；未新增迁移。
  - 证据闭合：1128 个本任务源码/矩阵/运行输入摘要未变；生产/Unit/Integration AI 程序集一致，Worker 项目依赖与最终 JIT Integration 闭包一致；TRX 与冻结发现 UID/FQN 精确匹配。正式入口工具集 65/65，test:integration:tooling 54/54、test:governance 57/57、test:naming 33/33、test:sql-safety 5/5、test:integration:partitions 及 git diff --check 通过。证据在忽略目录 artifacts/r05f4d/ 与同名前缀日志，平台截断的捕获日志字段不作为完整日志证据。
  - 环境与边界：main 基线 4472c86aed1c7b7e11c99d4950815a596f2a5b60，任务快照 ai-knowledge-index-status-r05f4d；Windows i7-12700H、64 GB、SDK 10.0.401，Docker SQL Server 2022 CU14/MySQL 8.0，FULLNET_TESTCONTAINERS_REUSE=0，单节点构建、2 GiB GC 上限、2 个处理器。完整 Host/Worker Native 发布运行、真实云模型及外部联动、容量未执行；完整 RAG 保持 Planned、Capacity-not-verified。后续继续失败重试、派生索引清理和共享客户端/Vue；R05 未整体完成。

- [x] R05f4c：原会话 HTTP 索引入队（独立于公开状态读取交付）。
  - [x] 在 `Features/ManageKnowledgeIndexing` 新增 Endpoint 与服务：POST 精确版本 `/index`，不接受请求体或查询字段，冻结已认证 Refresh/OIDC 原会话；API Key/签名身份不得入队。响应仅含 TaskId/VersionId，202 不证明 queued/ready。
  - [x] 服务使用独立 `ai.knowledge_documents.index_enqueue` 权限及知识库、文档、来源、上传四项权威权限；服务器查询当前所有者、文档/知识库版本、最新已完成导入及精确模型批准。仅 TXT/Markdown，与 Worker 已启用格式一致；调用已有原子 SessionService，不改变原绑定或重置任务。
  - [x] 后台原会话创建/续租、模型派发、清单/向量持久化与激活也核对新增入队权限；撤销后停止下一阶段。普通角色须显式授予该独立权限；超级管理员仍受原会话、租户隔离和资源所有权约束。Unit 逐边界先验证遗漏能失败，再与真实会话双库回归关闭。
  - [x] `Persistence` 新增 Host/Tenant 作用域查询与静态行物化，模块 API 注册最小入队依赖，JSON 上下文登记仅标识响应。所有 Identity Port 均在 AI 事务外，SQL/授权等待后复核取消、作用域及来源版本。
  - [x] Unit 覆盖可信身份、逐权限拒绝、来源/作用域漂移、事务、取消、异常和重放；真实 HTTP 双库验证普通所有者 Host/Tenant、原会话幂等、新登录不可接管、越权/跨租户、旧版本/删除/撤权及请求字段拒绝。复用现有数据库与身份夹具，不能仅用替身证明策略与真实 SQL，不属于 messaging-heavy。
  - [x] 运行规定 AI 影响集、AOT 分析与架构检查，同步测试矩阵及受影响运维/能力文档，检查 diff 与基线后在 main 提交。公开 queued/processing/ready/failed/deleting/deleted 的读取与完整性证明保留下一独立切片，原 pending_processing 契约不变。

  R05f4c 本地证据（2026-10-05，原会话 HTTP 索引入队）：
  - RED：独立权限目录 1/1、真实所有者 POST 1/1（应为 202，原实现 404）、五个后台边界遗漏权限 5 项失败，以及畸形 OIDC 会话回落 2/2 均先取得可失败验证。只读审查指出最终授权等待中的来源变化窗口；TCS 阻塞最后一项授权并更新知识库版本，回归 1/1 在旧顺序下错误成功，修正为最终授权后再读来源。零发现、夹具/内部泛型代理失败均不计有效 RED。
  - 实现与复核：服务器读取最新完成导入、所有者与精确模型批准；202 仅 TaskId/VersionId。原子创建复用现有任务与不可替换会话，不重置终态或收费；最终授权后查询来源，不把等待前快照作为成功证明。畸形 OIDC 原会话不能回落 Refresh，API Key/签名拒绝。五项精确权限贯穿创建、续租、派发、持久化及激活；普通角色升级须授予新增权限。独立只读复核确认竞争窗口修正，无剩余 Critical/Important，审查不替代测试。
  - GREEN：入队与后台阶段回归 49/49，真实 HTTP 双库专项 2/2（2m 41s 367ms），零失败/跳过，覆盖普通所有者 Host/Tenant、同绑定重放、新登录不能接管、越权/跨范围、无权限/字段拒绝、旧来源/删除/审批或导入失效、PDF 422、旧无绑定任务 409，以及真实续租后撤销新增权限即停止。Tenant 切换会撤销原 Host 会话，夹具明确验证旧 token 401，并用新鲜 Host/Tenant 会话验证跨范围 404；未放宽生产认证。
  - 最终验收：完整 AI Unit 1120/1120（1m 18s 594ms）、AI Architecture 7/7、AOT Architecture 73/73、SQL 目录/Worker/Host Profile 11/11，零失败/跳过。API 与 Worker AOT 分析，以及 Integration/Unit/Architecture 和 Worker JIT 重建均零警告/错误，保留依赖漏洞审计。node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-index-api-r05f4c --phase slice 退出 0，规定 Ai 双库影响集 70/70（25m 42s 175ms），正式工具集 65/65；未新增迁移。全 Unit 4589 与 Integration 裸发现 1231 只作发现核对，规定主分片 1176 项无遗漏/重复，不宣称全项目全部执行。
  - 证据闭合：1142 个代码/矩阵/运行输入摘要未变，AI 生产/Unit/Integration 三份程序集一致，Worker 74 个项目依赖与最终 JIT Integration 闭包一致；正式 TRX 与冻结发现的全部 UID/FQN 精确匹配，零跳过。pnpm test:integration:partitions、test:integration:tooling 54/54、test:governance 57/57、test:naming 33/33、test:sql-safety 5/5 及 git diff --check 通过。平台截断 1 项超长捕获文本字段，测试身份和结果完整，截断区域不作为完整运行日志。证据位于忽略目录 artifacts/r05f4c/ 及同名前缀日志。
  - 环境与未验证项：基线 d422f166e1fd60676fafd53189b8e3000a344466，Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401、Docker SQL Server 2022 CU14/MySQL 8.0，FULLNET_TESTCONTAINERS_REUSE=0，构建单节点、2 GiB GC 限额、DOTNET_PROCESSOR_COUNT=2。源文件不在最终构建中变更；早期增量编译零发现已强制重建纠正，不计通过。历史复用数据库保持停止且数据保留。公开索引状态/ready、共享入队客户端/Vue、完整 Worker 进程/真实外部联动及云计量、检索、完整 Host/Worker Native 发布运行和容量实测仍未完成。完整 RAG 保持 Planned、Capacity-not-verified 保持；下一独立切片接入公开状态与当前完整性证明。

- [x] R05f4b：显式启用的知识文本 Worker 队列领取与宿主接入。
  - [x] 在 `Persistence/AiKnowledgeIndexWorkerSql.cs` 与 `AiKnowledgeIndexWorkerRecord.cs` 增加双库有界稳定扫描，只读 AI 自有最小代次/租户/所有者元数据。排除无原会话、失效来源/批准、非文本、活动租约、终止态及耗尽尝试；Global 语句进入精确目录。每个候选经独立可信租户作用域和既有条件更新领取，扫描不是派发授权。
  - [x] 新增 `Knowledge/AiKnowledgeIndexWorkerOptions.cs`、`AiKnowledgeIndexWorkerCoordinator.cs`、`AiKnowledgeIndexHostedProcessor.cs`，默认关闭，单轮上限 1–32、轮询 250–60000 ms；原会话/权限与预算继续由真实整代次消费者复核，禁止长事务、共享 DbSession 心跳及免费重发。拒绝/阶段异常只用受当前租约保护的 index_unavailable 终止，不猜测用量；关闭取消保留租约恢复，旧令牌不写新状态，日志不包含正文/凭据。
  - [x] `AiModule.AddBackgroundServices` 注册最小 Scoped 索引依赖及 Hosted 循环，API 不启动索引循环；Worker 同步覆盖既有和新投影 AOT 物化，配置使用 source-generated BindConfiguration，不引入 HTTP 上传管理或新项目。API 原会话入队和公开 ready 仍下一切片接入。
  - [x] Unit/双库先取得行为 RED。覆盖默认关闭、参数边界、取消/外层事务、真实 Worker DI、Host/Tenant 隔离、争抢、过期代次/旧无会话排除、完整两批执行和费用保留；读取/执行、条件更新和真实账本/指针必须使用生产边界。执行规定 Ai/架构目录影响集、相关 Unit/Architecture/API 与 Worker AOT 分析，记录原生未验证边界后提交 main。

  R05f4b 本地证据（2026-10-05，显式启用的知识文本 Worker 内部消费边界）：
  - RED：夹具修正后的 Worker Unit 18 项中 11 失败、7 通过，失败对应缺失扫描、取消/事务复核、独立作用域和 SQL 过滤；真实扫描/消费者双库专项 2/2 在已取消 token 未抛处失败。初次测试过滤零发现和 NSubstitute 内部泛型投影自动代理异常不计为有效 RED；后者改用显式执行器 Port 夹具，未扩大生产类型可见性。
  - 实现与审查：固定双库 TOP/LIMIT 有界扫描仅投影 AI 自有来源/所有者/租户，Global 精确目录登记；原会话所有者/租户、当前来源/批准、文本格式、状态/尝试/租期复核后，在独立 Scoped 上下文使用既有条件更新争抢。默认关闭、满批继续、竞争退让、作用域异步释放与取消退出已验证；不并发共享 DbSession 心跳。真实整代次消费者复核原会话、精确权限和预算，拒绝/阶段异常仅受当前租约保护写 index_unavailable；关闭取消保留 Running 及账本，到期恢复不免费重发已付费缺失向量。独立只读审查发现 Worker 缺失 AI 权限目录；真实 PermissionSnapshotReader 的普通 Host/Tenant 用户四项权限被过滤为零，回归 2/2 有效 RED 后补上幂等 Contributor 注册，复核确认修复，无新增可确认缺陷。
  - GREEN：Worker/静态投影专项初次 20/20，真实 Worker DI、扫描、租约、双库账本和完整消费者专项 2/2（2m 47s 098ms），覆盖 Host/Tenant、已知/未知用量、拒绝、模型丢失、关闭取消/到期恢复、来源变更/活动租约/尝试耗尽及旧无会话任务排除，核对实际指针和付费保留。最终完整 AI Unit 1068/1068、AI Architecture 7/7、AOT Architecture 73/73、Global SQL/Worker/Host Profile 边界 11/11，零失败/跳过。API/Worker AOT 分析与最终三个 Release 测试构建、Worker 主项目强制 JIT Rebuild 均零警告/错误，74 项依赖与新鲜 JIT 产物摘要一致；全 Unit/Integration 仅核对发现数与分片，不宣称全项目测试全部执行。
  - 规定影响集：node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-worker-r05f4b --phase slice 退出 0，Ai 双 Provider 68/68，零失败/跳过（23m 18s 789ms）。正式 TRX 与冻结发现全部 UID/FQN 一致；1125 个代码/矩阵/运行输入摘要未变，生产/Unit/Integration 三处 AI 程序集 SHA256 一致。pnpm test:integration:tooling 54/54、正式入口工具集 65/65、test:governance 57/57、test:naming 33/33、test:sql-safety 5/5，1174 项分片无遗漏/重复；git diff --check 通过。平台截断 1 项超长捕获文本字段，测试身份和结果完整，截断区域不作为完整运行日志。原始证据在忽略目录 artifacts/r05f4b/ 及同名前缀日志。
  - 环境与未验证项：基线 b6577d6e82f3a0818123a260b17060eb4e9b761f，Windows i7-12700H（14 核/20 线程）、64 GB、SDK 10.0.401、Docker SQL Server 2022 CU14/MySQL 8.0，FULLNET_TESTCONTAINERS_REUSE=0，构建单节点、2 GiB GC 限额、DOTNET_PROCESSOR_COUNT=2，保留漏洞审计。历史复用数据库保持停止且数据保留。已接入 opt-in Hosted 循环、生产 DI 和内部实际消费；原会话 API 入队、公开 ready、完整 Worker 进程/真实外部联动与云计量、检索、完整 Host/Worker Native 发布运行及容量实测尚未完成。完整 RAG 保持 Planned，Capacity-not-verified 保持，下一切片继续原会话 API 入队和公开状态边界。

- [ ] 建立文本 PDF、Markdown、纯文本、超长段落、表格、空文档、扫描 PDF/不支持格式及解析上限的失败用例。
- [ ] 实现 Token 预算分块、有限重叠、Worker 租约与取消、预算预留/结算、幂等索引写入；旧代次不得覆盖新版本或删除状态。
- [ ] 验证 Worker 崩溃/重试、模型用量未知、索引不可用、部分写入与删除竞争；失败可定位并重试，不能将空索引标记 ready。
- [ ] 交付 R07 所需的受授权失败重试端点、最小索引删除/文件释放任务和崩溃恢复；重复删除幂等，清理失败仍禁止访问。R08 只扩展批量重建、增量更新和代次切换，不承接首版必需的删除安全与重试。

### R06：按权限检索、问答及可信引用（P1，依赖 R02/R05）

**新增责任位置：** `src/Modules/Full.NET.Modules.Ai/Features/AskKnowledge/`、`Knowledge/AiKnowledgeRetrievalService.cs`、`tests/Full.NET.UnitTests/Ai/AiKnowledgeCitationTests.cs`；扩展 Ai 的序列化/OpenAPI 与双库 Integration。

**产出：** 问答请求仅选择获准知识库；结果包含答案、拒答状态和服务端引用 ID。引用映射为文档版本/片段/页码，不接受模型生成任意 URL。沿现有聊天/模型执行边界接入，不在业务端点直连供应商。

**历史与传输契约：** 为知识库派生答案/摘要记录完整候选来源依赖，历史显示/导出/再送模型时复核，依赖撤权则隐藏正文并剔除上下文；普通聊天历史保持原语义。首期生成有界缓冲，引用校验与派发前授权通过才返回，不复用 `AiChatCompletionStreamer` 的逐 Token `onDelta` 输出。记录授权判定点，不承诺召回已发送数据。

- [ ] 覆盖租户/角色/组织/文档授权、先检索后撤权、伪造引用、无答案、注入文本、来源删除与旧索引命中。
- [ ] 覆盖生成期间撤权、旧答案历史展示/导出、后续追问、摘要复用和未引用但参与生成的来源撤权；通过可控门闩确认派发前撤权不输出正文，拒绝时仍按实际/未知模型用量结算。
- [ ] 实现服务端过滤、候选批量权威复核、有界上下文、引用允许集校验和独立来源授权；拒绝无证据回答及未经授权的工具动作。
- [ ] 对 R02 冻结评估集生成基线；安全泄漏零容忍，未满足质量门槛保持未验收并记录失败样例。

### R07：Vue 知识库与问答页面（P1，依赖 R04/R06）

**新增：** `ui/admin/src/views/AiKnowledgeBasesView.vue`、`AiKnowledgeDocumentsView.vue` 及对应 `.test.ts`；扩展 `ui/admin/src/views/AiChatView.vue` 与共享生成客户端。路由/菜单按现有机制登记，禁止修改冻结 Layui。

- [ ] 增加知识库/文档管理、导入状态、失败原因、重试、版本与来源预览；问答页展示可点击引用和明确拒答。
- [ ] 覆盖无权限、索引未就绪、文档已撤权、模型/索引不可用、取消、空态及重复提交；前端隐藏不代替 API 授权。
- [ ] 完成类型检查、受影响组件测试、规范语言资源；页面自动化/人工验收条目登记到 R12，不能提前标记 Verified。

### R08：增量更新、删除与索引重建（P2，依赖 R05–R07）

**修改：** R04/R05 新增目录、Ai 持久任务/事件入口；新增 `tests/Full.NET.IntegrationTests/Ai/AiKnowledgeIndexRecoveryTests.cs`，以双库测试方法覆盖相同场景。

- [ ] 以本系统文件替换/文档更新事件建立幂等增量任务；业务可靠事件沿事务 Outbox，缓存失效沿既有直接删除/Backplane，不把缓存通知写入 Outbox。
- [ ] 在 R04/R05 最小删除/恢复基础上扩展完整新代次校验后切换、旧代次批量清理、死信运维和索引对账/重建；撤权先由权威源生效，复用首版 tombstone 与重试机制。
- [ ] 验证乱序、重复、更新与删除竞争、模型换维度、同维度换模型、切换中崩溃与回退；记录恢复操作和保留策略。
- [ ] 后续格式切片增加 DOCX 并覆盖表格/位置与资源上限；扫描件 OCR、外部文件服务器连接器继续保持范围外，不在页面承诺支持。

### R09：Prompt 发布、反馈与检索优化（P2，依赖 R02/R06/R08）

**新增责任位置：** `src/Modules/Full.NET.Modules.Ai/Features/ManagePromptVersions/`、`Features/ManageAnswerFeedback/`；扩展 R02 评估语料及 R07 Vue 页面。新增持久化成对迁移，Prompt 版本不混入通用 Settings。

- [ ] 实现不可变 Prompt 版本，绑定模型策略、语言、输入输出 Schema、工具允许集与检索策略；Run 固化版本，支持停用与回滚。
- [ ] 反馈绑定真实回答/引用、可信用户范围与权限；反馈脱敏审核后才能进入评估集，禁止直接信任反馈指令。
- [ ] 在同一评估集比较基础检索、关键词/向量融合和重排，记录质量、用量与耗时；仅启用有证据收益的策略，失败保留旧策略。

### R10：节点恢复与审批一致性（P2，依赖 R01/R09）

**修改：** `src/AI/Full.NET.Agents/Workflows/AgentWorkflowRunner.cs`、`src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentRunCoordinator.cs`；扩展 `tests/Full.NET.UnitTests/Ai/AiAgentRunCoordinatorTests.cs`、现有 `AiWorkflowCheckpoint*Tests.cs` 与 `AiApprovalRecovery*Tests.cs`。

- [ ] 先复核既有租约/检查点/审批实现，复用已满足的能力；新增节点完成后崩溃、长模型调用、租约失效、取消和版本不兼容的回归用例。
- [ ] 定义并持久化节点输入/输出摘要、定义/模型/Prompt 版本及 OperationId；已知完成不重跑，未知外部副作用进入对账，不能从 checkpoint 缺失推断未执行。
- [ ] 审批恢复复核当前会话/权限、参数摘要、工具/策略版本及业务状态；全节点预算和 deadline 必须约束实际派发，未知用量不得计零。

### R11：受控业务路由、数据去向和观测（P2，依赖 R06/R09/R10）

**修改/新增责任位置：** `src/AI/Full.NET.Agents/Workflows/`、Ai 运行时与模型策略；`docs/operations/ai-provider-network.md`、`docs/operations/ai-operation-budgets.md` 在实现完成时同步，不提前把方案写成运行能力。

- [ ] 首个路由只允许“知识库问答”和“现有授权工具任务”；封闭意图枚举、独立工具允许集，未知意图澄清/拒绝，保留顺序执行和总预算。
- [ ] 将数据分类策略应用于 Embedding、重排、生成及遥测；私有化模式故障不得切换公网 Provider，测试每个阶段的拒绝行为。
- [ ] 关联 Run/Step/Trace 的阶段耗时、调用量、拒答、预算拒绝与错误；日志脱敏、指标低基数，不将文档正文或用户/租户 ID 放入 Metrics 标签。
- [ ] 不增加专家群聊、任意动态插件或微服务拆分；出现真实业务收益证据后另修订本计划。

### R12：原生闭包、运营恢复与集中验收（P2，依赖前述切片）

**修改：** `tests/Full.NET.IntegrationTests/NativeAot/NativeApiAiE2ETests.cs` 及 Worker 原生探针；测试矩阵 `eng/testing/test-matrix.json`、现有受影响 CI、Vue 页面验收与 `docs/roadmap/capability-status.md`。

- [ ] 汇总并补齐各切片的原生产物证据，覆盖真实 Embedding/索引写入、带权限检索、删除、解析、工具及审批恢复；同时覆盖确定性测试 Provider 与获准环境的选定真实 Provider，明确各自证明范围。此项不允许把 R05/R06 的 AOT 兼容检查延后。
- [ ] 演练索引丢失重建、Provider 故障、文档撤权、任务重复派发、密钥/网络策略变化与应用回退；核对双库同语义。
- [ ] 集中收敛页面自动化与人工验收；分别测量 API、Worker、向量库、模型服务的资源和延迟，不引用文章的百分比作为验收结果。
- [ ] 按精确提交核对必要 CI 终态，更新能力矩阵和运维文档；存在失败、跳过或部署环境缺失时仅标明已实现/局部验证，保留 `Capacity-not-verified`。

### 执行与验证入口

本节不另设测试流程，遵守 [开发质量 §11](../../../rules/development-quality.md#11-测试与验证)。已有命令入口：

```text
pnpm test:dotnet:unit -- --selection ai-runtime
pnpm test:dotnet:unit -- --selection ai-module
pnpm test:dotnet:architecture -- --selection ai-agentic-boundaries
pnpm test:integration:partitions
pnpm test:governance
pnpm test:aot:analyzers
pnpm test:aot:worker:analyzers
```

按实际影响选择执行，不每项机械全跑。新增测试先接入矩阵且保持最低发现数；双库、真实浏览器、Linux Native 发布/运行按 §11 默认走获授权的 GitHub Actions。禁止未授权推送以取得证据；本地可完成部分与远端未验证项分别记录。

停止条件：权限泄漏、错误租户、迁移不可恢复、重复业务写入、出网策略绕过或 AOT 静态闭包破坏立即修复；未经选型批准的新基础设施不得进入生产装配。页面差异可登记后继续功能建设，不能借此延后上述风险。

**覆盖核对：** 格式/入库 R03–R05；权限/引用 R04/R06；评估 R02/R06/R09；生命周期 R08；工作流 R01/R10/R11；数据/预算/观测 R05/R10/R11；Vue R07；AOT/恢复/状态 R12。历史 T00–T15 的未关闭项在触及相关切片时复核，不由新增任务勾选自动关闭。

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
- 2026-09-13（续 13）：Worker 启动失败根因：`Notifications` 后台闭包依赖 `IHostFileReferenceClaimService`，`FilesModule.AddBackgroundServices` 未注册跨模块引用契约；已抽取 `RegisterHostFileReferencePorts` 并在 Worker 路径注册。Native API E2E `HttpClient` 超时 60s→120s 缓解 CI 流水号/工作流慢路径。
- 2026-09-13（续 14）：流水号 Native E2E 挂起根因：`HostSerialRuleService` 注入 `IDataApprovalScenarioPolicyPort` 与 `SerialRuleChangeApprovalSource` 形成 scoped DI 环；已拆出 `HostSerialRuleReader`、审批门禁上移至 Endpoint，Worker 退役命令剥离模块 `IHostedService`（`WorkerMaintenanceRegistration`），统一 `NativeAotTestTimeouts`（启动 5m / HTTP 3m）。
- 2026-09-13（续 15）：CI `34764372414` 证实流水号已通过（失败点前移至 Document `POST /document/host/items` 500）；补 `DocumentJsonSerializerContext` 的 `IReadOnlyList<Guid>`；撤销误伤 SignalR 的 `Realtime__Enabled=false`；Worker 退役扫描改为剥离全部 `IHostedService`（含工厂注册）。工作流实例启动仍 180s 超时，待下一轮排查。
- 2026-09-13（续 16）：Worker 退役命令在写出 JSON 后 `Environment.Exit`，避免 `StopAsync` 阻塞进程退出；Workflow E2E 失败时附带原生日志路径；`WorkflowJsonSerializerContext` 补 `JsonElement` 元数据。
- 2026-09-13（续 17）：CI `34769145802`（`9847de0a`）原生日志证实 Document `POST /document/host/items` 500 非 JSON 序列化，而是 Files DI 环：`HostDocumentItemManagementService` → `IHostFileUploadWriter` → `HostFileManagementService` → `HostFileQueryService` → `IHostFileContentReader` → `HostFileQueryService`。`030d2277` 令 `HostFileContentReader` 直接经 `IQueryExecutor` 读取元数据以断开环；`critical_http_flow` 失败时附带原生日志。Workflow `POST /instances` 仍 180s 超时（待查）。
- 2026-09-13（续 18）：CI `34770977784`（`030d2277`）Document 创建 500 已消除；`critical_http_flow` 失败点前移至 `POST /api/v1/messaging/dead-letters/replay`（`VerifyMessagingDeadLetterFlowAsync` 1261 行，180s 超时）。Workflow `POST /instances` 仍 180s 超时。5 passed / 4 failed。
- 2026-09-14（续 19）：根因定位为 Messaging 订阅目录 Scoped DI 环：`IntegrationEventSubscriptionCatalog` 构造解析 `OrganizationUnitChangedKafkaSubscription` 时枚举全部 `IIntegrationEventHandler`，经 `NotificationIntentService`→`IOutboxWriter`→`EffectiveEventDeliveryOwnerResolver`→目录再次解析而挂起。修复：`59593df5` 延迟访问目录 + 直接注入 Kafka Handler；Native E2E replay 附带原生日志。
- 2026-09-14（续 20）：CI `34773307191`（`59593df5`）replay/workflow 180s 超时消除（`critical_http_flow` ~23s）；仅 2 失败：`VerifyAiModuleNativeClosureAsync` 使用进入租户前的 Host token（`identity.session_not_active`）。Workflow 双库已通过。修复：组织流后重新 `LoginAsync` 再验 AI 闭包。
- 2026-09-14（续 21）：CI `34774858699`（`5630fea9`）Native API external-process E2E **failed: 0**（`critical_http_flow` SqlServer/MySQL、`workflow_approval_flow`、Messaging replay 均绿）。Worker Native E2E 仍因 `fullnet.workflow.instance.{completed,rejected,cancelled}` 被 Notifications 与 DataApproval 各注册一个 `IIntegrationEventHandler`，`ValidateUniqueRoutes` 启动即失败。修复：在 `Workflow.Contracts` 引入终态 Sink 契约，Workflow Worker 注册唯一 fan-out Handler，Notifications/DataApproval 改为 Sink 实现。
- 2026-09-14（续 22）：S3 Native E2E 因 Docker Hub `minio/minio` pull access denied 失败；测试容器改用 `ghcr.io/coollabsio/minio:RELEASE.2025-10-15T17-29-55Z`，并支持 `FULLNET_MINIO_TEST_IMAGE` 覆盖。
- 2026-09-14（续 23）：CI `34776703267`（`bfb08dde`）Worker Native AOT dual-database E2E **failed: 0**（含 `VerifyVersionRetirementAsync` 退役扫描）。CI `34776703255`（`bfb08dde`）API Native AOT external-process E2E **failed: 0**；S3 Provider E2E **2/2**（SqlServer/MySQL）；Kafka Replay **failed: 0**。
- 2026-09-14（续 24）：主 `ci` `34778722667`/`34776703288` 在 `build-test` 单元门禁失败：`IdentityModuleRegistrationTests` 需登记 `OrganizationUnitChangedIntegrationEventHandler` 直接 Scoped 注册；`MySqlPublishedUuidCompatibilityTests` 计数随 `199_OcrFoundation` 直接使用 `BINARY(16)` 调整为 15。Native AOT 专用 workflow 仍绿。
- 2026-09-14（续 25）：主 `ci` `891d9919` 的 `build-test` 仍失败：架构测试需登记 AI MCP 委托/远端连接粗粒度权限、`ai.mcp_remote.*` 权限码重命名、`AiBackgroundToolAuditPort` 租户写入白名单，以及 MCP 服务令牌解密路径 allowlist。本地 `pnpm test:dotnet:architecture` **225/225**。
- 2026-09-14（续 33）：CI `34833513790`（`5f96ff61`）**api-mysql/api-sqlserver/build-test/Native AOT 绿**；失败：`client-build-test`（`fn_reporting_export_task.RowCount` 注释漏提交）、`messaging-heavy`（`EventDeliveryPilotTestSupport` 未注入 `Func<Catalog>`）、`infrastructure`（Development seed 缺 `regions.administrative_baseline`）、`real-stack-e2e*`（改密后 UI 仍用旧密码）、`migrations`（部分恢复演练，待查）。续 32 修复已落地，待 push。
- 2026-09-14（续 32）：`api-sqlserver` 首轮 **79/81**（Document recycle purge FK、`HostFolderSql.SoftDelete` SQL Server 保留字 `file` 别名）；修复：`document.host_recycle_bin.purge_dependents` 级联删 preview/access/tag/version 子表；`active_file` 别名；`fn_reporting_export_task.RowCount` 注释登记；聚焦 Document+Files **2/2**、`pnpm test:naming` **31/31**。`api-sqlserver` 81/81 全分片复验进行中。
- 2026-09-14（续 31）：续 30 九项根因修复后全分片复验 **80/81**（~26m）；唯一剩余项 `OpenApi_documentation_follows_contract_with_mysql` 因 `fullnet-client-v1.openapi.json` 未随 Notifications `ProducesProblem(401/403/404)` 与 Files preview `application/octet-stream` 同步更新。`node scripts/openapi/snapshot-client-openapi.mjs --update` 重生成快照（+181 行）；`OpenApiDocumentationApiMySqlTests` **1/1**、`pnpm openapi:client:snapshot --check` SqlServer+MySQL PASS。全分片再跑确认 **`api-mysql` 81/81**（~25m）。
- 2026-09-14（续 30）：`api-mysql` 全分片单独复验 **72/81**（较旧构建 66/81 +6）；剩余 9 项：Document 模板克隆争用×3、Jobs HTTP handler 断言、Notifications OpenAPI 401、Observability 实例列表空、ReleaseNote retract Conflict、Regions/Settings enum OpenAPI。续 29 基础上：`CloneMySqlTemplateAsync` 复用 `GET_LOCK`；`JobsExecutionCancelAssertions` `WaitForStateAsync` 改用已 `SetHost()` 的 worker scope。
- 2026-09-14（续 29）：`ApiSchemaTemplate` MySQL 模板引导增加 `GET_LOCK` 跨进程互斥，修复并行 `dotnet test`/分片与聚焦批跑争用 `fullnet_it_schema_mysql` 导致的 `database exists`/迁移半拉子重复列；清理损坏模板后 `Tenant_unit_management_follows_contract_with_mysql` **1/1**。`api-mysql` 全分片（81）单独复验进行中。
- 2026-09-14（续 28）：Workflow 退回目标 SQL 列别名 `AssignedUserId AS AssigneeUserId`（修复 JIT Dapper 物化 500）；`WorkflowAssigneeResolver` 默认发起人策略在 Tenant 上下文信任已认证发起人（修复 `Tenant_scope_approval_matrix`/`Todo_return`）。本地：`Todo_return` MySQL+SQL Server **2/2**、`WorkflowApiMySqlTests` **5/5**、Import/Files/Dict 聚焦 **4/4**、`WorkflowAssigneeResolverTests` **3/3**、`pnpm test:naming` **31/31**。`api-mysql` 全分片复验进行中。
- 2026-09-14（续 27）：续 26 基础上补充：`HostFileSqlTestParameters` 修复 MySQL `@FolderId` 空值参数；E2E `real-stack-auth.mjs`/`provision-viewer.mjs` 自动清除 `MustChangePassword`；ImportExport error-receipt OpenAPI 契约对齐 xlsx MIME。`api-mysql` shard 本地执行中。
- 2026-09-14（续 26）：主 `ci` integration 门禁本地收口（HEAD `f2bf4c1f` 工作区未提交）：Identity OpenAccess/OnlineSession/HostUserDirectory、Workflow CC/并行 tally SQL、Files MySQL folder soft-delete 与 file-reference claim Host 上下文、`IntegrationTestAuthHelper` 统一 Host 登录；`filesPreviewHostFileContent` OpenAPI 补 `application/octet-stream` 并更新 `fullnet-client-v1.openapi.json`；`HostFolderTreeNodeArray` 契约映射与 `fn_reporting_export_task.RowCount` 注释登记。本地 `pnpm test:naming` **31/31**、`pnpm test:integration:smoke` **8/8**、`pnpm test:dotnet:architecture` **225/225**、`Host_file_management_follows_contract_with_mysql` **1/1**、`openapi:client:snapshot --check` 通过。
- 未执行/未关闭：主 `ci` 全矩阵复验（含 integration-shard 全量）、Vue 集中 a11y/移动端人工验收。T15 Linux Native API/Worker E2E 主路径已绿，整体未关闭。
