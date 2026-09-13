# Full.NET AI 与 Agentic Web 安全及持久运行设计

- 状态：已获当前用户“按计划执行”授权的实施设计；能力尚未完成，不声明运行验证通过。
- 日期：2026-09-08。
- 代码基线：`f74515d1b9e4544e4c75eeb623892970790b4d0c`。
- 依据：[总体架构 §18](2026-07-17-fullnet-architecture-design.md#18-ai-与-agentic-web)。
- 实施与验证跟踪：[唯一活动计划](../plans/2026-09-08-ai-agentic-web-alignment.md)。
- 本文为该主题安全、数据所有权和恢复语义的专项事实源；计划中的设计摘录与本文冲突时必须同步收敛，不并行维护相互竞争的决策。
- 依赖具体版本、Native AOT 可达闭包与标准客户端互操作必须由实际实验验证；本文不批准改变 API/Worker/Migrator 角色、改用 EF Core 或放宽双库和授权边界。

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

T02 网关能力决策：由 Provider 内的 `OpenAiGatewayPolicy` 读取宿主 `FullNet:Ai:OpenAiGateways` 配置，按规范化端点（含基路径）与精确模型标识匹配 `SupportsStreamingUsage`。默认不发出可选流式用量扩展，官方域名也不隐式放行；非法/重复配置在 Ai 装配时拒绝。该策略只调整请求协议字段，不修改凭据、网络或预算授权，不启用工具、多模态和 Embedding。缺失用量保持未知，结算继续遵循原有保守预留政策。

T03 凭据切片决策：当前同步聊天/目录请求使用作用域内的短期不透明引用。Ai 在已授权查询后签发引用，将其绑定配置标识、版本、模型、端点及冻结的选项；Provider 经 `IProtectedModelCredentialStore` 读取受保护值，再内部解密。作用域结束清除引用，其他作用域和篡改后的绑定不能读取，Provider 工厂与消费者采用 Scoped 生命周期。配置写入使用 Provider 的 `IAiModelCredentialProtector`，保留历史保护 purpose。该实现不新增 SQL 或持久化对象，不声称已有长期凭据句柄、跨请求立即撤销或密钥轮换存储；T07 的持久运行恢复必须重新授权并解析新绑定，禁止持久化短期引用。

T03 网络切片决策：新增 `Full.NET.AI.Providers.Http` 作为 OpenAI、Ollama 两个 Provider 的实际共享消费者依赖，集中维护连接时的地址政策，避免复制安全规则。该项目不依赖 Ai 业务模块；Composition 注册 Provider 专用命名客户端。默认公共 HTTPS，只有宿主 `FullNet:Ai:Network:OllamaAllowedOrigins` 精确列表允许 Ollama 的内网/环回和 HTTP；配置固定到宿主生命周期，变更后重启。不接受模型配置或工具参数自行授权内网。链路本地、元数据、未指定、多播及保留地址始终拒绝；混合 DNS 结果中任何禁区地址导致整次连接拒绝。禁用重定向、代理和 Cookie，每次新连接只解析一次并直接连接已校验 IP，HTTP 固定使用经过 TCP 回调的 1.1。

当前实现（T02/T03，2026-09-08）：现有聊天通过 `IChatClient` 调用两个 Provider，连通性通过独立 `IAiModelConnectivityProbe` 扩展点调用同一 Provider。`ModelBinding.CredentialReference` 只携带上文定义的短期引用，已移除 T02 过渡期的密文字段；其 `ToString()` 不输出引用或供应商选项。此对象不得持久化到日志、审计或检查点。业务编排已无保护/解密实现；配置写入保护、Provider 解密与连接时网络政策已实现，但长期凭据存储、轮换/撤销和部署验收尚未完成。当前聊天工厂只支持文本，对非文本内容和非空调用选项明确拒绝；Embedding 随 T13 引入实际接口消费者。连通性目录必须按供应商协议精确解析，不能从任意正文推断模型存在；目录可访问不等于推理权限与配额可用。

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

T05 落地边界：当前仅实现请求作用域只读执行，拒绝非空 RunId、API Key 和所有副作用；当前 JWT 会话由 Identity 权威 Port 逐次复核。审计 Id 复用为 OperationId，参数哈希保存在受控摘要中；210 双库迁移只扩展状态 CHECK。独立 OperationId/RunId/审批列、持久委托与未知回执恢复仍属于后续运行时，不把本地次数/合作取消预算当成跨实例持久预算。实际验证及迁移部署/回退约束见唯一活动计划的 T05 执行记录。

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

T04 聊天切片的收尾使用原请求捕获的可信租户快照与既有生成代次条件。每项清理使用独立作用域和五秒等待期限，取消或停用租户只阻止新派发，不阻止本代状态收敛。迟到驱动在实际退出后释放其作用域；后续清理不复用同一连接。最终持久化、结算或释放失败时不得发送成功结束事件。

T06 落地边界：scope 锁与操作账本使用可信 Host/租户编码，原月份与价格快照随 OperationId 固化，现有租户配额在同事务继续约束。未知用量保留预留并允许补录；成本字段包含保守记账，不能等同已核实的账单。当前 Provider 尚无硬费用认证，要求硬上限时失败关闭；聊天是首个消费者，Agent/Embedding 实际调用随对应任务接入。配置、切换与回退见 [预算运维说明](../../operations/ai-operation-budgets.md)。

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
