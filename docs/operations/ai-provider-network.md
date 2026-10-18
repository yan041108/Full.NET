# AI Provider 网络与能力配置

适用于正式 Provider 的聊天、Embedding 和模型目录探测，包括 OpenAI/兼容网关、Azure OpenAI 与 Ollama。默认只允许公共 HTTPS，拒绝未批准的私网、环回及 HTTP。旧版本可直接访问的本地 Ollama 在升级后必须显式配置批准列表；内网 OpenAI 网关目前不支持策略豁免，不能用 Ollama 键代替 OpenAI 协议。

## 批准 Ollama 地址

在可信宿主配置中填写精确源地址（协议、主机、端口，不含路径、查询、用户名或密码）：

```json
{
  "FullNet": {
    "Ai": {
      "Network": {
        "OllamaAllowedOrigins": ["http://ollama.internal:11434"]
      }
    }
  }
}
```

容器/Kubernetes 可以使用环境变量 `FullNet__Ai__Network__OllamaAllowedOrigins__0=http://ollama.internal:11434`。确认批准目标由管理员控制；HTTP 明文例外仅适用于受信网络。模型配置仍填写对应端点，不能通过模型配置、请求参数或工具输入新增网络批准。

默认列表为空。支持多个精确源，不支持通配符与任意私网总开关；域名、协议或端口不同需要分别批准。配置在宿主装配时读取并固定，修改后需重启所有 API 实例，重启会终止这些实例上的活动流。

## 仅私网 Provider 模式

可信宿主可设置 `FullNet:Ai:Network:PrivateOnly=true`（环境变量 `FullNet__Ai__Network__PrivateOnly=true`），并同时配置上述 `OllamaAllowedOrigins`。此模式覆盖注册的聊天、Embedding 和目录探测 HTTP 客户端；只允许精确批准的 Ollama 源，空批准列表关闭所有 Provider 出口。OpenAI 兼容网关与 Azure OpenAI 均拒绝，不把它们的协议或密钥交给 Ollama。键缺失或 `false` 保持既有网络策略；显式空值、非法布尔值或对象形状使装配失败。

在此模式下，批准的 HTTPS 域名也只能解析到私网或环回；全部 DNS 结果先校验，再按已校验 IP 连接。包含任何公网或特殊用途地址就拒绝整组，不尝试剩余私网地址来掩盖混合结果。批准列表不能包含公网字面 IP。私网连接失败只尝试同组已校验地址，不能切换到公网 Provider；协议层既有错误和未知计费恢复规则继续适用。

此开关在宿主装配时冻结，修改后须重启 API 和使用 Provider/MCP 的 Worker 实例；配置重载不能关闭正在使用的边界。它控制正式 Provider 与 MCP 客户端网络出口，不代表知识库数据分类、重排、遥测、向量库或宿主其他 HTTP 出口已应用同一策略；这些阶段仍按 AI 计划分别验收。私网 HTTP 的信任网络约束与 HTTPS 系统证书校验保持原规则。

## MCP 客户端出站边界

远端 MCP 使用独立的 `FullNet:Ai:Mcp:Client:ApprovedOrigins` 精确源列表，不借用 Ollama 批准。每项只包含 HTTP/HTTPS 的协议、主机和端口，不能包含业务路径、凭据、查询或片段。实际 MCP 端点可以包含路径与查询，但不能包含凭据或片段。

`FullNet:Ai:Network:PrivateOnly=true` 时，只允许已批准源的私网或环回目的地；即使配置或 DNS 返回公网 HTTPS 地址也拒绝。开关缺失/false 时保留公共 HTTPS；明文 HTTP 在两种模式下都要求精确批准及私网/环回结果。无效布尔值、对象、空批准项与非法源在宿主装配时失败，批准列表和开关共同冻结，修改后须重启。

每个请求校验 URI，每次新连接校验 URI 与连接主机/端口一致、单次 DNS 的全部结果，然后仅连接已验证 IP；混合或空结果在任何连接尝试前拒绝。连接失败只可尝试同一轮已验证的其他 IP，不修改端点或切到公网。命名客户端禁用自动重定向、系统代理与 Cookie，强制 HTTP/1.1，保留系统 HTTPS 证书校验，池中连接最多复用两分钟。

源批准只约束网络去向；远端工具目录批准、精确权限、当前租户/会话及统一执行器仍分别裁决。私有网络不使工具或响应可信，也不证明未知副作用已对账。真实 TCP/DNS 规则由相关 Unit 回归验证；双库的 MCP 互操作夹具使用受控 TestServer 传输，不能替代生产 DNS、TLS 或 Linux 原生网络验收。

## 网络行为与限制

- DNS 的全部返回地址须通过校验；一个结果进入禁区就拒绝整次新连接。连接使用本次已校验的 IP，不再次解析主机名。池中连接最多复用两分钟；每个新连接重新解析和校验，每次请求仍校验目标 URI。
- HTTP 明文例外只允许私网或环回结果；即使域名在批准列表中，解析到公网也拒绝连接。公共地址必须使用 HTTPS。
- 链路本地、元数据地址、未指定、多播、保留/文档地址以及可绕过判断的特殊 IPv6 转换地址不接受批准列表豁免。IPv4 映射 IPv6 按对应 IPv4 检查。
- 自动重定向、系统代理和 Cookie 容器禁用。3xx 作为上游失败返回，需管理员直接配置最终批准端点；依赖系统代理的环境需要调整网络出口，不能假设代理设置继续生效。
- 请求使用 HTTP/1.1，确保经过受控 TCP 连接回调；HTTPS 证书校验保持系统默认，不能跳过证书校验。聊天与目录客户端按 Provider 隔离，凭据仅在请求头上设置。
- 目录探测不证明模型推理权限、配额或实际可用性。策略拒绝返回安全失败信息，不回显原始响应或凭据。

当前证据为地址规则、DNS/连接协同、客户端配置、本机套接字重定向回归和 AOT 分析；生产网络、Kubernetes DNS、双库 Host 链路与 Linux 原生进程仍须在部署环境验收。

## Provider HTTP 观测

启用 Ai 模块时，宿主自动订阅 `Full.NET.Ai.Providers` 的 Meter 和 ActivitySource，使用既有 OpenTelemetry 导出配置。聊天、Embedding、目录探测的九个命名客户端记录 `fullnet.ai.provider.http.requests`（单位 `{request}`）及 `fullnet.ai.provider.http.duration`（单位 `ms`），并在当前 Trace 下创建 Client Activity。

此处计量进入 Provider Handler 的一次 HTTP 尝试，耗时截至响应头返回；响应体、流式生成、持久化和结算不在其中。2xx 的 `succeeded` 只表示 HTTP 状态成功，不能作为回答质量、模型拒答、用量或费用结算证明。请求在进入 Handler 前已取消时可能没有观测记录；没有自动重试或切换 Provider。

标签只有固定集合的 `ai.provider`、`ai.operation`、`ai.outcome`。操作为 `chat`、`embedding`、`connectivity`；结果为 `succeeded`、`redirected`、`rejected`、`rate_limited`、`server_error`、`invalid_status`、`cancelled`、`policy_rejected`、`network_error`、`failed`，未知标签归一化。此来源不记录 URL、凭据、模型名、提示词、正文、异常消息、用户或租户标识；宿主其他 HTTP 诊断来源仍按各自配置管理。

监听器采样、发布、测量及 Activity 回调异常被隔离，不能替换原响应或原调用异常；这不代表诊断事件必然送达。ActivitySource 或 Meter 首次初始化失败时，对应空结果会保留至进程退出，需排查诊断配置并重启恢复。当前能力仅为 Provider HTTP 观测，Run/Step 全链路关联与完整数据去向门禁仍按 AI 计划推进。

## Agent 执行观测

Ai 宿主同时订阅 `Full.NET.Ai.Execution`，记录 `fullnet.ai.execution.attempts`（`{attempt}`）和 `fullnet.ai.execution.duration`（`ms`）。指标只有 `ai.stage`、`ai.outcome` 两项固定标签；Agent 阶段为 `run`、`workflow`、`tool_loop`、`node`、`model`、`tool`、`step_write`，知识问答阶段见下表，未知阶段归为 `other`。尝试包含本地准入拒绝与恢复，不等同于已派发、已付费调用，嵌套阶段不能直接相加作为运行总数。

| 知识问答阶段 | 观测范围 |
| --- | --- |
| `knowledge_answer` | 来源准备及两次收费阶段的完整内部执行 |
| `knowledge_embedding` | 查询 Embedding 的预留、派发、独立结算和等待后复核 |
| `knowledge_retrieval` | 有界向量读取、准备编排及来源复核 |
| `knowledge_rerank` | 非空候选的本地余弦排序与分类门禁；空候选仅校验查询向量，不计重排 |
| `knowledge_context` | 完整来源分类复核与有界上下文构造；早于引用身份及正文序列化 |
| `knowledge_generation` | 缓冲生成的预留、派发、独立结算及答案校验 |
| `knowledge_validation` | 最终严格 JSON/引用解析和全部来源复核 |
| `knowledge_delivery` | 存储及清理等待后的最终交付资格复核 |
| `knowledge_index_text` / `knowledge_index_pdf` / `knowledge_index_docx` | 原索引批次的来源准备、预算准入、模型派发、独立结算及返回前复核 |

索引批次在 Trace 使用可信原任务 `ai.knowledge.task.id`，不将任务作为 Agent RunId；准入返回后关联已有 `ai.operation.id`，包括不能再次派发的重复回执。准入尚未返回时允许没有操作标签，不能为观测查询或创建业务操作。指标仍严格只有 stage/outcome。非空返回为 `completed`，正常空返回为 `not_processed`；异常按原预算、授权、取消或失败分类并原样传播。预取消在开启观测前拒绝，监听器故障不改变执行、收费或父 Activity。`completed` 仅表示一个批次方法返回，不证明向量写入、事务提交、整代次 ready 或最终费用核证。

知识阶段沿用当前父 Trace；仅收费阶段和交付复核关联已有 OperationId，不生成 RunId 或持久 StepId。`completed` 表示本阶段原方法返回，交付复核成功不证明客户端收到响应。`refused` 只来自严格解析后的结构化拒答；预算异常、撤权和取消分别为 `budget_rejected`、`authorization_required` 和 `cancelled`。其他异常默认 `failed`，原令牌已取消时沿用共享源的取消收尾分类。正文、引用内容、分类、身份和异常消息均不进入标签或事件。

本地重排和上下文入口先按首次有界索引冻结候选，再拒绝非精确 public/internal/restricted 来源；未入选前八块的来源仍属于分类依赖。分类不替代读取权限和精确模型批准，也不隐式新增 restricted 私网政策。两个本地阶段仅记录固定 stage/outcome，不读取原文、分类值或业务身份；分类失败沿原 InvalidDataException 传播，观测失败不能改变业务结果。

收费问答、来源预检和向量检索入口的知识库选择只读取一次外部 Count，按原顺序索引复制一至八个非空、互不重复的标识，不调用外部枚举器。复制后重新检查事务状态，复制期间取消及非法选择不会进入配置/权限查询、预算预留或模型派发。快照不授予读取权，全部所选知识库仍须通过原权限、分类、模型批准及来源完整性复核。

管理 Embedding 测试入口在配置查询前冻结输入：批量 Count 只读一次，最多索引复制16项，原始单条8000、总32000字符限制早于 Trim，空批量仍回退单条输入。只有新建且操作标识匹配的预留授予本次派发和结算所有权；绑定、工厂或派发前取消按已知零用量结算，进入 GenerateAsync 后未收到合法报告保留未知消费。合法报告先记录再校验向量、预留上限和取消；坏向量、超额、释放失败或取消不丢弃已有报告，不将超界用量转成溢出的整数。完整向量不回显，结算异常仍向调用方传播。

阶段按实际方法进入记录：生成在结算前的输出预检失败由 `knowledge_generation` 记录，尚未调用最终校验时不生成 `knowledge_validation` 事件；预留或来源准备失败也不会虚构未执行的后续阶段。

Trace 使用 Internal Activity，在当前父上下文下关联 RunId、已有 OperationId 和 0–63 的步骤序号；Provider HTTP 为模型阶段的子 span。Trace 不记录提示词、正文、异常消息、定义键、模型名、工具名或用户/租户标识，指标也不含业务ID。步骤写入的 `step_write` 使用原租约 RunId、原步骤 StepId 和 OperationId，在租约 fencing 成功后开启；只在 Trace 输出 `ai.step.id`，指标仍只有阶段与结果。原步骤未带 TraceId 时，将实际创建的观测 Trace 写入既有 `fn_ai_agent_step.TraceId`；调用方给定值保留，未采样或诊断故障时允许为空。不新增操作、步骤、SQL结构或费用，也不为补观测重放调用。

`step_write` 的 completed 仅表示本地写入方法返回；嵌套事务的外层仍可回滚，尝试指标和 Trace 均不是提交、派发或费用凭证。持久步骤在原事务提交后才可作为数据库事实。换 Worker 后通过已存 RunId/StepId/TraceId 关联各次追踪；不从历史数据恢复父 Trace，也不声称所有跨进程 span 属于同一 Trace。当前回归覆盖不同 Worker 的重新领取和不同作用域，不等同于操作系统进程重启或生产导出器验收。

`processed` 只表示 Worker 本次处理返回true，包含已提交的失败、授权拒绝或暂停；`not_processed` 表示返回false。内部执行器的 `completed` 和模型/工具边界的 `succeeded` 不证明运行最终交付或费用结算。固定结果还包括 `denied`、`approval_required`、`reconciliation_required`、`budget_rejected`、`authorization_required`、`cancelled`、`failed`、`refused`；`refused` 也用于静态工作流结构化校验门禁拒绝，不代表已检测所有模型自然语言拒答。

每个范围独立收尾，父Activity恢复及采样、测量和停止回调故障不改变业务结果。诊断事件不保证送达；此源首次初始化失败也可能保留空源，修正诊断配置后需要重启进程恢复。授权、预算、账本、审批与持久化仍由原业务边界判定。

## 声明兼容网关的流式用量能力

OpenAI 兼容协议不代表支持所有可选字段。默认不发送 `stream_options.include_usage`；包括官方端点在内，也不根据域名猜测某个模型的能力。确认端点与模型支持后，在同一宿主配置中添加：

```json
{
  "FullNet": {
    "Ai": {
      "OpenAiGateways": [
        {
          "Endpoint": "https://gateway.example/v1",
          "ModelId": "chat-model",
          "SupportsStreamingUsage": true
        }
      ]
    }
  }
}
```

端点包含 API 基路径，尾部斜杠会统一处理；模型名精确匹配并区分大小写，不支持通配。端点、模型或基路径不同需分别声明。`false` 或未登记均不发送该扩展；未知字段、缺失/无效值和重复绑定使 Ai 装配失败，配置修改后须重启宿主。声明不改变网络批准、身份权限、最大输出或其他调用选项，也不启用工具、多模态和 Embedding。

供应商未返回用量时，消息的 Token 字段保持未知；现有配额结算会保留保守预留额度，不按零用量释放。已验证支持用量返回的网关应显式声明本能力。即使未请求扩展，响应实际包含合法 usage 时仍会记录。请求被拒绝时不自动删除字段重试，避免重复调用和计费。

连接回调机制参考 [.NET 10 ConnectCallback](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.connectcallback?view=net-10.0)。实施状态见 [AI 演进计划](../superpowers/plans/2026-09-08-ai-agentic-web-alignment.md)。


## 聊天错误与取消契约

聊天流启动前，输入或模型不可用返回 422、不存在或不属于当前用户的会话返回 404、重复生成返回 409，配额拒绝继续返回 403。响应体为标准 ProblemDetails；客户端应读取 `code` 和 `title`，不能直接按 SSE 解析所有响应。流启动后的执行错误使用既有 `error` 事件，保持已发送的 HTTP 状态。

只有最终消息保存、配额结算和租约释放成功后才发送 `done`。请求断开时仍按原租户与生成代次执行独立有界清理；各清理阶段最多等待五秒，驱动未退出时不会提前释放其作用域。清理失败返回安全错误，持久租约期限继续提供恢复兜底，不自动重放模型请求。租户停用会停止新派发，但不阻止已有代次收尾。


## 分阶段数据分类策略（R11u，已实现、待集中验收）

宿主启动时冻结 FullNet:Ai:DataPolicy。阶段仅接受 chat、embedding、rerank、context、telemetry、connectivity、mcp；每个阶段的数组仅接受 public、internal、restricted，大小写精确。缺省阶段允许这三个已知分类，显式空数组拒绝该阶段；未知键、未知分类、标量或非连续数组使装配失败。PrivateClassifications 指定必须进入私网闭包的分类，缺省为空。修改配置后重启宿主，不能由模型或请求扩大范围。

知识索引使用来源的权威分类；完整检索来源取最严格分类，用户问题、聊天历史、Agent 和 MCP 参数缺少低敏证明时按 restricted 处理。阶段拒绝先于凭据读取与派发。PrivateClassifications 只收紧实际 DNS/套接字边界，仍沿用已批准 Ollama 源；它不为 OpenAI/Azure 或任意私网地址增加网络批准，失败不自动改用公网 Provider。

telemetry 拒绝时抑制 AI 自有 Trace、Metrics、故障日志和预算诊断标识，以及本批命名 Provider/MCP HTTP 客户端的默认日志、OpenTelemetry 自动诊断和连接仪表。嵌套范围只能收紧，后台执行继承冻结策略。此边界不关闭安全/业务审计、可靠运行事件或通用宿主入口诊断；这些仍遵守各自脱敏与保留规则，不能把诊断抑制当作授权或账本豁免。

本批生产依赖和测试消费者已完成 JIT 编译，AI/授权以原组加精确补验覆盖 4857 个官方成功 UID；真实双库 OpenAPI 导出一致，前端类型与聚焦组件验证通过。原失败和本机重启中断单列；完整影响集、Native 发布/运行及真实供应商环境仍待集中验收，不外推能力状态。


## 显式 Responses 输入计数（开发合批，待验收）

宿主可在精确 `FullNet:Ai:OpenAiGateways:<n>` 端点/模型配对声明 `SupportsResponsesInputTokens=true`，继续显式提供原 `SupportsStreamingUsage` 布尔值。仅填写经独立验证支持 Responses 的 HTTPS 端点和不可变模型标识；禁止通配。严格输入准入还需 `FullNet:Ai:InputAdmission:RequireExactChatTokens=true`。Ollama、Azure Chat 及未批准端点保持失败关闭，不跨协议套用计数。

新文本传输将同一冻结 `model/input` 分别交给 `responses/input_tokens` 与 `responses`，生成侧固定 `store=false`、流式输出及有界 `max_output_tokens`；生成控制字段不参与输入计数。计数后再次复核原授权、取消、模型/来源及剩余额度，超量或坏回执不发送生成请求。生成返回的模型身份必须精确匹配批准标识，别名自动换成其他快照会拒绝交付。收费回执缺失保持未知，计数值不能替代真实 usage。

参见 [官方 Token counting 说明](https://developers.openai.com/api/docs/guides/token-counting)。本地受控 HTTP 回归不等于真实云端/兼容网关验证；未取得对应证据前不要把新声明理解为所有模型已获认证。
