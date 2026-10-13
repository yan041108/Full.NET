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
- 2026-10-07 推进更新（替代上条尚未接入 Worker/公开入口的阶段状态）：文本完整索引、原会话入队/重试、当前 ready 与删除恢复已交付；本批 R05f 接入显式启用的 PDF 运行时、入队/重试、Worker 分流和当前读取证明，完成同批本地验收。Native PDF 证据覆盖真实 HTTP 与已结算代次恢复，不外推真实解析或云模型整代次派发。下一阶段 R06 建设权限检索、问答和可信引用，完整知识库/RAG、质量和容量门禁仍未全部关闭。

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

#### R06a 合批执行：权限召回、上下文和引用流水线

main 基线 `6529a2ab481a2a04ca278f9226c15b6bcad6ac51`，快照 `ai-knowledge-retrieval-r06a`。按当前授权在同一会话实施并统一提交，不派生实施代理。

- [x] 新建 `Knowledge/AiKnowledgeVectorRanker.cs` 与 `AiKnowledgeRetrievalTypes.cs`：`Rank(ReadOnlyMemory<float>, IReadOnlyList<AiKnowledgeRetrievalChunk>, CancellationToken)` 校验精确维度、有限非零向量及唯一代次/序号，以 double 计算余弦并按规范 Guid/序号打破平分。先验证正确排名、平分稳定、非有限数、零向量、维度漂移和超限拒绝；不静默截断候选。
- [x] 新建 `Persistence/AiKnowledgeRetrievalSql.cs` 与 `Knowledge/AiKnowledgeRetrievalService.cs`：`RetrieveAsync(IReadOnlyList<Guid>, Guid, AiModelConfigRecord, ReadOnlyMemory<float>, CancellationToken)` 冻结输入；四条精确权限与当前角色/机构在事务外复核。双库批量限定请求知识库、来源最新版本、当前模型空间和完整活动代次；复用已有静态 Status/Vector 行类型和完整 TXT/PDF 证明，读取实际有界载荷。最多 8 个知识库/文档、32 个批次、256 个片段、8 MiB 载荷；SQL 上限加一，超限整次拒绝。来源批量权威查询在读载荷后和交付前复查，单次工作预算 25 秒，未确认清理拒绝。
- [x] 新建 `Knowledge/AiKnowledgeAnswerContext.cs` 与 `AiKnowledgeAnswerPipeline.cs`：`PrepareAsync(..., string question, CancellationToken)` 建立最多 8 片段、4096 Token 的完整上下文和 UUID v7 服务端引用，文本写入 JSON 数据字段；`AcceptAsync(context, ReadOnlyMemory<byte> response, CancellationToken)` 先冻结有界 JSON、拒绝未知/重复字段和伪造引用，再复核所有参与生成的来源（包含未引用来源），成功才交付正文。不裁剪来源，不用引用名单替代完整依赖，不执行工具或逐 Token 输出。
- [x] 在 `tests/Full.NET.UnitTests/Ai/` 增加真实规则、门闩撤权、同版本目的地漂移、载荷篡改与上下文取消测试；已有真实 TXT/PDF 双库索引场景追加生产检索与引用流水线断言。开发先运行聚焦 RED/GREEN，合批再运行完整 AI、受影响架构、正式双库和治理，按实际发现更新唯一矩阵，最后提交 main；以下保留首轮失败、重跑结果和未关闭的全局门禁，不宣称整套验收全绿。

本批是 R06 的内部检索与缓冲引用消费者，不增加 HTTP、DI、持久化答案或模型派发；下一批连接受预算的查询 Embedding/生成及完整来源依赖历史后开放问答。SQL 仅访问 AI 自有表；角色/机构来自既有最小 Port，不入本地事务。本批不把受控查询向量/模型 JSON 作为真实模型质量基线，也不宣称完整 RAG 或新增内部路径 Native 已验收。

**R06a 本地证据与边界（2026-10-07）：**

- RED/GREEN 覆盖稳定排名、数值/规模边界、严格 JSON、伪造引用、取消与未引用来源撤权。审查发现的最后权限等待窗口、捕获载荷与活动回执绑定、最新角色/机构复核均先建立失败用例，再修复并复核；最终只读审查未发现剩余 P1/P2 阻塞项。
- 最终 Release 构建通过。实际 `node artifacts/r06a/run-ai.mjs` 使用 `pnpm test:dotnet:unit -- --no-build --selection ai-module` 对应的唯一矩阵参数，追加四 Worker 设置和 TRX，完整 AI 实际 1818 通过、零失败、零跳过；新增 69 个用例，完整 Unit 发现数同步到矩阵。`pnpm test:dotnet:architecture -- --no-build --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol' --minimum-expected-tests 80` 实际通过全部 80 项。该结果是架构约束验证，不是新增检索消费者的 Native 发布/运行证据。
- 正式双库使用任务快照的 `pnpm test:integration:affected -- --snapshot ai-knowledge-retrieval-r06a --phase merge`，UID 去重并确认 SQL Server/MySQL；实际 90 项、89 通过、1 失败、零跳过，退出码 1（dotnet 2），40m52s。唯一失败是未修改的 `AiKnowledgeIndexRetryApiMySqlTests.Http_retry_preserves_session_and_recovery_data_async` 在文本代次执行返回 false；不是新增消费者调用路径。本批 TXT/PDF 四个双库核心用例均通过，包含 Host/Tenant、两层授予、角色/机构撤权、模型目的地漂移、载荷篡改和缓冲交付。
- 未改源码/产物，实际直接运行 Integration DLL，以 `FullyQualifiedName~AiKnowledgeIndexRetryApiMySqlTests|FullyQualifiedName~AiKnowledgeIndexRetryApiSqlServerTests`、最低发现数 2、超时 15m 和独立 TRX 复现；2 项均通过、零失败、零跳过，退出码 0，4m27s。初次失败时任务仍 running、已有清单和一个批次、尚无活动指针（`retry-failed-state.txt`）；不能据此确定失败阶段或原因，也不将重跑视为修复或把首轮改成通过。该既有恢复用例的稳定性仍待定位。
- `verify-acceptance.mjs --recheck` 核对完整 UID/FQN、两份 TRX 时间和同一冻结输入，成功运行观察覆盖全部 90 个目标 UID，首轮失败单独保留。先前被主动停止的旧源码运行不计为通过。507 个源码/矩阵输入及 7 个构建产物已冻结，四个位置的 AI DLL 一致（`artifacts/r06a/final-inputs.json`）；本批内部消费者已实现并有本地 JIT 核心证据，正式集合的首轮失败仍是未关闭项。
- `pnpm test:naming` 33 项、`pnpm test:sql-safety` 5 项、`pnpm test:governance` 57 项通过；正式任务入口附带的 Integration tooling 65 项通过，分片核对 1194 项，无遗漏或重复。最终 `git diff --check`、暂存差异检查及 main 分支检查通过，仅提交本批 14 个文件。
- 扩大架构扫描实际 233 项、229 通过、4 失败：既有 TXT/PDF ActivationStore 异步命名、Cleanup/Index Worker 租户写入登记、PDF `Task.Run`/`.Result` 与 PDF ActivationSql 非静态构造。已修复本批新增消费者自身的异步命名与静态 SQL 声明，保留既有门禁和失败记录。10 个相关既有源码及 4 个门禁与任务基线 Git blob 相同（`baseline-architecture.json`）；该比对不替代完整基线重跑，也不把失败报告为通过。上述全局门禁仍待单独关闭。
- TXT/PDF 双库夹具使用真实生产 SQL、载荷和完整代次证明，受控会话/角色/机构 Port 验证资源两层授予与等待期间撤权；不外推真实 Identity 登录、云模型质量或容量。上下文只取前八个完整片段，整个 JSON 超过 Token 上限整次拒绝；文本大块夹具仅用一个完整片段验证缓冲交付，PDF 夹具覆盖完整准备流水线，不宣称自动适配上下文大小。
- Windows x64、i7-12700H、约 64 GB、SDK 10.0.401、运行时 10.0.12；SQL Server 2022-CU14 / MySQL 8.0，测试进程 2 个处理器、GC 堆 2 GiB，AI Unit 独立四 Worker，容器不复用。R06 父项保持未关闭。R06b 下一批接入收费查询 Embedding/生成、预算结算、完整来源历史依赖和公开问答，再补新增可达路径 Native 证据及 R02 真实端到端质量基线；容量仍 `Capacity-not-verified`。

#### R06b1 合批执行：收费问答执行闭环与完整块容量适配

main 基线 `1964f73cb2aed2eb4fa88723395eab6ca7e875c1`，快照 `ai-knowledge-paid-answer-r06b`。继续在当前会话实施、测试并提交 main。本批不增加公开入口，历史持久化和新增公开可达路径 Native 证据仍属于后续 R06b。

- [x] 修改 `Knowledge/AiKnowledgeAnswerContext.cs` 与 `tests/Full.NET.UnitTests/Ai/AiKnowledgeAnswerContextTests.cs`：按排名选择至多八个完整块，每加入一块计算完整 JSON 的固定词表 Token；遇到超限停止，不截断正文或跳过较高排名。连第一块都放不下则拒绝；整份候选依赖仍保留在检索快照。先证明当前八块整包拒绝，再验证完整块适配、边界拒绝、位置和输入冻结。
- [x] 扩展 `Knowledge/AiKnowledgeRetrievalService.cs` 的 `PreflightAsync(IReadOnlyList<Guid>, Guid, AiModelConfigRecord, CancellationToken)`：冻结请求并复用当前权限、角色/机构、模型和完整活动代次证明；无查询向量也能在收费前拒绝不可读或损坏来源。随后实际召回必须与这份来源快照一致。
- [x] 新建 `Knowledge/AiKnowledgePaidAnswerService.cs` 的 `ExecuteAsync(IReadOnlyList<Guid>, Guid, AiModelConfigRecord, AiModelConfigRecord, string, CancellationToken)`：通过现有模型工厂和预算账本依次执行一次查询 Embedding、一次有界非流式生成。两个阶段各自预留、禁止复用预留重发、客户端创建后再复核、保存已知计量后才验证输出；未知计量保留，未派发使用零计量。异常/取消/撤权仍独立结算，结算失败禁止交付，结算等待后重验来源及生成模型。返回经过引用校验的答案及完整准备快照，不能把该临时快照当作已持久化历史。
- [x] 在 `tests/Full.NET.UnitTests/Ai/AiKnowledgePaidAnswerTests.cs` 复用完整 PDF 激活夹具，以可控客户端和预算 Port 覆盖 Host/Tenant、预留拒绝/重复、派发前后撤权/同版本模型漂移、已知/未知/非法计量、恶意引用/工具、取消和结算失败。`tests/Full.NET.IntegrationTests/Ai/AiKnowledgeRetrievalAssertions.cs` 在既有真实 TXT/PDF 双库场景消费实际收费流水线与生产账本；保持真实模型质量未验收。
- [x] RED/GREEN 后运行完整 AI Unit、相关架构和治理，更新唯一测试矩阵；按快照执行双库影响集并保留真实失败记录，检查差异与分支后提交。R06 父项、公开 Ask、历史显示/导出/追问撤权、R02 质量和容量保持未关闭。

**R06b1 本地证据与边界（2026-10-07）：**

- 一批接入查询 Embedding、缓冲回答生成、两个独立预算操作、完整块容量适配及全部来源复核。来源快照保留所有候选依赖，最前面的完整块装不下则拒绝，不裁剪正文或跳过较高排名。生成目的地、凭据和 `AiChatPermissions.Send` 与最后来源查询绑定；首次模型读取前冻结入口范围，等待后范围改变时拒绝，禁止在新范围收费、按旧范围结算。
- 新增 49 个 Unit 用例。审查发现生产适配器拒绝非空 ChatOptions、最后权限等待中只撤 Send、非合作客户端创建/调用/同步释放及入口范围漂移四类问题，均建立失败回归再修复。真实注册工厂通过本地 HTTP Handler 验证 Embedding JSON 和缓冲生成传输；不把受控供应商响应作为云模型质量证据。外部 IO 复用既有边界，冻结回执后不接受迟到计量或再次派发，结算使用原租户的独立五秒作用域。最终只读复核未发现剩余具体 P1/P2；复核人未代跑测试。
- 最终 Release Unit/Integration/Architecture 构建均为零警告、零错误。`node artifacts/r06b/run-ai.mjs` 使用唯一矩阵的 `ai-module` 参数及四 Worker 设置，实际 AI Unit 1867 通过、零失败、零跳过；完整 Unit 发现 5336 项，矩阵同步。`verify-acceptance.mjs --unit` 精确核对 UID/FQN 与最终 TRX。510 个源码/矩阵输入、7 个产物冻结，四处 AI DLL 一致；后续验收只复用这些未变输入。
- `pnpm test:dotnet:architecture -- --no-build --filter 'FullyQualifiedName~AiDependencyBoundaryTests|FullyQualifiedName~NativeAot|FullyQualifiedName~MemoryPackControlledProtocol' --minimum-expected-tests 80` 实际 80/80、零失败/跳过。补查四组既有全局门禁，实际 14 项、10 通过、4 失败，分别是 TXT/PDF ActivationStore 异步命名、两个 Worker 租户写入登记、PDF 异步边界和 ActivationSql 非静态构造。`baseline-architecture.json` 核对 9 个相关源码和 4 个门禁与本批基线 Git blob 相同；AuthorityBoundary 仅 XML 摘要改变，实现体在换行规范化后相同。该比对不替代完整基线重跑，四项失败仍未关闭。
- `pnpm test:integration:affected -- --snapshot ai-knowledge-paid-answer-r06b --phase merge` 正式选择双库 90 个目标，首轮实际 88 通过、2 失败、零跳过，退出码 1（dotnet 2），40m49s726ms。两次失败均为 PDF 夹具仍以原索引费用 37 校验整个模型账本；本批六场景查询 Embedding 新增 79（五次已知各 7、一次未知保留 44），实际总额 116。修正夹具逐操作核对收费并返回查询 Embedding 费用，在原期望中相加，保留恢复不新增费用、清理不退费及原索引操作账本保留断言；生产代码未改变。只读复核确认计数/范围和原验证均保留，未代跑测试。
- 保存首轮三处夹具源码、Integration DLL、冻结输入及原始 TRX，重建后 `node artifacts/r06b/run-fixture-recheck.mjs` 精确重新发现并执行全部受改动影响的 TXT/PDF 双库四个方法，实际 4/4、零失败/跳过、退出 0，4m58s315ms，包含 Host/Tenant 和真实生产账本。`verify-acceptance.mjs` 核对两份 TRX 的完整 UID/FQN、先后时间、仅三处夹具源码/一个 Integration DLL 改变、其余 507 输入与生产/Unit/Architecture 产物未变，最终有效通过覆盖 90 个目标，无遗漏/重复。首轮失败仍保留，不生成伪造的单轮全绿报告；R06a 既有重试用例本轮通过，不视为已定位或修复其历史稳定性问题。
- 正式 TRX 两个测试结果的超长捕获文本被平台截断，测试身份与结果完整，截断区域不作为完整诊断证据。原始记录与核对报告位于忽略目录 `artifacts/r06b/`。Windows x64、i7-12700H、约 64 GB、SDK 10.0.401、运行时 10.0.12；SQL Server 2022-CU14 / MySQL 8.0，测试进程 2 个处理器、GC 堆 2 GiB，AI Unit 四 Worker，双库两 Worker，容器不复用。结论仅适用于本批本地 JIT 消费边界。
- `pnpm test:naming` 33 项、`pnpm test:sql-safety` 5 项、`pnpm test:governance` 57 项通过。正式影响集入口的 tooling 65 项通过、Integration 分片 1194 项无遗漏/重复；最终影响规划仍为 Ai、integration-matrix 与双库 Smoke。检查 `git diff --check`、暂存差异、main 分支和冻结输入后，仅提交本批 14 个文件，保护无关改动。
- 本批仍是内部消费者，没有 HTTP/DI 接线或已持久化答案。临时快照含内部模型和向量，禁止直接作为公开契约或历史保存。公开 Ask 与历史详情、MCP 摘要、后续聊天取上下文的完整来源权限保护合并为下一批；R06 父项、R02 真实质量、新增公开 Native 发布/运行与容量均保持未关闭，容量仍 `Capacity-not-verified`。

#### R06b2：公开问答、来源依赖持久化及历史复核

复用 R06b1 执行器，按依赖分批交付。R06b2a 已实现下述内部持久化与统一复核基础；公开入口和所有已确认的历史消费者仍待接线及验收，不能据此关闭 R06b2 或完整 RAG。

- [ ] 先建立来源未引用但参与生成、等待期间撤权、删除/版本变化、损坏依赖记录及跨租户的失败用例；证明历史正文、引用摘要与后续模型输入都不会绕过来源复核。普通聊天无来源依赖时保持原语义。
- [x] 在 AI 自有持久化边界增加成对迁移、静态 SQL 和最小完整来源依赖/引用记录，绑定答案消息与服务器确认的位置。保存版本化受控元数据，不保存临时检索快照、向量或受保护密钥；未知或损坏依赖拒绝释放正文。
- [ ] 增加 `Features/AskKnowledge/` 与源生成公开契约，通过可信主体和精确权限接入已授权知识库、模型选择及 R06b1 执行器。复用聊天生成槽位、原范围独立清理、租约监视与跨实例取消，外部模型调用不入事务；短事务按本代租约保存答案及全部依赖，不能先发送正文再补历史。
- [ ] 统一历史来源复核：`AiChatSessionQueryService.GetByIdAsync` 的详情映射、通过该查询取摘要的 `AiMcpSessionSummaryReader`，以及 `AiChatStreamService.LoadRecentMessagesAsync` 的模型输入。撤权消息隐藏正文/引用并从后续模型上下文剔除，稳定机器码说明不可用状态；新增出口必须复用同一边界。普通聊天若消费知识答案，其回复及后代必须继承全部来源依赖闭包，并缓冲输出、在落库及交付前复核；超过依赖上限必须拒绝，不能截断。完整传播尚未实现时，普通聊天必须始终排除所有派生历史，不能把转述保存成无来源标记的普通消息。
- [ ] 使用真实生产 SQL/账本及受控模型，成对覆盖 Host/Tenant、TXT/PDF、重复并发、取消/租约失效、落库失败和完整依赖撤权。同步 DI、物化/JSON 静态闭包、OpenAPI 及新增 HTTP 可达路径 Native 发布/运行证据，保持云质量与容量未验收。
- [ ] 合批运行完整 AI、受影响双库及新增 Native 路径，核对当前输入和原始报告，在 main 提交；仅关闭有实际证据的 R06 子项，继续 R07 页面交付。

**R06b2a 内部来源依赖与落库合批（2026-10-07，本地内部验收完成）：**

- main 基线 `925d625561825156e450b5b1489a75520dec5271`，快照 `ai-knowledge-public-answer-r06b2`。同批交付完整依赖编解码、254 双库迁移、统一来源读取复核和原子答案落库，保护原有两项无关未跟踪文件。
- 元数据绑定消息、会话、可信所有者和租户；最多八个完整来源及八条服务器确认引用，UTF-8 上限 16384 字节。严格拒绝未知版本、缺失/重复/未知字段、跨范围记录及非法位置；未展示引用的来源也必须保存。独立 `IsKnowledgeAnswer` 标记确保空或损坏依赖不能降级为普通消息；254 支持逐步中断重放、丢失列/注释/禁用约束恢复及旧历史保留。
- 事务外统一复核精确操作权限、当前角色/机构、两层资源资格及全部来源版本；实际 TXT/PDF 回执、清单和载荷证明后再次读取权威权限与来源。答案正文和依赖在当前生成租约下原子写入，锁定会话后以数据库实际 UTC 再检查租约，消息锁等待跨过期限会抛错回滚全部写入；提交后再复核交付资格。
- 只读审查指出活动摘要指针不能代替实际完整证明、锁等待前应用时钟不能证明执行时租约、恢复夹具不能运行无界最新迁移；分别补实际 TXT/PDF 读取证明、会话锁与数据库 UTC 末次判定、精确冻结五项前置/目标脚本。复审未发现当前内部交付剩余 P1/P2。真实 TXT/PDF 双库四个方法覆盖 Host/Tenant 正常落库、重放拒绝、独立连接消息锁等待到期后的原子回滚、保持指针但破坏回执拒绝及末次来源删除拒绝。
- 尚未注册 DI、HTTP 或历史消费者；调用方取得槽位、插入占位、监视取消、独立清理与交付正文均属于后续公开接线。普通聊天必须传播完整依赖闭包并缓冲复核输出，或始终排除派生历史；不能把知识转述保存成普通消息。当前内部服务不能证明公开历史正文已受保护。
- RED：新增依赖用例在占位实现下 20 失败、8 通过；实现后新增 62 项回归通过。`pnpm test:dotnet:unit -- --selection ai-module` 最终 1929/1929、零失败/跳过（1m11s480ms）；实际发现 Unit 5398，矩阵只同步已确认数量。首次迁移聚焦 3 通过、1 因 SQL Server 测试中文裸字面量损坏失败；改用参数绑定，原始失败日志保留。
- `pnpm test:aot:analyzers` 通过，零警告/错误，恢复默认 JIT 后 Integration 构建通过。Native AOT 架构完整 73/73；首轮子构建资源耗尽失败保留，回收本任务退出父进程遗留的构建节点、限制处理器后重跑。额外边界架构实际 19 项、18 通过、1 为既有 PDF 激活 SQL 静态目录违规；该源文件及扫描/门禁与基线未变，同时保留误设最低数量 20 的非零退出记录，不报告整个架构集合通过。
- `pnpm test:naming` 33/33、`pnpm test:sql-safety` 5/5、`pnpm test:governance` 57/57；正式影响集前置工具链 65/65、治理 57/57，Integration 分片 1198 项无遗漏/重复。初轮复用 SQL Server 容器被 OOM 杀死后中止，不计通过；改为不复用的独立双库容器。
- `pnpm test:integration:affected -- --snapshot ai-knowledge-public-answer-r06b2 --phase merge` 正式发现 94 项。为减少逐场景重复迁移，保留无模板运行已完成的 32 项后主动中止该任务进程，原始退出 1 不计为单轮全量通过。按既有官方 TRX 2.3.2 序列化依据严格核对原始日志完整 Passed 帧及 UID/FQN，精确补跑剩余 62 项；补跑使用不复用的私有双库容器及只含结构/journal 的 schema 模板，每个用例仍独立供给业务数据库。`node artifacts/r06b/lineage-run-remaining.mjs` 退出 0，62/62、零失败/跳过（31m24s223ms）；两条成功结果诊断按 TRX 字段上限截断，身份及结果完整。`node artifacts/r06b/lineage-verify.mjs` 退出 0，核对最终精确并集 94、全部 UID/FQN、1215 项冻结源码/矩阵输入和七个程序集未变，正式及测试 AI DLL 摘要一致。不伪造单轮全绿报告；原始发现、冻结输入、成功/失败/中止日志、真实补跑 TRX 和 `lineage-local-acceptance.json` 保留在忽略目录 `artifacts/r06b/lineage-*`，不覆盖 R06b1 记录。
- Windows x64、.NET SDK 10.0.401、Docker SQL Server 2022-CU14 / MySQL 8.0；集成进程限制两个处理器，不复用容器。本批没有新增公开 Host/Worker 可达路径，未重新发布或运行 Native 宿主、调用真实云模型或执行容量实测；公开问答仍未完成，完整 RAG 保持 Planned，容量保持 Capacity-not-verified。

**R06b2b 历史消费者合批交付（2026-10-07）：**

- 基线 main `1575dc4b315d966584296a13258087052c961840`，快照 `ai-knowledge-public-consumers-r06b2b`。本批复用已有 schema，不新增表或迁移；跨模块权限/角色/机构仍走既有最小 Port，不读写对方表，也不引入跨模块事务。
- [x] RED：缺失来源标记读取、未授权映射、跨消息等待撤权、损坏记录和普通聊天转述均先复现。公开 Ask 写入端点另列 R06b2c，当前批次完成历史、MCP 摘要及普通聊天上下文三个真实消费入口。
- [x] 历史整批复核最多 64 条派生消息、512 个完整依赖，同一来源只验证一次实际完成证明，最后一笔 SQL 同时复核全部请求来源。超限不截断依赖，派生消息失败关闭；普通消息保持原语义。详情映射和 MCP 摘要只接收经资格复核的正文及服务器引用，不返回内部元数据。
- [x] 普通聊天在 SQL 和内存两层排除所有派生历史；完整追问依赖传播继续单独待办，不能把知识转述落成普通消息。
- [x] R06b2c 公开入口交付：新增会话内非流式 Ask 契约，复用 Chat.Send 与四项精确来源读取权限、可信所有者、原会话生成模型及显式 Embedding 模型。槽位/占位短事务，外部收费执行在事务外，正文缓冲、来源复核、租约原子落库和独立原范围清理后交付；引用只有服务器确认的标识和位置。
- [x] 同步 DI、静态行物化、JSON 源生成、OpenAPI 及历史 HTTP 路径 Linux Native 发布/运行。统一完整 AI、双库正式 merge、治理/命名/契约与只读复审后在 main 提交；保留真实结果及未验证项，完整 RAG/云质量/容量不提前关闭。

**R06b2b 最终验收证据（2026-10-07）：**

- pnpm test:dotnet:unit -- --selection ai-module：1955/1955，0 失败、0 跳过；完整 Unit 发现 5424 项，矩阵按真实发现同步。Native/MemoryPack/AI 依赖/全局 SQL 目录架构组合实际 82/82；PDF 激活 SQL 的静态构造已修复，目录门禁未放宽。
- AOT 分析单进程重跑构建 0 警告/0 错误；OpenAPI 双提供程序生成一致，208 项工具测试通过，96 个契约相对基线兼容。命名 33、SQL 安全 5、治理 57 项通过。
- 正式 merge 发现 AI 82 + smoke 8 = 90 项，双 Provider；首次完整运行 86 通过/4 失败/0 跳过（36m16s558ms），最终 TRX 已正常生成；首次运行中 TXT/PDF 四项因夹具插入未标记且带来源的非法组合，被迁移 254 的现有 CHECK 拒绝。修正夹具并显式验证该非法写入被拒绝；保留首次失败，受影响四项使用新编译程序集单独补验 4/4，0 失败、0 跳过（6m02s549ms）；新 DLL 与已编译 obj 输出哈希一致，UID 为正式 90 项集合的严格子集。两份 TRX 按 UID/FQN 合并恰好 90/90 最终 Passed、0 跳过，原失败不改写；最终 Windows Integration 重建 0 警告/0 错误且 DLL 与补验输出一致。
- 首次 Linux API 原生发布因 2 GiB GC 堆上限触发 ILC OutOfMemoryException；重试仅调整构建资源为 4 CPU、12 GiB 容器/6 GiB GC 堆，并保留编译缓存，发布/告警/体积门禁不变。重试发布成功（1420718ms），17 条既有精确 allowlist 告警通过，原生 ELF 138237344 字节通过体积门禁；Native 核心过滤器实际发现 29 项（矩阵最低数 10，不能等同发现数）；首次 45 分钟全局时限中止，26 项全部 Passed、0 失败/跳过（45m16s212ms），官方 2.3.2 完整记录序列化及 UID/FQN 已核对；剩余 3 项独立补验 3/3、0 失败/跳过（6m29s258ms）。最终按唯一 UID/FQN 合并恰好 29/29 Passed，两次执行的 API/Worker 工件 SHA-256 完全一致；原始中止记录保留。验收容器初始 2 CPU、8 GiB/2 GiB GC 堆，后调为 4 CPU；补验 4 CPU、8 GiB，结果仅说明该范围本地验收通过。Worker 门禁复用此前已发布的原生工件并记录哈希，不据此宣称本批重新发布 Worker。
- 只读代码复审未发现待修正 P1/P2；生产实现、测试输入及 DLL 校验保留在 rtifacts/r06b2b。公开 Ask、完整追问依赖传播、RAG 云质量与容量验证继续待办。

**R06b2c 公开 Ask 合批执行计划（2026-10-07）：**

- 基线 main `d731f6c0b0b57da68627255e0c4b431adc71d1b7`，快照 `ai-knowledge-public-ask-r06b2c`；沿用现有 schema 与最小权威 Port，只在 AI 表内启动/完成短事务。
- [x] 会话内 `POST /api/v1/ai/chat/sessions/{sessionId}/knowledge/ask`：输入问题、1–8 个知识库和显式 Embedding 模型，生成模型固定为原会话配置，权限为 Chat.Send 加四项来源读取权限；返回已确认消息、引用位置与明确拒答。
- [x] 与普通聊天共用生成租约、取消注册和独立续租；短事务获取槽位并插入用户消息和知识占位，事务外执行两阶段收费；失败/取消仅收敛本代未完成占位，独立原范围释放槽位，清理等待后重新授权才交付正文。
- [x] RED 后覆盖输入、所有权、槽位竞争、取消、启动失败及引用/拒答；双库实际 HTTP 验证公开入口，补齐 DI、源生成 JSON、OpenAPI 与 Native API 可达路径证据。统一测试并只读复审后提交，完整 R06/R07、追问依赖传播、真实云质量和容量继续待办。

**R06b2c 本地证据（已完成）：**

- 服务端公开非流式 Ask、共享槽位与取消、两阶段账本及服务器引用/拒答一并交付。Vue 增加 `askAiKnowledge` 与严格响应守卫，运行时 OpenAPI 双库一致，新增 Operation 与生成客户端已同步；R07 页面仍未完成。
- RED 先复现服务未注册及客户端入口不存在；启动/执行单元验证覆盖 Host/Tenant、输入、所有权、竞争、请求与注册表取消、续租失败、引用、拒答、清理失败与交付前撤权。`pnpm test:dotnet:unit -- --no-build --selection ai-module` 1988/1988，零失败/跳过；实际全 Unit 发现 5457，只更新发现门槛，不宣称执行全 Unit。
- `pnpm --filter @fullnet/admin test -- src/api/ai-chat.test.ts` 18/18；共享契约 226/226；共享契约 build 与 Vue typecheck 通过。`pnpm test:openapi` 208/208；运行时快照精确 594 Operation，`pnpm test:openapi:breaking -- --base-ref d731f6c0b0b57da68627255e0c4b431adc71d1b7` 96 契约兼容，离线快照与生成零漂移检查通过。
- Host.Api 私有输出 AOT/Trim 分析构建通过（零警告/错误）；Native AOT、AI 依赖、SQL 目录/范围/数据访问架构最终 82/82。首次自定义过滤实际 79 项、门槛 82 导致退出失败，保留原结果；补齐 SQL 范围与数据访问后精确发现 82 项并全部通过，没有降低门槛。
- 聚焦 MySQL HTTP 首次正常回答和拒答成功，撤权按既有 `409 ai.knowledge.answer_unavailable` 拒绝；断言误写 403，已修正并追加精确机器码，生产查询与授权不变。修正测试缺少 JsonElement using 的构建失败，以及 OpenAPI 并发占用 DLL 的构建复制失败均保留；之后私有输出正式构建零警告/错误。正式重跑与原生运行结果见下，全部新功能的真实 TXT/PDF Host 公开 HTTP 场景均已在双库通过。
- `node artifacts/r06b2c/affected-private.mjs --snapshot ai-knowledge-public-ask-r06b2c --phase merge` 正式 Ai + Smoke 去重集合 90/90、零失败/跳过（38m 20s 289ms），构建零警告/错误；前置工具链 65/65、治理 57/57，Integration 分片精确 1198 项无遗漏/重复。TXT/PDF 双库真实来源分别验证公开 Ask 回答、拒答、输入与权限拒绝、模型返回前撤权、历史隐藏、会话竞争和主动取消；实际 Embedding 费用纳入原场景总量，未用模拟数据库替代权威 SQL。
- `python -X utf8 artifacts/r06b2c/verify-acceptance.py` 逐项核对正式 90 与 Native 10 的精确 FQN/结果、架构真实 82 项、冻结输入与输出 SHA、运行的原生二进制，退出 0。两个成功 Integration 用例的过长诊断字段被既有 TRX 扩展截断，用例身份与结果完整，不作为失败或跳过；原始控制台、真实 TRX、发现记录及 `acceptance.json` 均保留。最终 `pnpm test:naming` 33/33、`pnpm test:sql-safety` 5/5、`pnpm test:governance` 57/57，以及本任务 Git 分支/status、未暂存/暂存 diff 检查通过；初始无关文件不纳入提交。
- 官方 API Native 发布副本 `node artifacts/r06b2c/native-publish.mjs` 退出 0：linux-x64、138597968 字节、41m 50s 710ms，17 项允许警告通过既有门禁；发布与运行 SHA-256 均为 `02b5c9962887687603ea0a8a0fa3d089dbb5d7d60b3a7c25f9ae7ef41bfc1f71`。源码没有 Worker 可达行为变化，本批未发布或运行 Worker，不能外推为新 Worker 验收。
- `node artifacts/r06b2c/native-core.mjs` 使用矩阵核心选择器排除未变 Worker，仅执行 API 精确 10 项；Native API 10/10、零失败/跳过（23m 53s 402ms），包含 SQL Server/MySQL 的实际公开 Ask 失败收敛、AI/MCP、Kafka 日志、关键 HTTP、SignalR、启动/就绪/退出及工作流。Linux 测试程序集在两个测试 helper 最新源码下重建，零警告/错误；原生进程日志、真实 TRX、发现集合和 SHA 保存在 `artifacts/r06b2c/native-core`。
- 原生问答使用模型地址漂移触发实际来源收费指纹不匹配，预期 `409 ai.knowledge.answer_unavailable`、失败占位空正文和槽位释放；该拒绝先于外部模型调用，不冒充成功云模型问答或网络政策运行验证。首次计划误以为会到达网络拒绝，调用链核对后修正 Native-only 测试断言与 import，Linux 测试程序集重新构建后运行。`native-only-test-delta.json` 记录唯一冻结差异，1533 项原 Ai/Smoke 输入及四个输出均未变，Native 测试源码和实际程序集另行记录 SHA。
- 使用忽略目录 `artifacts/r06b2c` 的官方工具副本，仅重定向独立输出并限制构建并行数，选择器、Provider 与最低发现门槛保持。源码/矩阵/客户端冻结 1534 项及四个测试输出单独记录 SHA；只读最终复审无阻断项。真实外部云回答、追问来源依赖传播、完整 R06/R07、容量与 SLO 保持未验证。

### R07：Vue 知识库与问答页面（P1，依赖 R04/R06）

**新增：** `ui/admin/src/views/AiKnowledgeBasesView.vue`、`AiKnowledgeDocumentsView.vue` 及对应 `.test.ts`；扩展 `ui/admin/src/views/AiChatView.vue` 与共享生成客户端。路由/菜单按现有机制登记，禁止修改冻结 Layui。

- [ ] 增加知识库/文档管理、导入状态、失败原因、重试、版本与来源预览；问答页展示可点击引用和明确拒答。
- [ ] 覆盖无权限、索引未就绪、文档已撤权、模型/索引不可用、取消、空态及重复提交；前端隐藏不代替 API 授权。
- [ ] 完成类型检查、受影响组件测试、规范语言资源；页面自动化/人工验收条目登记到 R12，不能提前标记 Verified。

#### R07a：目录、文件版本与公开 Ask 页面合批（2026-10-07，Build-verified）

- [x] 沿用 Art Design 增加知识库创建/编辑、文档创建/编辑/删除与分页，登记精确权限导航。
- [x] 复用生成客户端交付文件上传、导入状态/失败码/手动重试、版本列表和受授权来源读取；文本只按纯文本预览，PDF 原文件下载。
- [x] 对话增加单知识库 Ask、明确拒答/来源不可用、引用来源入口；取消、重复提交、页面卸载和作用域变化保护。
- [x] 建立失败测试后统一运行组件、共享契约、语言、生产构建和相关治理验证，在 main 提交。

PDF 页内预览仍为 R07 后续项；处理策略与成员授权编辑见后续 R07b，索引派发/状态/重试及多知识库选择见后续 R07c。页面真实自动化和人工验收登记 R12；本批最多标记 Build-verified，不借用 R06 服务端证据标记页面 Verified。

- main 基线 `1173f95d756aab795370e3465f692bd016a3d3a9`，快照 `ai-knowledge-vue-r07a`。后端仅扩展两个静态导航元数据，不改变 Endpoint、DTO、持久化、迁移或 DI；冻结 Layui 未变。Vue 适配器直接消费既有共享生成操作，覆盖门禁支持显式共享消费者，不移动稳定生成清单的操作所有权。
- 上传结果不明时保留原 request key 和乐观版本；当前会话/租户或知识库改变时清空结果，晚到响应与卸载不回填。原文 Endpoint 的实际 MIME 是 octet-stream，文本预览必须匹配精确版本元数据；UTF-8 严格解码、8 MiB 上限、最多展示 100,000 字符且不渲染 HTML。历史引用缺少知识库绑定时让用户选择当前授权库，不猜测历史绑定。
- 取消先提交服务端，再中止本地流并有界刷新状态；服务器未终止时继续轮询和允许人工刷新。即时拒答有专门状态；历史 API 没有拒答判别字段，已完成空 assistant 显示“未生成回答”。来源失效时隐藏旧回答与引用。
- 行为失败测试覆盖无权限调用、上传幂等重试、导入失败重试、迟到响应、撤权后纯文本关闭、实际 octet-stream 来源、取消状态刷新和历史空回答。只读复审发现的来源 MIME、取消仍在生成、历史空回答三项问题已修复，最终复审没有剩余 P1/P2。
- `pnpm --filter @fullnet/admin test --maxWorkers=6`：261 文件、959/959，211.10s；`pnpm --filter @fullnet/admin build`：类型检查和生产构建退出 0。共享客户端 60 文件、226/226，语言 8/8，表单设计器 3 文件、8/8；`pnpm test:openapi` 210/210，workspace 检查退出 0。首轮默认 Vue 并发出现 10 项超时；另一次低并发运行主动中止，均不计为通过，最终完整结果保存 `artifacts/r07a/admin-tests-final.log`。
- `pnpm test:dotnet:unit -- --selection ai-module --no-build`：1989/1989、零失败/跳过，39.976s；Release Unit 和 Integration 构建完成，Integration 构建零警告/错误。矩阵按实际新增导航测试同步 Unit 5458、AI 1989；Integration 分片 1198 项无遗漏/重复，治理 57/57。
- 收尾追加 AI Unit 的默认 20 Worker 运行出现一项既有 PDF 子进程 1 秒截止时间夹具清理断言失败，1988 成功/1 失败，保留失败日志，不计为通过。相同源码/程序集以 `DOTNET_PROCESSOR_COUNT=4` 执行同一正式选择，1989/1989、零失败/跳过（2m 01s 477ms），不放宽断言或降低发现数；最终日志 `artifacts/r07a/ai-unit-accepted.log`。该对比支持资源竞争相关的不稳定表现，不宣称已经修复存量夹具。
- 原首屏包体门禁超限后按同环境基线与候选构建比较：首屏 minified +1.11%、gzip +0.69%，全部 JS +1.26%。首屏预算对齐本批开始前的实测基线，保留 5% 停止条件；三项 `pnpm test:bundle-budgets` 通过，详见 [包体核对](../../verification/2026-10-07-ai-knowledge-vue-bundle.md)。这是增长核对，不宣称浏览器体验优化。
- `pnpm audit:clients` 仍未通过，当前阻断为 `GHSA-qhr7-859c-m2p7` 等跨客户端工具链存量告警。已将 undici 7.29.0 → 7.29.1 并完成冻结安装及受影响测试，不扩大为整个客户端工具链升级，不增设审计豁免或宣称发布候选全绿。
- 正式 `pnpm test:slice -- --snapshot ai-knowledge-vue-r07a` 选择完整双库 AI 82 项。首次复用容器因模板冲突及 SQL Server 连接失效中止；隔离重跑在 Docker 引擎不可连接后没有终态/TRX，不能计整轮通过。严格按现有 TRX 扩展 2.3.2 序列化格式核对原始日志，31 项完整 Passed UID/FQN。恢复引擎后，`FULLNET_TESTCONTAINERS_REUSE=0 FULLNET_API_SCHEMA_TEMPLATE=1 node artifacts/r07a/run-integration-remaining.mjs` 在独立容器使用既有只读 schema 模板加速，精确发现并补跑其余 51 项，退出 0、51/51、零失败/跳过（31m 01s 156ms）；两项成功用例的诊断文本按既有 TRX 字段上限截断，用例身份和结果完整。原中断记录保留，不生成伪造的单轮全绿报告。
- `node artifacts/r07a/verify-acceptance.mjs` 退出 0，核对 3992 项后端源码/配置与 247 DLL 的冻结输入未变、正式/Unit/Integration 三处 AI DLL 一致、AI Unit 1989 通过摘要，以及双库原 31 + 补跑 51 的完整 82 UID/FQN 精确并集。原始日志、发现、TRX、SHA 与 `acceptance.json` 保留在忽略目录 `artifacts/r07a/`。最后两处契约消费者与包体基线的只读复审没有实质阻断项。Windows x64、i7-12700H、约 64 GB、.NET SDK 10.0.401、Docker 29.6.2、SQL Server 2022-CU14 / MySQL 8.0；本地资源共享，不外推容量或 SLO。
- 本批没有重新发布/运行 Native 宿主、真实云模型调用、页面自动化或人工验收，也没有容量/SLO 实测。R07 总体仍未关闭，容量保持 `Capacity-not-verified`。

#### R07b：处理策略与两层授权维护合批（2026-10-07，Build-verified）

- main 基线 `dd14a1aa48a4a809b09fe26be4e3e589c5ae7543`，快照 `ai-knowledge-vue-r07b`。本批只改 Vue 消费层、语言资源与覆盖清单，复用已批准的生成操作；不修改服务端、SQL、DTO 或生成操作所有权，按 §11.1 纯客户端范围验收。
- [x] `ui/admin/src/api/ai-knowledge.ts` 补齐策略、库/文档用户、角色、组织授权和受作用域保护的候选薄适配；测试验证请求版本、路径绑定和运行时响应守卫。
- [x] 新增 `AiKnowledgePolicyDialog.vue`：独立策略权限、分类与精确模型版本选择、撤销时成对清空、保留旧批准而不自动升级；模型分页仅使用模型读取权限，不请求凭据。
- [x] 新增 `AiKnowledgeGrantsDialog.vue`：库/文档两层用户、角色与组织授权独立读取/替换，原授予版本、最大 100 项、角色/组织候选分页；当前接口只提供用户 UUID，用户授予用明确标识列表，不借 Host 用户目录扩大 Tenant 范围。
- [x] `AiKnowledgeDirectory.vue` 增加精确权限入口，先以入口和行为测试建立 RED，覆盖只读、重复提交、冲突、关闭/撤权/租户或资源切换及晚到响应；同批组件、共享契约、语言、构建、包体与治理验收后提交 main。

本地证据（Windows x64，原始报告 `artifacts/r07b`）：

- API、目录入口与弹窗分别建立 RED：3 项、2 项、18 项预期行为失败；后续 3 项测试选择器失败定位为 Element Plus 把标记放在 textarea 本身，修正测试后受影响组件通过，原始报告保留。
- 最终 `pnpm --filter @fullnet/admin test src/components/AiKnowledgePolicyDialog.test.ts src/components/AiKnowledgeGrantsDialog.test.ts src/components/AiKnowledgeDirectory.test.ts src/api/ai-knowledge.test.ts --maxWorkers=4`：4 文件 43/43，无跳过。
- `pnpm --filter @fullnet/admin test --maxWorkers=6`：263 文件，991/992 通过，唯一失败是未改动的 `App.test.ts` 账户安全页 5000ms 超时。保持源码不变，`pnpm --filter @fullnet/admin test src/App.test.ts --maxWorkers=1`：4/4 通过，无跳过；不将首次全量命令报告为成功，也不重复累计重叠用例。
- `pnpm --filter @fullnet/admin build`（vue-tsc + Vite）、`pnpm --filter @fullnet/client-contracts test`（226/226）、`pnpm --filter @fullnet/admin-i18n test`（8/8）、`pnpm test:openapi`（210/210）、`pnpm test:governance`（57/57）均通过，无测试跳过。
- `pnpm test:bundle-budgets` 三项通过；初始静态 JS 1,454,545 bytes（+1.97%）、gzip 381,310 bytes（+1.16%），既有基线及 5% 限额不变。
- `pnpm test:integration:affected:plan -- --snapshot ai-knowledge-vue-r07b --phase slice` 返回 11 个变更文件、目标 `none`；按 §11.1 不重复后端 Integration 或 Native 发布。只读复审未发现需修复项，`git diff --cached --check` 通过。
- 依赖与锁文件未改，本批未重跑全工作区依赖审计；R07a 记录的存量审计失败不因此关闭。未完成真实页面/人工验收，不推导端到端、Native、容量或 SLO 已验收。

延后索引生成操作/页面与多库 Ask，R07 总体不提前关闭。真实页面及两层授权交互验收继续登记 R12。

#### R07c：索引操作与多库问答合批（2026-10-07，Build-verified）

- main 基线 `062c20c21c1c780aa48876bcc31de6c1c523668a`，快照 `ai-knowledge-vue-r07c`。复用已有服务端索引与 Ask，不改 C#、SQL、授权或模型派发规则。
- [x] 从 SQL Server/MySQL 真实运行时 OpenAPI 同步三个索引操作，生成共享模型/守卫/操作，补齐薄适配及请求路径、无请求体、响应守卫测试。
- [x] 版本列表增加精确索引权限入口，弹窗交付状态查询、入队与原任务重试；写操作不自动重发，不用旧状态或旧 taskId 重试；失败、重复提交、关闭/资源/会话变化丢弃迟到响应。
- [x] 问答交付 1–8 个知识库的有界多选，跨页保留已选项、唯一 UUID 校验、会话切换清理；提交冻结资源列表，引用不猜测来自哪一库，来源选择仅建议本次请求范围并由服务器再次授权。
- [x] 合批运行受影响组件、共享契约、OpenAPI/生成一致性、多语言、构建、包体和治理验证；记录真实页面验收项到 R12，复审后提交 main。PDF 页内预览继续后续项，R07 不提前关闭。

本地证据（Windows x64，原始报告 `artifacts/r07c`）：

- `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore -m:2 -nr:false` 成功，零警告/错误。`FULLNET_TESTCONTAINERS_REUSE=0 FULLNET_API_SCHEMA_TEMPLATE=1 pnpm openapi:client:snapshot -- --update --no-build` 从当前产物导出 SQL Server、MySQL 各 1/1 OpenAPI 测试，零跳过，两库规范内容一致。
- `pnpm openapi:client:generate` 成功；`pnpm openapi:client:generate -- --check` 零漂移，`pnpm openapi:client:snapshot -- --check --offline` 通过。三个现有索引 Operation 进入 manifest/规范快照及生成产物；总操作 594 → 597，知识库组 32 → 35，保持原操作、权限与响应字段，未手工编辑生成文件。
- API/多选、索引弹窗、页面入口/多库来源先建立 RED，原始记录保留。首轮组件 61/62：唯一后续失败是测试夹具非 UUID 被生成守卫拒绝，修正夹具后最终七个受影响文件 64/64；OpenAPI 原固定操作清单未含新增三项，补齐登记与索引参数/无正文/响应模型断言后通过，不放宽门禁。
- `pnpm --filter @fullnet/admin test --maxWorkers=4`：266 文件 1020/1020；`pnpm --filter @fullnet/client-contracts test`：226/226；`pnpm --filter @fullnet/admin-i18n test`：8/8；`pnpm test:openapi`：210/210；`pnpm test:governance`：57/57，均无跳过。
- `pnpm --filter @fullnet/admin build`（vue-tsc + Vite）成功；`pnpm test:bundle-budgets` 三项通过，初始静态 JS 1,457,655 bytes（+2.19%）、gzip 381,967 bytes（+1.34%），既有基线与 5% 限额不变。
- `pnpm test:integration:affected:plan -- --snapshot ai-knowledge-vue-r07c --phase slice`：25 个变更文件，目标 `none`；C#/SQL 未变，本批不重复业务 Integration 或 Native 发布。真实双库仅证明本批运行时 OpenAPI/生成范围，不替代页面或云模型验收。只读复审未发现阻断项。
- `pnpm audit:clients` 退出 1，仍为既有未复核高危 `GHSA-qhr7-859c-m2p7`；未改依赖或锁文件，不申报审计通过。本批保持 Build-verified，PDF 页内预览与真实页面验收仍待后续，不推导端到端、Native、容量或 SLO 已验收。

#### R07d：受授权 PDF 页内预览合批（2026-10-07，Build-verified）

- main 基线 `dd0cb8845a8a1a23b7d0f8dbd0768fe04d9731c7`，快照 `ai-knowledge-vue-r07d`；沿用来源端点及精确版本 MIME，不改服务端契约。
- [x] 为来源 PDF 分支及独立预览组件建立 RED；只接收重新授权取得的最多 8 MiB 字节。
- [x] `AiKnowledgePdfPreview.vue` 与 `knowledge-pdf.ts` 延迟加载固定 PDF.js 版本及本地 Worker/字体资源；仅 Canvas 单页渲染，关闭注释、表单和 XFA，不使用 PDF URL/链接/脚本。限定 500 页、4 百万像素、最大边长 2400、每次解析/渲染 15 秒，超限/加密/损坏文档显示下载退路。
- [x] 一起交付引用页定位、前后翻页、0.5–2 倍缩放；不存在的引用页明确失败。关闭、换源、撤权、卸载后停止 Worker/渲染并清除画布，迟到结果不能回填。
- [x] 运行组件/共享多语言、实际浏览器 PDF Worker 冒烟、完整 Vue、构建/包体/审计/治理验证；只读复审后提交 main。浏览器模拟授权证据不替代 R12 双库真实页面及人工验收。

本地证据（Windows x64，原始日志与浏览器截图 `artifacts/r07d`）：

- 来源与画布先运行 RED，缺失能力导致 7 失败/8 通过。首轮 GREEN 暴露卸载后画布引用已经置空，改用卸载前清理；异步组件测试显式声明 ES 模块。最终三个受影响文件 20/20，覆盖引用定位、页数/像素边界、缩放、超时、取消、换源迟到、字体资源白名单与重新授权。
- `pnpm --filter @fullnet/admin test --maxWorkers=4` 完整独立运行：268 文件、1032/1032，零失败/跳过（483.10 秒）。首次与浏览器并跑出现未改动 Users 页的 5 秒超时后主动中止，保留失败日志，不报该轮全量通过。
- `pnpm --filter @fullnet/admin-parity-e2e exec playwright test ai-knowledge-pdf.spec.mjs --project=vue-admin --workers=1`：真实 Edge/本地 PDF Worker 3/3；像素断言证明矢量页面绘制和翻页，缩放/关闭归零/损坏/无权/403 重新授权通过，PDF 自带链接不触发外部请求。早期 CLI 参数、Hash 路由和自定义 Dialog 标题定位夹具失败均保留，修正后通过；HTTP 为模拟，不能冒充真实服务端 E2E。
- 初轮 OpenAPI 208/210 因 PDF 辅助器误放 HTTP 适配目录而失败；只将文件与测试移到 `src/components/` 并同步 import/mock，不改实现或放宽 manifest。机械调整后受影响 20/20、浏览器 3/3（25.9 秒）、`pnpm test:openapi` 210/210，`pnpm --filter @fullnet/admin build`（vue-tsc + Vite）重新通过。完整 Vue 证据来自目录调整前，调整后补验实际受影响范围。
- 共享契约 226/226、多语言 8/8、治理 57/57、workspace 检查通过；生成产物零漂移、离线快照/manifest 检查通过。`pnpm test:bundle-budgets` 三项通过，初始静态 JS 1,458,935 bytes（+2.27%）、gzip 382,449 bytes（+1.46%），原基线与 5% 限额不变；PDF 组件/资源映射约 142.05 kB、PDF.js 约 431.12 kB、Worker 约 1,264.34 kB，均按 PDF 预览延迟加载。
- 固定 `pdfjs-dist` 6.4.299（Apache-2.0），保留 CMap/Foxit/Liberation 完整独立许可，并随 Vue 发布到 `public/licenses/pdfjs/`；完整条款与包内原文一致，仅去除两份许可的六处行尾空白以通过仓库检查。只读复审及机械移位复审未发现阻断项。`pnpm audit:clients` 仍退出 1：既有未复核高危 `GHSA-qhr7-859c-m2p7`；直接镜像 audit 端点失败，官方 registry 原始扫描未发现新 PDF.js/可选 canvas 依赖路径 finding，均不等于全项目审计通过。
- 影响集 16 个文件、Integration 目标 `none`；没有 C#/SQL、公共 HTTP 或 Host.Api 可达变化，不重复业务双库或 Native 发布。本批保持 Build-verified；R07 总体、R12 真实双库页面/人工验收、RAG 云质量及容量验证继续待办，不推送。

### R08：增量更新、删除与索引重建（P2，依赖 R05–R07）

**修改：** R04/R05 新增目录、Ai 持久任务/事件入口；新增 `tests/Full.NET.IntegrationTests/Ai/AiKnowledgeIndexRecoveryTests.cs`，以双库测试方法覆盖相同场景。

- [ ] 以本系统文件替换/文档更新事件建立幂等增量任务；业务可靠事件沿事务 Outbox，缓存失效沿既有直接删除/Backplane，不把缓存通知写入 Outbox。
- [ ] 在 R04/R05 最小删除/恢复基础上扩展完整新代次校验后切换、旧代次批量清理、死信运维和索引对账/重建；撤权先由权威源生效，复用首版 tombstone 与重试机制。
- [ ] 验证乱序、重复、更新与删除竞争、模型换维度、同维度换模型、切换中崩溃与回退；记录恢复操作和保留策略。
- [ ] 后续格式切片增加 DOCX 并覆盖表格/位置与资源上限；扫描件 OCR、外部文件服务器连接器继续保持范围外，不在页面承诺支持。

#### R08a：旧代次保留与自动清理合批（2026-10-07，本地专项已验证）

- main 基线 `a7ce50edc98c91ce01fcda6549599ce04e9c8502`，快照 `ai-knowledge-retired-generation-r08a`。仅扩展 AI 自有清理 Worker/SQL，不新增 HTTP、跨模块表访问、收费派发或数据迁移。
- [x] 先建立 Worker 选项/扫描/分派 RED；在现有 cleanup Worker 增加独立默认关闭的旧代次清理开关及 24–8760 小时保留窗口（默认 168）。
- [x] 完整代次切换后，只扫描已完成且被当前已完成代次替换的旧任务，当前指针最近切换必须已过保留窗口；活跃、排队/运行、无后继及未到期任务不清理。游标/批量/取消预算复用现有恢复循环。
- [x] 独立可信范围及短事务复用 TXT/PDF 当前代次完整证明，锁定来源、模型、历史绑定、清单、向量、批次、收费凭据、当前指针与后继，再删除旧向量/清单；不改活动指针、原文件、任务/会话/批次/费用凭据。失败与取消回滚，重启/重复与扫描后指针变化安全；PDF 写事务直接等待提交/回滚，不经过可放弃迟到 IO 的读取边界。
- [x] 成对 SQL Server/MySQL 真实验证 Host/租户、范围拒绝、保留窗口、实际两代切换、失败恢复和原账本不变；AI Unit、影响集、SQL/相关架构、实际 AOT 与复审完成，本批在 main 交付。全量架构两项存量失败及 R08 增量事件、重建/对账、DOCX 继续独立待办。

本地执行证据（`artifacts/r08a/`，Windows .NET SDK 10.0.401；Linux SDK 10.0.400 / Runtime 10.0.11）：

- Worker RED 19 项中新增 4 项失败；成对后继向量损坏 RED 2/2 失败，证明仅信任完成态元数据会错误回收旧载荷。随后改为同事务完整证明，覆盖来源/模型/导入/清单/回执/向量损坏、扫描后来源/指针变化、异常/取消回滚、重启及原审计快照不变。
- `pnpm test:dotnet:unit -- --no-build --selection ai-module`：1993/1993，零失败/跳过（2m12.746s）。初轮未改动 PDF 错误洪泛夹具发生子进程清理超时，隔离 26/26 后完整 AI 重跑通过，未放宽清理预算。
- `pnpm test:integration:affected -- --snapshot ai-knowledge-retired-generation-r08a --phase merge`：真实发现 94 项，双 Provider 均存在。原轮 90 通过、4 项因新增夹具将完成态改为运行态时漏设租约而失败；原取消夹具的异常类型断言也已改为接受派生取消异常。成对补齐运行态租约、恢复终态清空租约后，独立输出程序集补跑 4/4（6m15.947s）。保留失败原轮，5334 个冻结输入仅该新 helper 变化，其唯三消费者对应 JIT 4 / Native 4；按失败与补跑 UID 精确对应核对，合并覆盖 94 项、零失败/跳过，不称原轮命令通过。
- `pnpm test:aot:analyzers`、`pnpm test:aot:worker:analyzers`：通过，零警告/错误。Linux API/Worker 实际 Native 发布及原有告警门禁通过，分别 17/15 条允许的依赖告警；不等于发布零告警。当前运行源码与 Linux 构建输入哈希相同，Worker 原轮和夹具补跑执行文件 SHA-256 相同。
- 实际 Linux API Native 核心 10/10（25m03.259s）；Native Worker 原轮 23 项中既有 19 项通过、仅新增四例发生相同夹具租约约束失败（38m17.021s）。修正后新增双库 TXT/PDF 四例 4/4（7m26.603s），按同一输入影响证明与失败 UID 精确核对，合并 23 项、零失败/跳过；不称原轮命令通过。每个新增用例覆盖 Host 与租户，并由真实原生进程完成成功回收，再校验当前载荷/指针、账本及重复/重启。PDF 准备阶段使用合成单页计划与真实持久化/结算/切换，不冒充实际 PDF 解析或云 Embedding 质量验收。独立测试目录初次缺依赖启动失败已保留，补齐后执行通过。
- SQL safety 5/5，治理 57/57，Integration 工具链 54/54；真实发现 Unit 5462、AI Unit 1993、Infrastructure 190、Full Integration 1202、Worker Native 23，分区覆盖与 UID 去重通过。发现数不冒充对应全量执行通过；矩阵及必需 Native 类型成对登记，未降低原门禁。
- 相关 Native AOT 架构 73/73，AI/SQL/租户边界 11/11；全量架构实际 231/233、2 失败、零跳过：既有 PDF 有界取消实现触发 `AsyncBoundaryTests`，既有 TXT/PDF 私有异步方法名触发 `NamingConventionTests`。五个 PDF 源文件与基线无差异，六个方法名在基线已存在；不新增笼统豁免或称全量架构通过。本批专项证明不提升全项目能力状态，R08 总体与 R12 真实页面/人工验收继续待办。
- 生产改动、租户精确例外、矩阵发现门禁及最后租约夹具修正均已只读复审，无重要未关闭问题。默认双开关显式启用、保留窗口及清理后的重建限制已同步运维文档；未进行容量实测。

#### R08b：当前版本对账、故障分类与恢复提示合批（2026-10-07～08，本地批量验收完成）

- main 基线 `1a51763d71a080ff625b96a8884825964d375205`，快照 `ai-knowledge-reconciliation-r08b`。新增精确来源版本的所有者只读对账端点，复用既有双库 SQL、任务/来源记录及 TXT/PDF 完整就绪证明，不新增迁移或收费派发。
- [x] TDD 建立缺失任务、过期绑定、来源/格式不可用、完成但证明损坏、运行租约到期/耗尽、删除、排队与就绪的稳定分类，恢复提示仅为 `none/enqueue/retry/rebuild/wait`。原冻结身份失配时返回独立重建提示，不能继续等待或沿失效会话重试；PDF 开关关闭不撤销已完整代次的读取。
- [x] 新增 `AiKnowledgeIndexReconciliationService`，在证明前后独立读取权威权限、范围、所有者、状态、来源与任务；最后一次权限等待之后再核对完整元数据，快照变化返回冲突。非所有者/错误租户不泄露数据，取消及事务边界失败传播。成对只读 SQL 复核导入与版本的内容摘要、大小及类型。当前 ready 仍由完整证明决定，原会话不可被对账接管。
- [x] 新增只读 HTTP 路径、源生成 DTO、API scoped 注册与 OpenAPI/客户端生成产物；双库 HTTP 覆盖 Host/租户、所有者限制、故障分类、字段与无写入/无收费。SDK 保留认证、语言、取消传递与响应守卫，OpenAPI 操作精确清单同步新增项。
- [x] 合批 AI Unit、相关架构、契约、影响集、AOT 分析/原生路径验证及只读复审，核对新鲜输入后 main 提交。重建执行与增量可靠事件继续待办，恢复提示不声称已执行重建或可免费重发。
- 有效行为 RED 覆盖分类缺失、权限等待后的任务变化、导入指纹损坏和原冻结身份失配；编译错误、容器 OOM 与文件占用失败分别保留，不计为行为 RED。只读复审发现的最后权限等待和导入指纹缺口已修复，最终无剩余重要问题。
- 最终 AI Unit 2038/2038；完整 Windows Unit 5506 通过、零失败、1 项 Linux FIFO 跳过，Linux 单项 1/1 通过。Windows 最低执行数不包含跳过项，原带门槛运行退出 9 的记录保留；最终完整运行退出 0，另以实际发现、TRX 和 Linux 补跑的精确 UID 核对完整 5507 项，矩阵门槛保持实际发现数。
- 正式 merge 影响集原轮 94/94（41m00.440s）；最终只修改新增 GET 使用的两项私有实现，对应双库 HTTP 两例重跑 2/2（3m42.813s），同时覆盖 Host/租户。冻结生产输入逐项证明其余路径未变，以精确 UID 合并未受影响 92 项与新两例，不宣称最终输入重新执行过整轮 94。
- 最终相关 Architecture 80/80（Native AOT 73、AI 边界 7）；完整 Architecture 原轮 231/233，保留既有 AsyncBoundary 与 NamingConvention 两项基线失败，不添加豁免。SQL 安全、治理与 Integration 工具链组合 116/116；命名 33/33、治理最终 57/57，分片实际发现 1202 项无遗漏/重复。
- SDK TypeScript 构建、230 项测试、OpenAPI 210 项、离线生成零漂移及相对基线的兼容检查通过。新增操作导致旧精确清单断言失败的记录保留，修正后完整契约检查通过。最终 API AOT 分析零警告/错误（2m39.44s）；初次共享输出构建竞争及 Windows DLL 占用失败保留，改为隔离分析输出并在构建完成后执行测试，不将旧程序集计为最终通过。
- 最终实际 Linux API Native 发布退出 0（30m50.681s），138723152 字节，17 项既有允许依赖警告通过官方门禁；完整原生核心 10/10、零失败/跳过（23m18.406s）。两库 AI 用例直接验证本批对账、当前 PDF 完整证明、原冻结身份失配及 Tenant 非空版本读取；PDF 使用受控已结算代次，不冒充真实云模型或完整解析质量验收。本批未重新发布/运行 Worker，不扩大 Worker 能力结论。
- `python -X utf8 artifacts/r08b/verify-acceptance.py` 核对 5558 项最终源码/契约/测试/矩阵输入未变、Unit 5507（Windows 5506 与 Linux 1 的精确并集）、AI Unit 2038、双库影响集 94、Native 10 的精确 UID/FQN及零用例失败；验证 Unit/Integration 的 AI DLL 一致，Linux 关键源码及实际执行二进制 SHA 与最终发布一致。原始失败记录、发现、TRX、发布清单和验收报告保留在忽略目录 `artifacts/r08b/`，不伪造单轮全绿报告。
- Windows x64、i7-12700H、约 64 GB、SDK 10.0.401；Linux SDK 10.0.400、Docker 29.6.2、SQL Server 2022-CU14 / MySQL 8.0。JIT 测试 2 个处理器、GC 堆 2 GiB、容器不复用；原生发布 4 CPU/12 GiB、GC 堆 6 GiB，原生测试 4 CPU/8 GiB、GC 堆 2 GiB，实际 MSTest 为 2 个 Worker。整批 main 提交；后续继续独立重建执行、增量可靠事件与 DOCX，完整 RAG 及 R12 页面人工验收状态不变，容量仍 Capacity-not-verified。

#### R08c：Vue 索引对账、恢复说明与显式恢复操作合批（2026-10-08）

- main 基线 `bf10c21b26c3e84293631e4bb138cbf43e838185`，任务快照 `ai-knowledge-reconciliation-ui-r08c`。复用 R08b 生成操作，范围为 Vue 适配、索引弹窗、双语文案及回归测试，不修改服务端、SQL 或生成契约。
- [x] 将所有者只读对账接入版本索引弹窗，展示稳定原因、恢复建议及文档/知识库快照；对账显式触发，所有权继续由服务端校验。
- [x] 只有已知且一致的建议可显式入队/重试，并保留精确权限、版本/任务绑定、原会话限制及不明结果先刷新；等待/重建不产生请求。
- [x] 验证拒绝、损坏响应、权限/版本切换与迟到结果，合批客户端/契约/构建和受影响 Mock 浏览器验证后 main 提交。保持 Build-verified，真实栈页面及独立重建执行仍待后续验收。
- 新行为 RED：适配及对账入口缺失使聚焦测试 11 项失败，17 项既有行为通过；实现后聚焦 50/50，覆盖 14 个原因/动作组合、无权操作、错版本/任务/快照、未知动作、不明结果、撤权及迟到响应。原始日志保留在 `artifacts/r08c-*.log`。
- 最终 `pnpm --filter @fullnet/admin test -- --maxWorkers=4` 全量 268 个文件、1069/1069，零失败/跳过，退出 0（361.25s）。初轮默认高并发与其他构建同场时，既有壳层强制改密用例超时，其余 1068 项通过；该文件单独复跑 4/4，随后限制 4 个 Worker 完整重跑通过，不改超时、降低发现数或删测试。Windows x64、Node 24、pnpm 10.26.0，复用本地 Edge 验证 UI，未调用真实云模型或扩大容量结论。
- `pnpm --filter @fullnet/admin build` 通过 TypeScript 与 Vite；`pnpm --filter @fullnet/client-contracts test` 230/230，`pnpm test:openapi` 210/210，生成器 `--check` 零漂移；`pnpm --filter @fullnet/admin-i18n test` 8/8、`pnpm test:localization` 7/7、`pnpm test:governance` 57/57，包体预算通过，许可证清单成功导出且没有新增依赖。
- `pnpm --filter @fullnet/admin-parity-e2e exec playwright test ai-knowledge-index-reconciliation.spec.mjs --project=vue-admin --workers=1` 3/3（真实 Vue/Edge + Mock HTTP），覆盖重建仅提示、重试冲突及撤权后清除、显式入队接受和无权入口；不冒充真实服务端/双库页面验收。初轮错误夹具缺少必需机器码导致统一错误回退，修正夹具后通过，不修改共享错误处理。
- `pnpm test:integration:affected:plan -- --snapshot ai-knowledge-reconciliation-ui-r08c --phase slice` 确认纯客户端影响集为 none，本批不重复服务端、双库或 Native AOT 重型门禁。`pnpm audit:clients` 退出 1，既有未审核 High `GHSA-qhr7-859c-m2p7` 仍待处理，不计为审计通过或新增依赖例外；页面能力保持 Build-verified。

#### R08d：DOCX 独立解析工具、表格位置与资源约束合批（2026-10-08）

- main 基线 `45a4796bf2025ee1586a38ded655f400a6c93588`，快照 `ai-docx-parser-r08d`。首批 DOCX 是独立可执行工具，复用 PDF 的标准输入/输出与固定错误模式，后续 Worker 才消费其静态协议；不将解析器注册进 API，不修改数据或放开上传格式。
- [x] 在 `src/Tools/Full.NET.Ai.DocxParser/` 增加单一 CLI 与有界 ZIP/XML 解析，验证 OPC 内容类型和唯一内部主文档绑定，只读取正文普通段落/基本表格；不解析宏、嵌入对象、字段、修订、嵌套/合并表格等未支持结构，不访问外链或解压到文件系统。
- [x] 输出原始字节 SHA256、大小、固定解析版本、提取正文及 UTF-16 区间，保留表/行/列、单元格首段与段数、表头标志；不声称 DOCX 拥有可稳定计算的页码，不执行文档指令。
- [x] TDD 覆盖真实 ZIP/XML 成功、空文档、错包、歧义主文档、DTD/深度/节点、解压放大、字符/结构预算、异常、取消与流所有权；失败无部分解析结果。固定输入 5 MiB，逐条实际解压上限 8 MiB、合计 16 MiB、最多 256 项，XML 深度/节点与输出独立有界。
- [x] 纳入 Solution/Unit，更新实际测试矩阵，合批 AI/完整 Unit、相关架构/治理及工具真实进程和 Linux Native AOT 发布运行。安全解析交付前只读复审；仅关闭本工具专项，不将 API/Worker DOCX 上传、索引、检索标为完成。
- 有效 RED 保留于 `artifacts/r08d/`：最初两个成功协议用例因缺失能力失败；复审追加属性修订/隐藏与逻辑空格、碎片属性、单元格修订和跨部件继承样式拒绝。大小写别名样式先复现 1 失败/1 通过，修复内容类型查找后最终专项 69/69。只读复审确认此前问题全部关闭，无剩余 P1/P2；未知内容继续拒绝，不增加 Word 排版或全格式支持承诺。
- 最终 Unit 构建零警告/错误（12s 18ms），完整 AI 2107/2107（2m 20s 329ms），实际发现 Unit 5576、AI 2107；矩阵同步实际数量。全量 Architecture 234 项中 232 通过、2 项存量失败（4m 36s 576ms），与 R08b 完全相同：PDF 异步边界 11 项违规与六个既有私有异步方法命名。新工具独立 BCL/无业务引用边界通过，不将全量架构报告为全绿。
- 完整 Windows Unit 5575 通过、1 项 Linux FIFO 跳过（3m 06s 640ms），退出 0。按测试 UID/FQN 对照实际发现集无遗漏；复用 R08b 的 Linux FIFO 通过 TRX，已核对相应测试、BuildingBlocks 和根依赖配置自基线未变，不把 Windows 跳过记录为通过。`artifacts/r08d-verify.py` 核对 4690 项冻结源码/测试/矩阵输入、专项/AI/完整 Unit 精确集合、同两项基线架构失败和原生 SHA；命名 33/33、治理 57/57、工具链独立 54/54。
- 最终源码 Linux Native AOT 发布成功、无警告，二进制 7893704 字节、SHA-256 `9abe330242d2acbabcc6ba444a9a2919c01c2508bbb7cf961c8655fc6db91ff8`。真实隔离容器 20/20，覆盖协议/UTF-16/表格、正常样式/外链可见文本、拒绝宏/修订/合并/大小写别名隐藏/DTD、解压与 XML/正文预算，以及输入持续阻塞的 30 秒强制终止。原生脚本首轮错误计算中文加 emoji 的 UTF-16 长度，修正断言后完整重跑；保留首次失败，不记作产品行为 RED。
- `pnpm test:integration:affected -- --snapshot ai-docx-parser-r08d --phase merge` 退出 0；唯一矩阵保持官方 `integration-matrix, smoke` 选择。Integration 构建零警告/错误（44s 86ms），工具链前置 65/65、治理 57/57，分片实际 1202 项无遗漏/重复；Smoke 双库 8/8、零失败/跳过（5m 22s 846ms），精确发现 UID 与原始 TRX 对照通过。Linux 锁定还原 `dotnet restore ... -r linux-x64 -p:PublishAot=true --locked-mode` 使用本工具 Linux 锁文件，退出 0。Windows x64、i7-12700H、约 64 GB、SDK 10.0.401；Linux SDK 10.0.400，测试进程 2 个处理器、GC 堆 2 GiB，数据库容器不复用。源文件、失败/成功日志、发现、TRX、原生及冻结验收清单保存于忽略目录 `artifacts/r08d/` 和 `artifacts/r08d-*`。
- 实际回归命令：Release `dotnet build` Unit；直接执行新鲜 Unit DLL，专项过滤 `FullyQualifiedName~AiDocxParser`、完整 AI 过滤 `FullyQualifiedName~Full.NET.UnitTests.Ai.` 及不带过滤的完整 Unit，分别保存 TRX；新鲜 Architecture DLL 不带过滤执行 234 项；`pnpm test:naming`、`pnpm test:governance`、`pnpm test:integration:tooling`，以及正式 merge 影响集。收尾再核对冻结输入和任务差异，在 main 提交整批。工具为本地 Build-verified / Linux Native-published，Worker/上传/索引 DOCX 接入继续待办，完整 RAG 状态和 Capacity-not-verified 不变。

#### R08e：DOCX 输出验收、模型分块与完整计划合批（2026-10-08）

- main 基线 `e720d80e6d07d7d438d43cd6d543a794227d96a8`，快照 `ai-docx-planning-r08e`。沿 R08d 独立工具接入 AI 内部消费算法；实际消费者为串联工具与规划的离线专项。后续 Worker 付费执行、持久化、激活和上传开放一起接入，本批不将它们标为完成。
- [x] `AiKnowledgeDocxOutputReader` 有界读取固定协议，复核可信原始摘要/大小、严格字段、Unicode、连续段落/表行列/单元格与 UTF-16 区间，拒绝任何部分或歧义结果。
- [x] `AiKnowledgeDocxModelChunker` 使用现有获批模型词表，逻辑段落/单元格内重叠，保持全局位置和完整文档坐标；块数、字符与 Token 全局累计，不解释文档指令或派发模型。
- [x] `AiKnowledgeIndexDocxPlan` 冻结全文、坐标和精确模型身份，重算真实 Token 与来源切片，生成有界完整批次和稳定摘要，拒绝伪造/遗漏/变更后的输入。
- [x] 先成功协议/串联用例有效 RED，再覆盖拒绝、预算、取消、集合变更与位置漂移；统一 AI/完整 Unit、架构、正式双库影响集和真实原生 DOCX 输出串联，复审后在 main 提交。API/Worker 原生可达闭包、HTTP/SQL/依赖及上传策略不扩展。

**实现与已完成验证：**

- 本批 5 个模块内类型、2 个 Unit 测试文件与 1 个静态闭包架构回归，没有新增宿主注册、SQL、公共 HTTP 契约或依赖。固定协议拒绝重复/遗漏/未知字段、无效 Unicode、来源不匹配、读取超限与不连续结构；冻结集合只读取一次数量并按固定范围复制。规划复核每个非空逻辑块的完整覆盖、重叠推进、代理对边界、原文切片和真实 Token，再将来源、坐标、模型/租户版本及所有预算纳入摘要。
- 成功协议和串联用例先在已构建占位实现上有效 RED（2 失败）；34 项边界专项有效 RED（10 失败）。只读复审发现的集合数量变更、UTF-16 半代理对、单元格虚报段落分别补充有效 RED 并修复，复审无剩余 P1/P2。最终 Release Unit 构建退出 0，零警告/错误（17s 54ms）；新鲜 DLL 专项 37/37，通过且无跳过（1s 506ms）。完整 Unit 5613 项，5612 通过、1 项 Linux FIFO 在 Windows 跳过、零失败（4m 26s 807ms）；完整运行包含 AI 2144 项，按发现 UID 与 TRX 核对，不再重复运行同一 AI 集合。
- 使用 R08d 未改动原生工具镜像 `sha256:baed15a1a0eb1f902135ac3eb0fc52f9c84c7d4d77034f22888e5982637eef57`，重新核对二进制 SHA-256 `9abe330242d2acbabcc6ba444a9a2919c01c2508bbb7cf961c8655fc6db91ff8`，以无网络、只读、非特权和资源限制执行真实 OOXML。工具正常退出且无 stderr；实际原始文件和 JSON 串联至本批 JIT 验收、分块、计划专项 1/1 通过（1s 497ms）。这只证明原生工具到 JIT 算法的本地链路，不代表 Worker 已接入或新算法完成 Native 发布。
- Architecture Release 构建退出 0，零警告/错误（16s 16ms）；完整 235 项，233 通过、零跳过（7m 14s 965ms），仅原有 `Production_sources_do_not_block_tasks_or_sleep_or_offload_to_thread_pool` 和 `CSharp_symbols_follow_project_conventions` 两项失败；按 R08d 原始 TRX 对照，未新增失败。新静态闭包回归通过。命名脚本 33/33、治理 57/57 均退出 0。
- Windows 跳过的 FIFO 用例复用 `artifacts/r08b/linux-fifo/linux-fifo.trx` 同一 UID 的真实 Linux 通过结果；自 `bf10c21b26c3e84293631e4bb138cbf43e838185` 起，其测试、BuildingBlocks 与根构建/依赖配置均无变化。Unit 与 Integration 使用的 AI 模块 DLL SHA-256 一致，为 `9f60e30ac934aa908a07088e79db579e3382d7961d1c576d56cd297df8f8b7da`。
- 实际命令包括 `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --report-trx --results-directory artifacts/r08e/unit`，以及 `dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 235 --report-trx --results-directory artifacts/r08e/architecture`；均在相应 Release 构建退出 0 后执行。完整 Unit 不带最低通过数参数，以精确发现 UID、TRX 与未变 Linux 单项证据合并验收，不能把 Windows 跳过报告为通过。命名与治理分别执行 `pnpm test:naming`、`pnpm test:governance`。
- 正式 `pnpm test:integration:affected -- --snapshot ai-docx-planning-r08e --phase merge` 退出 0，保留唯一矩阵的 `Ai, integration-matrix, smoke` 选择。Integration 构建零警告/错误，工具链前置 65/65、治理 57/57，分片 1202 项无遗漏/重复；原定 AI 与 Smoke 合并 94/94，零失败/跳过（41m 46s 301ms）。发现 UID 与原始 TRX 精确对照通过；日志字段的 TRX 流式截断不影响 94 项结果及结果状态，原运行日志保留。收尾验收脚本退出 0，逐项复核 4697 个冻结输入、37 项专项、完整 Unit/AI、存量架构失败、94 项双库和真实原生串联证据。
- 原始 RED/Green、发现 UID、TRX、原生输入/输出和复核清单保存于忽略目录 `artifacts/r08e/` 与 `artifacts/r08e-*`。冻结 4697 项代码、测试、脚本、项目和矩阵输入，验收后再核对未变化。测试环境沿用 Windows x64、i7-12700H、约 64 GB、SDK 10.0.401；测试进程 2 个处理器、GC 堆 2 GiB，双库容器不复用。Worker 付费执行、双库持久化、激活及 DOCX 上传仍待办，完整 RAG 与 Capacity-not-verified 状态不变。

#### R08f：DOCX 完整清单、向量保存与恢复证明合批（2026-10-08）

- main 基线 `152273325f5e33064eb12d489bf3cb3193ed7c52`，快照 `ai-docx-persistence-r08f`。沿 R08e 的完整计划增加实际双库保存消费者，不复制 PDF 页码，也不把保存证明解释为来源授权、真实模型收费或 ready。
- [x] 新增 `Knowledge/AiKnowledgeIndexDocxManifestCodec.cs` 与 `AiKnowledgeIndexDocxVectorCodec.cs`：完整清单保存全文、空白块、逻辑坐标和精确模型身份，重建原计划并核对摘要；向量批次绑定指定完整计划与原块位置，拒绝截断、尾部、未知格式、坏 Unicode、伪造 Token、错批次、非有限或零向量，按实际字节预算限界。
- [x] 新增 DOCX 专属清单/向量 Store 与固定成对 SQL：原会话五项精确权限、当前来源、模型批准、租约和可信租户每次权威复核；短事务写后取消/过期回滚，唯一竞争只恢复同原载荷，向量必须绑定已结算账本和完整清单，整代证明不得漏批或重复 OperationId。
- [x] 双库 255 迁移新增 `fn_ai_knowledge_docx_manifest`，只扩展向量格式约束；保留历史文本/PDF数据，探测式创建、先校验启用新守卫再移除旧守卫，记录旧结构和每个半完成步骤重放验证。不得自动收缩已有 DOCX 格式或删除载荷。
- [x] 先建立可失败的清单/向量/Store 验证，再运行专项、AI/完整 Unit、架构、命名/SQL治理与正式双库影响集；真实数据库验证 Unit 无法证明的原字节、账本关联、事务和迁移恢复。更新实际矩阵并只读复审，最后在 main 合批提交。
- 有效 RED 覆盖能力缺失的清单/向量编码、Store 成功路径、缺少迁移、原始摘要大小写伪造，以及删除后只剩 DOCX 清单的双库清理遗漏；不把编译失败、无效 fixture 或容器连接问题计为 RED。补齐删除和退役扫描、相同任务锁下的 DOCX 清单删除与状态派生数据检测，保留任务、会话及账本。两个协议 GUID 使用网络字节序，固定原字节黄金用例通过。
- 本批新增 DOCX 专项 162/162；完整 Unit 5774 通过、1 项 Windows FIFO 跳过，AI 精确子集 2306/2306。完整 Architecture 235 项中 233 通过，剩余同两项存量失败：PDF 异步边界 11 项违规及六个既有私有异步方法命名；新增 GUID 转换问题已修正。命名 33/33、SQL 安全 5/5、治理 57/57、工具链 54/54。正式 `pnpm test:integration:affected -- --snapshot ai-docx-persistence-r08f --phase merge` 合并 Ai、migration-255 与 Smoke，100/100、无跳过（44m 44s 323ms），发现 UID 与 TRX 完整一致；全分片 1208 项无遗漏或重复。
- 只读复审及 GUID 修正复审未发现关键/重要问题；最大文档恢复成本仍须在启用前实测，完整代次证明当前重复重建清单与词表计数，不宣称吞吐或时限保证。API/Worker AOT 分析均零警告/错误，分别 3m 22s 520ms 与 2m 30s 100ms；默认 JIT 还原图恢复成功。
- 最终代码按现有 publish contract 在 Linux SDK 10.0.400 实际发布：API 17 项、Worker 15 项告警均在既有精确清单内，未增加 suppression；API 原生 SHA256 `4a4be5fc14bbf4668eac1abdf8fecea28f19ef3a51e92f3a826c70b37794c6b9`，Worker `0c5a90586294a42bacd1f0a8c09b975f8d123c2a25fa37dfc66b3d568a0590b7`。完整矩阵必要原生集合 33 项按宿主分组取得通过证据：API TRX 10/10（23m 40s 630ms）；Worker 整组触及 45 分钟总时限，退出 3，原始摘要记录 21 成功、0 失败、0 跳过（46m 08s 634ms），报告器保留逐结果流水而未输出 TRX。逐帧恢复流水的 21 个唯一 UID，与摘要核对；未完成的两个 SQL Server 文件清理/引用对账用例单独补跑 2/2（4m 13s 720ms）。21 项流水加 2 项补跑 TRX 精确覆盖 Worker 23 项，三组并集与矩阵 33 个 UID 相同、无遗漏；原始整组超时不报告为整组通过，补跑与原始 Worker 的实际二进制摘要一致。
- 真实不可变 DOCX 工具输出串联到本批 JIT 清单和向量协议，1/1（1s 989ms）；工具镜像 ID 和二进制 SHA 沿 R08d，原始文件摘要、坐标、全量空白及向量字节均复核。Linux FIFO 继续复用 R08b 通过结果，已核对对应测试、运行时代码及根依赖配置自基线未变；本批 BuildingBlocks 仅新增 255 SQL 资源，不将 Windows 跳过计为通过。
- 实际入口为新鲜 Release Unit/Architecture DLL、上述正式 merge 影响集、`pnpm test:naming`、`pnpm test:sql-safety`、`pnpm test:governance`、`pnpm test:integration:tooling`、API/Worker AOT analyzer，以及基于现有 Native publish contract 的本地 helper 和 Linux 外部进程测试；原始命令、RED/Green、发现集、TRX、结果流水及 SHA 保存于忽略目录 `artifacts/r08f/` 与 `artifacts/r08f-*`。`r08f-verify.py` 验证冻结 5349 项输入、精确结果集合、同两项架构基线失败、流水完整帧及二进制一致性，退出 0。
- Windows x64、i7-12700H、约 64 GB、SDK 10.0.401，Docker 29.6.2；SQL Server 2022-CU14 / MySQL 8.0，数据库容器不复用。JIT 测试 2 个处理器、GC 堆 4 GiB；Linux 原生发布 4 CPU/12 GiB、GC 堆 6 GiB，测试 2 CPU/8 GiB、GC 堆 4 GiB。整批 main 提交；后续合批接入受信解析启动、Worker 付费执行、完整恢复/激活及上传开放，并先收敛轻检查和复审再固定代码运行重型验收。
- API/Worker 不注册本批格式消费者，DOCX 上传及 paid dispatch/激活继续按完整链路接入；原生工具至本批 JIT 持久化链路以独立实际输入验证，不声称 Host/Worker DOCX Native 发布或完整 RAG 已完成。

### R09：Prompt 发布、反馈与检索优化（P2，依赖 R02/R06/R08）

**新增责任位置：** `src/Modules/Full.NET.Modules.Ai/Features/ManagePromptVersions/`、`Features/ManageAnswerFeedback/`；扩展 R02 评估语料及 R07 Vue 页面。新增持久化成对迁移，Prompt 版本不混入通用 Settings。

- [ ] 实现不可变 Prompt 版本，绑定模型策略、语言、输入输出 Schema、工具允许集与检索策略；Run 固化版本，支持停用与回滚。
- [ ] 反馈绑定真实回答/引用、可信用户范围与权限；反馈脱敏审核后才能进入评估集，禁止直接信任反馈指令。
- [ ] 在同一评估集比较基础检索、关键词/向量融合和重排，记录质量、用量与耗时；仅启用有证据收益的策略，失败保留旧策略。

#### R09a：真实回答反馈、版本冲突及撤回合批（2026-10-09，本批验收完成）

- 基线 main `098ba9e5cfad74feea1694fa65749d61409dd341`，任务快照 `ai-answer-feedback-r09a`；受保护文件在快照哈希前排除，其他任务改动保留。
- [x] `ManageAnswerFeedback` 同批交付本人反馈 GET/PUT/DELETE 及仅供本人清除的 GET `/feedback/state`；固定评价/原因和最多八个真实引用，不接收正文、任意 JSON 指令或可信评审声明。反馈始终 `pending_review`，不得自动进入评估集或模型输入；Prompt 发布与审核导出仍独立待实施。
- [x] 双库 `256_AiKnowledgeAnswerFeedback.sql` 保存应用端 UUID v7、可信租户/用户/消息、完整 lineage 摘要、引用标识、版本及撤回状态。会话锁串行化首次创建，版本控制修改/撤回；原消息、会话和反馈在同一 AI 本地事务复核，外部当前授权/来源证明在事务外完成。反馈不保存答案正文或原文，不产生模型调用/新收费。
- [x] API 使用 `ai.knowledge_answer_feedback.read/submit/withdraw` 精确权限和权威会话检查；提交及读取复用当前完整历史依赖证明，其他用户、其他租户、普通/未完成消息及伪造引用均拒绝。撤回仍须当前精确权限及本人会话，来源撤权后允许清除本人反馈；`state` 仅按 Chat.Read+Feedback.Withdraw 返回本人消息/状态/版本，不返回评价或引用。角色树登记反馈读取、提交和撤回动作，Chat.Read 扩展到 Host|Tenant，不扩大其他聊天写权限；失败/取消/版本冲突回滚。
- [x] 源生成 DTO、Dapper AOT 物化、运行时 OpenAPI 与共享客户端、Vue 反馈入口集中接入；UI 保留输入并提示 409/结果未知先刷新，切换用户/租户/会话、卸载及撤权时中止并丢弃晚到响应。
- [x] 先建立行为失败证据，统一运行直接回归、双库实际 API/迁移、完整 AI Unit、受影响架构/AOT 和原生路径。按 R-20261008-concentrated-acceptance，独立生成应用留到 AI 模块建设完成后集中验收，本批不重复启动。只读安全审查后在 main 提交本批，记录既有失败及未验证边界，不提升完整 RAG/容量状态。

- 有效失败证据覆盖固定评价与引用校验、权限范围及 UI 未知结果恢复；权限拆分后 UI 专项 33/33，共享客户端 239/239，Vue 生产构建通过。Playwright 本批浏览器流程 3/3 使用受控 API 响应，真实 HTTP 与数据库行为由以下双库集成证明。
- `node artifacts/r09a/run-final-fast.mjs`：新鲜 Release AI Unit 2565/2565；Architecture 235 项中 233 通过，同两项既有失败为六个私有异步方法命名及 PDF 异步边界 11 项违规。API AOT analyzer 零警告/错误，默认 JIT 还原及 Integration Release 构建通过；不把存量架构失败报告为通过。
- `node artifacts/r09a/run-paired.mjs` 按 merge 影响集合并 Ai、迁移 256、Smoke 与双库实际运行时 OpenAPI，共 100 个唯一 UID。首轮 94 通过、6 失败，均为新夹具把统一 Validation 的标准 400 错写为 422；修正断言和 PUT/DELETE 响应元数据后，`node artifacts/r09a/run-retry.mjs` 补跑六项及变更后的双库 OpenAPI 8/8，无跳过。`verify-formal-union.py` 核对全部 UID/FQN、100 项通过并集与仅三项输入变化；原始失败记录保留，不宣称首轮整组通过。双库恢复覆盖未记账重放、消息唯一约束及外键恢复，真实 API 覆盖 TXT/PDF/DOCX、并发/幂等、租户/用户/权限、来源撤权与清除。
- 最终运行时契约双库重新导出并归一化，602 个操作；共享客户端生成及类型检查通过。OpenAPI 静态 210/210、命名 33/33、SQL 安全 5/5、测试工具链 73/73、治理 59/59；集成发现 1271 项（完整 Integration 1210、Native 61）及分片检查无遗漏/重复。并未执行全部 1210 项 Integration 或 6034 项 Unit。
- `node artifacts/r09a/run-native.mjs` 按现有发布契约实际发布 Linux x64 API，17 项告警均在既有精确清单内，未增加 suppression；Linux Integration 构建零警告/错误。原生启动脚本修改进度输出时被写入 CRLF，shell 首行失败、未执行测试；保留失败日志，核对并修正为 LF 后，`node artifacts/r09a/run-native-focus.mjs` 复用相同二进制及测试 DLL，双库关键 HTTP 与 AI PDF/DOCX 链路 4/4，无跳过；覆盖反馈版本、伪造引用拒绝及来源撤权后清除。Worker 可达路径本批未改变，不重复发布 Worker。最终 `freeze.mjs verify` 核对 5424 项源码/测试/契约输入未变，`verify-results.py` 核对全部结果身份、计数和原生二进制摘要；原始命令、日志、TRX、输入摘要及验收清单保存在忽略目录 `artifacts/r09a/`。
- Windows x64、i7-12700H、约 64 GB、SDK 10.0.401；SQL Server 2022-CU14 / MySQL 8.0 使用本批独立容器。Linux API 发布 4 CPU/12 GiB，原生测试 2 CPU/8 GiB；遵守工作区及机器重型测试锁。构建使用 `NuGetAudit=false`，未执行在线漏洞审计。只读安全复审及权限/SQL 修正复审未发现关键或重要问题；R09 的 Prompt 发布、反馈审核/脱敏导出与检索质量对比仍未完成，完整 RAG、云端模型质量及容量保持未验证。按集中验收规则，独立生成应用留到 AI 模块建设全部完成后统一执行。
- 本批原生 API SHA256 `595c3f091459d3ca8431b2c779d2cda0c6bb486a36ef23f85b469ab9eeedae68`；发布使用上述基线与冻结工作区输入，冻结后只追加验收文档，提交内源码与受测输入一致；不将二进制内基线版本冒充最终 Git 提交。

#### R09b：纯文本 Prompt 不可变版本、发布治理与真实 Run 绑定合批（2026-10-09，本批范围回归完成，保留架构基线失败）

沿用 R09 批次，在 main 基线 `7cba28f3f62e133b4868eddae33b12eacb832ffd` 上执行；任务快照 `ai-prompt-publication-r09b`。本批集中实现目录/版本读取、幂等创建、追加草稿、显式发布、回滚、停用、Run 精确绑定和 Vue 页面接入。模型策略固定修订，格式 v1 仅支持纯文本、规范语言、一个 `{{input}}`、固定输入输出 Schema、零工具和无检索；不放开工具循环、Workflow 或任意 Schema。

- [x] 不可变草稿及乐观修订；创建不自动发布，追加不改变发布指针。
- [x] 发布仅接受草稿，回滚只接受曾发布版本，精确权限独立；停用增加撤销代次。
- [x] Run 固化版本 UUID/序号、正文摘要、原始输入及代次；发布切换保留旧版本，停用后的旧 Run 不因重新发布复活。
- [x] Worker 在客户端建立前及执行器调用前复核授权、作用域、正文完整性和模型修订；失败不调用模型并结算预算。
- [x] 双库 257、恢复重放、约束/注释/历史保留、租户隔离、精确权限与并发幂等回归。
- [x] Vue 草稿及发布操作确认、Run 绑定、上下文失效及晚到响应防护；生成客户端和双库 OpenAPI 同批同步。
- [x] 固定源码后集中运行本地影响集、API/Worker Native AOT 及浏览器验证，核对实际测试身份后提交。

Token 值只限制现有预算预留；实际提供程序输入分词/输出硬限仍待建设。完整工作流模板、反馈审核与脱敏导出、检索策略对比继续保留为 R09 未完成项。运维协议见 [纯文本 Prompt 版本](../../operations/ai-prompts.md)，原始命令和 RED/Green 证据在 `artifacts/r09b/`，不将首轮小范围回归当成最终验收。

本批本地验收记录：

- 最终暂存检查清理 9 个新文件末尾多余空行；逐个验证受测文件摘要及修改前后字节前缀完全相同，差异仅 EOF 换行，依据见 `format-transition.json`。业务逻辑、SQL、契约与测试断言未改变，复用已通过的运行证据，仅重做格式、源码输入与暂存范围检查。
- 首轮真实原生 6 项中 4 通过、2 失败（退出 2、零跳过），精确定位 API `find_owned` 与 Worker `select_lease` 缺少 `AiAgentRunRecord` 静态物化。补齐完整/租约投影及已可达检查点读取和同步注册；原失败 TRX/日志/产物保存在 `native-before-row-fix/`，不计为通过。变更仅进入原生闭包，既有 JIT/迁移证据按未受影响范围复用，源码差异见 `row-production-transition.json`。
- `node artifacts/r09b/run-zero-green.mjs`：双库固定竞争及预算 4/4、零失败/跳过，验证有价/无价、混合绑定两个方向、同载荷重放、赢家保护、用户取消仍保守计量、租户 Token 只退一次及请求次数不退款。随后 `node artifacts/r09b/run-row-green.mjs`：最终 AI Unit 2601/2601、零失败/跳过，实际发现 Unit 6070；含 10 项两库静态物化回归。API/Worker AOT analyzer 构建均零警告/错误。
- 正式影响集实际发现 100 个唯一测试。原 `node artifacts/r09b/run-paired.mjs` 首轮 98 成功、2 失败、零跳过；持久快照沿源生成上下文使用 PascalCase，原 HTTP camelCase 夹具读取错误。只修正夹具后 `run-paired-retry.mjs` 精确补跑 2/2；保留原退出 2 和 TRX，并核对通过并集为 100。随后修复未生成 Run 的落败请求按 unknown 计量导致额度保留的问题，先建立实际 RED 2/2 失败再改为已知 0/0；原生链路改为实际 API 登录/创建。该修复仅影响落败结算，最终补跑前述双库 4 项，其余 96 项按未受影响范围复用，UID 与发现集一致；变化及依据见 `zero-production-transition.json`。
- `node artifacts/r09b/run-native.mjs`：最终 Linux API/Worker 两次实际发布均退出 0，现有告警门禁分别为 17/15 项；Linux Integration 夹具按本次未影响 JIT 的源码差异复用上轮退出 0 的构建；记录 DLL SHA256，并与实际探针运行文件相符，依据见 native-build-reuse.json。原生实际发现并运行 6/6、零失败/跳过，覆盖双库关键 HTTP、AI/Prompt 元数据与发布生命周期，以及真实原生 API 创建精确绑定、真实原生 Worker 拒绝已撤销旧代次并成功执行同版本当前代次；每库唯一提供程序请求严格为 `Native current epoch`，两个进程正常关闭。API 140164288 字节、SHA256 `fa7f32ffb534d849f5c7703174fcbcd1cbbe221e9ddbe80fac86fe6749d54ae6`；Worker 90430744 字节、SHA256 `a0ad1bfd07ea02539083b80b79097bad4e716a5548a2f3c01479ec6a70152440`，均与实际运行文件一致。旧候选在两次发布及构建完成后主动停止尚未完成的原生测试（退出 143），完整记录保存在 `native-before-zero/`，不计为通过。
- `finish-light.mjs`、`run-tooling.mjs` 等本批脚本：Vue 组件/页面 12/12、共享客户端 239/239、浏览器模拟 API 流程 2/2，类型检查和生产构建通过；OpenAPI 静态 210/210、命名 33/33、SQL 安全 5/5。实际双库运行时 OpenAPI 与冻结快照相符，共 609 个操作；生成客户端已同步。更新矩阵后工具链 73/73，Integration 分片实际 1212 项无遗漏或重复。
- 最终 `run-row-green.mjs` 重跑 Architecture 235 项中 233 通过、2 项既有失败，失败消息与 R09a 基线逐项完全一致，保留非零退出码。治理选择 59/59；会读取受保护文档的 Markdown 扫描未执行，替换为先排除两条受保护路径、再读取的 385 文件编码检查，不报告被排除文件已扫描。
- `python artifacts/r09b/collect-acceptance.py` 核对 TRX/UID、源码冻结、原失败与补跑、原生文件摘要及其他任务未修改；摘要 `artifacts/r09b/acceptance.json`。测试范围只包含本批能力；完整工作流模板、反馈审核/脱敏导出、检索对比和实际 Token 硬限仍属 R09 待办。旧 API/Worker 不理解新绑定；必须关闭所有 API 的新 Run 准入并等待已进入的创建请求结束，优雅停止旧 Worker，升级全部 API/Worker 并核对工件后再开放。旧消费者回退前须保持当前 Worker 将绑定 Run 处理至终态，等待在途调用退出并优雅停止 Worker；取消状态不证明外部调用已停止，未知调用须对账。准入关闭不阻止消费队列。未提供自动能力协商，不宣称新旧混布/回退已验证，具体门禁见运维说明。独立生成应用按 AI 模块全部完成后集中验收；在线 NuGet 审计未执行（本地命令使用 `NuGetAudit=false`），真实云模型质量及容量保持未验证、`Capacity-not-verified`。
- 环境：Windows x64、i7-12700H、约 64 GB、SDK 10.0.401；Linux Docker SDK 10.0.400/运行时 10.0.11，SQL Server 2022-CU14/MySQL 8.0。双库不复用容器；最终原生发布 8 CPU/12 GiB，构建及原生测试 2 CPU/8 GiB、GC 堆 2 GiB。整批 main 提交，保护其他任务改动及受保护文件。

#### R09c：单次文本 Run 输出请求上限（2026-10-09，集中交付）

- [x] 单次文本 Run 在真实模型调用边界使用冻结的 `OutputTokenLimit`，绑定及未绑定 Prompt 均发送 `min(冻结预算, 4096)`；保持已存在的更低调用方上限。
- [x] OpenAI 兼容与 Azure OpenAI 发送 `max_tokens`，Ollama 发送 `options.num_predict`；流式及非流式调用均支持显式 1–4096 上限，无选项或空选项沿用 4096。
- [x] 中立客户端只开放 `MaxOutputTokens`，其余 SDK 选项逐项静态拒绝；模型覆盖、工具、扩展字典与原始请求工厂均不得静默启用，并通过 SDK 属性集合防漂移。
- [x] 运行级外观克隆选项、不修改调用方，显式 `IChatClient` 服务查询仅返回自身，底层资源仍由协调器释放；正数与极大值、取消传递、解包和资源所有权均有验证。
- [x] 原生 API/Worker 双库实际调用同时验证旧代次拒绝及当前绑定请求 `num_predict=100`；保留旧聊天调用兼容和纯文本失败关闭行为。
- [x] 集中回归、静态/AOT 分析、实际 Linux API/Worker 发布及受影响原生运行完成，更新运营保证、旧 Worker 升级门禁和实际测试计数；按 main 显式范围交付。

本批完成输出**请求参数**约束，不宣称提供程序不可忽略该参数、实际 Token 数或费用的独立硬限。实际输入分词、超量/缺失回执精确治理、真实云端模型守限、Workflow 总输出预算、反馈审核/脱敏导出和检索策略对比仍待完成，R09 保持未整体关闭。旧 Worker 仍发送固定 4096，所有承接 Run 的 Worker 须升级；沿用 R09b 绑定能力的停准入、等待在途和正常停机门禁。

**本地验证记录：**

- 基线 `30ed661d67df3290238468334f60a1bae46178ca`，冻结 6455 个实际输入。`node artifacts/r09c/run-red.mjs` 的修正夹具版本先观察 17 项能力缺失失败；`node artifacts/r09c/run-owner-red.mjs` 观察资源所有权失败。最初完整架构 235 项为 232 通过/3 失败，其中新增服务定位器表面问题已收紧，原日志保留；显式接口可选参数的编译失败也保留并修正。
- `node artifacts/r09c/run-final-fixed.mjs`：Unit 实际发现 6109，AI **2640/2640** 无跳过；API/Worker AOT analyzer 均零警告/错误。受影响架构实际 **85：83 通过/2 原有失败**，与本轮初次完整扫描的 150 个未影响通过 UID 合并为 235：233 通过/2 原有失败；完整错误信息与 R09a/R09b 基线一致。失败仍为存量 TXT/PDF 异步命名及 PDF 阻塞调用，未扩宽豁免。
- 同命令完成测试工具 **73/73**、命名 **30/30**，Integration 规范分片发现 **1212**，无遗漏/重复；分片发现不计为执行 1212 项。`node artifacts/r09c/run-static.mjs`：UUID **3/3**（命名合计 33）、OpenAPI 静态 **210/210**、治理 **59/59**（排除会读取受保护文档的精确仓库全量扫描项），替代扫描在读取前排除保护路径，覆盖 **385** 份 Markdown。公共接口/DTO 字段及数据库结构未改变，本批无新迁移或前端改动。
- `node artifacts/r09c/run-native.mjs`：实际 linux-x64 API/Worker 发布均退出 0、警告门禁只允许已有第三方告警；API **140164320 bytes / SHA256 `659548f59b9a3399adbbce41351873814bfab256d0c07985f96e033f1723399a` / 1132915 ms**，Worker **90430776 bytes / SHA256 `77ad37c417f48a244b1ebba58e923018e2e8b420942116beb81594c35df4856f` / 327869 ms**。Linux JIT 夹具以当前源码重新构建，实际发现并运行 AI API 与 Prompt Worker 双库 **4/4**，零失败/跳过，运行 SHA 与本次产物相符；未重复身份模块完整流程，也未把旧二进制结果外推到本批代码。
- `python artifacts/r09c/collect-acceptance.py` 核对 TRX、UID、冻结源码、JIT 夹具及运行二进制摘要；摘要 `artifacts/r09c/acceptance.json`。本地 Windows x64/i7-12700H/约 64 GB/SDK 10.0.401；Linux Docker SDK 10.0.400、runtime 10.0.11，SQL Server 2022-CU14 与 MySQL 8.0；原生发布 8 CPU/12 GiB，运行及夹具 2 CPU/8 GiB。双库使用新测试数据库、不复用 Testcontainers 数据库。独立生成应用待 AI 模块全部实现后集中验收；在线 NuGet 审计未执行（`NuGetAudit=false`），云端模型、回答质量、旧消费者混布/回退及容量未验收，保持 `Capacity-not-verified`。既有外部改动和受保护文件保留。

#### R09d：三策略评估、资源门禁与失败报告合批（2026-10-09，本批工具验收完成）

- [x] 基于 R02 现有评分器交付 `basic` / `keyword_vector` / `rerank` 三策略比较，重新评分全部冻结案例；模型、Prompt、解析、分块、索引及数据/质量策略一致，只有检索版本可变且须各自明确。候选不能自报通过、删题或覆盖门槛。
- [x] 固定 `rag-strategy-comparison-v1`：召回或带引用回答率绝对提升至少 0.02，基础和候选均通过既有质量/安全门禁，逐案例无退化；总 Token/费用 ≤ 基础 1.10 倍，P95 ≤ 1.20 倍，调用 ≤ 1.25 倍。未知用量、币种不一致、未测执行/耗时、无收益或超预算保留基础策略；多个合格候选按收益和资源稳定排序，工具不改生产配置。
- [x] 费用/Token/调用按 decimal 尾数与位数精确交叉比较，P95 按 IEEE 754 实际值与十进制门槛精确比较；展示比例溢出保留 null。修复评分器有限大耗时均值溢出，超出报告类型的用量合计明确拒绝为 InvalidDataException。
- [x] 接入已有 Unit 命令的 `ai-retrieval-comparison` 选择集；外部三策略完整 UTF-8 JSON 最多 8 MiB，严格拒绝重复/未知/缺失字段，报告使用 CreateNew 防覆盖。合法候选失败先写报告再非零退出，默认参考不产生启用推荐。报告不复制问题、答案、片段或评审正文，摘要绑定完整规范化运行；不把此工具当作生产脱敏审核或评审者认证。
- [x] 新增比较及报告回归、既有评分器回归、AI 模块集中验证与只读复审，同步唯一测试矩阵和既有评估 README；仅暂存本批文件，按 main 交付。

本批完成离线比较工具、推荐门禁和报告消费入口，不宣称已实现关键词/向量融合或重排运行器，也不宣称真实模型产生收益。`end_to_end` 与语义评审仍是外部声明，启用策略必须人工核对原始检索、模型、用量与评审证据。真实三策略运行及收益验证、反馈审核/脱敏导出仍待完成，R09 保持未整体关闭。

**本地验证记录：**

- 基线 `58a88ed7d562da37f471e30db01f8833e355bc96`。比较首轮 RED 36/36 能力缺失失败、报告入口 RED 7/7 失败；数值边界 RED 9/9 失败，补充精度组 2 项为 1 失败/1 已通过。首次测试断言编译失败与自有测试包装的锁嵌套等待均保留原始日志，已修正；未将这些结果计作通过，未终止其他任务进程。
- `node artifacts/r09d/run-numeric-green.mjs`：最终 Release 构建零警告/错误，新功能 54/54 通过。`node artifacts/r09d/run-final.mjs` 复用同一最终源码的 Release DLL，实际发现 Unit 6163，评估 **98/98**、AI **2694/2694**，均零失败/跳过；外部测试构造观测的合格入口 1/1，通过，超预算入口预期 1 项失败/exit=2，先保存报告并推荐保留基础策略。这两组是控制路径实验，不是实际模型/检索性能。
- 同一最终命令：测试工具 **73/73**、命名 **30/30**、治理 **59/59**；会读取受保护文档的精确全库 Markdown 项未执行，替代扫描先排除保护路径再读取 **385** 份文档。Integration 分片发现 **1212**、无遗漏/重复，发现不计作执行。受保护路径先于读/hash 排除，既有外部改动保持原摘要。
- `python artifacts/r09d/collect-acceptance.py` 核对新功能、评估与 AI 的实际 UID 集合、正式 TRX、退出码、外部失败报告及最终 DLL；冻结 **6460** 个实际输入，验收记录位于忽略目录 `artifacts/r09d/`。只读复审确认数值、读取限额及禁止覆盖修正后无剩余 P1/P2。
- 影响集仅选择测试矩阵工具；生产源码/依赖、API/Worker 可达路径、SQL、迁移和前端未改变，本批未重跑原生发布/双库业务场景或架构扫描，不把 R09c 历史结果当作新执行。在线 NuGet 审计未执行（`NuGetAudit=false`）；独立生成应用仍按 AI 模块范围全部实现后集中验收，真实云端模型、回答质量和容量未验收，保持 `Capacity-not-verified`。

#### R09e：结构化反馈审核、版本失效与标签统计合批（2026-10-10，本批本地验收完成）

- [x] 同批交付审核队列、批准/驳回与标签统计三个 Endpoint；精确读、审核、导出权限挂在现有 AI 对话授权页，仅交互用户可用，禁止本人审核本人，不授予其他用户的会话/正文读取权。
- [x] 审核按服务器 JOIN 证明的原所属人解码来源，当前评审者证明知识库/文档双层授予、权限、角色和机构；最终单次 SQL 复核所有依赖的当前 ACL、版本、文件、任务、租约与完成摘要。分页游标由 Data Protection 保护并绑定主体/租户，十五分钟有效，不释放被过滤行的 UUID。
- [x] 独立迁移258保存审核双版本绑定；本人修改或撤回令旧审核失效且保留单调审核版本，重提交不复活批准。同载荷重放幂等，旧版本409，失败结果和取消回滚；双库未记账重放恢复约束/说明并保留既有审核，物理删除反馈级联清理审核。
- [x] `feedback-labels-v1` 固定最前64项当前批准窗口，仅释放固定评价、原因、引用数量与反馈计数；每格至少五名不同提交者，无主体/反馈/引用标识、正文、时间或客户端筛选。始终声明 `IsPartial=true`，不自动进入评估、训练或模型输入，不产生模型费用。
- [x] 修正本批来源 SQL 将空租约哨兵当作持久化 GUID 参数的问题及空反馈标识进入查询的问题，补充真实 HTTP 400 回归；同时修正既有 PDF/DOCX 测试夹具“完整输出后挂起”的就绪信号竞争，保留取消清理断言，解析器生产行为不变。

小样本抑制不保证匿名化，实时精确计数仍有跨次差分风险。当前交付结构化标签审核与有界统计；原始问答脱敏审核、正式评估集准入、Vue 审核操作页、真实三策略运行及收益验证仍待完成，R09 不整体关闭。协议见 [知识库运营说明](../../operations/ai-knowledge-bases.md#结构化反馈审核与标签统计)。

**本地验证记录：**

- 基线 `ee8581753c9f531d866074ec7c75394b312193de`，冻结6474个实际输入。规则 RED 14项为7失败/7通过，来源边界 RED 9项为4失败/5通过；初始最小发现数和权限目录期望已修正。Windows真实双库首轮内存不足，未计为通过；Linux首轮发现空 GUID 参数错误，保留日志后停止本批测试并修正。后续双库首轮为4通过/2 MySQL清理失败：API测试模板的CREATE TABLE LIKE未复制外键，修正本模块夹具显式重放258并核对CASCADE，正式迁移未改动。重放夹具首次从已打开连接读取连接串，密码被驱动隐藏导致2项认证失败；改用受控数据库选项中的原始连接串，重新构建集成程序集后双库6/6通过，未输出凭据。AI全集两次出现既有 PDF 夹具取消竞争；独立实验20次中4次 EPIPE/清理缺失，修正就绪信号后全新目录20次均清理完成且无 EPIPE，不用重复通过替代原因定位。
- `node artifacts/r09e/run-sql-repair.mjs`：最终 Release Unit/Integration/Architecture 构建均零警告/错误，Unit发现6190，AI **2721/2721** 零失败/跳过；受影响架构 **87：85通过/2存量失败**，仍为旧 TXT/PDF 异步命名及 PDF 阻塞调用，未扩宽其豁免。旧源码首次架构87项的新增游标告警已通过固定用途的精确边界检查修正，当前实际重跑87项；最后一次变更仅为 Native 测试清理夹具，Unit/Architecture 输入不变，集成程序集另行重建；不外推全仓架构通过。
- `node artifacts/r09e/run-tooling.mjs`：测试工具 **73/73**、命名 **30/30**、OpenAPI静态 **210/210**、治理 **59/59**；受保护文档在读取/hash前排除，替代 Markdown扫描覆盖385份。Integration分片实际发现 **1214**，无遗漏/重复，迁移分片发现550；发现不计作执行这些全部测试。
- `node artifacts/r09e/run-delivery-verification.mjs`：在一次共享重型资源/工作区锁中复用 Windows构建的RID无关集成程序集，Linux实际运行 PDF/DOCX来源证明、审核事务与迁移258恢复双库 **6/6**；随后实际发布并启动新 linux-x64 API/Worker。首轮原生4项中Worker2通过、API2失败于夹具只删会话的清理断言；会话到消息没有级联外键，夹具修正为与生产相同的先删消息、再删会话，保留消息到反馈到审核的级联断言。`node artifacts/r09e/run-native-fixture-repair.mjs` 重新构建并交接最终测试程序集，仅此测试文件改变，生产输入与已发布原生产物摘要不变；复用产物重跑 AI API与Prompt Worker双库 **4/4**，零失败/跳过。Native HTTP覆核权限、密文游标、批准/拒绝/重放、过期反馈、来源删除、统计阈值与级联清理；历史提交者为明确构造的服务器绑定夹具，不代表真实多用户登录或云模型生成。
- 新API **140542640 bytes / SHA256 `8daace699d90e59e59394ad4fbc42c587134b1eabd4eadfa959bad8f3f26494a` / 985000 ms**；新Worker **90582136 bytes / SHA256 `5ddb9ca0484f6a76e0da69f943708c33a799a826fb16c86707391ba8aa981f23` / 287851 ms**。`python artifacts/r09e/collect-acceptance.py` 核对TRX、实际发现/UID、输入冻结、首轮与最终测试程序集、唯一 Native 夹具变更及运行Native文件摘要，证据位于忽略目录 `artifacts/r09e/`。只读安全复审在来源联合读取、密文游标和静态SqlStatement修正后未发现剩余P1/P2；复审不代替运行验收。
- 本地Windows x64/i7-12700H/约64GB/SDK10.0.401；Linux Docker SDK10.0.400、runtime10.0.11，SQL Server2022-CU14与MySQL8.0。Native发布8CPU/12GiB，Linux验收2CPU/8GiB，使用新测试数据库、不复用Testcontainers数据库；排队曾因系统重启中断，核实进程标识被重用后仅清理本任务旧锁并重新执行。忽略目录中的一次性资源协调副本仅将重型锁轮询缩短为5ms，工作区锁仍为100ms；独占锁路径、所有权、失效检查及释放协议与仓库脚本一致，仓库脚本未改，不终止其他任务或抢占其锁。独立生成应用待AI模块范围全部实现后集中验收，在线NuGet审计未执行（`NuGetAudit=false`）；真实云模型、回答质量和容量保持未验证、`Capacity-not-verified`。其他任务改动经摘要比对保持不变，本任务未读取、hash或修改受保护文件，仅本批文件在main提交。

#### R09f：Vue 反馈审核、统计下载与共享客户端合批（2026-10-10，Implemented / Build-verified）

- [x] AI 对话页交付反馈审核入口，独立控制队列读取、批准/驳回及统计导出权限；无权限动作不进入 DOM，导出不依赖队列读取权限。队列只显示固定标签、引用数量、状态和双版本，支持密文游标、被过滤的空页继续翻页及前页返回。
- [x] 审核确认冻结当前标签及双版本；409、未知写入结果后要求刷新，失效游标刷新回第一页。身份、租户、权限变化、关闭与卸载取消在途操作并清除载荷，迟到响应不能恢复旧上下文。
- [x] 统计预览与 JSON 下载分别取得当前统计，SDK 和页面双重投影固定字段，释放下载 Blob URL；中英文明确有界窗口、小样本抑制不保证匿名化、批准不等于评估集准入，不输出问答正文或主体/资源标识。
- [x] 双库实际运行时 OpenAPI 规范化结果一致，追加登记三个操作，冻结快照共612个操作；通过生成器更新 SDK。公共出口明确选择校验包装，闭合评价/原因、版本、分页与统计界限，透传取消信号。Vue 审核适配层独立登记，保留原用户反馈 API 稳定绑定。

**本地验证记录：**

- 基线 `0d8cf0c0aebb15d2770445a73444171678307031`。缺失能力 RED：SDK13项失败、页面入口1项失败；补充取消回归首次1失败/13通过。公共出口从实际 `src/index` 测试，首轮17失败/4通过定位星号导出选择了生成操作，显式包装出口修正后完整客户端 **260/260**。权限撤销夹具改为真实 Pinia 权限快照，保留首轮48通过/1失败日志；最终相关 Vue **49/49**，零失败/跳过。
- `node artifacts/r09f/run-openapi.mjs`：复用未改变的 R09e 后端源码及集成 DLL，在 SQL Server/MySQL 实际取得运行时 OpenAPI **2/2**，零失败/跳过；程序集 SHA256 `ae2b5007f1b1e50ca4a56733c48de00f4fa1c6544e872c6d707db9004bd93d24`。Windows 本地客户端测试与构建；双库 OpenAPI 在 Linux SDK10.0.400/runtime10.0.11、2CPU/8GiB 容器中执行，使用共享资源锁。
- `node artifacts/r09f/run-checks.mjs`：生成产物离线精确检查、OpenAPI **210/210**、共享客户端构建、Vue 类型检查与生产构建通过。首轮登记数量仍冻结609与既有路由绑定扩展触发失败；拆分新适配层、保留既有绑定并更新操作/API登记断言后原兼容门禁通过，未扩大豁免。`node artifacts/r09f/run-final-checks.mjs`：拆分后的 Vue **49/49**、相对基线96份契约的向后兼容、命名 **33/33** 均通过；最终实际输入冻结6479项，排除保护路径后的 Markdown 编码扫描385份通过。
- 最终测试包装误加参数分隔符，导致 Vitest 启动全量后台测试并出现无关用例超时；仅停止本任务误启动进程，保留失败日志，修正包装后按三个受影响文件重跑49项全部通过。这次误启动不计为通过，未据此修改无关实现或宣称全部后台测试通过。
- 只读安全复审及适配层拆分复审未发现有证据的 P1/P2，复审不代替运行验证。后端生产路径、依赖及迁移未改变，本批未重建 .NET、重发 Native 或重跑后台业务功能测试；R09e 记录保留原执行范围，不作为本批新执行。未新增依赖，未执行在线依赖审计。
- 本批证据保留于忽略目录 `artifacts/r09f/`；受保护路径先于读取/hash排除，其他任务改动保持原摘要，仅本批文件在 main 提交。真实栈页面与独立生成应用按 AI 模块范围全部实现后集中验收。原始问答脱敏审核、正式评估集准入、真实三策略运行及收益验证仍待完成，R09 不整体关闭；真实云模型、回答质量与容量未验证，保持 `Capacity-not-verified`。

#### R09g：离线人工反馈候选准入、冻结语料与评分链路合批（2026-10-10，本批工具验收完成）

- [x] 候选包严格读取 UTF-8、重复/未知/缺失/空值字段及2 MiB限额；最多32样本、每项16片段、全局字符预算，固定本地虚构范围、来源与位置，不复用生产资源标识，不执行反馈指令或调用模型。
- [x] 独立脱敏审核与准入审核绑定整个候选摘要，两名审核者标签互不相同且不同于提交者，来源许可、脱敏与指令风险复核缺一不可；明显敏感格式拒绝，不自动改写内容、不保证匿名化。标签是操作人声明，明确 `operator_attested`，不构成身份认证或生产授权。
- [x] 仅追加新数据版本，完整保留冻结合成基线、来源和门槛；全局片段标识唯一。单文件封装语料与回执，省略原始来源摘要及提交者/审核者/证据标签；总语料与逐样本输出摘要防止意外改写沿用旧回执，摘要不构成签名或防伪认证。
- [x] 接通现有专用评估入口，要求完整新旧案例外部观测，拒绝 `offline_reference`、占位版本、未测耗时与可回答案例已知零模型调用，不回退旧参考；未知用量仍为null，实际执行真实性需受控记录核对。新语料及其报告均不得覆盖旧文件，质量失败先保存报告再非零退出。
- [x] 更新唯一测试矩阵与实际操作说明，保持其余固定基线回归读取旧语料；批量规则、文件、篡改、来源绑定、资源、评分及失败链路本地验证后在 main 提交。

**本地验证记录：**

- 基线 `dc209f3dc54a469a257af0a250e37751f33c5e4a`，任务快照 `ai-feedback-sample-admission-r09g`。缺失边界 RED **1/1失败**；初步35项通过后补充回执 RED **1失败/35通过**。复审补充全局片段冲突与复制参考入口，合并 RED **3失败/35通过**；全局唯一、逐样本 `PayloadSha256` 与专用运行门禁修正后最终专项 **38/38**，零失败/跳过。修复过程一次测试数组展开语法编译失败已修正并保留原日志，不计为通过。
- `node artifacts/r09g/run-focused.mjs final-focused`：最终 Release构建零警告/错误。`node artifacts/r09g/run-concentrated.mjs` 复用相同最终源码的程序集：评估 **136/136**、AI **2759/2759**、准入默认入口 **1/1**，零失败/跳过；Unit实际发现 **6228**，发现不计作执行全部Unit。测试工具 **73/73**、命名30与UUID3共 **33/33**、治理 **59/59**、Integration分片实际发现 **1214**，无遗漏/重复，分片发现不计作执行。
- `node artifacts/r09g/run-probes.mjs`：实际外部候选读取/生成与专用评分各 **1/1**；覆盖写入、复制参考、缺少外部观测和语义质量失败各预期 **1项失败/exit=2**。覆盖拒绝保持原文件摘要，非法参考不输出有效报告，合法质量失败先保存 `Passed=false` 报告。观测由明确构造的合成控制输入组成，仅证明工具链和门禁，不代表真实反馈、模型调用或端到端质量。
- 只读安全复审的两项P2与补充回执问题均已修正，复审未发现剩余有证据的P1/P2；复审不代替执行证据。生产源码、依赖、数据库与API/Worker可达路径均未改变，本批不重跑数据库业务或Native发布，不将历史R09e结果作为新执行。环境为本地Windows x64、SDK10.0.401，最终集中测试限制4处理器并使用工作区锁和独立临时目录；构建 `NuGetAudit=false`，未执行在线依赖审计。
- 证据在忽略目录 `artifacts/r09g/`，保护路径在读取/hash前排除，其他任务改动保持原摘要。使用方式见 [评估语料说明](../../../tests/ai-evaluation/README.md#人工脱敏反馈候选准入r09g)。本批仅关闭离线人工候选工具与评分链路，生产原始问答脱敏读取、当前授权/撤权失效、在线准入与真实三策略收益仍待完成；R09不整体关闭，真实栈页面和独立生成应用按AI模块范围集中验收，容量保持 `Capacity-not-verified`。

#### R09h：有界三策略实际检索计算与外部证据入口合批（2026-10-10，本批离线工具验收完成）

- [x] 测试项目内实现精确余弦基础排序、中英文/编号固定分词 BM25、RRF 融合及前20候选重排；每案例先过滤冻结范围与允许来源，再统计词频、排名及重排，不让其他来源正文影响 IDF 或候选窗口。
- [x] 向量与查询完整绑定已验证语料的数据版本/摘要、全部片段/案例及模型空间；128维合成特征与1536/3072维外部模型声明明确区分，有限非零向量用double累加，禁止混用模型与重复/遗漏输入。
- [x] 重排只验证并排序外部供应的完整窗口分数，窗口外、重复、遗漏及非有限分数拒绝；同分保留融合顺序。缺分数明确 not_measured、召回null，任一案例未测时总召回仍为null，不推断模型已执行。
- [x] 严格有界 UTF-8/JSON 文件入口，原冻结语料或R09g验证准入封装，后者必须提供完整外部向量；新建报告不覆盖输入/既有证据，序列化摘要后及写入前复核取消。报告只含标识、排名、召回与本地阶段增量耗时，不复制问题、正文或原始向量。
- [x] 更新唯一测试矩阵、受控外部文件探针与操作说明，集中回归、只读复审和本批范围核对后在main提交。

**本地验证记录：**

- 基线 `906358784e7c16ff8b1a1bd083994d3c8a37a550`，快照 `ai-retrieval-execution-r09h`。缺失能力RED **2/2失败**，初步34项及扩充48项通过。审查发现机器键正则允许末尾换行，新增回归先得到 **2失败/48通过**；报告写入取消回归合并得到 **3失败/48通过**。正则严格锚定及取消边界修正后最终 **51/51**，原RED/TRX保留。
- `node artifacts/r09h/run-focused.mjs review-green`：最终Release构建零警告/错误；`node artifacts/r09h/run-concentrated.mjs` 复用同一程序集，评估 **187/187**、AI **2810/2810**、默认报告入口 **1/1**，零失败/跳过。实际Unit发现 **6279**，只同步实际矩阵门槛，不将发现数报告为全量Unit执行。
- `node artifacts/r09h/run-probes.mjs`：外部向量未重排、完整供应分数排序、R09g准入语料读取计算各 **1/1**；覆盖写入、窗口外片段和准入语料缺少外部输入各预期 **1项失败/exit=2**。旧报告字节不变，非法输入不输出有效报告。外部输入为明确构造的1536维控制向量与控制分数，仅证明实际计算及文件边界，不冒充真实Embedding/重排模型调用。
- 测试工具 **73/73**、命名30与UUID3共 **33/33**、治理 **59/59**；Integration分片发现 **1214** 无遗漏/重复，未执行其业务用例。Markdown受保护排除扫描 **385** 文件通过。只读复审确认两项具体边界已修正，无剩余阻塞问题；审查不代替执行证据。
- `python artifacts/r09h/collect-acceptance.py` 核对专项/评估/AI精确UID包含关系、三个预期拒绝、6279发现数、最终DLL摘要、**6487** 冻结输入与本批9文件范围。生产源码、数据库、依赖及API/Worker可达路径未改变，本批不重跑双库业务或Native发布，不把历史结果算为新执行；全量Unit及Architecture未执行。Windows x64、SDK10.0.401，集中测试4处理器、工作区锁及独立临时目录；构建仅本次 `NuGetAudit=false`，在线依赖审计未执行。
- 证据在忽略目录 `artifacts/r09h/`，受保护路径先于读取/hash排除，其他任务改动保持原摘要。操作说明见 [评估语料说明](../../../tests/ai-evaluation/README.md#有界三策略检索计算r09h)。本批仅关闭离线实际检索计算工具；真实模型提供者调用、完整答案/引用/独立语义质量、端到端Token/费用/延迟及生产策略激活仍待验收。默认生产基础策略不变；生产问答脱敏读取、当前授权/撤权与在线准入仍未关闭，R09不整体关闭，真实栈页面/独立生成应用按AI模块集中验收，容量保持 `Capacity-not-verified`。

### R10：节点恢复与审批一致性（P2，依赖 R01/R09）

**修改：** `src/AI/Full.NET.Agents/Workflows/AgentWorkflowRunner.cs`、`src/Modules/Full.NET.Modules.Ai/Runtime/AiAgentRunCoordinator.cs`；扩展 `tests/Full.NET.UnitTests/Ai/AiAgentRunCoordinatorTests.cs`、现有 `AiWorkflowCheckpoint*Tests.cs` 与 `AiApprovalRecovery*Tests.cs`。

- [ ] 先复核既有租约/检查点/审批实现，复用已满足的能力；新增节点完成后崩溃、长模型调用、租约失效、取消和版本不兼容的回归用例。
- [ ] 定义并持久化节点输入/输出摘要、定义/模型/Prompt 版本及 OperationId；已知完成不重跑，未知外部副作用进入对账，不能从 checkpoint 缺失推断未执行。
- [ ] 审批恢复复核当前会话/权限、参数摘要、工具/策略版本及业务状态；全节点预算和 deadline 必须约束实际派发，未知用量不得计零。

#### R10a：累计用量保真与取消派发边界合批（2026-10-10，本批本地验收完成）

- [x] 工作流与工具循环共用累计规则：首次模型调用初始化用量；后续任一输入/输出用量缺失时，该流累计保持null，不以之后的已知值补齐未知总量。工作流恢复按定义前缀判定是否已有模型调用，避免从null误判首次调用。
- [x] 拒绝负数模型用量和负数检查点；已知用量采用checked累加，溢出失败关闭。输入/输出总量全部校验成功后再更新工作流状态，输出流溢出不部分写入输入流累计值。
- [x] 模型/工具返回后、工具逐次执行间及OperationId解析后再次检查取消；已取消请求不继续派发后续工具或返回成功。只读审查指出解析OperationId期间取消仍可能派发，新增失败回归后修复两个运行消费者。
- [x] 更新唯一测试矩阵与[操作预算说明](../../operations/ai-operation-budgets.md#工作流与工具循环的累计计量)；保持公共签名、检查点序列化字段/版本、数据库与授权契约不变。

**本地验证记录：**

- 基线 `6712515178227263d3baa130a99e8df96ebf7106`，快照 `ai-runtime-metering-cancellation-r10a`。首次RED **16失败/7通过**，初步 **23/23** 通过；扩充输入/输出溢出原子性与OperationId取消回归后，派发边界RED **2失败/25通过**。`node artifacts/r10a/run-focused.mjs final-green` 最终 **27/27**，零失败/跳过，Release构建零警告/错误；原RED/TRX保留。
- `node artifacts/r10a/run-concentrated.mjs`：AI **2837/2837**、运行时 **115/115**、受影响Architecture **82/82**，零失败/跳过；实际Unit发现 **6306**，未将发现数报告为全量Unit执行。Integration与Architecture新鲜构建、API/Worker `FullNetAotAnalysis=true` 分析构建均零警告/错误；只复用核对过摘要的程序集，不重复构建已冻结输入。
- `node artifacts/r10a/run-tooling.mjs` 与 `node artifacts/r10a/run-extra.mjs`：测试工具 **73/73**、命名30与UUID3共 **33/33**、治理 **59/59**；Integration分片发现 **1214** 无遗漏/重复，未执行其全部业务用例；Markdown受保护排除扫描 **385** 文件通过。
- 影响分类要求完整Smoke；`node artifacts/r10a/run-delivery.mjs` 的Linux功能阶段覆盖SQL Server/MySQL完整Smoke及工作流检查点、审批恢复、Agent租约各双库用例，**14/14**，零失败/跳过。同一Integration与Agents程序集作为原生消费者；所有源码、测试、构建输入共 **6489** 项保持冻结，保护其他任务改动。
- 同一交付脚本新鲜发布linux-x64 Native API/Worker：API **140546864** 字节、1037476ms，Worker **90586360** 字节、365055ms；分别 **17/15** 条既有依赖告警通过白名单门禁，不表述为实际Native发布零警告。两个可执行文件SHA-256分别为 `2fdab1341bc6b8858a0082399cb86639a471267a3371fe4f81f87ac2a479f86d`、`288ff30278aa5c61bcab63422471a5247cf53c29b9a219d19469829568a60eaa`；耗时仅是本次构建记录，无容量结论。
- 交付脚本按唯一矩阵完整核心Native过滤器执行，发现与TRX精确UID一致，**35/35**，零失败/跳过，42m26.335s；包含双库AI/工作流、Kafka日志、PDF恢复、知识版本退役、旧Prompt拒绝及Worker后台/文件路径，不把最低发现门槛10当成执行数。Integration程序集SHA-256为 `38ce1ac021745846054f533ffc5fdf5fbef7dfe72de5f4ec473549205a4a56ee`；Unit、Integration、Linux功能与Native消费者的Agents程序集摘要一致为 `99f4f6b9b9a553584d38350f39c8636f32694e9e84521b5cd8b3ada4267adfaa`，Native消费者实际可执行文件摘要与上述新鲜发布一致。
- Windows x64、i7-12700H、63.7GiB内存、本机SDK10.0.401、Docker29.6.2；Linux发布SDK10.0.400，发布8CPU/12GiB，功能/原生消费者2CPU/8GiB并限制GC堆2GiB，集中Unit回归4处理器，使用工作区/机器重任务锁和独立临时目录。构建仅本次 `NuGetAudit=false`，在线依赖审计未执行；全量Unit、全量Architecture未执行，不把历史验收算作本批新执行。
- `python artifacts/r10a/collect-acceptance.py` 核对专项/运行时/AI的精确UID包含关系、两个RED、双库与Native发现/执行一致性、程序集及产物摘要、6489冻结输入与本批7文件范围；只读复审确认OperationId取消后派发问题已修复，无剩余具体缺陷。证据在忽略目录 `artifacts/r10a/`，受保护路径先于读取/hash排除，既有其他任务改动保留；审查不代替执行证据。
- 本批修复累计计量与协作式取消边界；不恢复历史已丢失用量，不证明模型提供者已支持deadline/硬取消，也不关闭节点完成凭证、未知外部副作用对账、长调用租约续期及审批当前再授权。R10原三项继续未勾选，无真实外部模型调用、费用/容量/SLO实测；独立生成应用按AI模块集中验收，保持 `Capacity-not-verified`。

#### R10b：工作流检查点准入与客户端创建前拒绝合批（2026-10-10，本批本地验收完成）

- [x] 静态Agent/工作流定义前置复核；检查点归属RunId、检查点Id、可推进序号、检查点自身定义版本、格式/框架版本及载荷摘要在模型客户端创建前校验，不用运行版本替代检查点版本。
- [x] 校验JSON、会话、游标、非负用量与全部已完成节点的非空输出；缺失、null、空字符串或空白前缀输出拒绝，不填补后继续派发。进入重命名或结束游标时复用执行器严格建议校验；新增可复用的纯校验API，不替代工具授权/人工审批，不改变检查点线格式或版本。
- [x] 检查点查询返回后先复核取消，再处理有效或损坏状态；避免忽略令牌的查询提供者让取消请求继续创建客户端、写失败状态或结算预算。正常恢复仅执行剩余节点，保留未知累计用量。
- [x] 双库检查点夹具读取真实运行与检查点后调用实际准入，替换原仅校验格式常量的断言；覆盖持久化元数据与会话、版本、归属、格式和摘要。同步唯一矩阵与[恢复准入说明](../../operations/ai-operation-budgets.md#工作流检查点恢复准入)。

**本地验证记录：**

- 基线 `fa937d1be78f427431c994cc5a7d425a561df3cf`，快照 `ai-workflow-checkpoint-admission-r10b`。测试夹具初次构建4处空引用错误已修正，不计为RED行为证据；有效行为RED **25失败/20通过**，初步 **45/45** 通过。扩充损坏检查点期间取消得到 **1失败/45通过**，修复后 **46/46**；只读审查指出空白前缀输出仍可准入，新增两个回归得到 **2失败/46通过**，收紧校验后 `node artifacts/r10b/run-focused.mjs review-green` 最终 **48/48**，零失败/跳过，Release构建零警告/错误。实现期间误用另一程序集internal校验类型的构建失败已改为复用公开纯规则，原始失败日志保留，不计为通过。
- `node artifacts/r10b/run-concentrated.mjs`：AI **2870/2870**、运行时 **148/148**、受影响Architecture **82/82**，零失败/跳过；Unit实际发现 **6339**，未执行全量Unit。Integration与Architecture新鲜构建零警告/错误；本批源/测试/构建输入共 **6491** 项冻结，9文件交付范围，其他任务改动保持原摘要。
- `node artifacts/r10b/run-tooling.mjs`、`node artifacts/r10b/run-extra.mjs`：测试工具 **73/73**、命名30与UUID3共 **33/33**、治理 **59/59**；Integration分片发现 **1214** 无遗漏/重复；Markdown受保护排除扫描 **385** 文件通过。复审确认前缀空输出问题已关闭，无新具体问题；静态审查不代替执行证据。
- `node artifacts/r10b/run-delivery.mjs` 的Linux双库功能阶段：完整Ai Integration 88项与相关Smoke 8项联合 **96/96** 通过，零失败/跳过，耗时 **35m00.156s**；精确联合过滤器按选择器生成 **48m** 执行预算，Windows与Linux发现集和实际TRX结果一致。
- 原生首轮完整35项串行门禁在 **45m25.765s** 总时限中止，完成33项、零断言失败，退出码3；缺少完整终态/TRX，保留原始日志与流式结果，不计为本批验收通过。`node artifacts/r10b/run-native-partitions.mjs` 将同一完整发现集按API 10项、Worker 25项拆成不重叠的两批，分别保留45m预算，复制隔离的测试源码/构建目录后并行执行；没有缩小用例范围、延长时限或修改实现。
- API/Worker的AOT分析构建均零警告/错误；本批实际Linux原生发布分别生成 **140546912** / **90590488** 字节，耗时 **21m02.096s** / **8m15.401s**，依赖警告 **17** / **15** 条全部通过既有允许清单，不报告为发布零警告。新产物核心门禁按上述分批完成 **API 10/10（23m43.683s）+Worker 25/25（34m03.972s）**，均零失败/跳过；两批TRX与各自发现集一致，互不重叠且并集严格等于原始完整35项，覆盖矩阵规定的Kafka、PDF和知识库退役类型。
- 本批消费者摘要：Integration `d6b0855d3fd6785f37e30dbd702d9338b3c372c87e4da1c1fed1be65cee297af`、Agents `eda679853ffe844a258d0e480f4274e8bb296eed6967725692340dcfc7858a36`、Modules.Ai `c5cabffb68c31e1f03110e1c191cfa193030a22133f8296cb612495f62a7d098`；功能与两批原生运行消费者分别核对。实际运行API SHA-256 `4d11db69ed18336277b239f358a3d25a022999d42c9a2ffb7cde20d38ca68b35`、Worker `ce09ad9d71d16c62018bef4fcb85691c8c92b1813c4df3ab64554a498ad259ca` 与新发布文件一致。首次原生脚本的模块摘要重定向受CR行尾影响，另从运行容器捕获核对；分批脚本使用LF并重新记录完整摘要，原始记录保留。
- 环境为Windows x64、i7-12700H、约64GiB RAM、SDK 10.0.401、Docker 29.6.2；Docker Linux环境20CPU、约19.3GiB RAM，发布SDK 10.0.400/Native Runtime 10.0.11。发布器8CPU/12GiB；功能和每个原生验收容器2CPU/8GiB、GC堆2GiB，两批并行且构建目录隔离；集中单元范围4处理器，外层工作区/同机锁与独立TEMP。`NuGetAudit=false` 仅作进程级覆盖，未执行在线依赖审计；未执行全量Unit、全量Architecture或全部1214项Integration，也不把这些局部结果扩大为真实云模型、生产容量或SLO证据。
- 本批仅关闭恢复准入缺口；SHA摘要不是外部节点完成证明，不证明历史用量真实或授权仍有效，无检查点的初始路径仍未提供崩溃后的安全重放。节点输入/输出持久凭证、未知外部副作用对账、长调用续租、实际deadline/全节点预算和审批当前再授权继续开放，R10原三项未勾选。独立生成应用按AI模块集中验收，保持 `Capacity-not-verified`。

#### R10c：失败结算租约保护与取消边界合批（2026-10-10，本批本地验收完成）

- [x] 会话拒绝及十类运行失败路径仅在状态提交通过租约保护后结算未知预算，并计入已处理数量；失租提交被拒绝时不改写账本。
- [x] 模型查询/提示词准入后、客户端工厂返回后、单次模型返回后复核取消；工厂忽略令牌返回的客户端仍释放，取消后普通异常不得改写失败状态或结算预算。
- [x] 新增34项行为回归，覆盖两种租约结果及单次文本、工具循环和工作流的取消边界；同步[运维说明](../../operations/ai-operation-budgets.md#运行失败提交与取消边界)。未知用量保持null，未知结算保留预留。
- [x] 固定源码副本的双库与API/Worker原生UID全范围核对完成；保留Worker首轮中止记录及原缺失UID的补跑证据，不将中止命令标为通过。

**已执行与范围：**

- 基线main `db17f85d2cbf11c496370d604b354e2a8e47debd`，快照 `ai-run-failure-fencing-cancellation-r10c`，本批6文件范围。首轮有效RED为73项中13失败/60通过；修复中的局部变量命名冲突导致构建失败，修正后76/76通过。新增创建前取消回归，移除守卫时82项中3失败/79通过，恢复守卫。
- 原工作区最终Release构建零警告/错误，但测试进程因内存不足退出，没有通过证据。保留失败日志；现场可用提交内存约2.3GiB，存在资源压力，未据此认定唯一原因。`node artifacts/r10c/run-focused-reuse.mjs` 复用该程序集，以4处理器/1GiB GC堆执行82/82；原工作区AI 2904/2904、运行时182/182、受影响Architecture82/82均零失败/跳过。这些结果不替代下面固定副本的最终交付核对。
- 另一窗口持续添加日志模块源码和原生测试；为防止验收输入漂移，固定副本取上述main提交，仅叠加本批AI修改，排除两个受保护文件后建立6602项输入摘要。副本仅用于构建与运行验收，开发及提交仍在main；共享矩阵中的日志选择器改动保持原样，提交时仅暂存本批AI阈值。
- 固定副本的原输入未变；进一步覆盖样例模块依赖及全部归档文件后，完整摘要共7751项。`node artifacts/r10c/run-linux-concentrated.mjs` 最终退出0：协调器82/82、AI2904/2904、运行时182/182、受影响Architecture82/82，均零失败/跳过；Unit实际发现6373，未执行全量Unit。Unit、Integration、Architecture及API/Worker AOT分析均零编译警告/错误，分析后恢复JIT依赖。
- Linux环境首次准备因Ubuntu软件源502退出100，保留失败日志，未禁用签名验证。缓存官方Node 24.21.0镜像的首次启动包装脚本添加PWD，原严格沙箱断言得到AI2904项中2失败/2902通过；最小环境实验证实包装脚本只有PATH输入却添加PWD。改用原二进制与隔离的musl加载器后，最小环境仅保留PATH，复用未变的Unit程序集重跑完整AI范围得到上述2904/2904；业务代码、环境白名单与断言未改变。原失败与成功TRX分别保留。
- `node artifacts/r10c/run-delivery.mjs` 的功能验收容器首次因只读挂载下缺少嵌套目标目录退出125，尚未执行测试；修正为独立只读任务挂载，保留原启动失败。该批API/Worker发布仍继续完成；`node artifacts/r10c/run-recovered-runtime.mjs` 复用本批新发布文件，在隔离构建卷中并行运行既定功能与原生范围，完整等待子任务退出后释放外层锁。
- 双库AI功能88/88通过，零失败/跳过，41m21.548s；原生API10/10通过，零失败/跳过，30m31.542s。Worker首轮在原45分钟预算后中止，退出3、45m09.061s，日志24通过/0断言失败，未生成完整TRX。使用与Linux生产结果流的DLL摘要一致的测试平台2.3.2.0官方读取器，完整读取原253116字节结果流，得到24个唯一Passed UID；保留原二进制与摘要，不生成替代TRX。唯一缺失UID `1abd8f61-178f-8f6a-92f4-fd56ba80f70d`（SQL Server文件引用认领对账）由 `node artifacts/r10c/run-worker-completion.mjs` 在同一冻结输入/消费者和原45分钟预算下补跑1/1通过、退出0、1m11.918s。原24项与补跑1项不重叠，恰好覆盖原25项发现集合；再与API10项核对完整35项原生发现集合，无遗漏或待通过UID。初轮中止仍不是整组Passed；补跑不改代码、断言或时间预算。
- API/Worker本批Linux原生发布均退出0，文件分别140555104/90590472字节、24m40.945s/5m32.679s，警告门禁分别17/15项已批准警告；不是零发布警告。实际执行API SHA-256 `c7c58a238f70bc91a364284beb7caff6f4881e369eb9124c13ccb400d3b97a50`、Worker `15c4df93c0892d3b52250f01e86fde4427877471271484b0dcf952f87dbbd3f4` 均与新发布文件一致。
- 固定副本消费者：Unit `64306269e3911b9274c2cb7073fc39918d54b633cac2327984b5db60254f81be`、Integration `239f329936cb5e90eb0a6a0aaac37bf696e70c47afadd1d5c616cf7c541c4729`、Agents `d6a774f215ab88f5cf517ab5bde751663f2977deb20dff5219d00eb0d4990da5`、Modules.Ai `5065e8e1736d55470464917f0ae2c49e03484ea5d46cfc4a3b1d00cdc26747ef`。Unit/Integration及功能、原生API、原生Worker和补跑消费者逐份核对；7751项冻结输入和main上本批实现/测试在补跑前后均一致。只读审查指出的创建前取消覆盖缺口已补六项回归，以3项失败RED证明后关闭，无新增具体问题。
- 环境为Windows x64、i7-12700H、约64GiB RAM；固定副本SDK容器实际为Ubuntu24.04.4（noble）、SDK10.0.400/Runtime10.0.11，Docker Linux约20CPU/19.3GiB RAM。集中编译/单元4处理器、GC堆2GiB；发布器8CPU/12GiB；功能与每条原生运行线2CPU/8GiB、GC堆2GiB。补跑继续使用同机重型锁与独立TEMP，等待其他验收释放锁后运行；未抢占外部进程。早前工作区工具73/73、命名30/30、治理59/59、UUID3/3及1214项Integration分片检查通过；交付前另核对受保护路径排除的Markdown和本任务差异。`NuGetAudit=false` 仅作进程级覆盖，未执行在线依赖审计。固定副本证据仅覆盖基线加本批AI修改，不外推到另一窗口持续变化的日志模块。
- 同一模块批次集中验收，独立生成应用在约定AI范围完成后统一执行。本批不改变SQL、迁移、公共HTTP契约或检查点线格式；未知外部动作、节点完成凭证、长调用续租、实际deadline/全节点预算及审批当前再授权仍未完成，R10原三项保持开放，容量仍为 `Capacity-not-verified`。

#### R10d：工具暂停边界与多分支派发上限合批（2026-10-10，本批本地验收完成）

- [x] 工具审批或对账返回后立即暂停；保留会话摘要、已发生用量、工具步数和暂停标志，同轮剩余工具及后续模型不再派发。暂停不在第八轮误报迭代预算耗尽。
- [x] 单次文本、工作流与工具循环统一使用运行冻结输出上限外观；每次请求受调用方／冻结值／4096 的最小值限制，客户端仍由协调器独占释放。模型请求调用前取消直接拒绝，不依赖提供者实现检查。
- [x] 新增24项行为回归（暂停16、协调器多分支6、预取消2），更新既有审批断言；同步唯一矩阵与[运行边界说明](../../operations/ai-operation-budgets.md#工具暂停与模型请求上限)。

**本地验证记录：**

- 基线main `1c27b71db2481fe0ed814527df81cfdb4b0e8aa4`，任务快照 `ai-tool-suspension-dispatch-budget-r10d`。`node artifacts/r10d/run-focused.mjs red` 有效RED为125项中25失败／100通过；产品修复后 `node artifacts/r10d/run-focused.mjs green` 定向125/125、零失败／跳过，Windows Release构建零警告／错误。独立静态审查无阻塞问题；会话仅摘要，完整工具回执仍待建设。
- 取基线归档，仅叠加本批10文件，排除受保护路径后冻结7753项输入；另一窗口日志改动保持原样，共享矩阵仅暂存本批四处阈值。`node artifacts/r10d/run-batch.mjs` 中Linux集中阶段完成：定向125/125、AI2928/2928、运行时206/206、受影响Architecture82/82，均零失败／跳过；Unit发现6397，未执行全量Unit。Unit、Integration、Architecture及API/Worker AOT分析均零编译警告／错误，分析后恢复JIT依赖。
- 固定副本工具54/54、命名30/30、治理57/57、UUID3/3、1214项Integration分片核对及受保护路径排除的386份Markdown检查通过。命名测试将生成目录改为LF，经字节比较确认仅CRLF转换后恢复归档原字节；Linux SDK生成的两个工具锁仅将构建期ILLink.Tasks从10.0.12改为10.0.11，已核对NuGet元数据及签名包摘要。原7753项归档输入不变，Linux派生清单单列7751项原输入与两个锁，不冒充原锁。最初分片检查缺少Windows副本Integration产物及功能预检锁摘要不一致均保留失败记录，修复环境后再执行。
- Docker服务在首轮运行期间退出；双库命令退出1073807364，原生API/Worker各退出125，保留unexpected EOF日志，不将中断组记为通过。使用与三个Linux消费者实际DLL摘要一致的测试平台2.3.2.0官方读取器完整读取结果流：双库52个Passed UID（2998915字节）、API5个（68470字节）、Worker8个（173331字节），无截断、重复或非通过记录。`node artifacts/r10d/run-interrupted-completion.mjs` 在同一冻结输入、程序集及本批原生文件上按精确缺失UID补跑双库36/36、API5/5、Worker17/17，均退出0、零失败／跳过；分别保留44m／45m原预算，不生成替代TRX。原记录与补跑集合不重叠，恰好覆盖双库88项及API10＋Worker25＝原生核心35项完整发现集合。
- API/Worker本批Linux原生发布各退出0，文件分别140555104/90590472字节、1457462ms/398274ms，警告门禁分别17/15项既有批准警告；不是零发布警告。实际执行文件SHA-256分别 `91571ca03536195c1cde78ba5172401a5e6ea1b33b19abf0e9b115c530b48032`、`c11bb049062e10b1e7bcd6a09e8c72e1548845a2f680804f10ae3a2e69d5e8d0`，与新发布产物一致。Unit/Integration消费者Agents `d0a4a1b9a9ec8a28c9491da9d710472c48d8ff6d9277fd4e999d840fd5f1281c`、Modules.Ai `efbc3cee2ced8cfa17edb96bea5c4f19636937656eadb9bc608bf57071fd3ff3` 一致；Integration `f3c505b9d6e58865b7a6f6bd9ac5d9643a31675b62e46948deff029bf6329e93`。`node artifacts/r10d/check-final-volumes.mjs` 核对三个运行卷全部Linux派生输入、程序集及官方结果读取器摘要；`python artifacts/r10d/collect-final.py` 核对UID闭包、原始结果、退出码与本任务范围，证据保留在忽略目录 `artifacts/r10d/`。
- 本地Windows x64、i7-12700H、约64GiB RAM；Linux SDK容器实际为Ubuntu24.04.4、SDK10.0.400／Runtime10.0.11，Docker29.6.2、约20CPU／19.3GiB RAM。集中编译／单元4处理器，发布8CPU／12GiB，功能及两条原生运行线各2CPU／8GiB、GC堆2GiB；使用同机重型锁与独立TEMP。仅进程级 `NuGetAudit=false`，在线依赖审计未执行；无全量Unit／Architecture／Integration、真实云模型、费用、容量或SLO结论。
- 无SQL、迁移、公共HTTP或检查点线格式变化，未知用量不补零。全节点累计预算、实际deadline、长调用续租、节点完成凭证、未知外部副作用对账及审批当前再授权仍开放，R10原三项不关闭。独立生成应用按AI模块集中验收，保持 `Capacity-not-verified`。

#### R10e：三分支长调用续租、期限取消与双库提交边界（2026-10-10，本批本地验收完成）

- [x] 独立作用域续租及成功 CAS 版本交接；正常收尾停止调度并等待在途续租，保持 Epoch 不变。
- [x] 三分支共用调用方、租约及运行 deadline 取消；拒绝／异常续租与静默到期均丢弃晚到结果，不提交状态或结算预算。
- [x] 续租和状态更新共用显式 SQL 的租约／deadline 存活条件；状态更新拒绝终态改写，双库同场景回归。
- [x] 新增31项行为／SQL边界回归；补齐既有夹具的运行期限与独立续租作用域，公共 bool 续租契约、HTTP、迁移及检查点线格式不变。

**本地验证记录：**

- 基线 main `f7fefc8823bac005e5f86c2a7b76cbc94631f35b`，快照 `ai-agent-lease-watch-r10e`。首次 RED 编译缺少测试夹具的 Messaging 引用，不算行为 RED；补引用后有效 RED 为154项中29失败／125通过，首次 GREEN154/154。
- 只读审查发现停止与续租 OCE 竞态；新增确定性成功／异常交接回归后，review-red156项中仅异常分支失败。修复仅让定时器等待吞掉停止取消，续租自身 OCE 仍取消执行；修复后只读复核无阻塞项。最终 Windows GREEN156/156，零失败／跳过，构建零警告／错误。
- 基线归档仅叠加本批11文件并排除受保护路径，冻结7756项输入；另一窗口日志改动保留，共享矩阵仅提交本批阈值及租约边界筛选器。`node artifacts/r10e/run-batch.mjs` 已通过Linux定向156/156、AI2959/2959、运行时237/237、受影响Architecture82/82，均零失败／跳过，Unit发现6428；但Docker引擎在Worker分析和API原生编译期间退出，原进程退出码不可取得，保留原始日志及字节摘要，不将原整组记为通过。确认旧进程及本批容器不存在后，仅清理该失效所有者两个锁，恢复Docker并执行 `node artifacts/r10e/run-resume.mjs`。复用已通过且输入未变的结果，仅重跑未完成分析及发布，再执行双库／原生验收；API和Worker发布使用隔离缓存并行，未删除共享缓存。Unit、Integration、Architecture及API/Worker AOT分析构建均零警告／错误，随后恢复JIT依赖，未执行全量Unit／Architecture／Integration。
- 双库功能按精确发现UID分为SQL Server44/44、MySQL44/44；原生核心按API10/10、Worker SQL Server12/12和其他13/13并行运行，全部零失败／跳过、退出0。各线使用独立构建卷、同一冻结源码和程序集，保留原44m／45m预算；发现及结果集合无遗漏／重复，分别完整覆盖88项AI功能和35项原生核心。
- API/Worker新Linux原生发布退出0，分别140584432/90619656字节，耗时1523271/591871ms，警告门禁分别17/15项既有批准警告；不是零发布警告。实际运行文件SHA-256分别 `6b566f907d8ebe23730da58c75fd023caaa1757a20f2a20882b747826a08649f`、`0e38369aed670bc3c387e93cac0bdbb5eaf1aecbc0cbb2aab136a90ff46a0d2e`，与发布产物一致。Unit/Integration消费者Agents `5a554d29e3df99b6316b6b437f8bd144c122df4072ccea364a55fd61df58aa71`、Modules.Ai `47b9041d102455a6df5090caf96caba1c6ac1b4c55f1c495d9f10d6b17698a96` 一致；Integration `51b9934021dfb6aa3f5121ae6aeb7bf22a0a003b42a83d5774136fdec608a9c7`。`node artifacts/r10e/check-final-volumes.mjs` 逐份核对五个运行卷全部Linux输入及消费者，`python artifacts/r10e/collect-final.py` 核对TRX、退出码、发现UID闭包、产物摘要及本任务范围。
- 固定副本工具54/54、命名30/30、治理57/57、UUID3/3、SQL安全5/5、1214项Integration分片发现核对及受保护路径排除的Markdown检查通过。命名生成目录仅CRLF转LF，经字节比较后恢复归档原字节；Linux SDK仅将两个工具锁的构建期ILLink.Tasks从10.0.12改为10.0.11，核对其余JSON、NuGet元数据及签名包SHA512，单列7754项原输入加两个派生锁，原7756项归档清单不变。
- 环境为Windows x64、i7-12700H、约64GiB RAM；Linux SDK容器Ubuntu24.04.4、SDK10.0.400／Runtime10.0.11，Docker29.6.2、约20CPU／19.3GiB RAM。集中编译／单元4处理器，发布8CPU／12GiB，双库和原生每条线各2CPU／8GiB、GC堆2GiB；由最外层同机重型锁管理，独立TEMP并等待全部子进程完成后释放。`NuGetAudit=false` 仅进程级覆盖，在线依赖审计未执行；无真实云模型、费用、容量或SLO认证。
- 仅提供协作取消与租约存活约束，不宣称强制终止非协作外部调用、持久节点凭证、费用认证、未知副作用对账、审批当前再授权或全节点累计硬预算。R10原三项仍开放；独立生成应用按AI模块集中验收，保持 `Capacity-not-verified`。

#### R10f：当前工具派发复核与未知写结果暂停（2026-10-10，本批本地验收完成）

- [x] 审计意图等待后重新构建生产工具目录，核对启用、版本、权限、副作用和参数，再复核当前主体／会话并使用当前 Handler；不以旧缓存批准派发。
- [x] 写 Handler 已派发时，异常、取消、输出超限与回执失败统一进入已有对账暂停错误码；失败回执能够保存时记录该错误，结果正文不交付，不自动重放。
- [x] 目录、授权、审批与审计 await 边界检查取消；只读失败保留原语义，新增33项回归，同步唯一矩阵与运维限制。

**验证记录：** 基线 main `0529c247265226d27a9faecff97cf292f4b19feb`，任务快照 `ai-tool-dispatch-outcome-r10f`。首轮有效 RED76项中20失败／56通过；扩展 RED78项中22失败／56通过。初次GREEN78项中6项失败是NSubstitute整数匹配器歧义，保留原日志，改用精确匹配后业务断言通过。独立审查发现回执期间取消仍可能交付输出，新增4项回归得到review-red82项中4失败／78通过，修复后 `node artifacts/r10f/run-focused.mjs review-green` 最终82/82，零失败／跳过、Release构建零警告／错误；复审确认阻塞关闭。

- 基线归档仅叠加本批6文件，排除受保护路径后冻结7757项输入；其他窗口改动保留，共享矩阵只提交本批AI阈值／筛选器。`node artifacts/r10f/run-batch.mjs` 退出0：Linux定向82/82、AI2992/2992、运行时270/270、受影响Architecture82/82，均零失败／跳过；Unit实际发现6461，未执行全量Unit／Architecture／Integration。Unit、Integration、Architecture与API/Worker AOT分析均零编译警告／错误，分析后恢复JIT依赖。
- 双库AI功能按精确UID分为SQL Server44/44和MySQL44/44；原生核心按API10/10、Worker SQL Server12/12和其他13/13运行，均退出0、零失败／跳过，完整覆盖88项双库功能及35项核心，无发现遗漏／重复，保留44m／45m原预算。五个独立构建卷逐份核对冻结源码、Unit/Integration消费者及实际可执行文件摘要。
- 本批API/Worker Linux原生发布退出0，分别140588528/90623752字节、1590099/789503ms；警告门禁分别17/15项既有批准警告，不宣称零发布警告。API SHA-256 `da7cfb4c988a2817d530536836ffbef4351c1f39dbd43e8224f346c20fe1eb13`、Worker `3a194af5ffd0b882ed7694129e5914e0cd199600cf49f317a640debadde1a072` 与运行文件一致；Agents `770a2de44d8149d08f08f6070c62a6c549148057666f732fc0b9e183bd286510`、Modules.Ai `e0efd168c2377a1b96fef6af8d6628a6827742c7f6dc292ad0b03f91d655e844` 在Unit及Integration消费者一致，Integration `719df0adbd638d0929b68edf197e926aefa794a04bf95334f244c2adce17a871`。
- 独立静态副本工具54/54、命名30/30、治理57/57、UUID3/3、SQL安全5/5、Integration1214项分片发现核对及受保护路径排除的Markdown检查通过。命名仅在静态副本把生成目录CRLF转LF，经完整字节比较核对；运行／发布冻结副本始终保留原字节。Linux SDK两个工具锁仅有构建期ILLink.Tasks10.0.12至10.0.11差异，其余JSON与NuGet元数据／签名包SHA512已核对，7755原输入加两个派生锁单列，原7757输入清单不改写。
- 环境为Windows x64、i7-12700H、约64GiB RAM；Docker29.6.2、Linux约20CPU／19.3GiB RAM，SDK10.0.400／Runtime10.0.11。集中编译与单元4处理器，原生发布8CPU／12GiB，每条功能及原生运行线2CPU／8GiB、GC堆2GiB；由同机重型锁及独立TEMP管理，全部子进程完成后才释放资源。API/Worker以隔离缓存并行发布，没有删除共享缓存。`NuGetAudit=false` 仅进程级覆盖，在线依赖审计未执行；无真实云模型、费用、容量或SLO认证。`node artifacts/r10f/check-final-volumes.mjs` 与 `python artifacts/r10f/collect-final.py` 核对报告、退出码、发现UID闭包、摘要及6文件范围。

本批不改变SQL、迁移、HTTP或线格式；持久节点完成凭证、自动对账／崩溃恢复、审批当前再授权及全节点累计硬预算仍开放，R10原三项继续未勾选，保持 `Capacity-not-verified`。

#### R10g：工作流逐节点持久凭证、崩溃恢复与事件序号合批（2026-10-10，本批本地验收完成）

- [x] 静态会话重命名工作流逐节点先持久化 PendingNode 与输入摘要，确认后派发；完成输出、用量、步骤、检查点与事件在同一模块事务中提交。明确拒绝的校验结果保留实际用量，不进入业务写入。
- [x] Binding 冻结定义及静态 Prompt 摘要、模型配置版本；恢复校验确定性 OperationId、节点前缀和输入／输出摘要。兼容历史前缀但不伪造旧凭证，新格式仍为1并追加可选字段。
- [x] 未知模型网络／独立超时与恢复中的 PendingNode 进入对账态，保留预算预留，不重复派发。Worker 只自动领取租约过期、期限有效且已有新节点事件／检查点的该静态工作流；其他分支和历史无凭证记录保持原边界。
- [x] 续租与节点提交串行交接 CAS Version；所有运行分支按最新持久事件分配序号，等待事件没有检查点也不重复。双库覆盖节点完成、过期领取、旧所有者拒绝及步骤／末段事件失败后的完整事务回滚。

**本地验证记录：** 基线 main `d3564b193ba08757cfa4fa6227a85dd114b49c60`，任务快照 `ai-workflow-node-persistence-r10g`，本批17文件。有效首轮RED142项中4失败／138通过；首次GREEN142项中8失败／134通过，校正旧夹具的节点提交与续租版本预期。review-red-build出现两处测试表达式编译错误，不作为行为RED；修正后review-red156项中3失败／153通过，review-green156项中1失败／155通过。事件序号扩展RED163项中8失败／155通过，修复未知模型结果、校验拒绝凭证与各分支恢复事件序号。最终 `node artifacts/r10g/run-focused.mjs final-green` Windows186/186，零失败／跳过，Release构建零警告／错误；Windows Integration Release编译零警告／错误。只读复审确认无剩余具体P1／P2阻塞，审查不替代运行证据。

- `node artifacts/r10g/run-batch.mjs` 的冻结Linux定向186/186、AI3040/3040、运行时318/318、受影响Architecture86/86均零失败／跳过；Unit实际发现6509，Integration分片发现1214，未执行全量Unit／Architecture／Integration。Unit、Integration、Architecture与API/Worker AOT分析均零编译警告／错误，分析后恢复JIT依赖。原批次因下述测试断言失败退出1，原结果完整保留，不标为整批一次通过。
- SQL Server／MySQL功能组各44项，首次均43通过／1失败／零跳过，退出2；失败均为新增检查点回滚断言误期待原始DbException，实际数据边界返回DataCommandException／UniqueConstraint。仅修正共享测试帮助类两处异常分类断言，补验同时验证内层DbException与完整回滚；`node artifacts/r10g/run-assertion-fix.mjs` 用隔离副本重编Integration，零警告／错误，精确定向双库检查点2/2、零失败／跳过、退出0。原86个Passed UID与这两个修正后Passed UID闭包恰好88，分别覆盖双库各44，不能称原组退出0。
- 原生核心API10/10、Worker SQL Server12/12和其他13/13，各线退出0、零失败／跳过，完整闭包35项。五个原运行卷使用同一7759项冻结源码与Integration程序集；断言补验仅替换登记的一个测试源码，补验Integration SHA-256 `1a8e3ff57068e0fd6d94e6b698d6c0895a2c635072cfb5e8ab454dd6226521ab`，Agents／Modules.Ai与原消费者完全一致。源码与发现集合无未登记变化或重复，原44m／45m预算保留。
- API/Worker本批Linux原生发布退出0，分别140684688/90711528字节，耗时1631489/797150ms；既有批准警告门禁分别17/15，不是零发布警告。API SHA-256 `44bb6f92e008c945be7c377fe335c85d427697643f0bdc38d79fa5ae8089d4f5`、Worker `7402d9e1694b4b984761881ef94a7b7bf0f3964f85630e8504a574c16c17cbee` 与实际运行文件一致；Unit及Integration消费者Agents `81429dd69396a4c9036571009c4f3dcb3af9676e463f8654ec02f3459bc85588`、Modules.Ai `6afc74e2f67891ac75922eb15a8ae5f0f3868fddda7d8b7590709fc8e66e0315` 一致，Integration `85eeeff048a559cd4505d48efd3d29fa0651c43b341fb01d419c32602ac75377`。
- 基线归档仅叠加本批实现并在读取／哈希前排除受保护路径，冻结7759输入，保护其他窗口改动；共享矩阵仅提交本批阈值。Linux SDK两个构建工具锁仅有ILLink.Tasks10.0.12至10.0.11差异，7757原输入加两个派生锁单列，原归档不改写；其余JSON与NuGet包SHA512已核对。独立静态副本工具54/54、命名30/30、治理57/57、UUID3/3、SQL安全5/5和受保护路径排除的Markdown检查通过，CRLF至LF只发生在静态副本并经字节核对。
- `node artifacts/r10g/check-final-volumes.mjs` 与 `python artifacts/r10g/collect-final.py` 核对TRX、退出码、发现UID闭包、全部源码及消费者／发布摘要。环境Windows x64、i7-12700H、约64GiB；Docker29.6.2、Linux约20CPU／19.3GiB、Ubuntu24.04.4、SDK10.0.400／Runtime10.0.11。集中编译与单元4处理器，发布8CPU／12GiB，每条数据库／原生运行线2CPU／8GiB、GC堆2GiB，由同机重型锁与独立TEMP管理；全部子进程完成后释放，发布缓存保留。`NuGetAudit=false` 仅进程级覆盖，在线依赖审计未执行。

无需迁移，HTTP契约不变；回退前暂停AI派发并排空在途运行，旧Worker不得忽略新增意图后重放新记录。证据只覆盖该静态工作流与受影响事件边界，不表示其他分支已具备节点凭证、自动外部对账、完整审批当前再授权、全节点累计硬预算、真实进程崩溃／云模型收费或容量认证。R10原三项仍开放，独立生成应用按整个AI模块集中验收，保持 `Capacity-not-verified`。

#### R10h：审批当前入场、原始恢复绑定与消费回滚合批（2026-10-10，本批本地验收完成）

- [x] 恢复门禁从批准计数改为双库有界候选读取；最多 32 条，逐条核对审批完整绑定、当前精确权限、原认证会话及 AI 自有目标状态，恢复不消费审批。
- [x] 写工具审计意图后再次复核审批；重命名目标查询后在本地事务之外重新授权，事务内仅复核 AI 自有数据并消费，版本冲突完整回滚。
- [x] 恢复请求匹配原租户、用户、认证会话、SecurityStamp、ActorScope、EffectiveScope 与 SessionKind；审批等待后重查原会话与 deadline，依赖忽略取消时也停止后续写派发。
- [x] 参数解析拒绝空标题、重复字段和身份字段；静态工具名及版本由注册目录与审批策略共同引用，保持 DI 无构造环和 AOT 静态闭包。

已完成定向证据：基线 main `75de03c9a105e3b53852c19f717ebcb5ef9e12fd`，任务快照 `ai-approval-current-admission-r10h`，本批 16 文件。初次 RED/GREEN 的查询代理夹具失败完整保留，不作为业务 RED；改为显式查询夹具后，隔离原 HEAD 源码的 `node artifacts/r10h/run-final-red.mjs` 实际 108 项中 55 个业务失败／53 通过，无夹具异常。`node artifacts/r10h/run-focused.mjs final-green` 108/108，零失败／跳过，Release 构建零警告／错误；`node artifacts/r10h/run-integration-build.mjs` Windows Integration Release 构建零警告／错误。独立只读安全审查及双库回归复审无新增阻塞问题；存量知识答案详情在事务内读取的拒绝路径单列后续，不纳入本批修复。

集中验收记录：

- `node artifacts/r10h/launch-batch.mjs`／`run-batch.mjs` 退出 0：冻结 Linux 定向 108/108、AI 3095/3095、运行时 373/373、受影响 Architecture 86/86；SQL Server/MySQL 功能各 44/44；原生核心 API 10/10、Worker SQL Server 12/12、其他 13/13，全部零失败／跳过。Unit 实际发现 6564，Integration 分片发现 1214 并核对无遗漏／重复，未执行全量 Unit／Architecture／Integration。
- Unit、Integration、Architecture 及 API/Worker AOT 分析构建均零警告／错误；分析后恢复 JIT 依赖。API/Worker 本批 Linux 原生发布分别 140709792/90732360 字节、耗时 1482001/693681ms，既有批准警告门禁分别 17/15；API SHA-256 `e99c6045f3b838631298d071f62b8aacdbcbef201555fc0db99ac328a8d6d44a`、Worker `d7c1afb861e2cdc08693f17aad5ab1f0bd5177712fbe3ca79a9c4475774b8f9f` 与运行文件逐线核对。
- 基线归档仅叠加本批修改，保护路径在读取／hash 前排除，冻结 7760 输入；五个独立运行卷复用一次 Integration 构建并核对同一源码、程序集和唯一测试 UID。Linux 两个构建工具锁仅 ILLink.Tasks 10.0.12→10.0.11，经完整 JSON 差异、NuGet 元数据与包 SHA512 核对，7758 原输入加两个派生锁单列，原归档不改写。
- 独立静态副本工具 54/54、命名 30/30、治理 57/57、UUID 3/3、SQL 安全 5/5 和受保护路径排除的 Markdown 检查通过；静态副本仅对象注释目录 CRLF→LF 单列，运行源码字节保持不变。`node artifacts/r10h/check-final-volumes.mjs` 和 `python artifacts/r10h/collect-final.py` 核对退出码、TRX、发现闭包及所有运行产物摘要，交付前再核对 main、本任务 diff、暂存范围和其他任务原摘要。
- 环境 Windows x64、i7-12700H、约 64GiB、SDK 10.0.401；Docker 29.6.2、Linux 约 20CPU／19.3GiB、SDK 10.0.400／Runtime 10.0.11。集中编译／单元 4 处理器，发布 8CPU／12GiB，各双库／原生线 2CPU／8GiB、GC 堆 2GiB；同机重型锁与独立 TEMP 管理，全部子进程完成后释放，保留发布缓存。`NuGetAudit=false` 仅进程级覆盖，在线依赖审计未执行。共享矩阵仅提交本批阈值和筛选器，其他任务改动保留。

现有审批没有目标历史版本；本批只比较批准时原始标题及预期变更，并由当前目标版本 CAS 保护写入，不证明标题从未改变后恢复。其他写工具、未知外部副作用自动对账、其他分支逐节点凭证与全节点累计硬预算仍开放，R10 原三项暂不关闭。无需迁移和 HTTP 契约变更，升级／回退须停准入并排空旧版本写派发；独立生成应用按整个 AI 模块集中验收，保持 `Capacity-not-verified`。

#### R10i：审批标题写入与知识历史事务边界合批（2026-10-11，本批本地验收完成）

- [x] 根因修复：会话重命名的标题更新与完整历史交付分开；普通 HTTP 在结果事务提交后读取详情，知识来源授权仍在事务外失败关闭。带知识答案的会话不再因为事务内来源守卫拒绝而阻断标题更新。
- [x] 审批工具事务只执行审批消费和标题版本 CAS，返回操作结果，不读取／返回历史正文；版本冲突和提交前取消／可信租户变化回滚。SQL 返回后及事务返回后再次检查取消，提交后不确定结果不能报告为未写入。
- [x] 新增 16 项 Unit 回归，扩展现有 SQL Server／MySQL 聊天和审批夹具，覆盖真实来源守卫、提交后详情契约、损坏知识依赖隐藏、版本冲突、原子消费成功／回滚及重复消费拒绝；无迁移、新公共 HTTP 字段或跨模块 SQL。

本地验证记录：

- 基线 main `caae13c3213055d877fd8ed075ff06bd987e3521`，快照 `ai-chat-rename-transaction-boundary-r10i`，本批 9 文件。首次测试编译的空值告警修正后，`node artifacts/r10i/run-focused.mjs behavior-red` 实际 67 项中 13 个业务失败／54 通过；原编译失败和业务 RED 保留。`node artifacts/r10i/run-focused.mjs final-green` 67/67、零失败／跳过，Release 构建零警告／错误。独立只读事务审查未发现本补丁新增 P1/P2；审查不代替测试。
- `node artifacts/r10i/launch-batch.mjs`／`run-batch.mjs` 退出 0：冻结定向 67/67、AI 3111/3111、运行时 389/389、受影响 Architecture 86/86；SQL Server／MySQL 功能各 44/44；原生核心 API 10/10、Worker SQL Server 12/12、其他 13/13，全部零失败／跳过。Unit 实际发现 6580，Integration 分片发现 1214 且精确 UID 无遗漏／重复，未执行全量 Unit／Architecture／Integration。
- Unit、Integration、Architecture 和 API／Worker AOT 分析构建均零警告／错误；分析后恢复 JIT 依赖。新鲜 Linux Native API／Worker 发布分别 140714016/90732360 字节，耗时 2065008/1032896ms，既有批准告警门禁分别 17/15。实际运行文件 SHA-256 分别 `0f9987596a3f9a37db0771beddc9e8e777efd881025980e5b5b4c3b856dc1681`／`b0562483112e83346546e06b93d19f93dbbc3c6660eb47d5d243a5c2c8c7a0eb`，各原生线逐一核对一致。
- 冻结 7761 输入，基线归档仅叠加本批修改，保护路径在读取／hash 前排除；五个独立运行卷复用一次 Integration 构建，核对源码、程序集及唯一 UID。仅两个构建工具锁 ILLink.Tasks 10.0.12→10.0.11 单列派生，经完整 JSON、NuGet 元数据与包 SHA512 验证，其余 7759 原输入不变。
- 独立静态副本工具 54/54、命名 30/30、治理 57/57、UUID 3/3、SQL 安全 5/5 和受保护路径排除 Markdown 通过。静态副本仅对象注释目录 CRLF→LF 单列，运行源码不改写。`node artifacts/r10i/check-final-volumes.mjs`／`python artifacts/r10i/collect-final.py` 核对原始退出码、TRX、发现闭包和产物摘要；main、任务 diff、暂存范围与其他任务摘要在提交前复核，共享矩阵仅暂存本批修改。
- 本地 Windows x64、i7-12700H、约 64GiB、SDK 10.0.401；Docker 29.6.2、Linux 约 20CPU／19.3GiB、SDK 10.0.400／Runtime 10.0.11。集中编译／单元 4 处理器，发布 8CPU／12GiB，各双库／原生线 2CPU／8GiB、GC 堆 2GiB；同机锁排队与独立 TEMP，全部子进程结束后释放资源，保留缓存。首次排队在其他任务释放锁附近遇到 Windows EPERM，验收尚未启动；核对原持有进程、容器和锁均已退出后，复验同一冻结输入并重试，未删除／抢占资源。首次 exit 1 与诊断单列保留，不计作通过。`NuGetAudit=false` 只在本次进程覆盖，在线依赖审计未执行。

HTTP 提交后详情读取失败／取消并不回滚标题，调用方须刷新版本；工具不确定结果继续受控对账，不自动复用已消费审批。R10 全节点累计硬预算、其他运行分支凭证、历史目标版本证明与自动外部对账仍开放；独立生成应用按整个 AI 模块范围集中验收，保持 `Capacity-not-verified`。

#### R10j：累计模型输出额度、恢复计量核对与流边界合批（2026-10-11，本批本地验收完成）

- [x] 单次文本、工具循环与工作流的模型请求共用运行输出额度；派发前预留本次上限，合法完整的已知回执才退回未用部分。未知计量保留全部预留，异常／取消／超额或负回执关闭后续派发，实际未知用量仍为 null。
- [x] 创建客户端前核对冻结预算与恢复用量，扣减已完成输出；有剩余模型时未知、耗尽、超限拒绝。新模型节点凭证按输入／输出分别与合计核对、传播未知及检查求和溢出；旧前缀仅核对新凭证下限，不补造历史事实。无剩余模型可保存未知事实。
- [x] 流只能枚举一次，请求及枚举令牌共同有效；Current 只读一次，读取、内容校验、取消、释放异常均关闭后续额度。完整结束和提前释放仍保留本次预留，不凭部分流退费。并发冲突不得报告为确认未派发；派发前额度拒绝清除未执行意图并失败，派发后不确定回执保持对账边界。
- [x] 新增 35 项 Unit 回归，调整既有模型模拟回执保持真实计量语义；现有 SQL Server／MySQL 夹具扩展已知、合计漂移、未知漂移、未知拒绝与无剩余模型未知保留的真实检查点写入读取。无迁移、公共 DTO、新 Endpoint 或定义／检查点版本变更。

本地验证记录：

- main 基线 `3305a98381465065973d7d8a828578921f008ecb`，任务快照 `ai-cumulative-model-output-budget-r10j`，本批 14 文件。`node artifacts/r10j/run-focused.mjs final-green` 214/214、零失败／跳过，Release 构建零警告／错误。业务 RED：初始输出 15 项 14 失败／1 通过，合并 24 项 22 失败／2 通过，边界 203 项 6 失败／197 通过；独立只读审查发现两项 P2 后补回归 214 项 11 失败／203 通过，修复后全绿并复审无新增 P1/P2。初始最低发现数量设置及新文件未进入构建求值的发现失败保留，不计作业务 RED 或通过。
- `node artifacts/r10j/launch-batch.mjs`／`run-batch.mjs` 完成构建与发布后因依赖锁校验退出 1；`node artifacts/r10j/resume-batch.mjs` 复用本轮新鲜产物，完成双库后，缺少原生分片复用脚本的启动错误被原样保留；补齐脚本后 `node artifacts/r10j/finish-native-batch.mjs` 只执行未运行的原生分片，集中结果组合退出 0：冻结定向 214/214、AI 3146/3146、运行时 424/424、受影响 Architecture 86/86；SQL Server／MySQL 功能各 44/44，原生核心 API 10/10、Worker SQL Server 12/12、其他 13/13，均零失败／跳过。Unit 实际发现 6615，Integration 分片发现 1214 且唯一 UID 无遗漏／重复，未执行全量 Unit／Architecture／Integration。
- Unit、Integration、Architecture 及 API／Worker AOT 分析构建零警告／错误，分析后恢复 JIT 依赖。新鲜 Linux Native API／Worker 发布 140743440/90753288 字节，耗时 1313646/493091ms，既有批准告警门禁 17/15。实际运行文件 SHA-256 `2fcbc6ca76ad2c7c1557c3d56b1b1e8e5ac4a2307e2ce9d274c8a46868d23c5c`／`b01f88d44bc2f7d9ba0984bca8054e6a0481f8e044837354d9213a92823428a4`，每条原生线逐一核对。
- 原生分片启动因漏带任务复用的 native-core.sh 失败；保留原 ENOENT 和首轮退出，不计作任何原生通过。补齐与上批相同、仅任务路径替换的脚本后，先等在途双库完成及资源释放，再取得同机锁执行原生三分片；未重跑已完成测试、重建或重新发布。
- 缓存复用令两个工具 restore 增量跳过，原锁保留 10.0.12，首轮校验器因此拒绝而退出 1；保留原退出与日志。只对两个工具 force-evaluate，依赖资产完整字节不变、已构建程序集 SHA-256 不变，锁文件产生已批准 Linux SDK 10.0.11 派生，再执行剩余验收，不重建或重新发布。首次恢复脚本末行 CRLF 导致文件路径尾部多出 CR，退出 1，规范为 LF 后同一校验通过，保留失败记录。
- 排队期间以实际程序集的源生成上下文复现旧夹具小写预算字段被读取为 0；改用正式上下文序列化后输入／输出均往返为 100。仅取消本任务尚未执行的排队进程，确认退出后清理其自有工作区锁，其他任务的重型锁不变；保留旧冻结源和诊断，再冻结的差异仅此 Integration 夹具，生产及 Unit 输入不变。静态检查按新冻结输入重跑通过，集中验收使用修正后的唯一输入。
- 冻结 7764 输入：基线归档只叠加本批修改，保护路径在读取／hash 前排除。上批缓存只用于编译复用，字节相同的 7747 输入保留原时间戳，变更输入及消费者重新构建；五个独立运行卷共用一次 Integration 构建，核对源码、程序集、唯一 UID。仅两个工具锁 ILLink.Tasks 10.0.12→10.0.11 单列派生并校验 JSON、包元数据及 SHA512，其余 7762 原输入不变。
- 独立静态副本工具 54/54、命名 30/30、治理 57/57、UUID 3/3、SQL 安全 5/5 与受保护路径排除 Markdown 通过；只有对象注释目录 CRLF→LF 在静态副本单列。静态首次运行早于副本复制完成而缺少目录，未产生通过证据，复制完成后重跑全部静态检查。`node artifacts/r10j/check-final-volumes.mjs`、`python artifacts/r10j/collect-final.py` 核对退出码、TRX、发现及产物摘要；提交前检查 main、diff、任务范围与其他任务原始摘要，共享矩阵仅暂存本批修改。
- 环境 Windows x64、i7-12700H、约 64GiB、SDK 10.0.401；Docker 29.6.2、Linux 约 20CPU／19.3GiB、SDK 10.0.400／Runtime 10.0.11。集中构建／单元 4 处理器，发布 8CPU／12GiB，双库／原生独立线 2CPU／8GiB、GC 堆 2GiB；同机共享锁排队、独立 TEMP，全部子进程结束后释放，保留缓存。`NuGetAudit=false` 仅进程覆盖，在线依赖审计未执行。

供应商实际遵守 MaxOutputTokens 仍依赖契约，此批不认证硬费用或实际输入上界；协作取消不证明外部请求未完成或未收费。累计输入实际硬上界、工具／全节点步数限制、其他运行分支凭证、历史审批目标版本证明与自动外部对账仍开放，R10 不整体关闭；独立生成应用按整个 AI 模块范围集中验收，保持 `Capacity-not-verified`。

#### R10k：累计输入回执与模型／工具派发步数合批（2026-10-11，本批本地验收完成）

- 本批沿用静态定义、运行冻结预算和节点凭证，不增加 SQL、迁移、公共 HTTP 字段或检查点格式。三种运行按输入回执累计扣减；恢复有剩余模型时，未知或耗尽输入在工厂前拒绝；流保守保留所有剩余输入，部分回执不退回。
- 工具循环的模型与工具请求合计最多 64 步，并保留 8 轮上限；本地拒绝工具同样计数，派发、OperationId 解析之前拒绝超限，最后允许步骤上的审批／对账正常暂停。
- 失败路径区分本地未派发拒绝与外部不可信回执，保护现有意图、租约、未知结算和对账语义。真实双库检查点覆盖未知输入、耗尽输入、零输入及无需剩余模型的未知事实。
- [x] 本批新增 36 项 Unit 回归。`node artifacts/r10k/run-focused.mjs red` 构建零警告／错误，231 项中按预期 13 失败、218 通过；非流式与步数实现后 231/231 通过。`node artifacts/r10k/run-input-red.mjs` 19 项中流边界按预期 6 失败、13 通过，随后才实现流输入预留。`node artifacts/r10k/run-focused.mjs final-green` 250/250，通过、零失败／跳过，保留各轮原始日志和 TRX。
- [x] `node artifacts/r10k/launch-scheduled.mjs` 先在工作区锁内运行纯 Linux 编译／无容器用例，再申请共享重型资源执行双库与原生验收，排队不侵占原测试预算。首次尚未执行的重型排队主动撤回，记录退出 4294967295；确认无自有子进程／容器并核对 PID／token 后仅释放自有工作区锁，不动其他任务重型锁，未产生测试失败或替代 TRX。冻结 7767 项输入，以 5db60942＋本批 AI 修改执行；仅字节相同输入保留上一批时间戳，复用构建缓存并按改动增量编译，2 个工具先强制恢复，派生 ILLink 10.0.12→10.0.11 仅跟随 Linux SDK，其他依赖字段／包摘要核对一致。
- [x] Linux 定向 250/250、AI 3182/3182、运行时 460/460、受影响架构 86/86，通过、零失败／跳过；Unit／Integration／Architecture 及 API／Worker AOT 分析构建零警告／错误。Unit 完整发现与唯一矩阵一致；不是全量 Unit／Architecture 执行。
- [x] 正式 slice 分类为 Ai／integration-matrix／Smoke，实际 AI 双库各 44/44，Smoke 双库各 4/4，原生 API 10/10＋Worker SQL Server 12/12＋其他 13/13，全部零失败／跳过。按唯一 UID 验证完整发现、分片、同场景双库和无遗漏；同源码消费者的 Agents／Ai／Integration DLL 与各独立运行卷摘要一致。
- [x] API／Worker linux-x64 实际 Native AOT 发布成功，批准告警门禁 17／15 通过，无新告警；本批发布 SHA-256 为 API `42531180bbc41c2fee1a177836a49fff4d72bd153fede8982d6130f7ad92bb59`、Worker `de245fc4f751fbe43319a86124cb2bb0d3b7dfd5d5f25e5f5ac9835bc9d6e690`，与实际外部进程运行一致。
- [x] `node artifacts/r10k/run-static.mjs` 与 `check-static-copy.mjs`：工具链 54、命名 30、治理 57、UUID 3、SQL 安全 5，通过；文档编码 386 文件通过，排除受保护路径在读取／哈希之前生效。独立静态副本仅规范化既有注释目录换行，运行源码保持冻结。
- [x] 按代码审查技能执行独立只读审查，非流式、恢复、意图清理、工具步数及流式补审均无确认 P1/P2。`node artifacts/r10k/check-final-volumes.mjs`、`python artifacts/r10k/collect-final.py` 核对最终 5 个独立消费卷、冻结输入、编译／发布摘要及精确测试集合通过；分支 main，其他 WIP 保留，仅提交本批 11 项路径及共享矩阵的 AI 变更。

环境：Windows i7-12700H／约 64 GiB，Docker Linux 约 20 CPU／19.3 GiB，Linux SDK 10.0.400／Runtime 10.0.11；主构建 4 CPU／4 GiB，API／Worker 发布各 8 CPU／12 GiB，独立功能及原生消费者各 2 CPU／8 GiB、GC 堆上限 2 GiB，延用既有执行预算。双库 Smoke 串接到对应功能运行线，未增加并行运行线。

报告汇总首次因 Windows 默认 cp936 解码 UTF-8 JSON 退出 1，失败日志保留；显式 UTF-8 读写后核对原始报告通过，未重跑或替换测试结果。

供应商没有请求前精确输入计量认证，本次回执校验不能撤销外部超量／收费；跨审批及崩溃的持久步数、其他分支节点凭证、历史目标版本证明和自动外部对账继续开放。独立生成应用按整个 AI 模块集中验收，R10 暂不整体关闭，保持 `Capacity-not-verified`。

#### R10l：审批目标版本快照、恢复与事务消费合批（2026-10-11，本批本地验收完成）

- 基线 main `0cc6f389fd5d550c022aebc4b7a5e8d1465dd096`，安全排除受保护路径后建立任务快照 `ai-approval-target-revision-r10l`；保留其他任务 WIP。
- 私有 `PresentationJson` 追加服务端目标版本；旧摘要可读但不可执行，重复 OperationId 不重写目标证明。恢复、执行前授权和事务消费共享原始标题、目标正版本与归属复核；同标题不同版本也拒绝。非法或重复快照字段失败关闭，创建等待后的取消不得继续持久化。
- 真实双库沿用审批恢复／HTTP 夹具，覆盖标题 ABA、旧快照拒绝、新审批成功及消费和版本条件写入回滚；不新增迁移、公共 HTTP 字段或外部副作用重放。
- 定向 RED 构建零警告／错误，77 项中 15 项预期失败、62 项通过、零跳过，随后实现；首轮新增测试缺少枚举命名空间的编译失败保留，不计作 RED 行为证据。
- 阶段验证：`node artifacts/r10l/run-focused.mjs green` 定向 77/77；冻结 Linux 源码定向 77/77、AI 3206/3206、运行时 484/484、受影响 Architecture 86/86，零失败／跳过。Unit／Integration／Architecture 及 API／Worker AOT 分析构建零警告／错误；Unit 全量仅发现 6675 项，未执行全量 Unit。工具链、命名、治理、UUID、SQL 安全及文档编码检查通过，独立只读安全审查无确认 P1/P2。
- 首次集中验收中断：`node artifacts/r10l/launch-scheduled.mjs` 的发布／克隆阶段发生 Docker 存储 I/O 错误，Worker 发布出现 `Bus error`，引擎返回 500，数据卷进入 `emergency_ro`。原始日志及非零退出码保留在忽略目录 `artifacts/r10l/infrastructure-attempt-1/`。用户继续授权后完整停止 Docker／WSL，受保护的数据盘正常挂载完成日志恢复；随后不写盘的 `e2fsck -f -n` 检查退出 0，Docker 恢复响应，原始冻结源码及关键程序集摘要通过复核。未强制修复、格式化或重置数据盘，恢复日志保留在 `artifacts/r10l/infrastructure-restart-attempt/`。
- 发布缓存整卷复制再次耗尽主机空间，第二次失败退出码保留在 `artifacts/r10l/infrastructure-space-recovery/`；完整停止 Docker／WSL 后再次正常恢复日志，不写盘的文件系统复核退出 0。已完成批次的验收产物迁往 E 盘，逐文件 SHA-256 一致且原路径保留；本批启动前要求至少 12 GiB 主机空闲空间，运行时低于 1.5 GiB 仅停止本批容器，保留失败状态。API／Worker 使用本批自有空发布缓存，取消发布缓存整卷复制。
- 剩余验收由 `node artifacts/r10l/launch-recovery.mjs` 接续，保留前两轮失败证据；复用上批已交付且无容器引用的五个验收缓存卷，同步本批冻结源码与输出并逐卷核对摘要，不复用上批测试结果。恢复后的双库功能精确成功 UID 并集、补充 Smoke、Native AOT 发布及原生运行已完成；MySQL 原始 MCP 取消失败和非零退出码保留，精确补跑独立记录。Docker 在数据盘和空间复核后恢复服务，验收全过程取得共享重型锁，排队不占实际执行预算。
- [x] `node artifacts/r10l/run-focused.mjs green`：77/77，Release 构建零警告／错误。新增 24 项 Unit 回归，原编译失败与 15 项预期 RED 保留。
- [x] `node artifacts/r10l/launch-scheduled.mjs` 先冻结编译及无容器验证，再取得共享重型锁；本批冻结 7769 项输入，只为字节相同源码复用上批时间戳与编译缓存。Linux 定向 77/77、AI 3206/3206、运行时 484/484、受影响 Architecture 86/86，全部零失败／跳过；Unit 完整发现与矩阵唯一 UID 一致，未执行全量 Unit／Architecture。
- [x] 首轮存储故障后 `node artifacts/r10l/launch-recovery.mjs` 完成 API／Worker 发布、SQL Server AI 44/44＋Smoke 4/4，以及原生 API 10/10、Worker SQL Server 12/12，以上均零失败／跳过。MySQL 功能首轮 44 项中 43 成功／1 失败／零跳过，MCP ping 因取消返回 cancelled；原平台退出 2、集中包装退出 1，报告和非零退出码保留。
- [x] `node artifacts/r10l/run-selected-retry.mjs` 取得共享锁后，以同一冻结程序集、原断言及既有 10 秒工具预算单独补跑失败的 MySQL MCP UID，1/1 通过，再运行遗漏的 MySQL Smoke 4/4；没有重建或放宽期限。测试及 MCP 调用链仅有 Git／工作区换行差异，代码归一化后与基线一致，原始日志显示取消，但未证实资源压力是唯一原因。精确成功 UID 并集为 AI 双库 88、Smoke 8，零未解决失败／跳过，不将原整轮报告改写为全绿。正式 slice 影响集为 Ai／integration-matrix，Smoke 作为补充；上批五个闲置验收缓存卷重新同步本批冻结输入与输出，各运行卷的源码、程序集摘要和发现／实际 UID 集合逐一核对，不复用上批测试结果。
- [x] 原生 Worker 其他分片首轮 13 项中 12 成功／1 失败／零跳过；MySQL TXT 退役夹具在执行原生退役前，JIT 文本代次准备返回 false，原退出 2 保留。知识库实现及两项退役测试在换行归一化后与基线相同；根因未被原日志确定。同一补跑命令以 `--filter-uid` 仅执行该 False 数据行，1/1 通过；使用同一冻结程序集、正式发布 ELF、原夹具和期限，无重新编译或发布。原生核心精确成功 UID 并集 35、零未解决失败／跳过，保留原首轮失败与独立补跑 TRX。
- [x] Unit／Integration／Architecture 及 API／Worker AOT 分析构建零警告／错误；API／Worker linux-x64 Native AOT 发布及运行摘要一致，既有批准告警门禁 17／15，通过。实际 API SHA-256 `8f34f6007029c0607bb8dd82c381deb68127d2beadef632a0876226a31769ebe`、Worker `6c3a46947b4714956e876c48ab65eae19ee84b5fd579f76b68e4bedd0aef123a`。
- [x] 工具链、命名、治理、UUID、SQL 安全及文档编码检查通过；独立静态副本仅规范化既有注释目录换行，运行源码不变。独立只读安全审查无确认 P1/P2 或实质覆盖缺口；`node artifacts/r10l/run-final-checks.mjs` 核对原始日志、TRX、最终消费卷及派生依赖锁，摘要为忽略目录 `artifacts/r10l/acceptance.json`。main 仅提交本批 11 项路径，共享矩阵只暂存 AI 变更，保留其他任务 WIP。

环境：Windows i7-12700H／约 64 GiB，SDK 10.0.401；Docker Linux SDK 10.0.400／Runtime 10.0.11。主构建 4 CPU／4 GiB，发布各 8 CPU／12 GiB，独立功能与原生线各 2 CPU／8 GiB；共享锁及独立 TEMP/TMP，排队不消耗执行预算。仅两个解析工具 ILLink 锁 10.0.12→10.0.11 跟随 Linux SDK 派生，其余输入保持原摘要；`NuGetAudit=false` 为一次性进程覆盖，在线依赖审计未执行。

升级／回退须停准入并排空旧 API／Worker，不允许混跑写工具。目标版本只证明该目标自批准起的版本匹配，不证明外部动作或付费用量。跨恢复持久步数、其他分支凭证、请求前精确输入界限及自动外部对账仍开放；R10 不整体关闭，独立生成应用按整个 AI 模块范围集中验收，保持 `Capacity-not-verified`。

#### R10m：静态工作流跨恢复持久步数与派发准入合批（2026-10-11，本批受影响验收完成）

- [x] 静态工作流新增可空累计 `ExecutionSteps`：新运行从零开始，模型与每个工具节点尝试共用 64 步上限；审批拒绝及取消保留已消耗额度。计数先递增，再与节点派发意图保存，节点完成、审批等待及恢复不重置额度。
- [x] 恢复在模型客户端创建前拒绝缺失、负数、低于已完成前缀、超限及剩余节点耗尽计数；执行器每个节点前重复检查。旧检查点缺失字段保持未知，不从游标补造历史次数；全部节点已完成时允许未知或耗尽计数最终提交。已有 PendingNode 优先进入对账且不结算，避免预算失败丢失未知外部结果。
- [x] 拒绝重复计数字段；保持检查点格式 1、既有 HTTP/预算契约及双库表结构。现有节点意图/完成断言覆盖计数；双库事务故障尝试写入耗尽值，重新读取确认步骤/事件故障均回滚计数。

本地证据：基线 `0a4f4a041d97b22ce01b001ee8e551f1f4d35d52`，任务快照 `ai-workflow-durable-steps-r10m`。首次 RED 231 项中 17 失败／214 通过／零跳过；初步 GREEN 231/231，扩展取消、模型末步和未知派发优先级后 237/237；审查建议补兼容例外，最终冻结输入回归 240/240。新增 27 项，原始 RED/TRX 保留；只读审查未发现已证明缺陷。

- `node artifacts/r10m/run-focused.mjs red`（另两次阶段参数为 `green`、`final-green`）实际三个独立阶段记录：Windows Release 构建均零警告/错误；首次构建 6m38.59s 已正常完成，未终止任何进程。Windows GREEN 仅上述当时范围；最终 240 项以 Linux 冻结输入为证。
- `node artifacts/r10m/run-validation.mjs`：AI 3233/3233、运行时 511/511、受影响 Architecture 86/86，零失败/跳过；Unit、Integration、Architecture 及 API/Worker AOT 分析构建零警告/错误。全量 Unit 仅精确发现 6702，未执行全量 Unit/Architecture。前置辅助脚本首次因未定义元数据变量退出 1，未改缓存或开始编译；修正后成功，不计为行为 RED。复验准备另因发现元数据未拼接命名空间退出 1，未派发任何测试；修正后才执行一次隔离复验，原失败退出码保留。
- `node artifacts/r10m/run-portable.mjs`：SQL Server/MySQL 原始完整 AI 各 43 通过／1 失败，零跳过；原始报告及退出码保留。另以 `node artifacts/r10m/run-selected-retry.mjs` 在原批结束后顺序隔离复验精确失败 UID，各 1/1 通过，保持相同程序集及产品期限；复验前同机持锁磁盘维护恢复了 Docker，环境变化不能证明资源根因。最终成功 UID 并集各 44，完整 Smoke 各 4/4 通过。SQL Server 原失败为知识问答撤权反馈预期 404 实际 500，MySQL 原失败为知识清理激活的 25 秒阶段内取消；失败路径源码与基线一致，资源／根因未证明，不能把重跑解释为缺陷已修复；规范分片发现 1214、两 Provider 精确 UID 并集 88 与 Smoke 8 无遗漏/重复。复用两个空闲功能缓存，运行时源码、测试、构建输入 7771 项及六个程序集摘要在同步前后核对；不复用旧测试结果。
- `node artifacts/r10m/run-static.mjs`：工具 54、命名 30、治理 57、UUID 3 全通过；受保护排除的 Markdown 386 文件通过。`python artifacts/r10m/collect-final.py` 核对精确 UID、原始 RED、输入/程序集、范围和退出码；本批 13 文件，其他任务内容保持快照摘要，唯一矩阵仅提交本批计数变更。

环境：Windows x64／i7-12700H／约 64 GiB，SDK 10.0.401；Docker Linux SDK 10.0.400／Runtime 10.0.11，主构建 4 CPU／4 GiB，功能线各 2 CPU／8 GiB。工作区/同机重任务锁与独立 TEMP/TMP；只继承相同字节输入的缓存时间，两个解析工具 ILLink 锁按 Linux SDK 派生，其他冻结输入不变。本次进程覆盖 `NuGetAudit=false`，未执行在线依赖审计。

本批为 `Aot-analysis-clean`，未新鲜 Native publish/运行；旧 ELF 结果不证明新代码，原生发布与运行及独立生成应用留待 AI 模块集中验收。升级／回退须停止工作流准入并排空旧 API／Worker，旧二进制会忽略计数，不能混跑或删除字段后恢复旧写派发。工具循环等其他分支的跨恢复预算/凭证、提供者请求前输入硬上界与自动外部对账继续开放；R10 不整体关闭，保持 `Capacity-not-verified`。

#### R10n：单模型派发凭证与崩溃恢复（2026-10-11，本批受影响验收完成）

本批基线 `b50604eabc8f2b1905f51b8de4ec1ed8ed1c6921`，在 main 合批推进单模型派发意图、完成凭证、恢复准入及双库领取，不扩展工具循环重放。任务快照 `ai-single-model-recovery-r10n` 排除受保护文件后建立，保留并隔离其他窗口的日志与工具链改动。

- [x] 在 `AiSingleModelRecoveryTests.cs` 先验证派发前无意图、未知结果被重发、旧检查点被忽略及租约提交失败仍派发的失败场景。
- [x] 新增模块内部 `AiSingleModelCheckpoint` 与静态 JSON 闭包，绑定 Run/Session/Definition、模型修订、冻结预算与实际输入摘要。协调器先保存意图再派发，完成后原子保存结果凭证和步骤；恢复完成结果不再创建客户端，未知结果保留预算转入对账。
- [x] 成对扩展过期单模型领取条件，复用既有检查点/事件与租约事务，不增加数据库表；双库验证真实读写、过期重领、旧 Worker 拒绝和事务回滚。补齐恢复载荷、模型/Prompt 漂移、取消与未知计量回归。
- [x] 集中运行当前输入的 AI、相关架构、AOT 分析及受影响双库，更新实际矩阵和运维边界并提交。Native 原生产物与独立应用仍按 AI 模块集中验收，工具循环逐步恢复和自动外部对账不由本批关闭。

本批新增 37 项回归。复审补齐检查点读取失败、会话撤权及模型/策略漂移时的预算保留；已完成凭证仅收敛终态，不再次创建客户端或调用模型。最终复审无新增可证实问题。

实际验证与边界：

- `node artifacts/r10n/run-focused.mjs red-retry`：首次行为 RED 为 211 项中 5 失败；随后 `green` 为 211 项中 3 失败，其中原取消断言需匹配新增意图、夹具模型 ID 改为类型化解析。`green-final` 为 243 项中 2 失败，精确暴露检查点读取异常发生在预算保留标记之前的问题；修复后由最终 Linux 277 项回归覆盖全部失败 UID。原始报告和退出码保留，不将中间失败记作通过。首次 Windows 构建中断记录保留，后续构建成功；最终代码以 Linux 冻结输入验证为准。
- `node artifacts/r10n/run-validation.mjs`：定向 277/277、AI 3270/3270、运行时 548/548、受影响 Architecture 86/86，零失败/跳过；Unit、Integration、Architecture 及 API/Worker AOT 分析构建均零警告/错误。全量 Unit 精确发现 6739，未执行全量 Unit 或 Architecture；规范分片发现 1214。
- `node artifacts/r10n/run-portable.mjs`：SQL Server/MySQL AI 各 44/44、Smoke 各 4/4，首轮全部通过、零跳过；精确 UID 并集为 AI 88 与 Smoke 8，无遗漏或重复。单模型凭证双库真实读写、事务回滚、过期重领及旧租约拒绝包含在既有检查点用例中。
- `node artifacts/r10n/run-static.mjs`：工具 54、命名 30、治理 57、UUID 3 全通过；受保护路径在读取前排除，Markdown 386 文件通过。文档收尾后重跑保护排除的 Markdown 检查。`python artifacts/r10n/collect-final.py` 核对精确 UID、历史 RED、7773 项冻结输入、程序集摘要、实际退出码与任务范围；只提交本批 12 文件，共享矩阵仅提交 AI 计数，其他窗口内容保留。

环境与 R10m 同机：Windows x64／i7-12700H／约 64 GiB；Docker Linux SDK 10.0.400／Runtime 10.0.11，主构建 4 CPU／4 GiB，功能线各 2 CPU／8 GiB。持工作区和同机重任务锁；复用前后验证源码及程序集摘要，只复用构建缓存，不复用测试结果。两个解析工具 ILLink 锁按 Linux SDK 派生，其他冻结输入不变；本次 `NuGetAudit=false`，未执行在线依赖审计。

本批为 `Aot-analysis-clean`，未新鲜 Native publish/运行或独立生成应用验收，仍在 AI 模块集中验收补齐。升级/回退须停准入并排空旧 API/Worker，不能混跑忽略凭证的旧 Worker。工具循环逐步凭证、运行终态与预算结算之间的存量故障恢复、请求前输入硬上界及自动外部对账继续开放；R10 不整体关闭，保持 `Capacity-not-verified`。

#### R10o：成功终态与预算结算原子提交（2026-10-11，本批受影响验收完成）

基线 main `3067fc479147a48b56602cd87d53630cfa8c4e05`；保护排除的任务快照 `ai-agent-completion-settlement-r10o`。本批归属 AI 模块，修复三个执行分支成功终态与账本之间的崩溃窗口，不新增表、公开协议或外部对账权限。

- [x] 在 `AiAgentCompletionTests.cs` 验证单模型、工作流、工具循环结算发生在完成事件的同一未提交事务；注入结算异常，先取得回滚缺失及错误改判的 RED。
- [x] 新增内部 `AiAgentRunCompletion`，以同作用域 `ICommandTransaction`、`AiAgentRunStore` 和预算存储完成原子提交；租约未命中不得结算，取消与异常回滚。协调器三分支接入，完成收尾失败不降级为普通失败。现有完成凭证保留供单模型/工作流重领；工具循环未知过程仍不自动重放。
- [x] 双库真实事务验证已知/未知计量、预算缺失与冲突、事务末端失败、旧租约拒绝、Host/租户隔离及重领后的幂等结算；保留历史终态未结算记录的外部对账边界。
- [x] 运行直接回归、AI 共享消费者、相关架构及 AOT 分析，按冻结源码执行受影响双库、同步运维记录并在 main 提交。Native 与独立应用仍按 AI 模块集中验收。

本批新增 17 项单元回归。`node artifacts/r10o/run-focused.mjs red` 首轮 249 项中 6 项按预期失败，原报告保留；`green` 为 260/260。`node artifacts/r10o/run-validation.mjs` 对冻结输入执行定向 294/294、AI 3287/3287、运行时 565/565、受影响架构 86/86；`node artifacts/r10o/run-portable.mjs` 补充本地事务边界 4/4。SQL Server AI 44/44 与 Smoke 4/4 首轮通过；MySQL AI 首轮 43 通过、1 MCP 远端 ping 返回 cancelled 失败，保留原始 TRX 与非零退出码。`node artifacts/r10o/run-selected-retry.mjs` 在原分片退出后按失败 UID 隔离复验 1/1 通过，并补充 MySQL Smoke 4/4；最终双库成功 UID 并集为 AI 88、Smoke 8，零跳过。全 Unit 精确发现 6756 项，仅执行上述受影响集合，不声明全 Unit 运行通过；Integration 分片发现 1214 项，无遗漏或重复。

失败调用直接经过统一工具执行器，不经过本批修改的运行完成事务。失败入口与 MCP/工具调用链源码均与基线一致；复验保持同一冻结源码、程序集和产品 10 秒期限，未放宽断言或重编译。取消的底层原因仍未证明，隔离复验通过不等于并发环境问题已修复。

复验首次排队因重型锁所属进程退出而停止，未启动测试；另一任务发布容器结束、确认原进程及进程家族退出后才归档遗留锁并恢复排队。排队失败退出码与清理前证据单独保留，没有中断其他任务的容器。

双库使用生产 DI 与真实事务，成对覆盖 Host/租户下已知与未知计量、预留缺失、冲突、错误作用域、结算后物理提交前故障、事件唯一键失败、旧租约拒绝，以及过期重领和终态幂等。兼容租户配额与运行、步骤、检查点、事件、账本共同提交/回滚，不新增迁移。成功收尾失败不改判执行失败；单模型/工作流沿已有完成凭证恢复，不重新派发。

验收环境为 Windows i7-12700H/64 GiB、Linux SDK 10.0.400 / runtime 10.0.11；构建限 4 CPU/4 GiB，双库消费者各 2 CPU/8 GiB。沿用已验证来源的缓存卷与工作区/重型测试锁，只复用构建资源，不复用测试结果。冻结输入 7776 项；两个解析工具锁文件的 Linux SDK 派生差异单独登记并核对。冻结后只纠正矩阵四个发现下限（6756/3287/565/260），运行命令原已强制新下限，筛选器及全部编译输入不变；修正矩阵后的独立静态副本重新通过检查。双库消费者源码和共享程序集摘要一致。

`node artifacts/r10o/run-static.mjs` 通过 tooling 54、命名 30、治理 57、UUID 3 及保护排除后的 Markdown 386 项检查。API/Worker 的 `dotnet build -c Release -p:FullNetAotAnalysis=true -p:NuGetAudit=false` 均零警告/错误；本批没有联网 NuGet 漏洞审计。独立只读代码复核无阻断发现；预算 Metrics 在外层物理提交前可能记录已回滚尝试的 P3 限制已记入运维文档，账本仍为对账依据。

本次只提交 10 个归属文件，共享测试矩阵仅暂存四个 AI 计数变化，其他窗口改动与保护排除路径保留。原始报告及核验摘要位于 `artifacts/r10o`。本批状态为 `Aot-analysis-clean`；新鲜 Native publish/运行和独立生成应用验收仍按 AI 模块集中补齐。历史终态未结算修复、失败/取消分支独立结算、工具循环逐步恢复、请求前输入硬上界、自动外部对账及提交后 Metrics 继续开放；R10 不整体关闭，保持 `Capacity-not-verified`。

#### R10p：失败与会话拒绝收尾原子结算（2026-10-11，本批受影响验收完成）

基线 main `201914da442b88db0181e5f3fb7fc77ca96e8805`；任务快照 `ai-agent-rejection-settlement-r10p`。本批扩展 R10o 的 AI 本地事务，覆盖模型不可用等失败、单模型/工作流派发前拒绝及可结算的会话失效；已有派发凭证要求保留预算时仍不结算。用户主动取消、历史终态修复和工具循环逐步恢复不在本批。

- [x] 新增 `AiAgentRejectionSettlementTests.cs`，先验证四条路径同事务和结算失败回滚的 RED，再补取消、事件失败和旧租约拒绝。
- [x] `AiAgentRunCompletion` 增加受限拒绝入口，固定状态到结算结果的映射；协调器所有拒绝结算复用该入口，失败不吞成成功、不二次改判或结算。
- [x] 扩展现有双库完成断言，覆盖失败/会话拒绝的 Host/租户账本、兼容配额和末端回滚，不新增表、迁移或公开契约。
- [x] 按最终冻结输入集中执行受影响回归、双库与 AOT 分析，记录未验证范围后在 main 提交。Native 与独立应用仍归 AI 模块集中验收。

本批新增 20 项单元回归。`node artifacts/r10p/run-focused.mjs red` 为 268 项、260 通过/8 预期失败，原报告保留；`green` 为 280/280。冻结验证流水线（`node artifacts/r10p/run-validation.mjs` 与 `node artifacts/r10p/resume-validation.mjs`）执行定向 314/314、AI 成功 UID 并集 3307、运行时 585/585 和相关架构 86/86；`node artifacts/r10p/run-portable.mjs` 补充本地事务边界 4/4，与后续精确补验共同覆盖 SQL Server/MySQL 各 44 项 AI、各 4 项 Smoke，成功 UID 并集 88 与 Smoke 8，零跳过。全 Unit 精确发现 6776 项，只执行本批受影响集合；Integration 分片发现 1214 项，无遗漏或重复。

Linux AI 首轮 3306 通过、1 项 DOCX 子进程取消清理失败（期望取消异常，实际清理未确认），原始 TRX 与非零退出码保留。`node artifacts/r10p/resume-validation.mjs` 对精确失败 UID 隔离复验 1/1 通过；失败调用链与基线一致，复验保持冻结源码、全部 Unit DLL 摘要和原 45 秒清理期限，未重编译或放宽断言。底层原因未证明，不将隔离通过写成并发问题已修复。随后从运行时回归继续原流水线。

SQL Server AI 首轮 42 通过/2 失败，其中一项为新增断言的预期错误：拒绝提交传入未知计量，既有账本收到未知回执应保留已知计量并幂等返回，不能期待结算冲突。已将该场景修正为 known-ledger，验证运行拒绝终态提交而原 known/succeeded、输入输出各 1、收费 2 和配额不变；独立只读复核确认契约一致。原冻结输入及失败报告保留，唯一集成断言文件以显式修订登记，生产及 Unit 程序集摘要保持一致，仅重建集成测试。`node artifacts/r10p/run-revision.mjs` 按精确 UID 复验 SQL Server 工作流断言；MySQL 原批因 Docker 引擎退出而中断，没有完整 TRX。沿此前授权恢复 Docker、核实进程及卷归属后清理本任务遗留锁；完整重跑在 44 分钟会话期限后终止调度，等待在途清理后以 Aborted/退出码 3 结束，终端汇总 23 通过、0 失败，仍不视为整批通过。随后用当前安装的 Microsoft.Testing.Extensions.TrxReport 2.3.2 官方 ReadAll 序列化实现读取两次原始结果流，并逐条核对完整帧、Passed 状态、结束时间、UID、源码与程序集，得到 35 与 23 条完整结果、成功 UID 并集 35；唯一修订断言的工作流用例不在这些结果中。原始流、摘要及中断/Aborted 状态全部保留，不伪造 TRX 或覆盖退出码。共享重型锁释放并留出其他窗口协调机会后，`node artifacts/r10p/run-tail.mjs` 只补跑剩余 9 项；该轮在 20 分钟会话期限后 Aborted，原始结果流恢复 5 个 Passed（含修订工作流）及 1 个 Failed，累计成功 UID 并集 40。唯一失败为既有 MCP 远程 ping 期望 succeeded、实际 cancelled；其调用链与基线一致，统一工具执行器原 10 秒预算未修改，具体取消根因未证明。`node artifacts/r10p/run-final-four.mjs` 在各阶段独立持有/释放重型锁，等待其他窗口验收先取得资源后，仅执行该 MCP 精确 UID 的一次隔离复验 1/1、此前未完成的 3 项 3/3 及 MySQL Smoke 4/4；全部使用原源码与最终程序集。两库成功 UID 并集 88，Smoke 总计 8/8；不宣称任何中断批次完整通过；未重复构建或执行无影响单元集合。

SQL Server 另有已有知识索引状态用例在文本代次生成断言返回 false；失败调用链源码与基线一致，修订集成程序集后隔离复验该精确 UID 1/1 通过。生产程序集、30 秒租约和 25 秒阶段期限保持不变；具体拒绝分支及底层原因未证明，不能将隔离通过写成问题已修复。

真实双库复用工作流夹具与生产 DI，覆盖 Host/租户下 failed、authorization_required 两种状态的正常结算、预算缺失、已知账本保真、错误作用域、外层提交前失败、事件冲突及旧租约拒绝。回滚保留原运行版本/检查点/事件/预算/兼容配额；失败场景补预留或重领后可用当前租约收敛，旧租约和重复终态提交不得再次结算。未知回执保留已知账本事实，结算异常回滚由协调器 Unit 受控失败覆盖。协调器取消传播、事件失败、失租及禁止二次结算由新增 Unit 回归保护。

冻结输入 7778 项，生产编译输入和共享程序集摘要一致，双库最终消费者源码按唯一测试断言修订核对；矩阵四个发现计数在冻结前同步，静态副本独立验证。沿用已核验来源的构建缓存及工作区/重型测试锁，不复用旧测试结果。环境为 Windows i7-12700H/64 GiB、Linux SDK 10.0.400/runtime 10.0.11；构建 4 CPU/4 GiB，双库消费者各 2 CPU/8 GiB。解析工具锁文件的 Linux SDK 派生差异单独记录，未修改仓库锁文件。

`node artifacts/r10p/run-static.mjs` 通过 tooling 54、命名 30、治理 57、UUID 3 和保护排除后的 Markdown 386 项检查。API/Worker `dotnet build -c Release -p:FullNetAotAnalysis=true -p:NuGetAudit=false` 均零警告/错误；本批未做联网 NuGet 漏洞审计。独立只读复核无阻断问题；准备阶段异常的直接回归为非阻断补充建议，现有事务内失败覆盖已通过。

只提交本批 8 个归属文件，共享矩阵仅暂存四个 AI 计数，保留其他窗口改动及保护排除路径。原始证据位于 `artifacts/r10p`。状态为 `Aot-analysis-clean`；新鲜 Native publish/运行与独立生成应用继续按 AI 模块集中验收。用户主动取消接口、历史终态未结算修复、工具循环逐步恢复、请求前输入硬上界、自动外部对账及提交后 Metrics 仍开放；R10 不整体关闭，保持 `Capacity-not-verified`。

### R10q：用户取消、事件与预算原子收尾（2026-10-11，本批聚焦验证完成）

沿用 main，基线 `24d0bde51e1b4e333ef94d6878162383a5a632d4`。取消仅接受现有 queued/running 状态，保留重复取消 422、其他所有者/租户 404；不扩大取消审批中运行的契约。现有先取消再吞预算错误会留下不一致终态，本批采用同一 AI 本地事务收敛。

- [x] 在 `AiAgentRunCancellationTests` 建立失败证据；覆盖 Host/Tenant、预算失败、事件失败、取消令牌与落败更新。
- [x] `AiAgentRunManagementService.CancelAsync` 使用命令事务包围取消和 unknown 预算结算；异常传播到外层以确保回滚，HTTP 使用统一安全异常映射；已知账本不降级，不推断零用量。
- [x] `AiAgentRunStore.TryCancelOwnedAsync` 在条件更新后写入单调序号 `run.cancelled`，同事务撤销租约；事件写失败或调用方取消均回滚。
- [x] 扩展现有双库 Agent API 夹具验证状态/事件/账本/配额原子性、所有权、重复、完成与取消先后竞争；运行聚焦 Unit、双库、AOT 分析与架构检查后仅提交本批文件。

- [x] 补齐 AG-UI 持久取消事件到既有终态边界的映射、终态分页耗尽与快照竞争去重；用真实流输出回归验证历史/对账载荷的源生成序列化。

本批新增 19 个 Unit 用例。取消回归先复现 18 例中 11 例失败，修复后 18/18；真实流输出先复现 6/6 失败，修复后 6/6。`node artifacts/r10q/run-consumers-final.mjs` 执行运行时、AG-UI、预算与租户配额消费方回归 **623/623**；发现 AI 用例 **3326**，未宣称完整 AI 套件已执行。工作区 Unit 发现 6805 例，其中 10 例属于其他窗口未提交日志测试，正式矩阵仅增加本批 19 例至 6795。

`node artifacts/r10q/run-build.mjs` 在冻结源码上完成 Integration/Architecture 构建与聚焦架构验证 **86/86**，API、Worker 使用 `dotnet build -c Release -p:FullNetAotAnalysis=true` 分析均为 **0 warning / 0 error**。`node artifacts/r10q/run-databases.mjs` 复用同一组经哈希核对的程序集，SQL Server **6/6**、MySQL **6/6**；合计 Agent API/AG-UI 4 例和 Smoke 8 例，均无失败或跳过，包含新增 Host/Tenant 原子取消场景。Linux 验证沿用已记录的两个解析器锁文件 SDK 补丁适配，未修改仓库锁文件；来源及哈希记录于 `artifacts/r10q/derived-locks.json`。

命名 30/30、矩阵 6/6、治理 57/57 通过；受保护路径读取前排除的 Markdown 检查通过。独立复审无阻塞项。原始中断、测试替身编译错误、Docker 引擎不可用及遗漏手工构造夹具事务参数的首次构建失败均保留，最终修复后结果与证据核对见 `artifacts/r10q/acceptance.json`；`python artifacts/r10q/collect.py` 校验 TRX 数量、退出码及源码哈希通过。共享矩阵仅提交本批三个计数与运行时过滤器，其他窗口改动保留。

不新增迁移、对外 DTO、权限或外部调用；历史终态修复、工具循环逐步恢复、输入硬上界、自动对账和提交后 Metrics 仍待后续。状态仅为 `Aot-analysis-clean`；完整 AI 回归、Native publish/运行、独立生成应用及页面验收继续按模块集中执行，R10 不整体关闭，保持 `Capacity-not-verified`。

### R10r：预算指标提交后发布（2026-10-11，已实现并完成本批验证）

基线 `27257ed963b740529322fa6315bc187de6f405d6`，沿用 main。预算账本的预留与结算指标必须等到最外层本地事务成功提交后发布；外层异常、失败 Result 或提交失败丢弃未提交事实。指标只作临时观测，不承担可靠业务通知或持久化恰好一次语义。

- [x] 用真实预算服务、Dapper 命令事务和 MeterListener 复现嵌套事务提前计数；覆盖预留、结算、回滚、幂等及监听器异常。
- [x] 在 Messaging 抽象增加窄提交观测契约，由同作用域 Dapper 命令事务管理；回滚清空，成功提交后逐项发布，观测异常不能改变已提交结果。DI 两个接口必须解析到同一实例。
- [x] 预算成功指标在事务内注册观测，拒绝/取消/失败尝试指标保持原语义；费用只使用配置币种，标签不增加用户、租户、模型或 Run。
- [x] 聚焦共享事务消费者与预算回归，扩展既有双库预算夹具核对真实提交/回滚时序；执行架构、AOT 分析、静态检查和独立复审，更新矩阵后提交本批文件。

本批新增 26 项 Unit（预算观测 12、事务观测 14），覆盖外层回滚/失败 Result/提交失败、重复预留或结算、监听器异常、重入顺序、外部事务拒绝登记、DI 同实例与忽略取消令牌后的提交阻断。首次有效红灯为预算 8/12 失败；复审后取消阻断补充用例也先出现预期失败，再修复。测试夹具的编译和异步释放错误及两次构建缓存校验失败均保留原始证据，没有登记为通过。

实际验证命令与结果（命令封装、完整过滤器和 TRX 在 `artifacts/r10r/`）：

- `dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release` 与 `node artifacts/r10r/run-green.mjs`：构建零警告/错误，46/46 聚焦回归通过。
- `node artifacts/r10r/run-consumers.mjs`：3,635/3,635、零跳过，含完整 AI Unit 3,338、Data 197、其他直接事务消费者 100；发现 UID 与 TRX 集合完全相同。完整 Unit 未执行；工作区发现 6,831，其中既有本地日志 WIP 10 不属于本批，提交矩阵为 6,821，AI 3,338、运行时 609。
- Linux Integration 与 Architecture 项目 Release 构建均零警告/错误；`node artifacts/r10r/run-resume-build.mjs` 中受影响架构过滤 86/86 通过，API/Worker 的 `-p:FullNetAotAnalysis=true` 构建均零警告/错误，仅证明 `Aot-analysis-clean`。
- `node artifacts/r10r/run-databases.mjs`：SQL Server 与 MySQL 各预算 1/1、Agent API/AG-UI/Smoke 6/6，共 14/14、零跳过，分别核对发现 UID 与 TRX。预算夹具独立进程运行，核对回滚无指标/无账本、新事务提交后计数/费用和重复结算无追加；双方复用同一组冻结程序集并在运行前后校验哈希。
- `node artifacts/r10r/run-static.mjs`：命名 30/30、矩阵 6/6、治理 57/57；保护路径先排除再读取的 Markdown 检查 386 文件通过。提交前另复核 Markdown、编译源哈希、外来改动不变与差异空白检查。独立复审未发现遗留阻断，本批不新增 SQL/迁移、外部调用、权限或 HTTP DTO。

构建从 HEAD 加本批文件冻结，未纳入日志/Helm/测试资源等外来 WIP；Linux SDK 10.0.400 与 Windows 10.0.401 的解析器锁差异只在构建缓存派生，仓库锁不改动。详细计数、哈希、范围和原始失败汇总见 `artifacts/r10r/acceptance.json`。

最终暂存差异检查另发现新事务测试文件末尾多余空行，仅删除该空行后通过检查；可执行文本未改变，不为纯格式重复构建或运行回归，编译源与提交源的两字节差异由 `postbuild-format.json` 严格核对并保留。

扩大的全量架构检查实际为 232 通过、3 失败、0 跳过：知识库 PDF 路径存在 11 处受禁止的 Task.Run/Task.Result，两个索引激活服务有 6 个异步方法缺少 Async 后缀，`AiAgentRunLeaseWatch` 尚未加入精确租户写入允许清单。失败检查与对应生产源码相对本批基线内容不变（仅归档行尾差异），本批受影响的 86 项架构检查已通过；保留失败证据，不降低检查规则。上述问题继续开放，不能据此宣称全量架构检查通过。

补充来源验证：76 个相关文件与 HEAD 内容一致，租户写入消费者集合也完全一致。以 HEAD 归档生产源码覆盖只读容器源码目录、复用本轮测试程序集，重新执行两项源码检查，得到相同 11 处异步边界违规与租户清单失败（2/2 失败为预期复现）；命名问题由相同违规源码确认，未另行编译整个基线，不能把此复现称为完整基线构建验收。证据保留于 `artifacts/r10r/architecture-baseline-inputs.json`、`architecture-baseline-source/`。

其余 AI 集成、Native 发布/运行、独立生成应用及页面继续集中验收；历史终态修复、逐步工具恢复、输入硬上界及自动外部对账继续开放，R10 不整体关闭，保持 `Capacity-not-verified`。

### R10s：PDF 有界异步边界与架构回归收敛（2026-10-11 启动，2026-10-12 本批完成）

基线 `701e0804bb74d712d4ce27cb21112259e1291924`，沿用 main。收敛 R10r 扩大架构检查发现的三项失败，保持现有检查规则，仅补充已复核的精确租约写入登记；同步阻塞入口仍须隔离以保持取消保障。

- [x] 在 `AiKnowledgePdfIoBoundary.cs` 建立仅供 PDF 使用的有界专用线程调度；普通 IO 与清理各 4 个后台线程及 8 个额外准入位置，最多各 12 个未完成操作。异步等待继续使用 await，容量耗尽立即拒绝；任务完成前不释放准入容量；清理在 Files 打开来源及预算预留前保留位置，未使用位置与迟到持有引用同样计入上限，已接纳资源的最终清理只提交一次且不再竞争准入。线程不继承首次调用的请求上下文，每个任务单独捕获和恢复 ExecutionContext，以保留已有来源证明寿命而不污染后续任务。先以旧 Task.Run 适配建立容量/线程隔离红灯，再实现调度。
- [x] 5 个 PDF 来源、规划、授权、模型与沙箱边界替换 Task.Run/Task.Result；清理使用独立通道，保留超时、取消、迟到资源释放、收费回执冻结及已经开始的证明事务等待。原始流不能强制终止，清理未确认仍须拒绝交付；不把线程边界宣称为任意代码沙箱。
- [x] 两个索引激活 Store 的 6 个私有异步方法补 Async 后缀；逐项核对租约监视器的独立 Host scope、CAS SQL 与 finally 清理后，仅将该精确文件登记到租户写入允许清单。SQL、迁移、权限、HTTP DTO 均不变化。
- [x] 新增真实调度、容量、取消、异常、上下文与清理回归，集中运行完整 AI Unit、全量 Architecture、API/Worker AOT 分析和静态矩阵检查；独立复审后更新运维说明与证据并提交。Native 发布/运行、生成应用和浏览器仍按 AI 模块集中验收，保持 `Capacity-not-verified`。

实际验证（本地集中批次，证据目录 `artifacts/r10s/`；i7-12700H、20 逻辑处理器、约 64 GiB 内存，Linux 验证容器 4 GiB 内存，Windows 完成后 CPU 配额由 4 提至 8；该范围不构成容量实测）：

- Windows `dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore -p:NuGetAudit=false -p:UseSharedCompilation=false -m:2` 通过，零警告/错误。新增 PDF I/O 13/13、完整 `FullyQualifiedName~Full.NET.UnitTests.Ai` 3351/3351，全为通过且零跳过；TRX 与完整发现 UID 集合相等。工作区发现 6844 项，其中 10 项既有本地日志外部改动不属本批；canonical Unit 下界从 6821 增至 6834、AI 从 3338 增至 3351，未宣称执行全量 Unit。
- 冻结 HEAD 加本批源码，不混入日志、Helm、CI 等既有改动。Linux `dotnet build tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release` 与完整 Architecture 235/235 通过、零跳过，三个既有门禁失败均已关闭；先前 R10r 232/235 是保留的历史失败记录，不回写为通过。
- API/Worker 分别 `dotnet build ... -c Release -p:FullNetAotAnalysis=true` 零警告/错误，保持 `Aot-analysis-clean`；未执行本批 Native 发布或真实运行。冻结源码命名 30/30、矩阵 6/6、治理 57/57、受保护路径排除后的 Markdown 检查及 Integration 分片发现完整性校验均通过。
- 红灯先保留原 Task.Run 适配，8 项中 4 项失败；独立复核发现清理饱和会丢失流释放和收费结算后，新增保留额度验证再产生 11 项中 3 项失败。实现提前保留、迟到引用和一次提交后，13 项全部通过；关闭等待和真实 SourceReader 满清理通道均有回归。两项 Important 复核问题（饱和所有权、参数重名）和容量运维说明已收敛。
- 首次冻结分片发现缺少程序集、Windows 文件复制保留旧时间戳导致复用旧 AI 程序集的编译失败及中止的 Linux 尝试均保留，不计为通过。刷新本批源码时间戳后重新构建，Unit 消费的 AI 程序集哈希与新模块输出相同；复用缓存先校验完整输入哈希，仅同步实际变化文件。Linux SDK 10.0.400 的两项解析器锁差异继续使用既有受控派生证明；为该验收容器复用本机已有 Node 24.12.0 运行时完成分片发现，不修改生产镜像。
- SQL、迁移、权限、HTTP DTO、预算结算规则均未改变；本批不重复 R10r 已通过的双库集成 14 项。Native 发布/运行、生成应用、浏览器/人工与真实云模型整代次验收继续按 AI 模块集中推进，R10 不整体关闭，保持 `Capacity-not-verified`。

### R10t：Agent 请求输入有界准入（2026-10-12，本批本地验收完成）

基线 main e711da2b50567103837eda6d9ca490690447c28b；任务快照 ai-agent-input-admission-r10t，保护排除路径在读取与摘要之前排除，保留其他窗口改动。

- [x] 在预算客户端的流式与非流式模型派发前统一检查消息、内容、工具说明及 JSON 参数；超限返回现有预算拒绝，不调用底层客户端，也不消耗输出额度。
- [x] 有界枚举并冻结受支持文本与工具载荷，拒绝未知对象和未经检查的扩展载荷；覆盖合法边界、累计规模、取消及修改来源后的隔离。
- [x] RED 后实现并集中执行受影响 AI、静态检查与 AOT 分析，记录实际结果，复核归属后在 main 提交。

此批约束服务端接纳的请求规模，不宣称供应商请求前精确 Token 或费用硬认证；历史终态修复、工具循环逐步恢复与自动外部对账仍待完成。原生产物、页面及独立应用继续按整个 AI 模块集中验收，保持 Capacity-not-verified。

本地验证记录：

- 初始两条超限行为 RED 2/2 失败；首轮实现编译出现三个类型/空值错误，已修正。SDK 工具默认空扩展字典误拒导致中间 AI 3367/3392 通过、25 失败，原结果保留；实际探针确认字典非空引用但 Count=0 后修正。复审 RED 38/46 通过、8 失败；有界 writer 仍存在框架内部转义分配，中间 AI 3396/3397 通过、1 失败，追加 JSON RED 46/50 通过、4 失败。最终改用 .NET 10 JsonMarshal 原始只读 UTF-8 视图，先限字节、有界复制、精确统计原始 UTF-16 字符，再对副本解析、检查并冻结。
- node artifacts/r10t/run-unit.mjs final-span：Windows Release 构建零警告/错误；完整 AI 3401/3401，零失败/跳过。新增准入 50 项与运行时集合 659 项均通过实际发现 UID 和完整 AI TRX 身份交集确认，未重复构建或执行。Unit 发现工作区 6894 项，含其他窗口 10 项本地文件日志测试；本批 canonical 矩阵 6884，不宣称执行全部 Unit。
- node artifacts/r10t/run-linux.mjs：冻结 HEAD 加本批归属输入，复用前/后均核对完整源码摘要；仅复用构建缓存，不复用测试结果。API 与 Worker FullNetAotAnalysis Release 构建零警告/错误。相关 Native AOT/MemoryPack/AI 依赖/异步架构筛选实际 88/88，通过、零跳过；Architecture 自身重建时不重复重建已经分析的项目依赖。Integration 分片仅实际发现 1214 项，五分片及 legacy/current 迁移无遗漏/重复；未执行 Integration 业务测试。SQL、迁移、权限、HTTP DTO 和账本行为未修改，本批不重复双库业务验收。
- node artifacts/r10t/run-static.mjs：命名30、矩阵6、治理57通过；保护排除的 Markdown 扫描通过。node artifacts/r10t/post.mjs 与 python artifacts/r10t/collect.py 核对实际 UID、TRX 无失败/跳过、最终源码 SHA、AI 模块/Unit 消费程序集同摘要、源码与输出时间及 main 基线。git diff --check 通过，其他窗口改动摘要保持原样；共享矩阵仅暂存本批 Unit/AI/runtime 计数及精确选择器。
- 环境为 Windows x64 SDK10.0.401，Linux Docker SDK10.0.400/runtime10.0.11，i7-12700H/约64GiB，Windows 限4处理器；本批 Linux 构建容器8CPU/4GiB，程序集进程4处理器，持工作区和机器重任务锁。固定解析工具 ILLink 锁沿已有 Linux SDK 派生证据；未变更仓库依赖。NuGetAudit=false，未执行在线依赖审计。只读复审的重要问题已修正，最终复审未发现剩余有证据的 Critical/Important；复审不代替执行证据。

原始结果与摘要保存在 artifacts/r10t；状态为 Aot-analysis-clean。没有本批新鲜 Native publish/运行、独立生成应用或浏览器/人工验收；继续按 AI 模块集中执行。请求接纳规模已交付，但供应商实际输入 Token/费用硬认证、历史终态未结算修复、工具循环逐步恢复与自动外部对账仍开放，R10 不整体关闭，保持 Capacity-not-verified。只提交本批六个归属文件，不推送。

### R10u：工具循环整轮凭证与崩溃恢复（2026-10-12，本地受影响验收完成）

本批在 main 合批推进工具循环派发意图、完成回执、恢复准入、预算保留与双库过期扫描。基线 dc5f1386588da16f30bf7cabf3978f78d5eb4b9a，任务快照 ai-agent-tool-loop-recovery-r10u；受保护文件在读取与哈希前排除，其他窗口日志与工具链改动保留。

- [x] 行为 RED：新增 23 项回归全部因缺失凭证或扫描能力失败，零跳过；首次 21 项全部失败但最低发现数误设为 22，原报告保留，不计规范 RED。正确发现数的第二轮 23/23 失败用于实施依据。
- [x] 将模块内部凭证改名 AiModelRunCheckpoint，保持单模型已有载荷兼容，并用于工具循环整轮 started/completed。意图先于任何循环派发、完成回执先于最终收尾；失租不继续派发或结算。
- [x] 当前会话、模型和实际输入校验后直接恢复完成回执与原用量；未知、损坏、缺失或不可读凭证禁止重发，撤权保留此前预算。后续本地预算拒绝和步数耗尽不被解释为整轮尚未派发。
- [x] SQL Server/MySQL 扫描同时增加工具循环定义；既有检查点双库场景成对覆盖真实写入、读回、完成回执回滚、过期扫描、旧 Worker fencing 及无凭证历史运行拒绝。无新表或迁移。
- [x] 当前输入的 AI、相关架构、AOT 分析、成对完整 AI Integration 与只读复审通过；逐项核对发现 UID、TRX、消费者程序集摘要与提交范围。共享测试矩阵仅提交本批 +28 计数。

本批实际验证与证据：

- node artifacts/r10u/run-final-unit.mjs final：Release Unit 消费程序集构建零警告/错误，AI 命名空间 3429/3429 通过、零失败/跳过（1m14s265ms），新增整轮恢复 28 项全部通过。首轮 GREEN 的 7 项旧断言失败保留，修正为意图事件先落库及未知派发不结算；不是删除回归。
- node artifacts/r10u/run-linux.mjs：冻结本批归属源码，Linux API/Worker 的 FullNetAotAnalysis=true 构建零警告/错误；受影响 NativeAot、MemoryPackControlledProtocol、AiDependencyBoundary、AsyncBoundary 架构 88/88 通过，零跳过；Integration 分片发现 1214 项仅证明分片完整性。状态 Aot-analysis-clean，不代表新鲜 Native 发布/运行。
- node artifacts/r10u/run-jit-retry.mjs 与 node artifacts/r10u/run-integration.mjs：按当前 JIT 属性从本地 NuGet 缓存 restore 并完整构建 Integration 依赖图，零警告/错误；验证源与消费程序集同摘要、JIT 消费不包含 AOT 注册表后，在隔离容器执行全部 AI Integration，SQL Server 44/44、MySQL 44/44 通过，零失败/跳过。两库都包含检查点真实事务、整轮回执恢复、过期扫描与 fencing。测试前后冻结源码与整个消费目录 SHA 校验一致。
- 保留的非验收结果：初次测试消费误用 AOT 分析依赖，SQL Server 多个进程内 Host 注册发生 DapperAotParameterRegistry 并发异常；两库混合模式运行主动停止（exit137），全部排除验收。改为完整 JIT 构建时，首次 --no-restore 使用 AOT assets 导致条件序列化依赖缺失（1 编译错误）；按相同 JIT 属性重新 restore 后通过。旧消费缓存的防误用实验明确拒绝（exit1）；首次同步脚本语法错误（exit2）发生在数据库测试前。没有修改产品 Dapper 注册器、依赖锁或 SQL 绕过问题；进程内多个 AOT Host 的注册限制仍不外推为原生多实例验收通过。
- node artifacts/r10u/run-static.mjs、node artifacts/r10u/run-tooling.mjs：命名 30、矩阵 6、治理 57、测试工具 54 通过；保护排除的 Markdown 扫描 386 文件通过。node artifacts/r10u/post.mjs、python artifacts/r10u/collect.py 核对发现与执行 UID 完全一致、原始 TRX、实际编译源码 SHA、新鲜输出、main 基线及其他窗口摘要。Unit 总发现 6922 含其他窗口 LocalFileLogging 10 项；本批规范矩阵 Unit 6912、AI 3429、ai-runtime 687，仅 AI 全量实际执行。独立只读复审的两项问题已关闭，最终无剩余有证据问题，复审不代替测试。
- 环境 Windows x64 SDK10.0.401、Linux Docker SDK10.0.400/runtime10.0.11、Docker29.6.2、i7-12700H/约64GiB；Windows Unit 限4处理器，成对数据库消费者各2CPU/8GiB，持工作区及机器重任务锁。NuGetAudit=false，仅本地缓存还原，未执行在线依赖审计；沿用固定解析工具的 Linux SDK 派生锁证据，未修改仓库依赖。

原始报告、失败实验、发现 UID、冻结源码与消费摘要在 artifacts/r10u。最终计划验收记录及架构 SQL 的两处 reason 文字在冻结后更新；生产代码与 SQL 和实际验收输入一致，文档再做结构、编码与 diff 检查。本批归属文件单独提交到 main，不推送。

证据边界：MySQL 官方 TRX 对单个已通过结果的捕获文本触发字段长度截断，结果状态、UID 与计数完整，保留工具提示；不声称捕获文本完整。首次汇总脚本因 Windows 默认 GBK 读取 UTF-8 复审 JSON 失败（exit1），明确 UTF-8 后汇总通过；原错误说明保留，未修改原测试报告。

逐步工具循环游标/回执/续跑、历史终态未结算修复、提供者请求前精确输入 Token 硬上界及自动外部对账仍未完成。审批暂停保留整轮意图，恢复进入对账，不自动从提示词重新启动。原生发布/运行、独立生成应用与页面按 AI 模块集中验收；R10 不整体关闭，Capacity-not-verified。

### R10v：历史终态预算账本修复（2026-10-12，本批已通过本地验收）

本批在 main 合批交付终态待结算扫描、可信作用域重绑定、版本占有、原凭证计量、兼容配额及 Worker 轮询接线。基线 be2feb16201ac6520e2ffc3146df2453c441f2c8，快照 ai-terminal-budget-repair-r10v；其他窗口改动与受保护文件保留。无新表、迁移或 HTTP 字段。

- [x] 新行为 RED 33/33 失败、零跳过，均因修复边界尚未交付；类型名调用只用于 RED 使测试可编译，正式实现后改为静态直接调用。两轮测试夹具编译错误（缺少租户引用、可空匹配器）保留，不计 RED。
- [x] SQL Server/MySQL 只扫描 completed/failed/cancelled/authorization_required/expired 与同 Scope、Id、RunId 匹配的 reserved/pending 账本；处理前由持久行绑定可信租户，当前状态和原版本不匹配就不结算。
- [x] 单模型及整轮工具循环的兼容原凭证才用于计量，冻结模型/输入/额度和预留关联必须匹配；其他载荷保留未知用量与全部预留。不读取或交付用户历史，不创建模型、工具、会话验证器，不重开运行。
- [x] 版本占有和预算、兼容配额结算处于同一 AI 本地事务；已 known/unknown 账本不重写，取消、作用域变化和外层失败回滚，竞争者只修复一次。
- [x] 独立审查确认两处边界：矛盾 not_dispatched 不可释放预留，满批坏历史绑定不可阻断后续修复。新增回归 RED 39 项中 33 通过、6 失败、零跳过；修正为仅 failed 兼容凭证可证明零用量，并按 Id 游标跨 Worker Scoped 轮询推进，拒绝/失败行也推进，空批重置。双库夹具补充真实坏绑定轮转与矛盾凭证断言。测试中的 SqlStatement 属性名编译错误保留，不计行为 RED。
- [x] 当前源码验收：AI 3468/3468（含新增 39）、完整 Architecture 235/235；SQL Server 与 MySQL 各 44 UID 均有当前输入的实际通过证据（SQL Server 完整复验 43 项通过、1 项失败，失败方法再复验 1 项通过）；MySQL（首轮 43 项通过、1 项失败，失败方法复验 1 项通过）。原始失败轮退出非零与日志/TRX 保留，不称任一失败轮或双库单轮全套通过，不伪造合并 TRX；源码和消费者哈希复核后只复用原先已通过的 UID，聚焦复验只证明其发现范围。最终所需双库 UID 无遗漏，复验零失败、零跳过。命名 30、矩阵 6、治理 57、工具链 54 通过；安全 Markdown 检查 386 文件。API/Worker AOT 分析及当前 JIT Integration 完整依赖图构建均零警告、零错误。独立只读复审两项 Important 已关闭。

证据保留在 artifacts/r10v：run-unit.mjs green3 执行完整 AI 聚焦；run-static.mjs、run-tooling.mjs 验证冻结源码；run-linux.mjs 执行 API/Worker AOT 分析及 AOT 专项架构；run-jit-retry.mjs 恢复并重建完整 JIT 图及全套架构；run-integration-retry.mjs 保留首轮连接异常的原始结果后，以 DOTNET_PROCESSOR_COUNT=1（程序集固定两个测试 Worker）执行 SQL Server 全部 44 项及 MySQL 失败方法复验；run-sql-final.mjs 以 sequential.runsettings 明确设置且日志确认一个测试 Worker，仅复验 SQL Server 剩余失败方法；双库完整运行已通过 UID 均仅在完整源文件/消费者哈希仍匹配后复用；post.mjs、collect.py 核对精确 UID、源文件和消费者哈希及分支。基线与本批白名单冻结 7793 输入，资源锁和独立双库工作量隔离，无关脏改动及两项受保护路径排除在读取/哈希/暂存之外。acceptance.json 与原始 TRX、日志保留失败尝试和有效 RED，不将编译失败当作行为 RED。正式 Unit 发现 6951、工作区 6961（含其他窗口 10 项），ai-runtime 726；未执行完整 Unit，不报告为全部通过。

复用现有双库工作流检查点/完成夹具，新增真实账本历史终态、原回执、未知保留、过期终态、并发、旧版本、租户拒绝、外层回滚及重复修复断言。待结算修复不等于未知外部请求已经自动对账；逐步工具续跑、请求前精确输入 Token 硬认证与自动外部对账继续开放。Native 发布/运行、独立生成应用及页面按 AI 模块集中验收，R10 不整体关闭，Capacity-not-verified。

### R10w：工具调用恢复输入与派发身份准入（2026-10-12，已实现、本地验收通过）

本批先关闭逐步恢复依赖的输入歧义：工具参数不能通过未知对象的 ToString 改写，同一运行的 callId 不能对应多个工具请求；整批校验与冻结先于任何工具执行。范围为 AgentToolLoop、内部 AgentToolCallAdmission 与工具循环回归；不改变数据库、权限、模型配置或检查点协议，不宣称已交付持久游标续跑。

- [x] 为未知类型、重复调用身份、完整批次超限、累计载荷及跨 await 修改建立可失败测试；合法 null/原生数值/JsonElement 保持无损。
- [x] 采用显式源生成与有界 JSON 复制，统一参数、模型响应和整批工具步数准入；整个运行内拒绝重复 callId，未经完整准入不得进入统一工具执行器。
- [x] 聚焦 RED/GREEN 后集中执行 AI、受影响架构、AOT 分析和 affected 选择集；其他窗口及受保护路径在读取/哈希/提交前排除。
- [x] 独立只读复核、更新实际证据和精确归属提交；原生发布/运行与页面/独立应用继续按 AI 模块集中验收，R10 保持开放。

行为证据：首轮测试夹具 7 个类型/可空编译错误保留，不计 RED；修正后新 39 项 RED 32 失败/7 通过，完整步数回归 RED 46 项 34 失败/12 通过。首轮完整 AI 3507/3507 通过、零跳过；独立复核发现解析异常漏检与原生参数节点漏算，新增 46 项聚焦 RED 5 失败/41 通过后修正，并补充 assistant 角色限制。顶层 SDK 诊断元数据只丢弃、不转发的边界已有两个原始通过用例，复审已关闭原问题、未发现新 Critical/Important。

最终本地验收：`node artifacts/r10w/run-unit.mjs final` 执行 AI 3514/3514（新增 46），`node artifacts/r10w/run-linux.mjs` 完成 API/Worker AOT 分析与 AOT Architecture 88/88，`node artifacts/r10w/run-tail.mjs` 完成 JIT Architecture 235/235、双库各 5/5（合计 Smoke 8 + AI 工作流检查点 2）、新鲜发现和输入/消费者/UID 核对，测试均零失败/跳过。末尾证据汇总首次因共用 TenantProvisioningTests 的提供程序位于方法名而非类名退出 1；修正为完整测试身份校验后，`python artifacts/r10w/collect.py` 退出 0，实际过滤器、数量及 UID 集合不变，保留原失败说明。构建零警告/错误；命名 30、矩阵 6、治理 57、工具链 54 项通过，受保护路径预先排除后的 Markdown 386 文件通过。完整 Unit 未执行，Integration 1214 为分片发现、并非执行数量；本批未执行完整 AI Integration 88。证据在 `artifacts/r10w/acceptance.json`，基线 `559e4583`、任务 `ai-tool-call-admission-r10w`，冻结 7795 输入，其他窗口改动保持原样。

本批状态为 Aot-analysis-clean；Native 发布/运行、独立应用与页面集中验收仍待完成，Capacity-not-verified。逐步持久游标/回执续跑、Provider 精确输入硬限和自动外部对账仍开放，R10 不整体关闭。

### R10x：工具循环逐步检查点与确定结果续跑（2026-10-12，已实现、本地验收通过）

范围：AgentToolLoop/AgentToolLoopCheckpoint、AiModelRunCheckpoint 和协调器的真实恢复路径。以追加可选字段兼容旧请求与 v1 整轮凭证，新增 v2 循环凭证仍存入现有租约事务，不新增表/迁移或依赖。冻结模型批次、工具版本与参数；工具派发前提交意图、结果确认后提交回执，恢复只执行未完成后缀。只有意图、坏绑定、未知用量或审批/对账暂停不能自动续跑。既有输出会话格式保留，持久正文不进入事件或诊断字符串。

- [x] 先以协调器剩余调用恢复建立 RED；补充运行时前缀、取消/失租、预算、版本撤回、损坏和重复身份测试。
- [x] 实现静态 JSON 闭包、完整轮次/结果前缀与运行/模型/预算/提示绑定；客户端创建前恢复准入，旧 started 保持对账，旧 completed 只结算。
- [x] 在现有双库检查点夹具覆盖新凭证提交、过期领取与事件冲突回滚；集中验证 AI、架构、AOT 和实际 affected 集合。
- [x] 独立只读复审、准确同步证据并提交 main；Provider 输入硬限、自动外部对账与审批后工具重派发仍单独开放，集中原生/页面验收不重复启动。

首轮 RED：17 项 16 通过/1 失败，证实原协调器不能从确定前缀恢复。中间完整 AI 回归 3553 项 3552 通过/1 失败，失败为旧审批事件数量断言，已适配新增逐步意图/回执。独立只读复核发现合法 JSON null 成功值兼容缺陷，真实 AgentToolExecutor 回归 RED 1/1 失败后按原 JSON 字段存在性修复；复核确认该 Important 关闭、未发现新的 Critical/Important。初始测试夹具可空编译失败原样保留，不计行为 RED。补查回执字符上界的可复现实验证明：原 JSON UTF-8 64002 字节的 HTML 字符串，默认编码后 value 为 384002 字符，旧 131072 字符界限不足。调整为执行器原始结果上限的六倍加固定封装余量，保留整个游标与外层正文上限；补充真实执行器 Unicode/HTML 输出回归。完整 AI 中间版本 3559/3559 通过；本批自己的初步 AOT 构建停止后重新冻结最终输入，不能当作最终验收通过。最终本地验收：`node artifacts/r10x/run-unit.mjs final` 执行 AI 3561/3561（新增 47），API/Worker AOT 分析与相关 Architecture 88/88；JIT 全量 Architecture 235/235。首轮双库并行执行中 SQL Server 48 项有 2 项取消/迁移超时，MySQL 完成 28 项（25 通过、3 连接超时）后被脚本 30 分钟时限中止，未生成完整 TRX，原始日志与二进制流保留。`node artifacts/r10x/run-recovery.mjs` 复用相同源码与消费者产物，保持产品超时预算及断言，采用矩阵 60 分钟执行时限、2 CPU/8 GiB、Workers=1，顺序补测 SQL Server 原失败 UID 各 1/1 与 MySQL 48/48；成功 UID 并集覆盖双库各 48（AI 88 + Smoke 8），补测零失败/跳过，不把首轮失败或中止改报通过，也不据此认定资源争用根因。既有两个工作流检查点入口包含本批新 v2 游标的事件冲突回滚、旧租约拒绝、过期领取和后缀恢复断言。构建零警告/错误；命名 30、矩阵 6、治理 57、工具链 54，通过预先排除受保护路径的 Markdown 386 文件检查。新鲜 Unit 发现 7054（规范 7044 + 外部日志夹具 10），runtime 819、checkpoint-admission 实际发现 328（矩阵下限 300）；完整 Unit 未执行，Integration 1214 为分片发现数，未执行范围不外推为通过。冻结 7798 输入；源码、消费者与精确 UID 集合核对通过，证据在 `artifacts/r10x/acceptance.json`，基线 `31d69b72`、任务 `ai-tool-loop-cursor-r10x`。

本批关闭确定模型/工具回执前缀的逐步续跑，不关闭审批暂停后的工具重派发、未知外部动作/费用对账或 Provider 请求前精确输入硬限。Native 发布/运行、生成应用与页面仍按 AI 模块集中验收，Aot-analysis-clean，R10 保持开放，Capacity-not-verified。

### R10y：工具循环审批回执恢复与累计重试预算（2026-10-12，已实现、本地受影响验收通过）

范围：沿用 R10x 的冻结批次，仅允许最后一个确定前缀回执为 denied/ai.tool.approval_required 的运行重新入场。恢复不代表授权，仍由原工具执行器按原 OperationId、版本、参数和当前会话/权限/审批复核；未知派发、对账回执和缺失历史计量继续停止。审批拒绝和重新入场尝试共同消耗原 64 步额度，持久化派发意图后失联不能重试。不扩大静态工具允许集，不新增写工具、审批类型、表、迁移或依赖。

- [x] 协调器审批恢复先建立失败测试，补充真实执行器审批复核、稳定绑定、前缀复用及拒绝场景。
- [x] 在现有游标增加受控审批尝试计数，兼容旧凭证零重试；重试前持久化未知意图，拒绝预算重置、坏前缀、版本变动和重复重放。
- [x] 复用现有双库检查点入口验证事件冲突回滚、租约恢复与审批重入，集中执行 AI、Architecture、AOT 分析和实际受影响双库用例。
- [x] 独立只读复核、记录原始结果、核对外部改动并提交 main；Provider 精确输入硬限和外部对账保持开放，原生/页面按模块集中验收。

首个协调器 RED 1/1 失败，随后完整 AI 3562/3562 与扩展边界回归 3588/3588 通过；解析器第二次求值覆盖冻结 OperationId 的新增回归 RED 1/1 失败，改为只在无原身份时解析，最终覆盖通过。初始测试缺少命名空间的编译失败保留，不计行为 RED。

本批本地验收：`node artifacts/r10y/run-unit.mjs final` AI 3589/3589（新增 28），零失败/跳过；`run-linux.mjs` API/Worker AOT 分析零警告/错误、相关 Architecture 88/88；`run-jit.mjs` 全量 Architecture 235/235、Integration 分片发现 1214 无遗漏/重复。`run-integration.mjs` 按真实 affected 集合顺序执行 SQL Server 与 MySQL 各 48/48（AI 88 + Smoke 8），矩阵外层时限各 60 分钟、2 CPU/8 GiB、Workers=1，产品内部 deadline 不变，零失败/跳过。既有双库检查点入口同时覆盖审批回执冲突回滚、过期领取、旧租约零派发、新租约重入和 v2 累计尝试；实际审批消费继续沿用现有业务 Handler。命名 30、矩阵 6、治理 57、工具链 54、预先排除保护路径的 Markdown 386 文件检查通过。Unit 实际发现 7082（规范下限 7072 + 外部日志夹具 10），runtime 847，checkpoint-admission 实际 336（下限 308），不声明完整 Unit 或完整 Integration 已执行。冻结 7800 输入，源码/消费者/UID 核对通过；独立只读两轮复核无遗留 Critical/Important。证据在 `artifacts/r10y/acceptance.json`，基线 `3c761e9f`、任务 `ai-tool-approval-resume-r10y`。

本批关闭工具循环的确定审批拒绝重入协议，不新增生产写工具或审批类型。旧 v1 正文无重试字段可继续读取；有重试的内层 v2 由新读取器处理，旧严格读取器失败关闭，升级/回退需保持写派发排空和原凭证。Provider 请求前精确输入 Token 硬限、未知外部动作/费用自动对账继续开放。Native 发布/运行、生成应用与页面仍按 AI 模块集中验收；Aot-analysis-clean，R10 不整体关闭，Capacity-not-verified。

### R11：受控业务路由、数据去向和观测（P2，依赖 R06/R09/R10）

**修改/新增责任位置：** `src/AI/Full.NET.Agents/Workflows/`、Ai 运行时与模型策略；`docs/operations/ai-provider-network.md`、`docs/operations/ai-operation-budgets.md` 在实现完成时同步，不提前把方案写成运行能力。

- [x] 首个路由只允许“知识库问答”和“现有授权工具任务”；封闭意图枚举、独立工具允许集，未知意图澄清/拒绝，保留顺序执行和总预算。
- [ ] 将数据分类策略应用于 Embedding、重排、生成及遥测；私有化模式故障不得切换公网 Provider，测试每个阶段的拒绝行为。
- [ ] 关联 Run/Step/Trace 的阶段耗时、调用量、拒答、预算拒绝与错误；日志脱敏、指标低基数，不将文档正文或用户/租户 ID 放入 Metrics 标签。
- [ ] 不增加专家群聊、任意动态插件或微服务拆分；出现真实业务收益证据后另修订本计划。

#### R11a：Provider 私有化网络闭包（2026-10-12，本批受影响验收完成）

基线 main `c91dab55ece7b71879cdcf35c2a83dbae47da06f`，任务快照 `ai-private-provider-network-r11a`。本批只落实宿主级 Provider 私网边界，沿用现有网络连接器、批准列表与静态注册，不新增依赖、公开 API、数据库或授权入口。

- [x] 为正式 Chat、Embedding、Connectivity 客户端统一冻结 `FullNet:Ai:Network:PrivateOnly`；仅批准的 Ollama 源可入场，默认策略保持兼容。
- [x] HTTPS 和 HTTP 同样验证全量 DNS 的私网/环回属性，公网、混合或保留地址失败关闭，不能退回公共 Provider。
- [x] 覆盖非法配置、重载、三类真实本机套接字及九类客户端拒绝；独立只读安全复核和受影响验证后提交 main。

此切片不关闭 R11 数据分类、业务路由、重排和遥测策略，也不关闭 R10 精确输入 Token 硬限或未知外部对账。Native 发布/运行、独立应用与页面按 AI 模块集中验收，保持 `Capacity-not-verified`。

实际证据保存在 artifacts/r11a：node artifacts/r11a/run-final-pipeline.mjs 新鲜执行完整 AI 3625/3625（新增 36 项）、完整 JIT Architecture 235/235 和 AOT 架构集合 88/88，零失败/跳过。API/Worker 的 dotnet build -c Release -p:FullNetAotAnalysis=true -p:NuGetAudit=false 零警告/错误，只证明 Aot-analysis-clean；未做本批新鲜 Native publish/运行。Provider 源路径经当前冻结分类器选择双库 Smoke，SQL Server/MySQL 各 4/4 通过，按矩阵超时顺序运行且各 Workers=1；没有重跑整套 AI 双库集成。

node artifacts/r11a/run-static.mjs 通过命名30、矩阵6、治理57、保护排除后的 Markdown386；node artifacts/r11a/run-tooling.mjs 通过54项。全 Unit 新鲜发现7118，正式矩阵7108，其他窗口 LocalFileLoggingTests 10项仅发现；未执行全Unit。AI Provider 集合实际发现183，最低数与新测试过滤只在测试矩阵维护。冻结7801输入，精确源码、Unit及Integration消费者摘要和双库发现UID核对通过；只复用构建缓存，不复用测试结果。

独立只读安全复核发现显式 null 失败开放，追加真实 RED 后修正并复核无阻断：初始非法值 RED4/4失败；null RED 的全AI3625仅1失败、其余3624通过。测试辅助代码曾调用不存在的 StreamReader API，编译失败证据保留，核对安装的参考 XML 后改为 ReadBlockAsync；编译失败不计作行为RED。Windows x64/i7-12700H/约64GiB、SDK10.0.401；Linux SDK10.0.400/Runtime10.0.11，主构建4逻辑处理器/4GiB，双库各2CPU/8GiB；未联网执行NuGet审计。此范围不证明真实云模型或 Kubernetes 私网端到端行为，不改变数据分类、遥测、MCP或向量库出口能力状态。

首轮 run-final-pipeline.mjs 在 SQL Smoke3/4 时停止，登录场景在租户引导的 Dapper 连接阶段发生 SQL TCP 连接超时；原始 TRX 和日志保存在 sqlserver-first。node artifacts/r11a/run-integration-retry.mjs 未修改代码、过滤集、处理器数、Workers 或产品超时，核对同一源码/消费者摘要后重跑完整 SQL4/4 并执行 MySQL4/4 通过；不把未复现的连接超时写成产品修复，保留失败历史。

#### R11b：Provider HTTP 安全观测（2026-10-12，本批受影响验收完成）

- main 基线 `9789f2d069a9a39611893a974b1e12664ed76ba6`；同批实现九个命名客户端计数、响应头耗时、固定标签 Activity 与 Ai 装配订阅，不新增依赖、SQL 或 HTTP 契约。
- [x] 完成 HTTP 状态、网络策略、取消与正文边界回归；诊断回调失败保留原响应/异常及父 Activity。
- [x] 完整 AI Unit、受影响双库、JIT/AOT 架构与 AOT 分析集中验收，核对冻结源码和消费者。
- [x] 同步实际证据并在 main 提交；R11 整体、Run/Step、完整数据去向和 R12 仍待后续。

指标来源为 `Full.NET.Ai.Providers`，只携带封闭的 Provider/操作/HTTP 结果标签，不记录 URI、身份、模型、正文或异常消息。计量截至响应头，2xx 不证明生成、拒答、用量或结算成功；仅继承当前 Trace，未新增持久 Run/Step 关联。诊断回调异常隔离，但首次源初始化失败需重启恢复。运维语义见 `docs/operations/ai-provider-network.md`。

实际证据位于 artifacts/r11b：node artifacts/r11b/run-final-pipeline.mjs 完成完整 AI Unit 3671/3671（新增46项），JIT Architecture235/235、AOT架构88/88，均零失败/跳过。API/Worker 的 dotnet build -c Release -p:FullNetAotAnalysis=true -p:NuGetAudit=false 均零警告/错误；只证明 Aot-analysis-clean，本批没有新鲜 Native 发布/运行。流水线在双库取得资源锁前因另一任务锁持有进程退出而停止，原退出1与错误记录保留；锁自行消失且核对无遗留临时容器后，node artifacts/r11b/resume-integration.mjs 仅续跑未开始的双库阶段并通过，不修改工具或产品。冻结分类器选双库 Smoke，SQL Server/MySQL 各4/4，顺序执行、Workers=1、原矩阵60分钟期限；未执行全部AI双库集成。

node artifacts/r11b/run-static.mjs 通过命名30、矩阵6、治理57及保护排除后的Markdown386；node artifacts/r11b/run-tooling.mjs 通过54项。全Unit仅新鲜发现7164，正式矩阵7154，含其他窗口10项LocalFile测试，未执行全Unit。Provider集合实际发现229，矩阵最低数227。7804项输入冻结，精确UID、生产源码及Unit/Integration消费者摘要核对通过；只复用构建缓存，不复用测试结果。

保留有效RED：初始注册观测9/9失败；SDK包装策略拒绝与父Activity回调4/4失败；冷启动递归发布1/1失败。修正后46/46聚焦通过；FULLNET_AI_TELEMETRY_FRESH_PROBE=1 的独立进程 publication-green 1/1通过，明确证明首次发布递归。只读安全复审无剩余Critical/Important；首次诊断源初始化失败需重启的限制已写入运维说明。

首次 collect.py 汇总因 Windows 默认 GBK 读取含中文的锁失败记录退出1；明确 UTF-8 后退出0。测试原始结果、精确UID与源码未改变，不将汇总失败改写为测试通过。

环境为Windows x64/i7-12700H/约64GiB、SDK10.0.401；Linux SDK10.0.400/Runtime10.0.11，构建容器8CPU/4GiB、托管4逻辑处理器，双库各2CPU/8GiB。未执行在线NuGet审计、真实云模型、集中独立生成应用或浏览器验收；R11全阶段数据去向、Run/Step全链路和R12继续待办，容量保持Capacity-not-verified。main仅提交本批九项文件，其他窗口改动与共享矩阵增量保持原样，不推送。

#### R11c：Agent 运行与步骤观测合批（2026-10-12，本批受影响验收完成）

- main 基线 `ac557421264889171a7b4aa58c625a3634b7bf56`，任务 `ai-execution-telemetry-r11c`；同批接入 Worker 处理尝试、单模型、工具循环、工作流节点及宿主订阅。
- [x] 真实运行失败证据、固定标签与 Trace 关联、审批/对账/预算/拒答分类、恢复不新增派发及诊断故障隔离回归。
- [x] 一次冻结最终源码，集中执行完整 AI、受影响双库、架构与 AOT 分析，核对消费者及精确UID。
- [x] 独立只读复核、同步实际证据后 main 提交；完整数据分类门禁与集中原生/页面验收继续待办。

指标来源 `Full.NET.Ai.Execution` 仅含固定 stage/outcome；RunId、已有OperationId和有界步骤序号只用于Trace，不记录定义键、模型、工具名、正文或用户/租户。工作流及循环的completed表示内部执行器返回，Worker的processed只表示本次处理返回true，包含已提交的失败/暂停，不证明业务成功或费用结算。当前Trace关联当前尝试，不新增持久TraceId或跨重启传播；正式持久StepId及完整Run/Step/Trace运营查询仍待后续。

实际证据位于 artifacts/r11c：node artifacts/r11c/run-final-pipeline.mjs 完成完整AI 3714/3714（新增43项），JIT Architecture235/235、AOT架构88/88，均零失败/跳过。API/Worker 的 dotnet build -c Release -p:FullNetAotAnalysis=true -p:NuGetAudit=false 均零警告/错误，只证明Aot-analysis-clean，没有本批新鲜Native发布/运行。冻结分类器选择Ai+Smoke，SQL Server/MySQL各48/48及48/48、合计96实际通过，顺序运行、Workers=1、原矩阵60分钟期限；没有执行全部Integration。

node artifacts/r11c/run-static.mjs 通过命名30、矩阵6、治理57及保护排除后的Markdown386；node artifacts/r11c/run-tooling.mjs 通过54项。全Unit仅发现7207，正式矩阵7197，其他窗口10项LocalFile测试只发现，未执行全Unit。Runtime实际890、Provider实际229；Integration分片发现1214，无遗漏/重复，不计为执行。冻结7807输入，源码、三类生产/Unit消费者、JIT及双库消费者摘要和精确UID核对通过；仅复用构建缓存，不复用旧版本测试结果。

初始4项RED全部失败，其中3项为真实缺少观测，另1项是新夹具误用工作流v1；首轮AI3675有3674通过/1夹具失败，核对正式v2后修正，保留原结果。新增夹具三处params目标推断CS8752编译错误保留，改显式ChatResponse类型，编译错误不计行为RED。分类回归9项7失败/2通过，复审追加标题拒答2项均失败；只修正分类后，最终完整AI包含全部43项通过。

此前41项聚焦及独立冷启动1项通过保留为中间证据；最终publication-final再次在独立进程以FULLNET_AI_EXECUTION_FRESH_PROBE=1运行1/1，核对最终编译消费者，明确首次发布递归。独立只读复审四项Important均关闭，没有剩余Critical/Important；父Activity回调、采样/测量/停止故障保留原结果或异常，无正文和身份标签。

sqlserver 官方TRX对2项结果的超长捕获文本按字段上限截断；状态、计数与精确UID完整，保留报告器提示，不声称截断区域文本完整。

mysql 官方TRX对2项结果的超长捕获文本按字段上限截断；状态、计数与精确UID完整，保留报告器提示，不声称截断区域文本完整。

Windows x64/i7-12700H/约64GiB、SDK10.0.401；Linux SDK10.0.400/Runtime10.0.11，构建容器8CPU/4GiB、托管4逻辑处理器，双库各2CPU/8GiB。Unit使用工作区互斥与独立TEMP/TMP，双库/原生使用同机重型锁；记录曾等待其他文档原生验收，不挤占执行期限。未执行在线NuGet审计、真实云模型、集中独立应用/浏览器或本批原生运行。R11业务路由、全阶段分类、持久StepId与跨重启Trace、输入硬限及未知外部对账继续待办，Capacity-not-verified。main仅提交本批十项文件；其他窗口与共享矩阵增量保留，不推送。

### R11d：封闭知识问答与授权工具任务路由

依据已批准 R11，本批完成显式封闭意图 `knowledge_ask` 与 `authorized_tool_task`，共享强类型请求协议、独立精确授权入口。知识路由不获得工具权限，复用公开 Ask 的会话槽位、完整来源复核和总预算。工具路由仅允许结构化校验版本 2 的会话重命名工作流，其独立工具集合为会话列表及会话重命名；继续顺序执行、逐工具授权、审批、幂等和恢复。不增加模型分类、动态/远程工具或预算池。使用说明见 [AI 受控任务路由](../../operations/ai-task-routing.md)。

- 未知意图、混合载荷、跨入口意图、空 Prompt 与其他定义在派发前拒绝；权威用户、会话和租户必须与冻结绑定一致。
- 新路由的直接重放及创建竞争落败都精确比较已有定义、版本、模型、Prompt、两项预算和 PromptBinding，防止返回旧定义或旧版本。竞争落败沿用已知零用量结算，旧入口保持兼容。
- 本地集中执行 `node artifacts/r11d/run-final-pipeline.mjs`，Integration 夹具修正后使用 `--resume-integration`，MySQL 引擎中断后通过 `node artifacts/r11d/recover.mjs` 和 `node artifacts/r11d/run-mysql-remaining.mjs` 补齐：AI 3768/3768、完整架构 235/235、AOT 架构 88/88；分类器实际选择 Ai-only，SQL Server 全批 44/44，MySQL 由原始 29 条完整 Passed 记录与剩余 15/15 TRX 合成 44 个唯一通过 UID，共 88 个唯一 UID。双库串行且每库一个工作线程，最终覆盖零失败/跳过；未把中断的整批退出码改写为通过。双库 HTTP 验证路由权限、来源正文和引用、重命名运行幂等、预算差异与跨定义冲突。
- 新增 54 项回归。原始 RED 保留：契约 1 失败、意图 24 中 2 失败、严格重放及空 Prompt 54 中 15 失败；后续完整 AI 全部通过。工作区 Unit 发现 7261，提交口径 7251，另有 10 项既有外部 WIP 只发现；未执行全 Unit。
- API/Worker AOT 分析和 Release 构建零警告、零错误。命名 30、矩阵 6、治理 57、工具链 54 与受保护路径排除后的 Markdown 扫描通过。只读审查无剩余 Critical/Important。
- 首轮 MSBuild 内存异常保留；降低构建并发并限堆后通过。首次完整 AI 在两处理器和内存压力下出现 2 项既有 DOCX 授权端口超时（3766 通过），同一源码/程序集恢复四处理器后完整 3768 通过，未放宽测试期限。测试常量断言的 MSTEST0032 构建失败与修正也单独保留。
- 首轮 SQL 的新路由夹具提前创建两条预留，导致既有 Prompt 精确数量断言期望 1、实际 3；确认后取消本批测试进程并保留原始失败及取消前结果，未作为验收。将新增验证移到原场景末尾，保持原断言，单独重建 Integration 并完整重跑双库。输入修订只改变这一 Integration 夹具及权限说明文字，所有业务、Unit、AOT 和架构输入保持原值，复用门禁证据有明确哈希关联。
- 修正夹具后的 SQL 尝试发生四项初始 TCP 连接超时，之后 Docker 引擎失联，26 个已记录身份没有完整 TRX，退出 125，未作为通过证据。依据此前用户明确授权恢复 Docker Desktop，核对 AOT/JIT/双库缓存源码与程序集哈希后，从头执行完整双库影响集；三项既有 kind 服务恢复。共享矩阵只保留 HEAD 与本批增量，外部 WIP 未进入提交或最终冻结矩阵。
- 后续 MySQL 又因引擎失联退出 125，保留原始二进制与哈希。使用本机安装的微软 `TrxTestResultSerializer.ReadAll`（2.3.2.0）读取 30 条完整记录，逐项验证 UID、Outcome 和时间：29 条 Passed、1 条连接超时 Failed。核对同一冻结源码/消费者哈希后，只执行失败 1 条与未完成 14 条，15/15 全部通过；不接受只有进度身份、半条记录或重复 UID。SQL 的完整结果和 MySQL 两段真实结果分别保留，原始连接失败仍在证据中。
- 冻结 7813 个输入，复用时间戳并验证源码及 producer/consumer 哈希。JIT 使用上批已验收缓存的独立副本，复制前后检查原源码及完整 Integration 程序集哈希，覆盖当前冻结差异后构建与验证；AOT 缓存独立保留，不以缓存替代当前行为执行。原始命令、退出码、TRX、来源和资源条件保留于 `artifacts/r11d/`。

本批关闭首个封闭路由切片；R11 完整数据分类及逐阶段私有拒绝仍待实现。实际输入 Token 硬限、未知外部结果对账、持久 StepId/跨重启 Trace 关联及 R12 原生运行/生成应用/页面集中验收未升级；保持 `Capacity-not-verified`。

### R11e：MCP 私有化网络闭包与真实连接校验（2026-10-12，本批本地验收完成）

基线 main `1ace5ee6a3ee5914477c0cbd962bf203a576b8f2`，快照 `ai-mcp-network-r11e`。本批合并交付可信配置冻结、逐请求/逐连接验证、整组 DNS 拒绝、IP 固定连接及重定向/代理/Cookie 禁用，不新增 SQL、迁移、依赖、权限或 HTTP DTO。

- [x] MCP 复用宿主 `FullNet:Ai:Network:PrivateOnly`，保持独立精确 `ApprovedOrigins`；批准不豁免公网或特殊地址。无效布尔值/对象、空或非法源在装配时拒绝，配置重载不能打开边界。关闭私有化时保留公共 HTTPS；明文 HTTP 始终要求批准的私网或环回。
- [x] 每个请求校验 URI，每次新连接验证 URI/连接主机与端口、单次 DNS 的全部结果，再仅连接已验证 IP；混合与空结果在任何连接前拒绝。Socket 失败只尝试同一轮验证的其他 IP，不切换目标。强制 HTTP/1.1，HTTPS 继续系统证书验证。
- [x] 新增 42 项 Unit，包含真实 TCP 重定向、原始请求版本/头、配置冻结、元数据/公私混合 DNS、重绑定、取消与失败候选。首轮四项有效行为失败及一个缺失 IConfiguration 夹具错误分别保留；修正夹具后有效 RED 5/5 失败。独立只读安全复审无剩余 Critical/Important，不代替运行证据。
- [x] `node artifacts/r11e/run-unit.mjs green` 聚焦 42/42，`node artifacts/r11e/run-final-unit.mjs` 复用相同构建执行 AI 3810/3810、零失败/跳过，源及 MCP 生产/消费程序集摘要一致。工作区 Unit 发现 7303，含其他窗口本地日志测试 10；规范矩阵 7293，仅实际执行 AI 集合，不报告全量 Unit 通过。
- [x] `node artifacts/r11e/run-pipeline.mjs`：API/Worker AOT 分析零警告/错误，AOT 架构 88/88、全量架构 235/235；JIT 依赖图零警告/错误，Integration 分片发现 1214 无遗漏/重复。正式分类为 Ai + Smoke，SQL Server/MySQL 各 48/48、合计 96 唯一 UID 全部通过，零失败/跳过；发现与 TRX 完全相同。
- [x] MCP 双库夹具启用可信私有化开关，验证公网在创建传输前拒绝以及既有授权远端工具成功；夹具使用 TestServer 传输，真实 DNS/TCP 约束由前述 Unit 证明，不外推生产 TLS/DNS。命名 30、矩阵 6、治理 57、工具链 54 与保护排除的 Markdown 387 文件检查通过。

冻结 HEAD 加本批白名单 7815 输入，未纳入日志/Helm/测试工具外来改动；分别校验 R11d 的 AOT、JIT 及双库缓存源码/完整消费者摘要后只覆盖当前差异，不创建或反复切换缓存。双库按提供程序顺序执行、一个测试 Worker，保持矩阵 60 分钟期限及独立 TEMP/TMP、工作区/机器重任务锁。Windows x64 SDK10.0.401，Docker Linux SDK10.0.400/runtime10.0.11，i7-12700H/约64GiB；Unit 4 处理器/2GiB GC，构建容器 8CPU/4GiB，双库各2CPU/8GiB。仅本地缓存 restore、NuGetAudit=false，在线审计未执行。

MySQL 运行期间原终端会话失联，外层进程退出且未保存返回码，原 stdout 仅保留启动行，不能声称整条 pipeline 退出 0；MTP 原始报告为 Completed、48/48 全部 Passed，发现 UID 完全一致。独立只读 `node artifacts/r11e/run-post-proof.mjs` 在当前两库缓存校验全部源码及完整消费目录摘要，退出 0，复用这些实际完成的结果，不重写未知退出码或重跑未变输入。核实原归属进程/子进程/容器不存在后只清理本工作区遗留锁，另一任务已取得的重型锁保留。

原始 RED、构建、发现、TRX、源码/消费者摘要与复审汇总在 `artifacts/r11e/acceptance.json`。main 仅提交本批文件、不推送；保留其他窗口改动和两个受保护路径。状态为 `Aot-analysis-clean`；数据分类的全部阶段覆盖、向量网络、持久 Step/跨重启 Trace、Provider 精确输入硬限、未知副作用对账与 Native/页面/生成应用集中验收仍待推进，R11 不整体关闭，保持 `Capacity-not-verified`。

### R11f：知识回答精确生成批准与分类快照复核（2026-10-13，本批本地验收完成）

基线 main `a3d1563f4a5de44760dd43cf5270e3a78c13dbc8`，快照 `ai-knowledge-processing-policy-r11f`。本批集中交付检索 SQL 与静态行读取、收费前后临时快照绑定和最终 HTTP 交付复核；不新增迁移、依赖、权限或公开 DTO，不改变持久历史协议。

- [x] SQL Server/MySQL 的 Sources/Vectors/DOCX 查询在原精确目的地、凭据和租户资格基础上，增加逐知识库 GenerationModelConfigId/Version 批准；未批准、部分批准、其他标识/版本在收费前拒绝。可选生成模型为空的只读检索保留原语义。
- [x] 分类使用二进制精确 public/internal/restricted 判定，避免默认排序规则放行大小写或尾空格。分类并非发送许可；合法分类仍须精确模型批准，restricted 不自动推导私有网络策略。
- [x] 内部权威记录和 native 行读取携带分类与生成批准，使同版本下分类/批准漂移进入现有快照比较。Embedding 和 Chat 的创建、返回、结算等待后均复核；存储、释放及 session/history 等待结束后，再以本次临时快照复核，之后没有异步等待才映射交付。最终拒绝不回滚已知结算或已完成存储，不重新收费。
- [x] 新增 40 项：未批准、未知分类、合法分类、收费等待窗口漂移 36 项，清理后最终交付窗口漂移 4 项；漂移用例确认实际进入对应回调和精确预留/结算次数。初始 RED12/12 失败及最终窗口 RED56 中4项失败均保留；最终 `node artifacts/r11f/run-unit.mjs green` 聚焦56/56通过。独立只读安全复审无剩余 Critical/Important。
- [x] `node artifacts/r11f/recheck-unit.mjs`：复用同一构建，完整 AI3850/3850、零失败/跳过；Unit工作区发现7374，包含其他窗口缓存与本地日志测试变更，规范矩阵最低门槛7333，仅实际运行AI集合，不宣称全量Unit通过。AI生产/消费程序集及本批源码摘要一致。
- [x] `node artifacts/r11f/run-pipeline.mjs` 的构建部分：API/Worker AOT分析零警告/错误，AOT架构88/88，全量架构235/235；JIT构建零警告/错误，Integration分片1214项无遗漏/重复。正式影响分类为Ai和integration-tooling。原SQL测试组中断，不能声称原pipeline整体通过；恢复证据见下文。
- [x] 双库合成夹具验证未批准请求0预留/0外部调用、有效批准后原业务/引用/会话/历史流程、返回后批准撤回拒绝与已知结算。API夹具 finally 恢复原生成批准，拒绝用例使用独立会话，保护调用者原始检索快照及既有消息计数。命名30、矩阵6、治理57、工具链54及保护排除的Markdown387文件检查通过。

首轮完整3846项出现70项失败，原因是旧PDF/DOCX夹具缺少新必需的 internal 分类；补齐夹具且增加实际到达漂移窗口断言后重新验证，保留失败报告。当前构建的首次完整3850项有1项既有 DOCX complete-hang deadline 清理用例失败；解析代码和该测试本批未修改，根因尚未确定，不能声称修复。相同构建该类连续3轮各26/26通过，随后完整3850/3850通过，首轮失败原始TRX保留。

SQL Server原组记录23项完整原生结果（22通过、1失败）后，Docker客户端unexpected EOF退出125，未生成完整TRX。失败为现有取消夹具等待模型门闩30秒超时，请求随后约42秒返回200；保留失败，不推断业务错误或宣称已修复。`node artifacts/r11f/run-post-proof.mjs` 只读复核两库冻结源码和完整消费目录通过；使用SDK官方TrxReport2.3.2.0读取完整记录，并按原44项发现UID保存中断证据。`node artifacts/r11f/run-recovery.mjs` 在相同源码、构建、夹具、单Worker和矩阵期限下补跑未完成/失败22项，22/22通过；复用未变输入的已完成22项，SQL覆盖44唯一通过UID。MySQL原组到矩阵60分钟期限中止、退出3，记录37项完整结果（36通过、1失败），另有7项未完成，没有完整TRX；文本Worker日志为OperationCanceledException后任务failed，25秒内部期限本批未修改，具体取消根因未确定。`node artifacts/r11f/run-final-post-proof.mjs` 只读复核当前两库输入/完整消费目录通过；`node artifacts/r11f/run-mysql-retry.mjs` 精确补跑1失败加7未完成共8项，8/8通过，复用未变输入的已完成36项，覆盖MySQL44项。两库合计88唯一通过UID；每个新组运行前后源码/完整消费目录摘要通过。未改变生产代码、构建、夹具或期限来规避失败，不宣称这些间歇失败已修复。不制造原两库完整44项TRX、不重写退出码或将未完成项算通过。原命名wrapper已消失，中断时在途用例清理不能追认；恢复用例保持隔离夹具并完成正常清理。

冻结 HEAD 加本批白名单7816输入；先验证R11e AOT、JIT和双库缓存源码/消费者摘要，再只覆盖当前差异，不创建新缓存或反复切换依赖图。双库按提供程序顺序、单测试Worker、矩阵60分钟期限执行，使用独立TEMP/TMP与工作区/机器重任务锁。Windows x64 SDK10.0.401，Docker Linux SDK10.0.400/runtime10.0.11，i7-12700H/约64GiB；Unit4处理器/2GiB GC，构建8CPU/4GiB，双库各2CPU/8GiB。本地缓存restore使用NuGetAudit=false；在线审计未执行。

Windows共享Unit/AI输出在本次完整运行完成后发生变化，不把后来的输出当成本次执行产物。原运行的执行前后程序集摘要一致，当时AI生产/消费摘要相同；保留原TRX和运行时证明，当前运行代码与冻结代码相同，注释差异见下文。双库使用独立Linux冻结消费闭包，不为后来的共享输出变化重跑已验证输入。

提交前发现8个本批文件新增/调整中文整行注释及换行；逐项核对完整差异均处于声明前、不在字符串内，非注释代码行和SQL文本完全一致，结构证明保存在 `artifacts/r11f/comment-only-proof.json`。不把最终字节摘要称为原冻结摘要，也不为注释变更重复启动未变运行代码的验收。并行窗口日志等无关改动期间发生变化，保留初始快照和提交前当前保护快照；不将它们纳入冻结输入或提交，外部临时夹具目录只记名称不遍历。

证据保存在 `artifacts/r11f/acceptance.json`，main仅提交本批文件、不推送，保留其他窗口改动及两个保护路径。状态为 `Aot-analysis-clean`；分类在rerank/遥测等全部阶段覆盖、向量网络、持久Step/跨重启Trace、Provider精确输入硬限、未知副作用对账及Native/页面/生成应用集中验收仍待推进，不整体关闭R11，保持 `Capacity-not-verified`。

### R12：原生闭包、运营恢复与集中验收（P2，依赖前述切片）

**修改：** `tests/Full.NET.IntegrationTests/NativeAot/NativeApiAiE2ETests.cs` 及 Worker 原生探针；测试矩阵 `eng/testing/test-matrix.json`、现有受影响 CI、Vue 页面验收与 `docs/roadmap/capability-status.md`。

- [ ] 汇总并补齐各切片的原生产物证据，覆盖真实 Embedding/索引写入、带权限检索、删除、解析、工具及审批恢复；同时覆盖确定性测试 Provider 与获准环境的选定真实 Provider，明确各自证明范围。此项不允许把 R05/R06 的 AOT 兼容检查延后。
- [ ] 演练索引丢失重建、Provider 故障、文档撤权、任务重复派发、密钥/网络策略变化与应用回退；核对双库同语义。
- [ ] 集中收敛页面自动化与人工验收；分别测量 API、Worker、向量库、模型服务的资源和延迟，不引用文章的百分比作为验收结果。

  R07a 页面验收清单：两页目录/分页/CRUD 精确权限、网络结果不明上传重试、导入失败刷新/重试、octet-stream 的 TXT/MD 授权预览与 PDF 下载、实时及历史引用来源选择、明确拒答与空历史回答、索引/模型不可用、取消到终态刷新、重复提交、路由库参数变化、租户切换与卸载晚到结果。需实际浏览器和人工证据后升级页面 Verified。
  R07b 页面验收清单：独立策略权限、原模型审批保留及显式版本升级/成对撤销、模型候选分页；库/文档两层用户/角色/组织读取与全量替换、候选读取独立权限及分页、只读和资源所有者错误、空集合撤销、100 项上限及无效/重复 UUID、409 保留输入、关闭/撤权/租户或资源/类别切换与卸载晚到结果。需实际浏览器和人工证据后升级页面 Verified。
  R07c 页面验收清单：精确文件版本索引状态/入队/原任务重试、组合权限、202 仅代表接受、未知结果先刷新、原会话及失败/超期条件由服务端裁决；1–8 库跨页选择与请求集合冻结、引用显式来源选择及请求范围/授权目录交集、历史引用缺少库绑定的选择、关闭/候选集合/资源/会话变化及卸载晚到结果。PDF 画布预览见 R07d；实际双库页面和人工证据完成后才升级页面 Verified。
  R07d 页面验收清单：真实授权端点返回的 PDF、引用页定位/翻页/缩放、关闭及撤权清画布、损坏/加密/超限/引用页不存在、字体兼容与辅助阅读下载退路。本批模拟 HTTP 的真实浏览器 Worker 冒烟不替代真实双库页面与人工验收。
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

### 2026-10-06：R05e4 PDF 完整计划与清单（本地切片验收完成）

- 消费 R05e1 已验收的分页分块，新增独立 knowledge-pdf-plan-v1 / knowledge-pdf-manifest-v1，保持文本 v1 字节与摘要兼容。来源、租户、知识库/模型精确版本、解析器/词表/默认参数、页码、页内 UTF-16 位置与完整批次元数据共同进入 SHA-256；摘要不授予外发、持久化或 ready 资格。
- 先建立失败测试，再实现有界完整冻结、16 条/32000 字符批次与严格清单解码。页号从 1 连续递增，同页块位置和覆盖终点前进，跨页位置重置；128 页、2048 块、全局字符/token 预算失败时不交付部分计划。清单不包含正文，限制 128 KiB、严格 UTF-8、固定版本、连续集合、精确 EOF 与根摘要。
- 验证真实分页分块到清单消费、位置和身份摘要隔离、恶意长度/截断/篡改、取消与不可变性；按官方影响集、AI 单元、架构和 AOT 分析门禁执行后提交。PDF 页感知持久化、向量载荷、Worker 与公开 PDF 入队留待后续切片；完整 RAG 保持 Planned、容量保持 Capacity-not-verified。

- 交付 `AiKnowledgeIndexPdfPlan.cs` / `AiKnowledgeIndexPdfManifestCodec.cs`；没有修改文本 v1、SQL、宿主注册或公开 MIME 门禁。基线 `728186b5df1b4718d143b8bbb2e9b27886cd394f`，任务快照 `ai-knowledge-pdf-plan-r05e4`；Windows x64 本地 .NET SDK 10.0.401、Release、测试进程限制 2 个处理器。
- 失败验证：有效分页输入在规划入口占位实现下返回 null，单项断言失败；实现后 PDF 聚焦 67/67，通过独立 Node 小端参考摘要、真实 C# 分页读取/模型分块消费、正确重算摘要但非法的元数据、边界与跨格式拒绝测试。最终 `node artifacts/r05e4/run-unit.mjs` 按官方矩阵参数执行 AI 单元 1282/1282；全 Unit 实际发现 4751，仅报告 AI 执行范围，发现数已同步唯一测试矩阵。
- `pwsh -File artifacts/r05e4/final.ps1` 退出 0：Host.Api `-p:FullNetAotAnalysis=true` 分析、Integration/Unit/Architecture/Worker 最终 JIT 构建均零警告/错误；架构 `ai-agentic-boundaries` 7/7、`api-native-aot` 73/73。`run-affected-integration.mjs --snapshot ai-knowledge-pdf-plan-r05e4 --phase slice` 执行正式 Ai / integration-matrix 影响集，SQL Server/MySQL AI 78/78、零失败/跳过。没有在此切片重新发布或运行 Linux 原生宿主。
- `pnpm test:governance` 57/57、`pnpm test:naming` 33/33、`pnpm test:integration:partitions` 无遗漏/重复；只读审查无阻断问题。`artifacts/r05e4/acceptance.json` 按完整 UID/FQN 核对 AI 单元与双库结果、1114 个源码/矩阵/运行输入 SHA-256 无变化，Worker 74 个项目依赖与 Integration JIT 闭包一致。TRX 对 3 项成功用例的诊断文本按既有上限截断，用例身份和结果集合完整；保留原始日志与结果，不把诊断限幅报告为失败或跳过。

### 2026-10-06：R05e5 PDF 页感知向量载荷与清单绑定（本地切片验收完成）

- 新增 Knowledge/AiKnowledgeIndexPdfVectorCodec.cs，消费 R05e4 的冻结 PDF plan/batch 与精确模型维度向量，输出独立 knowledge-pdf-vector-batch-v1。显式小端编码来源/模型/租户身份、固定算法与参数、完整计划摘要、批次序号/摘要、全局块序号、页号、页内 UTF-16 位置、原文/embedding 输入与 IEEE float；文本向量 v1 字节保持兼容。
- Encode 只接受完整 plan 的指定批次；全部集合和维度先限界再复制，严格 UTF-8、16 条/32000 字符、512 KiB 总载荷、1536/3072 与批准模型匹配、有限且非零范数。Decode 验证算法、身份、页范围、位置推进、序号、剩余字节、向量数值和精确 EOF，并从实际分页块复算批次摘要，不返回部分载荷。
- ReadBatch 消费完整 PDF manifest 原字节、预期 batch ordinal 与 vector 原字节，重新解码并核对完整计划身份/摘要、批次摘要、片段数量/序号/token/页范围；返回实际捕获载荷 SHA-256 的快照，不能用重编码替代原字节。该证明不授予来源访问、外发、持久化或 ready 资格，实际 store 接入仍需权威上下文、操作/代次与批准版本复核。
- 新增 Unit/AiKnowledgeIndexPdfVectorCodecTests.cs：先失败验证，再实现；覆盖真实 PDF plan→manifest→vector→ReadBatch、跨页重置、后续批次从非零序号开始、两个模型精确 float bit、输入别名隔离、坏向量/字段/UTF/长度/EOF、自洽但错清单/位置/完整计划/批次、取消与限额；独立 Node 参考固定 wire SHA。更新唯一测试矩阵，按官方 AI 影响集、架构、AOT 分析与治理/命名/分片验证后在 main 提交。本切片不修改 SQL、Worker 或公开 PDF 入队；持久化接入为下一阶段，完整 RAG 保持 Planned、容量保持 Capacity-not-verified。

- 基线 `295d3d34742d7826b1f9db2d0849ab279ac7ba50`，任务快照 `ai-knowledge-pdf-vectors-r05e5`。有效 PDF 批次在 Encode 占位实现下产生空载荷，实际单项测试失败；实现后 `dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter FullyQualifiedName~AiKnowledgeIndexPdfVectorCodecTests --no-ansi --progress off` 聚焦 79/79，零失败/跳过，Release 构建零警告/错误。
- 实现前独立 Node 小端参考得到 13011 字节和 SHA-256 `4A338029C9055F00BB64FB5A1CCBA5D83A113DCB0F823AA5217F5DF17C95575C`，C# wire 固定测试一致。全 Unit 实际发现 4830、AI 1361，本次新增 79；唯一测试矩阵按实际发现数更新，发现数量不代表全 Unit 已执行。只读审查未发现阻断问题，审查不替代运行验证。
- `dotnet build src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj -c Release -p:FullNetAotAnalysis=true`（单节点、关闭共享编译）分析零警告/错误；恢复默认 JIT 依赖图后 Integration/Unit/Architecture/Worker Release 最终构建均零警告/错误。`node artifacts/r05e5/run-unit.mjs` 消费官方矩阵参数，AI 单元 1361/1361，零失败/跳过；`run-dotnet-test-suite.mjs architecture --selection ai-agentic-boundaries --no-build` 7/7、`--selection api-native-aot --no-build` 73/73。
- 环境为 Windows x64、i7-12700H、64 GB 内存、.NET SDK 10.0.401、Docker 双提供程序；测试进程限制 2 个处理器、GC 堆 2 GiB，容器不复用。此次没有重新发布或运行 Linux 原生宿主，也没有调用真实云模型或执行容量实测。
- `pwsh -File artifacts/r05e5/final.ps1` 退出 0；`node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-vectors-r05e5 --phase slice` 执行正式 Ai / integration-matrix 影响集，SQL Server/MySQL AI 78/78，零失败/跳过，26m 29s 115ms。`artifacts/r05e5/acceptance.json` 逐项核对 AI 单元与双库完整 UID/FQN、1113 个源码/矩阵/运行输入 SHA-256 未变化；Worker 74 个项目依赖与 Integration 最终 JIT 闭包一致。
- `pnpm test:governance` 57/57、`pnpm test:naming` 33/33、`pnpm test:integration:partitions` 合计 1184 项无遗漏/重复。TRX 对 3 项成功用例的诊断文本按既有字段上限截断，用例身份和结果完整；保留原始日志与结果，诊断限幅不计为失败或跳过。下一切片为 PDF 持久化接入，继续核对权威租户/来源、原会话、操作结算、租约与模型批准版本；完整 RAG 未整体完成。

### 2026-10-06：R05e6 PDF 完整清单持久化（本地切片验收完成）

- 基线 `b9073869f52cb30e3f23438b948c5778f2339429`，快照 `ai-knowledge-pdf-manifest-r05e6`。新增 `Knowledge/AiKnowledgeIndexPdfManifestStore.cs`，消费 PDF plan，提供 `PersistAsync(binding, taskId, leaseToken, plan, token)` 与 `ReadSnapshotAsync(binding, taskId, leaseToken, token)`；只证明当前任务完整计划，不授予向量齐全、模型外发或 ready。
- 保留 `fn_ai_knowledge_text_manifest` 的既有物理名称、任务主键、外键、SQL scope 与静态行物化器；同一任务的文本/PDF 清单互斥，避免建立可并存的第二权威集合。252 双库迁移先增加允许文本/PDF 且保持 8–131072 字节的约束，再移除旧文本限定约束，部分完成重放保留原行。更新实际表/载荷注释及对应元数据；未授权改旧迁移、回填或修改清单正文。
- PDF store 冻结原字节，核对可信租户、来源/知识库/模型精确版本、`application/pdf`、原会话五项精确权限；授权在事务外，写入仅复用 AI 本地 INSERT 的锁和实时来源条件。唯一竞争只能复用同字节 SHA，写后到期/取消必须回滚；恢复核对格式、原字节 SHA、PDF decoder 和等待后的最新权威条件。继续复用既有清理和 HasDerivedData 判定。
- 先建立 `AiKnowledgeIndexPdfManifestStoreTests` 的有效 Host/Tenant 保存失败证据；补齐跨来源、权限撤销、等待漂移、损坏行、文本/PDF 互斥、唯一竞争、事务/取消/基础设施故障测试。新增 `Migration252AiKnowledgePdfManifestRecoveryTests`，冻结迁移上界，真实双库证明旧行保留、部分约束/元数据重放、格式/大小/唯一/FK 守卫。
- 新增 `AiKnowledgeIndexPdfManifest*Tests` 与共享断言，用生产 executor/transaction/session/task 路径验证 Host/Tenant 真实保存、相同载荷竞争、改载荷拒绝、来源/批准/原会话漂移、事务回滚与清理。按实际发现同步唯一矩阵和 252 恢复选择器；官方 slice 影响集、AI Unit、AOT 分析/架构、命名、SQL 安全和分片通过后在 main 提交。本项保持公开 PDF 入队和 Worker 未接入，完整 RAG 为 Planned，容量为 Capacity-not-verified。

- 失败验证分两层：PDF Store 占位返回 false 时有效 Host/Tenant 保存 2/2 失败；缺少 252 脚本时双库恢复测试 2/2 失败（脚本发现为 0）。编译期类型错误不计为 RED。实现后首次聚焦 Unit 38/38、双库持久化/恢复 4/4；后续严格格式修正以最终构建和正式 slice 重新验收。
- 只读审查指出双库字符串比较会忽略尾随空格；252 为文本 26/PDF 25 字节同时校验精确格式和长度，补充两类尾随空格拒绝。MySQL 按命名规则 §10.6 使用 INFORMATION_SCHEMA 与固定字面量 PREPARE/EXECUTE，登记精确文件级 dynamic_sql 债务；既有非法格式导致新约束验证失败时保留旧守卫，不隐式改写旧数据。复核无新增阻断问题。
- 清单在模型调用前保存，不要求已结算的付费模型操作；此 store 仍要求完整导入、当前批准模型、原会话五项权限与持有租约。操作结算、向量齐全和完整代次发布证明属于后续向量 store；当前没有 DI、Worker 或公开 PDF 入队接入。

- `pwsh -File artifacts/r05e6/final.ps1` 退出 0：Host.Api `-p:FullNetAotAnalysis=true` 分析零警告/错误（10m 46s）；恢复默认 JIT 后 Integration/Unit/Architecture/Worker Release 最终构建均零警告/错误。`node artifacts/r05e6/run-unit.mjs` 按官方矩阵参数执行 AI 单元 1399/1399，零失败/跳过；全 Unit 实际发现 4868，只报告本次 AI 执行范围。架构 `ai-agentic-boundaries` 7/7、`api-native-aot` 73/73。
- `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-manifest-r05e6 --phase slice` 执行正式 Ai / integration-matrix / migration-252 影响集，SQL Server/MySQL 82/82，零失败/跳过，36m 32s 222ms；包含 AI 80 项与 252 恢复 2 项，新增持久化/恢复 4 项在最终严格约束实现下均通过。`artifacts/r05e6/acceptance.json` 逐项核对完整 UID/FQN，1122 个源码/矩阵/运行输入 SHA-256 未变化，Worker 74 个项目依赖与 Integration 最终 JIT 闭包一致。
- `pnpm test:naming` 33/33、`pnpm test:sql-safety` 5/5、`pnpm test:governance` 57/57；`pnpm test:integration:partitions` 合计 1188 项无遗漏/重复（API 双库各 195，迁移 532，基础设施 186，消息 80；迁移旧段 250、当前段 282）。TRX 对 3 项成功用例的诊断文本按既有字段上限截断，用例身份和结果完整；保留原始日志与结果，不计为失败或跳过。
- Windows x64、i7-12700H、64 GB、.NET SDK 10.0.401、Docker 双提供程序；测试进程限制 2 个处理器、GC 堆 2 GiB，容器不复用。本次没有重新发布或运行 Linux 原生宿主、调用真实云模型或执行容量实测。下一切片为 PDF 向量持久化与操作结算绑定；完整 RAG 保持 Planned，容量保持 Capacity-not-verified。

### 2026-10-06：R05e7a PDF 向量格式双库扩展（本地切片验收完成）

- 基线 main `1d0c8a700995460d04c0abc56e2de4b0d79d9b27`，任务快照 `ai-knowledge-pdf-vector-schema-r05e7a`；保护任务前两项无关未跟踪文件。本切片只交付后续 PDF vector store 的数据库前置，不接入运行时、模型调用、结算或 ready。
- 文件职责：新增双库 `253_AiKnowledgePdfVector.sql` 扩展既有向量表；新增 `Migration253AiKnowledgePdfVectorRecoveryTests.cs` 覆盖真实 DDL/中断恢复；更新对象注释来源、生成目录、精确 MySQL dynamic_sql 登记及测试矩阵。沿用批次主键/外键，不建新表，不重写旧载荷。
- [x] RED：先运行新双库恢复用例；无 253 脚本时，精确脚本数断言及非法历史行拒绝断言必须失败。
- [x] GREEN：先新增并验证 Formats 守卫，再移除旧 Bounds；文本/PDF 格式精确字节长度分别 25/29，维度白名单 1536/3072、cosine 精确 6 字节、载荷 8..524288。固定 INFORMATION_SCHEMA 字面量 DDL，不接受外部输入。
- [x] 验证：半完成新旧守卫并存、未记账重放、丢失/禁用约束、丢失注释；保留文本/PDF 原字节及摘要；拒绝未知/大小写/尾空格格式、越界维度/载荷、重复及孤儿行。合法旧文本允许应用回退读取；非法历史行失败关闭且旧守卫不删除，由人工显式修复后前滚重放，不自动收缩含 PDF 的表。
- [x] 验收：Release Integration 构建、正式 affected slice（注册 migration-253，自动追加 Ai）、naming/sql-safety/governance/partitions；核对最终发现 UID/FQN 和结果、退出码及跳过信息；审查后 main 提交。
- 后续 R05e7b：私有 PDF vector store 复用现有已结算账本/原会话/任务 SQL，绑定已持久化完整 PDF 清单；本切片不能证明该运行时行为。完整 RAG 仍 Planned，容量仍 Capacity-not-verified。

- RED 证据：补齐测试命名空间引用后 Release 构建零警告/错误；目标脚本尚不存在时运行 `dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --filter FullyQualifiedName~Migration253AiKnowledgePdfVectorRecoveryTests --no-ansi --progress off`，四项均按脚本数量为 0 / 非法历史行未被迁移拒绝的预期失败，退出 2、零跳过。首次缺少命名空间的编译错误未计为 RED。
- GREEN 证据：最终双库脚本及恢复用例 Release 构建零警告/错误；聚焦 4/4 通过、零失败/跳过（33s 261ms），覆盖新增及启用新守卫两阶段的非法历史行失败关闭。只读代码审查无阻塞问题；MySQL 新约束存在时 MODIFY Payload 恢复注释由真实用例通过验证。
- `pwsh -File artifacts/r05e7a/final.ps1` 退出 0；其中 `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-vector-schema-r05e7a --phase slice` 正式 Ai / integration-matrix / migration-253 影响集 84/84，零失败/跳过，33m 11s 447ms；包含 AI 80 项和 253 双库恢复 4 项。`artifacts/r05e7a/acceptance.json` 核对完整 UID/FQN 与 354 个任务源码、矩阵和 Integration 运行输入 SHA-256，均未变化。原始 TRX 对两项成功用例的诊断文本按既有字段上限截断，用例身份和结果完整。
- `pnpm test:naming` 33/33、`pnpm test:sql-safety` 5/5、`pnpm test:governance` 57/57；正式影响集内工具链 65/65。`pnpm test:integration:partitions` 合计 1192 项无遗漏/重复（API 双库各 195，迁移 536，基础设施 186，消息 80；迁移旧段 250、当前段 286）。
- Windows x64、.NET SDK 10.0.401、Docker SQL Server/MySQL；最终测试进程限制 2 个处理器、GC 堆 2 GiB，容器不复用。本次验收为数据库格式和恢复范围，没有新增运行时写入、模型调用、结算或 Worker 接入；未重跑独立 Unit/AOT/容量套件，未重新发布 Linux 原生宿主。下一项 R05e7b 继续 PDF 向量持久化与操作结算绑定；完整 RAG 保持 Planned，容量保持 Capacity-not-verified。

### 2026-10-06：R05e7b PDF 向量持久化与操作结算绑定（本地切片验收完成）

- 基线 main `653505fcb9a88be3589b318a27a5176da8705e25`，快照 `ai-knowledge-pdf-vector-store-r05e7b`。使用 fullnet-module-delivery、writing-plans、test-driven-development；沿用 253 双库格式，不新增迁移或表。
- 私有 `AiKnowledgeIndexPdfVectorStore` 提供 `PersistAsync(binding, taskId, leaseToken, operationId, plan, ordinal, vectors, token)`、`ReadSnapshotAsync(binding, taskId, leaseToken, ordinal, token)` / `IsStoredAsync`；恢复回执包含操作标识与已核对的 PDF 批次。复用现有向量/清单行静态物化器，参数使用字典；不增加 DI、公开入队、Worker、模型调用或 ready 路径。
- [x] RED：Host/Tenant 合法完整清单、已结算匹配操作的持久化/恢复必须先失败；观察能力缺失断言，不将编译错误计为 RED。
- [x] 实现：首次等待前冻结分页向量；从数据库捕获完整 PDF 清单原字节并校验实际 SHA、来源和批准模型；写入原子且不可覆盖。原会话五项权限在本地事务外授权，所有等待后复核租约、来源和绑定；插入后的取消/到期在事务内抛出回滚。
- [x] 双库 SQL：在既有任务/批次/租约/原会话/已结算成功账本守卫上追加完整 PDF 清单原始 SHA 和实际字节摘要、精确格式；账本结算时间非空。恢复最后 SQL 再核对当前账本及原载荷，单批结果不授予整代次 ready。
- [x] 验证：匹配与不匹配计划/批次/模型/维度/租户；缺失/损坏/旧格式清单或向量；五项权限、异步等待撤权/源漂移；并发同载荷幂等、异载荷拒绝；基础设施错误传播、取消与过期回滚、清理不遗留；真实双库直接生产 Statement 的守卫（Unit 无法证明真实 SQL/事务）。更新矩阵后运行正式 affected slice、Unit/Architecture、命名/SQL安全/治理/分片和只读审查。
- [x] 本地验收后 main 单独提交。完整 RAG 保持 Planned，容量保持 Capacity-not-verified；后续再建设 PDF 批次派发及完整代次证明。
- 初始 RED：占位持久化返回 false，Host/Tenant 合法保存 2/2 失败；首次聚焦 Unit 47/47。只读审查发现末次向量 SQL 等待的撤权窗口，追加回归得到权限/会话/来源/模型 4/5 失败（到期已拒绝），追加末次读取后的原会话授权、模型与来源复核后聚焦 Unit 52/52；审查复核无剩余阻断。
- SQL Server 的 string 参数默认 Unicode，格式参数字节长度使用固定 varchar 转换；列格式仍按二进制精确比较和 25/29 字节限制。SQL Server/MySQL 聚焦测试 2/2，通过生产批次/预算预留结算与向量事务验证 Host/Tenant、两种维度、最后单块页、同载荷竞争、账本/清单拒绝、到期回滚及清理保留账本。合成分页与受控原会话授权不代表真实 PDF 文件解析、HTTP 登录或云模型调用。
- 恢复的账本/清单证据只对应最后一次向量 SQL 判定点，之后复核原会话与来源；不能把跨模块的分步验证当作原子授权快照或后续 ready 资格。正式本地影响集验收完成。
- `pwsh -File artifacts/r05e7b/final.ps1` 退出 0：Host.Api `FullNetAotAnalysis=true` 构建通过，零警告/错误；恢复默认 JIT 后顺序构建 Integration、Unit、Architecture 与 Worker，全部通过。正式 AI Unit 1451/1451（37s 801ms）、AI 边界架构 7/7、Native AOT 架构 73/73；Ai/Tenancy 三处程序集副本及 Worker 的 74 项 JIT 项目依赖一致。
- `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-vector-store-r05e7b --phase slice` 正式 Ai / integration-matrix 影响集 82/82，零失败/跳过，33m 16s 600ms；本切片没有新迁移，不追加迁移恢复选择器。`artifacts/r05e7b/acceptance.json` 核对全部 UID/FQN 与 1117 个任务源码、矩阵和运行输入 SHA-256，均未变化。两项成功用例的诊断文本按既有 TRX 字段上限截断，用例身份和结果完整。
- 唯一矩阵按实际发现同步 Unit 4920、AI Unit 1451、正式 Integration 分片 1194：SQL Server/MySQL API 各 196，迁移 536，基础设施 186，消息 80；无遗漏/重复。正式影响集内工具链 65/65、治理 57/57；命名与 SQL 安全分别 33/33、5/5。
- Windows x64、i7-12700H、64 GB、.NET SDK 10.0.401、Docker SQL Server/MySQL；测试进程限制 2 个处理器、GC 堆 2 GiB，容器不复用。本次没有真实云模型调用、公开 PDF 入队/Worker 接入、Linux 原生宿主重新发布/运行或容量实测。下一切片为 PDF 批次派发及完整代次证明；完整 RAG 仍 Planned，容量仍 Capacity-not-verified。

### 2026-10-06：R05e8a PDF 完整代次证明（本地切片验收完成）

- 基线 main `6631da77b82a8d63467f019df6a101b345428cc8`，沿用本轮快照 `ai-knowledge-pdf-dispatch-r05e8a`。调用链确认尚缺 .NET 隔离解析进程接入，先交付不依赖模型派发的全量证明，解析桥接与派发后续实施。
- 契约：私有 PDF store 只消费数据库中的完整清单，逐批验证真实载荷、分页位置、模型请求指纹和成功结算，再比较前后全量回执。无调用方正文/清单、公开入队、DI、Worker 或 ready 扩展。
- 数据归属只涉及 AI 自有表，跨模块读取复用原会话授权 Port，授权不进入本地事务。沿用现有 253 格式，无新迁移；瞬时证明不构成跨模块原子授权或后续激活资格。
- [x] RED：Host/Tenant 两种维度的完整两批分页证明先失败。
- [x] 成对 SQL：完整清单原字节 SHA、成功已结算账本、向量实际 SHA、原始批次数、固定格式和有界排序；额外未结算批次也不能被 JOIN 隐藏。
- [x] 逐批验证与前后回执比对：缺失/重复/调序/篡改/损坏/替换、撤权/来源/模型/会话/清单变化、到期/取消、异常传播，不累计全代次向量。
- [x] 真实双库生产 SQL 与完整证明验证；本地正式 affected slice、Unit/Architecture、命名/SQL安全/治理/分片与只读安全审查。
- [x] 验收后 main 单独提交，完整 RAG 保持 Planned，容量保持 Capacity-not-verified。

- 私有 `ReadWholePlanProofAsync(binding, taskId, leaseToken, token)` 不接受调用方清单。冻结数据库完整 PDF 清单后，按序逐批核对实际载荷、分页位置、模型请求指纹、操作标识与原字节 SHA，再比较前后全量回执；返回专用 PDF 瞬时证明（来源、原会话、清单摘要、轻量回执），不累计整代次向量。消费者后续激活仍须在提交事务中复核。
- 四个 Host/Tenant、SQL Server/MySQL 回执 Statement 复用既有任务/来源/原会话守卫，增加完整 PDF 清单原字节 SHA、精确 PDF 格式与已结算成功账本约束；保留实际向量载荷 SHA、原始批次数与 2049 行上限。多出的未结算批次不能被 JOIN 隐藏。末次回执等待后再次核对完整清单、原会话五权限、租约、来源和完整模型绑定；同版本 Endpoint/Organization 漂移也拒绝。
- 有效 RED：Host/Tenant 两种维度 4/4 正向用例失败于证明为空；最终聚焦 Unit 91/91。补充首/末回执等待的同版本目的地漂移、清单替换及取消传播，覆盖缺失/额外/重复/调序/篡改回执、首批在末批等待时改变、权限/会话/来源/模型失效、到期与异常传播。只读审查最终无剩余 Critical/Important/Minor；生产内部类型没有为动态代理扩大可见性。
- `pwsh -File artifacts/r05e8a/final.ps1` 完成 Host.Api `FullNetAotAnalysis=true` 构建（11m 51s，零警告/错误）、恢复默认 JIT 并顺序构建 Integration（8m 08s）、Unit（1m 19s）、Architecture（26s）与 Worker，均零警告/错误。工具宿主中断于 AI Unit 运行阶段，核对程序集一致后用 `artifacts/r05e8a/resume.ps1` 续跑：AI Unit 1490/1490（39s 646ms）、AI 边界架构 7/7、Native AOT 架构 73/73（11s 435ms）；Ai/Tenancy 三处副本与 Worker 74 项 JIT 项目依赖一致。
- 首次续跑的正式影响集因 Docker 服务停止而在容器初始化前失败 82/82，不计为通过；恢复已有 Docker Desktop/服务并确认 1116 项冻结输入未变后，`pwsh -File artifacts/r05e8a/resume-integration.ps1` 退出 0。其生产命令 `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-dispatch-r05e8a --phase slice` 正式 Ai / integration-matrix 影响集 82/82，零失败/跳过，25m 46s 010ms；完整 PDF 证明直接覆盖真实双库生产查询，未增加迁移恢复选择器。
- `artifacts/r05e8a/acceptance.json` 核对完整 UID/FQN 与 1116 项任务源码、矩阵及运行输入 SHA-256，全部一致。两项成功用例诊断文本按既有 TRX 字段上限截断，用例身份和结果完整。唯一矩阵按实际发现同步 Unit 4959、AI Unit 1490；正式 Integration 分片 1194：API 双库各 196、迁移 536、基础设施 186、消息 80，无遗漏/重复。工具链 65/65、治理 57/57；命名 33/33、SQL 安全 5/5。
- Windows x64、i7-12700H、约 64 GB、.NET SDK 10.0.401、Docker 29.6.2；SQL Server `2022-CU14-ubuntu-22.04`、MySQL 8.0。测试进程限制 2 个处理器、GC 堆 2 GiB，容器不复用。合成分页和受控原会话授权不能证明真实 PDF 解析、HTTP 登录或云模型派发；本次没有 Linux 原生宿主重新发布/运行、公开 PDF 入队/Worker/ready 接入或容量实测。后续 R05e8b 建设 .NET 隔离 PDF 解析接入与内部批次派发；完整 RAG 仍 Planned，容量仍 Capacity-not-verified。

### 2026-10-06：R05e8b1 .NET 隔离 PDF 解析桥接（本地切片验收完成）

- main 基线 `74532ac4973fa37aeae346a348a922065cdcc277`，快照 `ai-knowledge-pdf-parser-bridge-r05e8b1`。先交付可独立审查的进程桥接，批次派发后续 R05e8b2 实施；无 DI、公开入队、Worker 或 ready 扩展。
- 在既有 CLI 增加显式 `framed-v1` stdin 模式：按可信来源长度读取原始 PDF 后保留管道；EOF、关闭或额外字节通知取消，持续覆盖解析与输出交付。原 EOF 模式保持兼容；取消须等待已有 Docker 控制器确认停止与清理，不能只杀控制客户端。
- 私有 .NET parser 接受调用者已授权取得的有界原始字节与可信 SHA/长度；首次等待前复制并校验实际原字节，固定可信 Node/CLI 路径及不可变 image ID，无 shell/请求路径，清除宿主模型凭据环境。进程输入、输出、错误与等待共同受限；完整 EOF、退出 0、零 stderr 与 PDF 输出读取器验收齐全后才返回全部页。审查发现任意 Stream 可忽略取消，因此本切片不承担来源读取；后续派发入口须在自己的可中止文件边界有界取得字节并复核授权。
- [x] RED：framed-v1 能力缺失导致合法帧/取消等用例失败；.NET 正向用例失败于返回空，编译错误未计为 RED。首次 .NET 聚焦中两项输出越界用例暴露清理重新抛出 InvalidDataException，修复异常收敛和必执行清零；Windows 背压先关闭已拒绝输出管道再通知 EOF。只读安全复核无剩余 Critical/Important/Minor。
- [x] 实现：跨平台 stdin 关闭取消；.NET 无外层事务、首次等待前冻结实际来源、固定参数、环境白名单、有界输出与整体截止，独立有界等待控制器清理。失败不返回部分正文，不把 SHA 声明、完整 JSON 或 stdout EOF 单独作为成功；完整输出但进程未退出、调用者缓冲变更和等待后事务变化均覆盖。
- [x] 聚焦与原生：.NET 桥接 26/26，`node --test` 隔离脚本套件 37/37。`run-framed-native.mjs` 使用显式不可变 parser/probe 镜像完成 6 项真实解析/运行取消/输出背压取消/超时及归属资源删除验证；另用内部友元探针完成 .NET → CLI → 原生镜像两种合成中文 PDF 与截止取消共 3 项，探针 AI 程序集与最终 JIT SHA-256 一致。既有 native 源码、原生产物和镜像身份核对后复用，没有重新发布宿主。
- [x] 构建与范围：`artifacts/r05e8b1/final.ps1` 的 Host.Api `FullNetAotAnalysis=true` 构建零警告/错误（14m 20s），默认 JIT Integration、Unit、Architecture 和 Worker 均零警告/错误；最终 AI Unit 1516/1516、AI 架构 7/7、Native AOT 架构 73/73。Ai/Tenancy 三处副本及 Worker 74 项依赖一致；矩阵按实际发现同步 Unit 4985、AI Unit 1516。
- [x] 双库证据：正式命令 `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-parser-bridge-r05e8b1 --phase slice` 首轮 81/82、失败 1、跳过 0（34m 27s）。失败为既有 MySQL 清理用例的文本整代次返回 false，该路径未引用本次私有解析器；失败日志/TRX 完整保留，不计首轮为通过。使用同一冻结输入，`focus-failure.ps1` 独立复跑对应 SQL Server/MySQL 场景 2/2、零失败/跳过（3m 58s），未复现、未修改或宣告修复该路径。按 §11.5 复用未变输入的成功证据，`verify-final-scope.mjs` 校验 1116 项输入 SHA 和完整 UID/FQN，正式影响集 82 项各自最后一次实际结果均 Passed；`acceptance.json` 同时保留首轮失败、复跑来源与最终结果，未伪造单轮全绿报告。
- [x] 治理：命名 33/33、SQL 安全 5/5、治理 57/57；正式影响集工具链 65/65、治理 57/57，Integration 分片 1194 项无遗漏/重复。Windows x64、i7-12700H、约 64 GB、SDK 10.0.401、Docker 29.6.2；SQL Server 2022-CU14、MySQL 8.0，测试进程 2 个处理器/GC 堆 2 GiB、容器不复用。未复现的首轮失败不外推为已定位或修复。
- 暂存结构检查发现测试空白行尾随空格，机械清理后重新构建 Unit（1m 19s，零警告/错误）并重跑 AI Unit 1516/1516（45s 918ms）。`whitespace-input-transition.json` 验证仅测试空白行及 Unit 程序集改变，生产与 Integration 输入全部不变；重新冻结后再次核对完整 UID/FQN。暂存 diff 检查通过。
- 收尾：main 单独提交本切片；下一项 R05e8b2 为内部 PDF 批次派发。完整 RAG 仍 Planned，容量仍 Capacity-not-verified；没有公开 PDF 入队、Worker、ready、云模型派发或容量实测，调用者仍须有界取得来源并复核原会话和来源。

### 2026-10-06：R05e8b2a PDF 来源有界读取（本地切片验收完成）

- main 基线 `affd66f35fcff54fe672ad34e755e4a071986d94`，任务快照 `ai-knowledge-pdf-dispatch-r05e8b2`。R05e8b2 的解析桥接只接受冻结字节，因此先独立交付 Files 独占流的读取边界，再接入原会话授权、PDF 清单冻结与付费批次派发。
- 本切片文件：`Knowledge/AiKnowledgePdfSourceReader.cs` 与对应 Unit；结果为可释放的私有字节缓冲，仍由消费者复核租户、原会话、来源与模型目的地。不增加公开 API、DI、SQL、迁移或 Worker 路径，不把缓冲完成视为派发资格。
- 执行顺序：先验证精确来源必须在 EOF 和流释放后才交付的失败测试；实现大小及 SHA-256 绑定、读取期限、独立清理期限、迟到读取与缓冲释放；补齐取消、事务变化、流故障和释放失败场景；按正式影响集执行验证并审查后在 main 单独提交。
- 资源约束：原字节最多 5 MiB，读取期限至多 25 秒，流清理最多 5 秒；流忽略取消、同步阻塞或释放失效时拒绝交付，迟到读取只能接触其私有小缓冲，不能写入已释放或交付的结果。
- 外层期限令牌与流 IO 令牌隔离，IO 回调使用 `CancelAsync()` 并纳入独立清理预算；取消先撤销并清空结果，再执行清理。读取、释放或回调无法确认结束时传播受控 IO 故障，观察迟到异常，等完整清理结束后才释放 IO CTS；不声称能强制终止任意不合作的 Stream。
- RED：有效正向测试因占位返回空失败；随后回归复现阻塞回调卡住调用者取消、自动 deadline，以及回调抛异常跳过释放的两场景。编译占位参数错误单独保留，不计为 RED。修复后聚焦 31/31 通过；只读审查的两项 Important 已修复，复审无剩余 Critical/Important/Minor。
- `artifacts/r05e8b2a/final.ps1` 完成 Host.Api AOT/Trim 分析与默认 JIT 验收构建，均零告警/错误；AI Unit 1547/1547、AI 架构 7/7、Native AOT 架构 73/73 通过。AI/Tenancy 三处副本和 Worker 的 74 项依赖一致，冻结 1113 项输入；`node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-dispatch-r05e8b2 --phase slice` 单次执行双 Provider AI 82/82，通过、零失败/跳过，33 分 32 秒。最终 `verify.mjs` 核对 Unit 与 Integration 的精确 UID/FQN 集合及全部冻结输入，原始控制台、TRX 与发现记录保留；TRX 两项测试的超长文本字段按平台上限截断，不改变用例结果。
- 实际组合探针 3/3：真实 FileStream 在交付前关闭，明文/压缩的两份合成 PDF 均经现有隔离原生工具取得两页中文；受控挂起输入按期限结束并完成清理，保留的源内存清零。探针与最终验收的 AI DLL 摘要相同，原生工具源码、二进制及两个不可变镜像均复核未变，未重建它们；没有遗留 PDF 归属标签容器。这不证明原会话授权、HTTP 登录、云模型派发或 Host.Api 原生发布运行。
- `pnpm test:naming` 33/33、`pnpm test:governance` 57/57、`pnpm test:integration:tooling` 54/54 通过；正式 affected 的工具链 65/65、治理 57/57 通过。`pnpm test:integration:partitions` 确认 1194 项无遗漏/重复，测试矩阵按实际 Unit 发现更新。Windows x64、i7-12700H、约 64 GB，SDK 10.0.401、Docker 29.6.2，SQL Server 2022-CU14 / MySQL 8.0；测试进程 2 个处理器、GC 堆 2 GiB、容器不复用。
- 后续 R05e8b2b 接入来源权威复核与解析规划；付费派发须先冻结全清单、取得新预留，并在客户端创建、模型返回及结算等待后复核，保持未知用量与禁止免费重发。
- 收尾：main 单独提交本切片。R05e8b2 的授权接入、全清单与付费批次派发尚未交付；完整 RAG 仍 Planned，容量仍 Capacity-not-verified。

### R05e8b2b：来源权威复核与 PDF 完整解析规划（随下方批次完成）

- 目标：将 R05e8b2a 的独占有界来源读取与 R05e8b1 隔离解析连接到现有 PDF 逐页分块、整份规划；不接入付费操作、Worker、公共 PDF 入队或 ready。
- 新增 `AiKnowledgeIndexPdfPlanningService.cs` 与对应 Unit：使用当前可信租户、原冻结会话、五项精确权限、有效租约、知识库批准版本和完整模型指纹。Host 使用就绪引用及内容端口，Tenant 使用 `ai`/版本归属内容端口。
- [x] 有效 RED：Host/Tenant 成功路径 2/2 失败于空结果；复核回归中同版本模型地址、组织与分类漂移及取消回调阻塞/抛错 7/7 失败；不合作模型查询、授权与引用端口 3/3 超出调用边界。编译错误不计为 RED。新增规划 Unit 81 项。
- [x] 一个总计不超过 25 秒的工作期限覆盖权威读取、Files、字节校验、隔离解析和本地规划；外部端口使用私有取消令牌，异步取消回调和迟到资源独立等待最多 5 秒，未确认清理即失败。未移交的流由入口释放，移交后由 SourceReader 独占负责，字节缓存始终清零释放。每次等待后复核，返回完整计划不代表长期授权。
- 派发读取最后一次同时绑定来源、知识库分类与完整模型目的地，覆盖版本号未变时的配置漂移；不投影凭据。共享 Dispatch 与 SQL Server/MySQL、Host/Tenant 四个 Activation 查询均补齐静态行读取字段。新增 6 项从实际 SQL SELECT 投影构造原生行读取输入的回归；有效 RED 为 4 项 Activation 缺列失败，Dispatch 2 项通过，最终规划/派发聚焦 104/104。
- 双库现有索引集成正向场景直接读取生产派发查询，核对新增字段及同版本组织变更，再恢复种子值。只读复核最终没有剩余 Critical/Important/Minor；没有改动文本付费服务的来源比较语义，也没有扩大内部类型的动态代理可见性。
- `pwsh -File artifacts/r05e8b2b/final.ps1` 的 Host.Api AOT 分析 17m 22s、默认 Integration 构建 13m 02s、Unit 构建 2m 08s、Architecture 构建 48s 与 Worker 构建 16s 均零警告/错误。AI Unit 1634/1634（1m 33s 527ms）、AI 架构 7/7、Native AOT 架构 73/73；Ai/Tenancy 三处副本及 Worker 74 项项目依赖一致。唯一矩阵仅按实际发现同步 Unit 5103、AI Unit 1634，未调整键顺序。
- 官方 Linux Native AOT 发布生成 Host.Api 137502496 字节，33m 49s；17 项既有允许警告通过官方警告门禁，不将此报告为零警告发布。Windows 构建与 Linux 容器产物目录隔离。首次 Linux 测试容器因一次性源码清单漏掉企业示例而编译失败，不计通过；补齐构建输入后重跑。
- `pwsh -File artifacts/r05e8b2b/native-probe.ps1` 真实 Files 受控来源→原会话/来源复核→既有 Native PDF 解析镜像→完整计划 3/3：非压缩/压缩两页中文 PDF、取消与来源流释放。探针夹具未初始化字段引起的编译错误先修正，不计通过。解析器/探针 Native 源码及二进制 SHA 与既有验收一致；本次未重建它们。正式 AI DLL SHA 与探针副本完全一致，未修改正式构建输入。
- 批量统一验收见下方；复用本段未变可达路径的 AOT 分析/发布证据，不重复为每个私有辅助类发布宿主。
- 本段保留批量推进前的规划切片证据；用户随后授权扩展为下方 b/c/d 同批实现、统一验收与提交。

### 2026-10-07：R05e8b2b/c/d PDF 规划、付费派发与单批持久化（本地切片验收完成）

- 用户要求加快推进、一次实现多个功能并统一测试；将规划、清单冻结、付费批次派发和单批持久化作为同批交付，完成前统一回归和提交。main 基线仍为 `8b7e2059555df7add10c8a8658339139dff97e91`，保留已有快照和验证日志，保护无关文件。
- [x] 在规划入口增加不重新读取正文的当前模型复核，绑定原会话、来源、分类和完整目的地；Host/Tenant 正向 RED 2 项失败，拒绝场景 5 项通过，实现后 7/7。
- [x] 内部 PDF 执行器先冻结完整清单再取得新预算预留；客户端创建、模型返回和结算等待后复核。进入可能计费调用前确定零用量，进入后异常保留未知用量；不自动重试或换模型。
- [x] 连接已批准的 PDF 单批向量存储，完整已存批次恢复不收费；缺失或未知操作禁止免费重发。7 项恢复/落库 RED 全部因占位能力失败；实现后使用真实清单与向量 Store 组合验证。双库现有 PDF 场景追加新消费者直接恢复与账本费用不变断言。保持全量证明与公共 ready 的后续边界。
- [x] 开发期间仅执行聚焦测试；统一核对完整 AI Unit、实际影响双库及已有 Native 可达路径证据，在 main 提交整批。先前正式双库影响集受运行时限中止，71 项通过但不计完整验收；重跑保持官方选择和预算。
- 私有 `AiKnowledgeIndexPdfEmbeddingService` / `AiKnowledgeIndexPdfPersistenceService` 不增加 DI、公开 PDF 入队、Worker 或 ready。每次只派发一个批次，先持久完整 PDF 清单，再使用稳定操作 ID 新预留预算；向量需成功结算后才能进入既有短事务，最后重读原载荷的操作与计划摘要。
- Provider 工厂、生成和释放共用独立 IO 生命周期；工作等待 25 秒、有界迟到清理 5 秒，无法确认清理即拒绝交付。结算使用独立 5 秒等待，不把迟到任务当作已停止。只读审查发现无界等待和计量闭包竞态，修正为锁保护的零/未知/已知收费回执，结算前冻结，迟到工作不能改写回执或开始计费。工厂/生成/释放超时回归有效 RED 为 2 失败/1 通过，最终复审无剩余 P1/P2。
- 聚焦 120/120，完整 AI Unit 1673/1673（1m 37s 550ms），AI 架构 7/7、Native AOT 架构 73/73；默认 Integration、Architecture 与 Worker 构建均零警告/错误。实际发现 Unit 5142、AI 1673，唯一矩阵仅修改数量。Ai/Tenancy 三处 DLL 及 Worker 74 项 JIT 依赖一致，冻结 1122 项最终源码、矩阵和运行输入。
- 最终 AI DLL `15FDC1A8DE47639DF0768D4E4978EC8493FD2011084E1BC5EB16A945CDDE09C0` 的真实受控 Files→来源/原会话复核→原生 PDF 隔离镜像→完整计划探针 3/3。一次性探针生成器提取新增嵌套测试类后的可见性编译错误已修正，未计为行为 RED 或通过。未访问云模型；完整 RAG 仍 Planned，容量仍 Capacity-not-verified。
- Native 可达范围：共享静态派发/激活投影与现有宿主闭包；三个新增 PDF 私有服务只互相引用，未注册或接入宿主。四项投影源码与 Linux 验收克隆逐项 SHA 一致。官方 Worker 原生发布 88656200 字节，13m 20s，15 项允许警告通过既有门禁；API 发布的 17 项允许警告同上。不将私有 PDF 派发的 JIT 证明外推为已公开的 Native 能力。
- 原生运行最终精确集合 27 项：API 10/10（27m 37s）、Worker SQL Server 8/8（13m 38s），另 Worker MySQL/Kafka 9/9 来自原中止运行的完整结果记录。为避免重复 SQL Server 运行，中止首次 Worker 全集；控制台为 Aborted、10 成功/0 失败/0 跳过，没有最终 TRX，保留原状态。按已安装 TRX 扩展 2.3.2 对应官方提交 `c3fdb81b8a5584664d82f4bcec6577f6d918b5fe` 的长度前缀序列化格式，严格校验原二进制日志完整帧、Passed 状态及 UID/FQN，取其中 9 项 MySQL/Kafka，SQL Server 使用独立完整 TRX。汇总精确覆盖原 27 UID，所有已选结果均通过，原生二进制 SHA 与正式发布一致；不生成或伪造中止运行的全绿 TRX。
- `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-planning-r05e8b2b --phase slice` 最终退出 0，正式双库 AI 82/82（34m 08s 447ms），零失败/跳过。两项成功用例的超长诊断文本按 TRX 字段上限截断，用例身份与结果完整。`verify.mjs` 核对 1122 项最终输入未变、AI Unit 1673 与 Integration 82 的精确 UID/FQN；原始发现、控制台、TRX、SHA 与中止记录均保留。
- 命名 33/33、SQL 安全 5/5；正式影响集工具链 65/65、治理 57/57，Integration 分片 1194 项无遗漏/重复。Windows x64、i7-12700H、约 64 GB、SDK 10.0.401；Linux SDK 10.0.400、Docker 29.6.2、SQL Server 2022-CU14 / MySQL 8.0。测试进程 2 个处理器、GC 堆 2 GiB，容器不复用；原生测试 SDK 容器各限 2 CPU/8 GiB。
- 收尾：整批 main 提交。后续集中推进 PDF 完整代次执行与激活，再接入公开 PDF 入队及 Worker；本批不宣告公开 PDF ready 或完整 RAG Verified，容量保持 Capacity-not-verified。

### 2026-10-07：PDF 完整代次执行、原子激活与完成恢复（本地批量验收完成）

- 用户继续要求批量推进；main 基线 `330c05538d88689377515985e09118db86b6ffaf`，任务快照 `ai-knowledge-pdf-generation-r05e8e`。同批交付完整代次执行、原子激活、原完成租约恢复及不合作权威端口的取消边界，不为每项重复全量验收。
- [x] 完整消费者从原冻结会话续租，冻结数据库完整 PDF 清单，按序执行所有批次并复核清单；总派发期限 15 分钟、单阶段 25 秒。完整已存批次直接恢复，缺失操作沿既有预留、派发、结算及持久化路径处理，不免费重发。
- [x] 新私有 PDF Activation Store 在 AI 自有短事务内重查权威来源、原会话、实际清单及每批实际向量 SHA/编码、完整模型目的地与请求指纹、成功已结算账本和原始批次数；逐批读取而不累计整代次向量。任务完成与活动指针一起提交，第二次写入失败、到期或取消须回滚任务完成态。
- [x] 完成任务仅允许原指针租约恢复；重新验证五项精确权限、当前来源/分类/模型地址/组织、实际载荷与已结算账本，不重新调用模型或收费。恢复证据不代替公开 ready 或后续读取授权。
- [x] 只读审查发现权限 Port 忽略取消时直接等待没有上限，已补独立 IO 令牌、同步阻塞隔离、工作等待与最多 5 秒迟到清理；未确认清理即拒绝，迟到异常被观察。仅包裹权威读取及原会话续租，不放弃计费操作、向量写入或激活写事务；复审无剩余 P1/P2。
- 行为 RED：初始组合 39 项中 21 项按缺失能力失败、18 项通过；不合作异步授权 3 项复现超出取消边界。同步阻塞的旧实现导致两次自有运行中止，保留中止状态，不计为通过。编译错误不计行为 RED。修复后组合 39/39、聚焦 170/170；取消回归允许已确认取消和未确认清理两种受控拒绝，超过外部 8 秒边界仍失败。
- Host/Tenant 的受控 17 页、两批执行组合实际调用规划、预算、付费消费者、持久化与激活；验证两次预留/成功结算、完成重放零新收费及第二批撤权/取消/非法向量时保留已付首批而不激活。解析进程输出和供应商受控，不表示真实云模型调用。
- 最终默认 Unit 构建零警告/错误（1m 49s）；`node artifacts/r05e8e/run-ai-local.mjs` 使用官方完整 AI 选择、4 个 Worker，1723/1723、零失败/跳过（54s 373ms）。首次 AI 回归 1720 通过/3 失败保留原日志；收敛构建竞争并修正取消测试对受控异常类型的过窄断言后重跑完整集合。实际发现 Unit 5192、AI 1723，矩阵仅同步两个数量。
- AI 架构 `ai-agentic-boundaries` 7/7、Native AOT 架构 `api-native-aot` 73/73；命名 33/33、SQL 安全 5/5、治理 57/57、工具链独立检查 54/54。Integration 分片 1194 项无遗漏/重复；正式影响集前置工具链 65/65、治理 57/57 通过。
- [x] `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-generation-r05e8e --phase slice` 保持 SQL Server/MySQL 完整 82 项选择。首次误用可复用容器，遇到共享模板数据库冲突及 SQL Server 容器消失，停止本任务运行，未计通过；改为 `FULLNET_TESTCONTAINERS_REUSE=0` 独立容器重跑，最终退出 0，82/82、零失败/跳过（36m 40s 971ms）。真实双库追加原子回滚、完整消费者恢复、完成原租约恢复、撤权及实际载荷/同版本模型/结算证据漂移拒绝，费用账本不变。
- `node artifacts/r05e8e/verify-acceptance.mjs` 核对 Integration 全部 82 UID/FQN 和结果、AI 最终 1723 项通过摘要、Unit 实际发现数量，以及 4255 项最终源码/矩阵冻结输入；正式、Unit、Integration 三处 AI DLL SHA 一致。原始失败/中止与最终成功日志、发现记录、TRX、验收清单保留于忽略目录 `artifacts/r05e8e/`。没有改动现有文本激活 SQL、行物化器、DI 或 Worker/Host 可达路径，复用上批未变闭包的原生发布/运行证据，本批未重新发布或运行 Native 宿主，不能外推私有 PDF 服务为已公开 Native 能力。
- Windows x64、i7-12700H、约 64 GB、.NET SDK 10.0.401；双库测试 2 个处理器、GC 堆 2 GiB、容器不复用。后续集中接入 PDF 公开入队、Worker 分流及状态读取；完整 RAG 仍 Planned，容量仍 Capacity-not-verified。
- 收尾：交付记录更新后再运行治理检查，最终差异检查通过，在 main 合并提交本批功能。

### 2026-10-07：R05f PDF 可信运行时、公开索引和状态读取合批

- main 基线 `570d0a9a04f22b6a93a3c34e7546e79c23644dc5`，任务快照 `ai-knowledge-pdf-runtime-r05f`。按用户要求同批实现以下四项及摘要兼容修正，统一验收后提交；不改迁移、公开 DTO 或冻结的 Layui。
- [x] API/Worker 共用 `FullNet:Ai:KnowledgePdf` 显式启用策略，默认关闭。启用时须配置 `NodeExecutablePath`、`CliScriptPath` 的可信绝对路径及固定 `sha256:` 镜像标识；Worker 另校验本机文件存在。解析器仅注册到 Worker，API 只注册读取证明。
- [x] PDF 公开入队与人工重试沿用原会话、原来源和精确权限边界；Worker 静态双库扫描受启用策略控制，按当前内容类型调用各自的 scoped 消费者。扫描记录不授予模型派发资格。
- [x] PDF 就绪状态复核当前读取者的权限、角色/组织关系、可见资源及实际完整代次；不依赖历史写入会话仍在线，不调用模型或修改费用账本。
- [x] 修正上传大写 SHA-256 与规范 PDF 计划小写摘要的比较，保持权威来源快照原表示。回归先复现两项失败，再覆盖整代次与双库清单、向量、激活和读取。
- 原生 HTTP 首次人工重试实际返回 409，定位为 Retry 条件 UPDATE 遗留文本格式谓词；没有放宽冲突断言。已补可信整数 `PdfEnabled` 和 PDF 条件，Unit 命令断言及双库实际 RetryService 写入回归防止再次只验证模拟返回值。首轮原生运行在失败后主动中止并保留原始状态，不计通过；原生 API/Worker 发布、AI Unit 和正式 merge 双库影响集均针对修正后输入重新验证。只读复审未发现本次修正新增 P1/P2。
- [x] Unit 实际发现 5218、AI 1749；SQL 修正后完整 AI 1749/1749、零失败/跳过（1m 03s 337ms）。首次资源竞争产生一项 OOM，不计通过，保留失败记录。修正后 API AOT 分析零警告/错误（3m 44s）；Windows Integration、Unit、Architecture 构建均零警告/错误，分别 12m 18s、1m 57s、41s；Linux Integration 重建零警告/错误（14m 46s）。AI 架构 7/7、Native AOT 架构 73/73。
- [x] `node scripts/testing/run-affected-integration.mjs --snapshot ai-knowledge-pdf-runtime-r05f --phase merge` 正式选择 90 项（AI 双库 82、宿主冒烟 8）。SQL 修正前单轮 90/90 不能替代修正后的回归；修正后 Docker EOF 中止运行保留原始日志，不计通过。恢复后的正式运行在 45m 23s 044ms 达到时限，中止、78 成功/0 失败/0 跳过，没有最终 TRX。按当前 TRX 扩展 2.3.2 对应官方提交 `c3fdb81b8a5584664d82f4bcec6577f6d918b5fe` 的长度前缀格式，严格核对原始二进制日志的 78 个完整 Passed 帧及 UID/FQN。沿开发质量 §11 第 5 项复用未变输入的有效结果，`node artifacts/r05f/run-integration-remaining.mjs` 精确发现并补跑剩余 MCP/工具 12 项，退出 0、12/12、零失败/跳过（7m 08s 739ms）。`verify-acceptance.mjs --local` 核对精确并集 90、4263 项冻结输入及相同 AI DLL；保留原中止状态与补跑 TRX，不生成伪造的单轮全绿报告。前置工具链 65/65、治理 57/57，Integration 分片 1194 项无遗漏/重复；PDF 场景直接覆盖实际重试、启用扫描、完整付费代次恢复、当前读取者授权和历史写入撤权，费用不增加。
- [x] Linux 原生门禁验收。修正后官方 API 发布退出 0，138002096 字节、21m 04s 823ms，17 项允许警告；Worker 发布退出 0，89142344 字节、11m 28s 744ms，15 项允许警告，均通过既有官方警告门禁。API Native HTTP 10/10、零失败/跳过（37m 32s 398ms），直接验证 PDF 入队、人工重试和 ready。Worker 首轮达到时限后中止，13 成功/0 失败/0 跳过（46m 08s 932ms），没有最终 TRX；原始完整帧覆盖 MySQL/Kafka、新增 PDF 两库及两项 SQL Server。按相同官方 TRX 序列化格式严格校验，`node artifacts/r05f/run-native-worker-remaining.mjs` 精确发现并补跑剩余 SQL Server 6 项，退出 0、6/6、零失败/跳过（8m 27s 820ms）。原生 Worker 使用 JIT HTTP 准备真实会话并验证 Native 扫描与已结算代次恢复；API 与 Worker 各自独立准备产物。最终精确并集 API 10、Worker 19、核心 Native 29，新增 PDF 类型与 Kafka 均满足必需发现门禁；API 下限保持 10、Worker 从 14 增至 16。保存真实补跑 TRX 和原中止日志，不制造单轮全绿报告，不将 Windows 的 29 项跳过计为通过。
- Docker 掉线中止首次原生发布和 Linux 构建，不计通过；恢复已有服务后，2 GiB GC 堆的发布在生成 ELF 对象时 OOM，后续增量链接因遗留零字节对象缺少 exports 再次失败。修正 SQL 后的发布又因引擎 `unexpected EOF` 中止，保留非零退出码；Docker CLI 重启命令本身超时退出 1，后续独立读取确认引擎实际恢复、版本 29.6.2，不把 CLI 命令报告为成功。清理专属缓存中未受本轮验收的 API 对象和 exports，沿官方发布命令重新生成；任务适配器限定 API 2 核、Worker 4 核，各 10 GiB 容器、4 GiB GC 堆，未改变官方发布属性或警告门禁。4263 项源码/矩阵冻结输入与原始成功、失败及中止日志保留在忽略目录 `artifacts/r05f/`。
- `node artifacts/r05f/verify-acceptance.mjs` 退出 0，核对 4263 项最终源码/矩阵输入未变，AI Unit 1749、双库 90 与 Native 29 的精确 UID/FQN、正式与测试 AI DLL 一致，以及 API/Worker 实际执行摘要和本轮正式发布一致。原始发现、成功/失败/中止记录、序列化格式依据、TRX、日志与 `acceptance.json` 保留在忽略目录 `artifacts/r05f/`；操作说明已同步 PDF 配置、启用边界和运行范围。
- Windows x64、i7-12700H、约 64 GB、SDK 10.0.401；Linux SDK 10.0.400、Docker 29.6.2、SQL Server 2022-CU14 / MySQL 8.0。测试进程 2 个处理器、GC 堆 2 GiB，容器不复用；原生 SDK 容器初轮 2 CPU/8 GiB，Worker 剩余 6 项补跑使用 4 CPU/8 GiB，进程与 GC 约束不变。原生 PDF 用例使用合成已结算数据证明恢复闭包，不冒充 Native Worker 的真实文件解析或云模型整代次派发；完整 RAG 仍 Planned，容量仍 Capacity-not-verified。治理和最终差异检查后在 main 提交整批，后续进入 R06 权限检索、问答与可信引用。

### 2026-10-08：R08g DOCX 执行、授权规划与已结算单批消费（本地批量验收完成）

- main 基线 `abd92695143a67f1911b19403f5c6d01295475a0`，快照 `ai-docx-execution-planning-r08g`。合批交付容器隔离控制器、CLI 帧协议、.NET 桥接、原会话规划、预算派发与单批持久化恢复。无新表、迁移、Endpoint、DI 或 Worker 注册。
- 数据归属仅 AI；Files/Identity 通过最小 Contract，授权不进入事务。来源读取复用独占原字节边界，DOCX 入口限 5 MiB。逻辑段落/表格坐标保留，不产生页码。
- 文件：`scripts/ai/docx-sandbox{,-cli,-publish}.mjs`、`AiKnowledgeDocxSandboxParser.cs`、`AiKnowledgeIndexDocx{Planning,Embedding,Persistence}Service.cs`；测试为对应 Unit 场景和 `tests/ai-evaluation/docx-sandbox/`。
- 接口：`ParseAsync(ReadOnlyMemory<byte>, string, int, CancellationToken)`；`PlanAsync(binding, taskId, leaseToken, token)`、`ReadCurrentModelAsync(..., prepared, token)`；`ExecuteAsync(..., ordinal, token)`；`ExecuteBatchAsync(..., ordinal, token)`。
- [x] 有效 RED：合法 Host/Tenant 规划、付费单批、持久化及真实子进程桥 7/7 失败于缺失能力，Node 协议 3/3 失败于占位结果。完整计划身份回归 4 项中来源大小与模型 ID 两项失败、版本两项已拒绝；恢复终末授权窗口的同版本模型地址/组织/分类漂移 3/3 失败；写入后不合作授权端口 2/2 超出取消清理边界。初始 Node 广泛运行因占位结果中止，编译错误及中止运行均不计通过。
- [x] 不可变镜像、无网络/挂载/凭据、512 MiB/零 swap/2 CPU/32 PID、资源归属清理；完整退出/零 stderr/停止并删除才交付。原字节校验、原会话、五项权限、租约及完整目的地在每次等待后复核，逻辑段落和表格保留坐标，不生成页码。
- [x] 清单先于预算、新预留才派发、原范围独立结算、未知用量不推断零、当前单批直接恢复、向量丢失不得免费重发。修复完整计划大小/模型 ID 核对及 Store 的同版本终末目的地漂移；事务外权威读取使用有界取消，事务写入直接等待，不放弃写入后后台提交。首轮组合 314 通过/3 失败属于恢复夹具复用已释放来源流，改用独占新流和已存在预留后重新验证，没有弱化拒绝断言。
- [x] 最终 Release Unit 构建零警告/错误；DOCX 聚焦 TRX 317/317、零跳过（34s 250ms），其中新增两类 Unit 155 项。`node --test --test-timeout=30000 tests/ai-evaluation/docx-sandbox/*.test.mjs` 37/37；命名 33/33、SQL 安全 5/5、治理 57/57。Unit 实际发现 5930、AI 2461，唯一矩阵仅同步两个数量，未降低任何门槛。
- [x] `pnpm test:dotnet:unit -- --no-build` 实际运行 5930，5929 成功/0 失败/1 Windows 下 Linux FIFO 跳过（4m 22s 428ms）；测试平台退出 9，包装命令退出 1，保留非零退出码，不将该命令报告为全绿。FIFO 用例及 BuildingBlocks、构建依赖相对本批基线未变，复用 `artifacts/r08b/linux-fifo/linux-fifo.trx` 的精确 UID 通过证据，跳过本身不计通过。
- [x] 全量 Architecture 235 项首次 232 成功/3 失败/0 跳过（7m 37s 989ms）。新增 DOCX 四个私有类漏登记到精确源码白名单，补登记且保持目录、数据库执行器和宿主注册守卫，Release 重建零警告/错误，原失败项新鲜 TRX 1/1；其余结果输入未变。精确并集合计 233 成功/2 既有失败：PDF 异步边界 11 处与 TXT/PDF 异步命名 6 处，逐行与 R08f 比对一致，不计全量 Architecture 通过。只读复审关闭恢复目的地、不合作读取和 Native runner 启动断言缺口，补登记复审无剩余 Critical/Important/Minor。
- [x] `node tests/ai-evaluation/docx-sandbox/run-native.mjs <parser-id> <probe-id> <results-dir>` 修正版最终 10/10，实际 Native 工具解析完整 OOXML、真实隔离/OOM/输出限额/总体期限、观察 `/app/parser` 启动后取消、同关联 ID 独立资源、CLI EOF/framed；没有单独验证控制器失联下的 watchdog。生产解析工具镜像 `sha256:baed15a1a0eb1f902135ac3eb0fc52f9c84c7d4d77034f22888e5982637eef57`、独立探针 `sha256:880173831c55d94b700f56655faa1be783762647c64ee8196a449a0ac2c8052e`，工具源码和产物未变。
- [x] 实际 Native DOCX → .NET 桥 → 受控 Host/Tenant Files → 原会话 → 完整计划两项通过：核对原始 SHA/大小、7 个逻辑块、6 个非空模型块、真实词表及清单编码往返；撤权复核拒绝、不重新打开正文。正式 Unit/Integration/Architecture 与探针四处 AI DLL SHA 一致。仅 Files/Identity/Query 端口受控，不外推真实 HTTP、云模型付费调用或 Native Worker；本批未重新发布 API/Worker。
- [x] 正式 `pnpm test:integration:affected -- --snapshot ai-docx-execution-planning-r08g --phase merge` 退出 0，96/96、零失败/跳过（43m 05s 299ms）；前置工具链 65/65、治理 57/57，Integration 分片 1208 项无遗漏/重复。Ai+smoke 精确发现并执行同一 96 个 UID/FQN，每个目标保留 SQL Server/MySQL。TRX 两项较长日志字段按报告器上限截断，测试标识和结果完整，不将它报告为原始日志未截断。
- [x] `python artifacts/r08g/verify-acceptance.py` 退出 0，核对 6386 项最终源码/矩阵/JIT 输入、317 项聚焦和 96 项正式双库的精确 UID、Native/桥接/Node 证据及四处相同 AI DLL。补登记的 Architecture 测试及其程序集重新冻结；暂存差异检查另移除新 Unit 文件末尾一个空行，保留修改前原文件并精确核对此前字节全部相同，按纯机械结构检查复用行为结果。其他验证输入未变；保留 Windows Unit 非零退出、Architecture 原失败和修复后单项 TRX，不生成伪造的单轮全绿记录。验收摘要为 `artifacts/r08g/acceptance.json`。
- 环境：Windows x64、i7-12700H、约 64 GB、SDK 10.0.401、Docker 29.6.2；测试进程 2 个处理器、GC 堆 2 GiB，正式双库 `FULLNET_TESTCONTAINERS_REUSE=0`。原始失败、中止、最终日志、TRX、发现与冻结记录位于忽略目录 `artifacts/r08g/`。
- 收尾：更新交付记录、治理与最终暂存差异检查后在 main 提交整批。完整代次激活、DOCX 上传/Worker/检索引用及恢复成本测量仍待后续；控制器失联下 watchdog 的独立验证也尚未完成。完整 RAG Planned，Capacity-not-verified。

### 2026-10-08：R08h DOCX 完整代次、原子激活与完成恢复合批

- main 基线 `a1d9dd40ca9896dd54868ff3db4ec1c385090d68`；继续原任务快照 `ai-docx-execution-planning-r08g`，正式影响集另以本批基线计算。两个无关未跟踪文件保持不读、不改、不暂存。
- [x] 完整清单驱动多批次执行，逐阶段原会话续租；付费操作独立结算，未知操作禁止免费重派。
- [x] AI 本地事务共同提交任务完成与活动指针；原字节载荷、成功结算回执、当前模型目的地及来源在锁内复核，跨模块授权保持事务外。
- [x] 已完成原租约重放只验证实际完整证明和当前授权，不打开来源、不调用模型、不增加费用。
- [x] 同一次证明内复用不可变完整计划；每次等待后仍读取授权和源行，实际原字节 SHA 匹配后才复用。先后测量同一受控查询夹具，明确数据、环境与局限。
- [x] 统一验证 Unit、Architecture、双库正式影响集，完成只读审查；原整轮失败、精确补跑和既有架构失败分别保留。DOCX 上传、Worker 注册、读取就绪及检索引用继续关闭。
- 有效 RED：28 项占位能力测试中 21 项因缺失能力失败、7 项通过；编译错误不计行为 RED。终末授权等待窗口的 7 项测试中 6 项先失败；SHA 漂移夹具最初没有改变原摘要，改用不同摘要后纳入最终 GREEN，不冒充原失败证据。
- 原会话五项权限、来源分类/大小/摘要、模型地址/组织和原指针绑定在终末授权等待后再次以短 AI 本地事务复核。实际载荷及结算证明对应之前的完整锁事务，不能外推 Identity 与 AI 的联合原子快照。只读审查提出的终末等待漂移及续租缺少直接回归已修正，复审无剩余 Critical/Important/Minor。
- Release Unit、Integration、Architecture 及原生消费者探针构建均零警告/错误。`pnpm test:dotnet:unit -- --no-build --selection ai-module` 2508/2508、零失败/跳过（2m 53s 057ms），包含本批新增 47 项；实际发现 Unit 5977/AI 2508，矩阵仅同步这两个数量。本批未运行全量 5977 Unit，不将发现数计为通过。
- `pnpm test:dotnet:architecture -- --no-build` 实际 235 项，233 通过、2 个既有失败、零跳过（6m 08s 587ms）；平台退出 2、包装命令退出 1。11 处 PDF 异步边界及 6 处 TXT/PDF 异步命名逐行与 R08g 一致，保留非零状态，不报告 Architecture 全绿。命名 33/33、SQL 安全 5/5。
- 真实 Native DOCX 工具输出接入当前 JIT 私有消费者，Host/Tenant 2/2 通过：31 个逻辑正文块、31 个模型块、两批付费执行、原子完成及原租约重放/拒绝。每批新鲜规划共打开来源两次；完成后的重放/拒绝不再打开来源、不调用模型、不收费。Files、Identity、模型、预算和 SQL 端口受控；真实双库单独验收，不外推真实云模型或 Native Worker。解析工具及隔离脚本未变，复用 R08g 不可变镜像，不重新发布 API/Worker。
- Windows x64、i7-12700H、约 64 GB、SDK 10.0.401；进程 2 个处理器、GC 堆 2 GiB。原基线 AI DLL 与最终 AI DLL 在同一受控查询夹具分别预热 3 次、采样 20 次：2048 短正文块/128 批完整证明 p50 2050→230 ms、p95 2605→296 ms、p99 2792→300 ms，零失败；每样本授权 3250 次、向量读取 256 次、回执读取 2 次均不变。未覆盖真实数据库/网络、最大正文字符数、最大 Token 或全部空白块布局，容量仍 Capacity-not-verified。
- 6393 项源码/矩阵/JIT 输入已冻结并核对；生产、三类测试和完整消费者探针五处 AI DLL SHA-256 均为 `8c4762aeb40eaf38cf84c9e27c7d7b6240395bab3505c971e8902cf3ec81cfd5`。失败夹具、修正后结果、原生和性能证据位于忽略目录 `artifacts/r08h/`。
- 后续合批范围已沿调用链确认：可信运行时配置及默认关闭、上传 MIME/5 MiB 边界、双库扫描与人工重试、Worker 派发、当前读取者独立就绪证明、逻辑段落/表格检索和问答历史来源证明、活动代次删除与退役。原索引会话恢复不能替代当前读取者权限；DOCX 不生成页码。接入批次须统一验证真实上传/会话/扫描/重试/公开状态/清理及 AOT 可达闭包，当前仅为待实施范围。
- 正式 `pnpm test:integration:affected -- --base a1d9dd40ca9896dd54868ff3db4ec1c385090d68 --phase merge` 完整运行 96 项，95 成功/1 失败/零跳过（41m 45s 778ms）；平台退出 2、包装命令退出 1。本批 DOCX 双库场景通过；旧 MySQL TXT 激活场景在原 25 秒期限内取消，会话读取、TXT 执行器、相关 SQL/断言均与基线逐字不变，不能据此认定资源压力是唯一原因。原始失败及 TRX 保留。
- 中途收到集中验收规则更新，保留其他任务新增的 CI、规则、客户端和测试工具改动。恢复 Docker 29.6.2 后，`node artifacts/r08h/run-integration-retry.mjs` 通过新同机重型锁及独立 TEMP/TMP，只补跑精确 MySQL TXT UID：退出 0，1/1、零跳过（2m 21s 568ms），未重建程序集、未改期限或断言。第一次补跑因 Docker 不可用而在业务执行前失败，原日志/TRX/退出码保留。最终精确通过并集 96；不生成伪造的原整轮全绿报告，不复跑未变的其余 95 项。
- 原正式前置工具链 65/65、治理 57/57、Integration 分片 1208 项无遗漏/重复；集中验收规则更新后的当前治理 59/59。6393 项冻结输入中仅外部测试工具 5 项在原运行之后改变，6388 项原输入及五处 AI DLL 不变；`input-transition.json` 单独登记原/当前摘要，不用旧工具回归证明新工具已通过。11 项受控探针/原始 DOCX 输入另行核对。`python artifacts/r08h/verify-acceptance.py` 退出 0，精确核对原 95 与补跑 1 的 UID/FQN、当前 AI 产物、原始失败与性能范围，摘要为 `artifacts/r08h/acceptance.json`。
- 收尾在 main 仅提交本批 19 项 AI 文件；两个受保护文件保持不读、不改、不暂存，其他任务的未提交改动不修改、不暂存。按新规则，后续同模块关联建设先集中完成与聚焦回归，独立生成应用在约定范围关闭后统一验收，本批未重复启动独立生成应用。完整 DOCX 运行链路仍待接入，完整 RAG 与容量状态不由私有消费者结果升级。

### 2026-10-08：R08i DOCX 运行链路集中接入（本地批量验收完成，既有缺口保留）

- 基线 main `56b7f7d1d8e26863a152192e6a859b851be7a43f`；沿用任务快照，仅修改本批文件。
- [x] 可信默认关闭配置、API/Worker 静态注册、匹配 MIME 与声明/实际 5 MiB 上传边界。
- [x] 双库扫描、入队、人工重试和对账；Worker 按可信来源派发 DOCX 完整代次。
- [x] 当前读取者独立证明、完整清单绑定向量召回、段落/表格引用及历史兼容；不生成 DOCX 页码。
- [x] 活动删除和退休代次锁内清理、双库安全回归、AOT 可达闭包及本批集中验收；既有 Architecture 失败和在线审计未验证状态单独保留。
- 数据由 AI 独占；Files/Identity 继续最小 Contract，外部授权不进入本地事务。AI 活动指针与完成态保持原子，失败不得释放正文或重发已收费操作；索引不新增跨模块写入、Outbox 或缓存。
- 先实现关联能力与聚焦回归，独立生成应用在本批范围完成后一次验收；未执行与既有失败分别保留，不提升完整 RAG 或容量状态。
- 实现复审已关闭畸形历史引用导致异常、DOCX 小写计划摘要被旧历史格式拒绝、取消后证明事务可能后台提交等问题；有效失败回归和修复后结果位于忽略目录 `artifacts/r08i/`。lineage v2 按完整来源依赖选用，拒答和未展示的来源仍参与历史证明；v1 字段和摘要约束保持兼容。
- 本批新鲜 Release AI Unit 2551/2551、零失败/跳过；Unit 实际发现 6020，仅同步矩阵实际数量。Architecture 235 项中 233 通过、2 项既有失败，11 处 PDF 异步边界和 6 处 TXT/PDF 异步命名与前批一致，保留非零退出码。
- SQL Server/MySQL 实际导出 OpenAPI 一致，共享客户端由生成器更新；新增坐标可省略/为 null，上传 MIME 纳入 DOCX。生成后客户端 239/239、Vue 相关 43/43、Vue 类型检查/生产构建及 bundle 预算通过；命名 33/33、SQL 安全 5/5、工具链 73/73、治理 59/59、Integration 分片 1208 项无遗漏/重复。
- API/Worker AOT analyzer 编译使用现有缓存依赖及一次性 `-p:NuGetAudit=false`，零编译警告/错误；原规定入口因 NuGet 在线审计连接失败未通过，在线依赖审计仍未完成，未修改项目审计设置或 IL 告警规则。
- 正式双库影响集实际发现并运行 98 个唯一测试，含 AI、运行时客户端快照与 smoke。首轮 96 成功、2 失败、零跳过（30m 59s 931ms），原平台退出 2；退休任务夹具错误地与当前后继使用相同创建时间，UUID 排序使其成为“最新任务”，清理正确拒绝。仅修正旧任务创建时间并追加后继当前/ready 断言，不放宽删除和拒绝断言；精确 SQL Server/MySQL 两项补跑 2/2、零跳过（3m 35s 938ms）。`python artifacts/r08i/verify-paired-acceptance.py` 核对原发现、UID/FQN、原 96 与补跑 2 的通过并集为 98，保留原失败 TRX；原报告器截断两项超长文本字段，标识和结果完整。
- 测试修正后的重建改变了 SDK 的 Git 信息版本，AI 原始 DLL SHA 因此变化；代码、方法 IL、类型/引用/成员、字符串堆、其他属性和资源的 51400 项比较一致，Contracts 6414 项一致，仅信息版本及确定性 MVID/PDB 不同。未变生产源码与有效原结果复用，不宣称当前四处 DLL 字节摘要相同。
- Linux 原生 API 发布退出 0，139367712 字节、17m 53s 925ms，17 项已有允许警告通过官方门禁。Linux Integration 构建零警告/错误（10m 46s）；原生 API 聚焦 4/4、零失败/跳过（9m 35s 533ms），核对发现 UID 与运行 TRX，并核对实际运行可执行文件 SHA-256 `13aaef93297b8d30b7510dcfd4e0fb3b2250da21a9772add1e52e49eb4d293c8`。覆盖 SQL Server/MySQL 关键真实 HTTP 的 DOCX 入队、人工重试、当前读取、对账和历史，以及 AI 工具/MCP 元数据；DOCX 上传由前述正式双库 HTTP 回归验证。受控已付恢复由 JIT Coordinator 执行，不报告完整 Native 核心集合或 Native Worker DOCX/云模型整代次已验证。
- 独立生成应用首轮基于 main `85fc4737` 的干净源码克隆，SQL Server/MySQL Minimal 均在创建阶段失败：模板只登记早期 Ai 迁移，而预设模块闭包不包含 Ai，不能识别 `240_AiKnowledgeBase.sql`。新增双库全部 Ai 迁移归属及各预设排除回归先失败；仅补 Ai 前缀归属、保留既有预设过滤，相关 8/8 通过并提交 `f13f0b72`。
- 生成应用第二轮继续发现两项真实进程中文诊断乱码，未放宽精确冲突消息、非零退出及不覆盖文件断言。独立字节实验复现 CLI 按 Windows 系统代码页输出，UTF-8 解码失败而 GB18030 解码准确；仅在 CLI 入口固定无 BOM UTF-8，零警告/错误构建后同一真实进程回归通过，提交 `86e2ece2`。AI/API/Worker 实现与配置未变，原两轮失败记录保留。
- 2026-10-09 收尾：干净独立源码克隆同步 `86e2ece2` 后，`node artifacts/r08i/run-created-app-encoding.mjs` 退出 0，SQL Server/MySQL Minimal 2/2、零失败/跳过（5m 22s 956ms）。实际生成共享客户端、CRUD、模块与宿主接入，执行迁移/重放、登录、细粒度权限及租户隔离，完成资源清理，克隆保持干净。一次性进程审计覆盖仅用于缓存依赖构建，不修改模板断言或项目审计策略；在线审计仍未验证。
- `python artifacts/r08i/verify-final-acceptance.py` 核对 AI Unit、Architecture 原失败、正式双库精确通过并集、客户端/Vue/OpenAPI、原生发现/运行与可执行文件 SHA、独立生成应用及源码候选；摘要为忽略目录 `artifacts/r08i/acceptance.json`。当前文档更新与退休夹具输入变化单独登记，未把原失败改写为单轮全绿。main 已提交本批实现及必要模板修复；最终仅提交交付记录，保留其他任务未提交改动和受保护文件。
- 环境：Windows x64、i7-12700H、约 64 GB、SDK 10.0.401；Linux Docker SDK 10.0.400/运行时 10.0.11，Docker 29.6.2。双库容器不复用；重型运行使用同机共享锁及独立 TEMP/TMP。原生发布 4 CPU/12 GiB，原生测试 2 CPU/8 GiB，不以执行耗时或聚焦结果升级容量。完整 RAG、真实云模型整代次、Native Worker DOCX 及最大正文真实数据库恢复成本保持未验证，容量保持 `Capacity-not-verified`。

#### R11g：知识问答固定阶段观测（2026-10-13，本批受影响本地验收完成）

基线 main `47e9f3454929c1853dbcf2d0696425af76ee2bd8`，任务快照 `ai-knowledge-telemetry-r11g`。本批合并交付知识回答、查询 Embedding、检索、缓冲生成、最终答案校验与交付资格复核六阶段；复用 `Full.NET.Ai.Execution`、现有宿主订阅和业务 OperationId，不新增依赖、SQL、迁移、HTTP DTO、权限、RunId 或持久 StepId。

- [x] 阶段和结果采用固定集合，指标仅 stage/outcome；Trace 仅在收费阶段和交付复核关联已有操作标识，不读取正文、分类、身份、凭据或异常消息。严格结构化拒答标记 refused，预算拒绝、撤权、取消及其他故障保持独立分类；不将尝试次数当成 Provider HTTP 调用量。
- [x] 不改变原来源检查、预留、派发、独立结算和最终来源复核顺序。completed 只说明原方法返回，knowledge_delivery 成功不证明客户端已收到响应。生成预检拒绝时仅记录实际进入的生成阶段，不虚构尚未执行的最终校验。边界说明见 [Provider 网络与阶段观测](../../operations/ai-provider-network.md)。
- [x] 新增 25 项真实收费路径回归，宿主/租户覆盖成功、结构化拒答、预算拒绝、预留后撤权、取消、模型故障、最终交付撤权与监听器故障。有效 RED25 中20失败/5通过；`node artifacts/r11g/run-isolated-unit.mjs green` 25/25，`node artifacts/r11g/run-isolated-unit.mjs final` 3875/3875，均零失败/跳过。独立只读复核无剩余 Critical/Important，不代替运行证据。
- [x] `node artifacts/r11g/run-pipeline.mjs` 完成 API/Worker AOT 分析零警告/错误、AOT 架构88/88、全量架构235/235及 JIT Integration 构建。真实分类选择 Ai＋Smoke，按提供程序顺序、单 Worker 和原矩阵60分钟期限执行，SQL Server/MySQL各48/48、合计96精确唯一UID通过。分片发现1214无遗漏/重复；不报告全部 Integration 已执行。
- [x] 命名30、矩阵6、治理57、工具链54和保护排除后的Markdown检查通过。完整Unit仅发现7358，与本批规范矩阵一致；仅执行完整AI3875，不宣称全Unit通过。

首轮共享工作区 Unit 构建12错误，来自其他窗口缓存测试与共享依赖程序集不同步；该结果不是行为RED，未改外来代码。改用 HEAD 加本批白名单的源码快照并本地缓存 restore 后构建通过；本批所有构建均零警告/错误。原错误记录保留。Windows 验证采用隔离输出、工作区互斥与独立 TEMP/TMP，后续回归复用同一构建。

完整AI运行前后，全部冻结源码和原1002个消费文件逐字节一致，AI及Agents生产/消费程序集相同。初次完整目录比较因既有RAG测试新增11个JSON夹具/报告失败；确认无旧文件改变后，仅将已知新增JSON输出形状单独列为测试产物，旧消费者仍全部验哈希，不重跑未改变的输入。原失败与输出清单保留。执行后只补观测边界说明及本段证据，不把文档最终字节视作运行前冻结字节。

冻结7818个输入，Linux AOT、JIT及双库沿用各自已有缓存；覆盖前核对R11f源码及完整消费者摘要，只传入当前差异，不新建缓存或反复切换依赖图。保留机器重型锁的排队事实，不中断其他窗口验收。Windows x64/SDK10.0.401、Linux SDK10.0.400/runtime10.0.11，i7-12700H/约64GiB；Unit4处理器/2GiB GC，构建8CPU/4GiB，双库各2CPU/8GiB。本地缓存 restore 使用 NuGetAudit=false，未执行在线依赖审计。

当前知识重排为本地余弦排序，向量读写为自有Dapper数据库，没有本批远程重排或向量网络消费者；不为未存在的调用链新增服务。持久StepId/跨重启Trace、Provider精确输入Token硬限、未知外部副作用对账与完整分类准入仍按原计划推进。Native发布/运行、生成应用、页面及运维恢复集中验收尚未升级，R11整体保持开放，Capacity-not-verified。

TRX报告器对过长捕获文本保留字段截断提示，状态、计数与精确UID另行核对完整；不声称截断区域文本完整。

原命令、退出码、TRX、发现UID、源码/消费者证明与审查位于 `artifacts/r11g/acceptance.json`。main仅提交本批8项文件，不推送；共享矩阵仅暂存HEAD加本批25项增量，其他窗口改动和两个保护路径保留。

#### R11h：已知官方 Embedding 输入 Token 准入（2026-10-13，本批受影响本地验收完成）

基线 main `71b0f3d7d23d9b446a4c1f4a9691e2a7d8048ccb`，任务快照 `ai-embedding-input-limit-r11h`。一批交付能力测试、知识问答查询和文本/PDF/DOCX 共享索引预算入口的输入门禁；不新增依赖、SQL、迁移、HTTP DTO 或权限。

- [x] 仅核准官方目的地、`openai_compatible` 及 `text-embedding-3-small` / `text-embedding-3-large` 使用固定离线 `cl100k_base` 精确计数。沿官方 Cookbook 采用单条8191 Token保守边界，严格 Unicode、取消及词表缺失/摘要漂移失败关闭。现有16条/32000字符约束同时限制计数成本；不裁剪、不重写、不改目的地或模型。依据与支持边界见 [AI预算说明](../../operations/ai-operation-budgets.md)。
- [x] 超限拒绝早于批次/操作身份创建、预算预留及模型派发，保持原授权、租户、来源复核与结算。保留 `AiEmbeddingBudgetRequest` 字节上界及历史请求指纹，避免旧索引回执和恢复失效；其他网关、模型和聊天输入未因此获得精确Token认证。
- [x] 新增30项专项回归，覆盖官方两模型、8190/8191/8192实际Token边界、无效Unicode、官方目的地允许集、未知目的地、取消、原预算/指纹及Host/租户准入顺序。修正夹具后，三原业务入口未接门禁的有效RED为10项中8失败/2通过；`node artifacts/r11h/run-isolated-unit.mjs green` 30/30，`node artifacts/r11h/run-isolated-unit.mjs final` 3905/3905，零失败/跳过。
- [x] 双库真实有效租约/来源上新增超限输入，核对批次行、预算行及Provider调用均未增加。`node artifacts/r11h/run-pipeline.mjs` 按真实分类仅选择Ai，夹具修复由 `node artifacts/r11h/run-fixture-repair.mjs` 完成，SQL Server原轮43通过/1夹具失败，修复后该失败UID单项重跑1/1通过；MySQL完整44/44通过，合并覆盖88唯一UID；提供程序顺序执行、单Worker、原矩阵60分钟期限。分片发现1214无遗漏/重复，不宣称全部Integration已执行。
- [x] API/Worker AOT分析零警告/错误，AOT架构88/88、全量架构235/235及JIT Integration构建通过。命名30、矩阵6、治理57、工具链54和保护排除后的Markdown检查通过。完整Unit仅发现7388，与规范矩阵一致；本批实际执行完整AI3905，未执行全部Unit。

首轮测试夹具误选只有2 Token的汉字，又遗漏索引记录版本，初始结果不作为有效超限RED；原日志/TRX保留。修正为固定词表实测选择3 Token字符、断言完整输入计数，并补齐文档/知识库版本，再对未接门禁的原业务入口建立失败证据。最初可空编译错误同样保留，修复夹具后构建通过。SQL原轮新增断言错误依赖Host DI注册，实际夹具手工构造该内部服务；改为同作用域AdmissionStore，保留原预算挂钩。修复仅改一项Integration夹具，生产字节未变；原SQL轮和补跑分别保留，不称原命令通过。首个补跑构建漏挂本地NuGet缓存而失败，补齐挂载后相同源码重新构建通过，原NETSDK1064日志保留。最终只读复核无剩余Critical/Important，复核不代跑测试。

冻结7820个输入；Windows完整AI前后冻结源码及原程序集/消费闭包摘要一致，AI/Agents生产与消费程序集匹配。既有RAG用例生成的已知JSON数据/报告单列运行产物并记录前后摘要，不豁免DLL或未知文件。Linux AOT、JIT及双库覆盖前验证R11g源码与完整消费摘要，仅传入当前差异。共享矩阵仅暂存HEAD加本批30项，其他窗口改动和两个保护路径保留。

Windows x64/SDK10.0.401、Linux SDK10.0.400/runtime10.0.11，i7-12700H/约64GiB；Unit4处理器/2GiB GC，构建8CPU/4GiB，双库各2CPU/8GiB。本地缓存restore使用NuGetAudit=false，未执行在线审计。TRX过长捕获文本若有截断提示按原记录保留，状态、计数与精确UID另行核对，不宣称截断区域完整。

本批不认证云Provider计量/硬费用、其他Provider/Chat输入或完整数据分类准入。持久StepId/跨重启Trace、未知外部副作用对账、集中Native发布/运行、生成应用、页面及运维恢复仍开放，R11整体与Capacity-not-verified保持原状态。原命令、退出码、TRX、发现UID及摘要证明见 `artifacts/r11h/acceptance.json`；在main提交本批10项文件，不推送。证据段落在测试结束后补齐，不把最终文档字节冒充运行前冻结字节。

#### R11i：真实步骤写入与持久 Trace 关联（2026-10-13，本批受影响本地验收完成）

基线 main `1d353deb127c351862c2cad6c6755be21b3c0110`，任务快照 `ai-agent-step-trace-r11i`。本批集中交付步骤写入观测、现有Trace列关联和不同Worker重新领取的双库验证；不新增依赖、SQL结构、迁移、HTTP DTO、授权入口或业务身份。

- [x] 共享 `AgentExecutionTelemetry.StartStep` 固定为 `step_write`，关联租约RunId、调用方StepId及OperationId；空标识不输出，不调用业务ID工厂。保留原 `Start` 签名，指标仍只有stage/outcome，不记录步骤键、入参摘要、正文、身份或异常消息。
- [x] Store在原租约CAS成功、确有步骤时才开启观测；原 `Step.TraceId` 保留，为空时才写实际创建的Trace标识，未采样/诊断故障保持空值。沿原步骤列、同一事务和fencing条件写入，不引入额外SQL往返、预算或模型调用。
- [x] 观察在 `ICommandTransaction.ExecuteAsync` 返回/抛出后结束；completed仅表示本地方法返回，外层嵌套事务仍可能回滚，指标是尝试而非持久提交或外部派发凭证。换Worker通过已存RunId/StepId/TraceId关联不同追踪，不把历史Trace当作新父上下文。说明见 [执行观测](../../operations/ai-provider-network.md#agent-执行观测)。
- [x] 新增24项回归，覆盖状态、原Trace、步骤/事件/提交故障、采样与监听器故障、空标识、旧租约、仅事件进度、取消和不同Worker作用域。有效RED24中14失败/10通过；`node artifacts/r11i/run-isolated-unit.mjs green` 24/24、`node artifacts/r11i/run-isolated-unit.mjs final` 完整AI3929/3929通过，零失败/跳过。Unit全部仅发现7412，与规范矩阵一致，不宣称全Unit已执行。
- [x] 原双库租约夹具新增真实两次Worker领取：旧租约不能写第二步骤，新Worker成功写入独立步骤/OperationId；两条数据库Trace分别对应监听器实际Trace，步骤行数严格为2。`node artifacts/r11i/run-pipeline.mjs` 的真实分类为Ai＋Smoke，SQL Server完整48/48通过；MySQL原组60分钟期限中止，官方完整记录46通过/1启动播种命令超时失败、另1未完成，同一输入精确补跑2/2通过，合并96唯一通过UID；顺序执行、每库1 Worker、原矩阵60分钟期限，不宣称全部Integration已执行。
- [x] API/Worker AOT分析零警告/错误，AOT架构88/88、完整架构235/235，JIT Integration构建及1214分片发现通过。命名30、矩阵6、治理57、工具链54和保护排除后的Markdown检查通过。独立只读复核无剩余Critical/Important，复核不代跑测试。MySQL原pipeline退出3且没有完整TRX，不声称原组通过；使用SDK官方TrxReport2.3.2.0读取47条完整记录，验证原48发现UID与唯一身份、状态及原始摘要，保留失败和未完成项。`node artifacts/r11i/run-mysql-retry.mjs` 在源码/完整消费者前后哈希一致、原构建和期限下补跑这2项；未修改生产、夹具或超时，未证明原启动超时已修复。原组结束自然释放共享重型锁，补跑再次排队，不中断其他验收。

首轮绿色构建因编辑脚本没有匹配CRLF，事务包装未写入而Trace表达式已替换，出现2项编译错误；改为明确补丁后构建通过，原错误保留，不称测试失败。审查在双库编译前发现第二测试步骤复用唯一OperationId，修正为独立测试操作后再次只读确认；这一修正仅影响Integration夹具，全部生产和Unit源摘要不变，不重复运行已验证的Unit。Unit运行前后源码和消费闭包摘要一致，之后的单文件Integration差异有归档清单和精确绑定。

冻结7821个输入；Windows源和Agents/AI生产/消费程序集摘要核对，既有RAG已知JSON数据/报告单列运行产物。Linux AOT、JIT及双库覆盖前验证R11h源码与完整消费摘要，只传当前差异，复用各自缓存。共享矩阵仅暂存HEAD加本批24项；既有协调器测试只暂存观测总数2→3，其他窗口新增的中文类型注释留在工作区，两保护路径未读写。

Windows x64/SDK10.0.401，Linux SDK10.0.400/runtime10.0.11，i7-12700H/约64GiB；Unit4处理器/2GiB GC，构建8CPU/4GiB，双库各2CPU/8GiB；本地缓存restore使用NuGetAudit=false，未运行在线依赖审计。TRX捕获长文本若有字段截断按原报告保留，状态/计数/精确UID另行完整核对。

本批验证不同Worker及作用域交接，不冒充操作系统进程重启、生产Trace导出器或所有收费/派发阶段已带持久StepId。全阶段分类准入、其他Provider/Chat输入Token硬限、未知外部动作/费用对账及集中Native发布/运行、生成应用、页面、运维恢复仍待推进。R11整体开放，Capacity-not-verified；真实命令、退出码、发现UID和摘要在 `artifacts/r11i/acceptance.json`。main提交本批8项文件，不推送；本段证据在测试后补齐，不把最终文档字节冒充运行前冻结字节。

#### R11j：索引分类与模型投影快照准入（2026-10-13，本批受影响本地验收完成）

基线 main `27b0e128fe59085a2255e06144629abd5c25d40a`，任务快照 `ai-index-policy-snapshot-r11j`。集中交付 Text/PDF/DOCX 分类门禁、索引预算前后的模型投影复核及真实双库零结算验证；不新增迁移、表列、依赖、HTTP契约或授权入口。

- [x] 分块边界只接受精确的 public/internal/restricted。预算前复核合法分类及配置Id/Version、ProviderKey、ModelId、EndpointBaseUrl、OrganizationId；预留后共享完整来源比较增加分类和四项模型投影。目的地按原字符串精确比较，合法续租的到期时间变化不视作策略漂移，租约Token/有效性仍由任务读取保护。
- [x] 新预留后来源失效/漂移不返回派发来源，沿真实账本结算 known 零用量 cancelled；清理失败传播。已有 reserved/unknown/known 不重派发、不推断零。原 UTF-8 字节加32预留、请求摘要和批次身份未变；restricted 不隐式新增私网政策。
- [x] 新增60项回归，覆盖三格式合法/非法分类、Host/Tenant预算前/后拒绝、五字段共享比较、有效对照、旧unknown、续租、结算异常及URI字符串绑定。有效RED42失败/18通过，GREEN60/60。补齐3组旧有效夹具的分类及模型投影，保留原断言。完整AI3989/3989、零跳过；全部Unit只发现7472，与规范矩阵一致，不宣称全Unit已执行。
- [x] 真实双库在生产预算Reserve返回后逐项改变分类/Provider/模型/Endpoint/Organization，Host/Tenant共10次独立新预留；断言无派发来源及持久known、ChargedTokens=0、cancelled。使用独立模型/知识库/任务，与原索引执行场景隔离。真实分类为Ai，SQL Server原44组42通过/2失败，补验2/2、通过UID并集44；MySQL44/44，共88唯一通过UID；每库1 Worker、顺序执行、原矩阵60分钟期限。
- [x] API/Worker AOT分析零警告/错误，AOT架构88/88、完整架构235/235；JIT Integration构建和1214分片发现通过。命名30、矩阵6、治理57、工具链54及受保护路径排除后的Markdown扫描通过。独立只读审查无剩余Critical/Important，未代跑测试。

原证据保留：首次夹具枚举名称编译错误修正后才形成有效RED；首次完整AI251失败均追溯至3组旧有效夹具缺字段，修正后一次完整回归3988通过/1 reference入口等待超时。原8秒WaitAsync期限及所有源码/消费输入保持不变，该方法3参数精确复验通过，随后完整AI3989全部通过；未证明原并行超时底层原因已经修复。SQL原组的DOCX持久化夹具同样遗漏分类，只补真实种子的internal；另1项入队用例在启动SQL连接时超时，保留原异常且不改生产或期限，底层原因未证明。原44完整结果及精确失败UID保留；第一次只构建Integration时临时/jit挂载改变MVC/覆盖率根目录，摘要门禁在测试前拦截；按原/work路径重建，恢复原元数据摘要后才补验；该单程序集重建按工作区互斥使用2CPU/2GiB、1GiB GC预算，双库仍按同机重型锁排队，原构建及门禁失败保留；修正后只重建Integration测试程序集，受影响2项补验通过，MySQL完整44组通过。不冒充原SQL组通过或整个消费程序集未改变。生产/Unit和AOT可达源不变，Integration DLL/PDB差异及单夹具差异显式绑定；AOT/架构既有证据继续复用。运行前后Unit源码及消费闭包摘要一致，既有RAG JSON数据/报告单列运行产物。外部窗口PDF类型summary注释留在工作区，仅冻结/暂存HEAD加本次分类字段；共享矩阵只暂存HEAD加本批60，保护路径不读写。

验证复用R11i的完整源和消费缓存，Linux AOT/JIT/双库分别核对原摘要后只覆盖当前输入差异。Windows x64/SDK10.0.401、Linux SDK10.0.400/runtime10.0.11、i7-12700H/约64GiB；Unit4处理器/2GiB GC、构建8CPU/4GiB、每库2CPU/8GiB。本地缓存restore使用NuGetAudit=false，不宣称在线审计。AOT仅静态分析，本批未新鲜Native发布/运行、生成应用或浏览器验收；集中验收按模块批次执行。

索引快照门禁不表示全阶段分类、其他Provider/Chat输入硬限、未知外部动作与费用对账已经完成。R11整体开放，Capacity-not-verified。实际命令、退出码、精确UID与摘要见 `artifacts/r11j/acceptance.json`；main提交本批，不推送。测试后补入本段证据，不将最终文档字节冒充执行前冻结输入。

#### R11k：本地重排与上下文分类快照和观测（2026-10-13，本批受影响本地验收完成）

基线 main `7f69c2d1618e1ed3afc64b8d22f2243cc6adc780`，任务快照 `ai-rerank-context-policy-r11k`。集中推进候选快照、全候选分类门禁、同步本地阶段观测与真实双库流程断言；无新迁移、依赖、HTTP入口或权限变化。

- [x] Rank和Context各只读取外部Count一次，以索引复制最多256项的候选；不可变候选/来源记录形成边界快照，不宣称向量缓冲区不可变。全部候选（包括前八块之外）必须有来源且分类为精确public/internal/restricted，先于向量计算、词表、引用Id与JSON；未知或缺失分类沿InvalidDataException失败。合法分类仍沿原权限/模型审批/交付时权威复核，不推导restricted私网政策。
- [x] 同步Run复用共享执行观测，增加knowledge_rerank和knowledge_context两阶段，沿原失败/取消分类和异常传播；阶段与结果使用既有有限标签，不携带分类、来源、身份或输入文本。空候选保留向量验证且不记重排；预取消不创建观测；监听器异常隔离和父Activity恢复保持原行为。
- [x] 新增44项，覆盖三格式合法分类、非法分类、256项尾部、空候选向量、预取消、五种监听器故障、回调替换集合和变化Count/禁止枚举。有效RED30失败/14通过，GREEN44/44；完整AI4033/4033，零跳过。全部Unit只发现7516，与规范矩阵一致，不宣称全Unit已执行；旧有效来源补internal，原支付观测6阶段扩为8阶段，保留原断言。
- [x] 真实known/unknown支付回答流程均捕获两个本地阶段和四次测量，验证completed、有限标签、无原始事件、父Activity恢复，保留原预算及索引账本断言。实际影响分类Ai + Smoke，SQL Server48/48；MySQL原48组47通过/1超时，精确1项复验通过，通过UID并集48，共96唯一通过UID；每库1 Worker，顺序执行，原矩阵60分钟期限。
- [x] API/Worker Native AOT分析零警告/错误，架构235/235及AOT架构88/88；JIT Integration构建与1214分片发现通过。命名30、矩阵6、治理57、工具链54和排除受保护路径后的387份Markdown扫描通过；独立只读审查无Critical/Important。

首次Unit GREEN构建出现Roslyn NodeStateTable初始化中的OutOfMemoryException，原exit1日志保留；当时主机提交内存约98%，具体分配根因未证明。仅重建Unit采用2处理器/4GiB GC预算并通过，测试仍4处理器/2GiB，未改源码、断言或期限。运行前后冻结源码与既有1063个消费文件摘要一致，10项既有RAG JSON/报告输出单列；Agents/AI生产与Unit消费程序集一致。复用R11j源及Linux缓存，覆盖前核对既有摘要；双库只同步明确生产项目产物、冻结源清单及完整Integration消费目录，不遍历外部克隆或治理夹具。外部窗口类型summary注释保留工作区，仅冻结/暂存HEAD加本批分类字段；共享矩阵仅暂存HEAD加44，其余脏文件保留，保护路径不读写。

Integration及架构程序集构建均exit0后，第一次完整架构执行中Docker后端退出，客户端unexpected EOF/exit125、引擎管道消失；架构无完成退出码，原日志和不完整流产物保留，不称其通过。按用户既有授权重新启动Docker Desktop，未单独删除或停止持久容器；底层退出原因未证明。恢复后核对冻结源、六项生产/消费程序集及完整消费目录摘要，复用成功构建，重新执行完整架构235及分片发现；双库在恢复后完整执行，无期限放宽或源码修补。恢复命令与退出码单列，不将原中断包装为成功。

MySQL原失败为Complete_text_execution_renews_lease_and_resumes_paid_batches_async中Ask API受控模型入口WaitAsync30秒超时，同时存在数据库读取/续租取消与连接异常日志；底层原因未证明。原exit2、48项完整TRX和失败UID保留，未修改源码、消费程序集、断言或期限；按精确发现UID补验1/1，运行前后核对同一冻结源/消费摘要。仅合并原47个通过UID及精确补验1个，未冒充原48组通过或已修复底层超时。

本地环境Windows x64/SDK10.0.401、Linux SDK10.0.400/runtime10.0.11、i7-12700H/约64GiB；Unit4处理器/2GiB GC、Linux构建8CPU/4GiB、恢复验收4CPU/4GiB、每库2CPU/8GiB。缓存restore使用NuGetAudit=false，不宣称在线审计。AOT仅静态分析，本批未新鲜Native发布/运行、生成应用或浏览器验收；集中验收按模块批次执行。

本批不关闭其他阶段分类、跨重启持久Trace、其他Provider/Chat输入硬限或未知外部动作与费用对账。R11整体开放，Capacity-not-verified。实际命令、退出码、精确UID及摘要见 `artifacts/r11k/acceptance.json`；main提交，不推送。本段测试后补入，不将最终文档字节冒充执行前冻结输入。
