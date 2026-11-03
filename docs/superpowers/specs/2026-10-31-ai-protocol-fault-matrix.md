# AI-A04 标准协议客户端与故障矩阵

状态：AG-UI只读标准客户端首批已完成本地聚焦验收。Unit33/33、官方客户端语料7/7、真实Kestrel双库HTTP同轮2/2、修复后实际Native API双库同轮2/2；原Native首轮0/2保留。AI运行页撤权清空Unit26/26及受控HTTP桌面/移动2/2通过，四图已查看；不是正式Vue真实Native栈。网络背压/写入期限释放、持续权限/令牌失效精确差集与MCP余项仍未关闭，A04整组未完成。

## 范围与既有证据

按AI计划接续未完成的外部协议验收；A01真实Provider配置等待获准参数期间，此组可独立推进。Workflow业务及前端不动，不调用真实收费模型，不提交推送、不跑全量、不复制整仓。

| 范围 | 当前真实实现与证据边界 | 本组余项 |
| --- | --- | --- |
| AG-UI服务端 | `AgUiEndpoint`提供既有Run的只读GET SSE；`AgUiStreamService`每批100、每秒轮询并重读归属，终态排空历史。终态分页、竞态及旧回执有单元覆盖。 | 标准客户端、实际断开/重连/去重、慢消费者、游标与当前授权失效组合。 |
| AG-UI集成 | 旧`AgUiProtocolTests`文本证据保持；新增固定官方`@ag-ui/client`1.0.3，通过公开`HttpAgent.requestInit`扩展消费既有只读GET，SDK负责帧解析/验证/订阅。双库真实Kestrel覆盖Host/Tenant。 | 新输入的实际Native、真实网络背压/写入截止释放，以及持续权限/令牌失效的精确差集；慢订阅不能替代这些证明。 |
| MCP | 当前C# SDK锁定2.2.0；标准list/call/read、受控远端ping及基本Bearer/元数据、网络策略已有原验收记录。 | 仅补未有原始证据的Origin/session/audience、令牌生命周期、恶意/超限、Schema漂移、注入、超时断流组合。 |

来源：`tests/Full.NET.IntegrationTests/Ai/AgUiProtocolTests.cs`、`McpServerInteropTests.cs`、`McpServerAuthorizationTests.cs`、`McpClientInteropTests.cs`，以及`tests/Full.NET.UnitTests/Ai/AgUiStreamTerminalTests.cs`、`Directory.Packages.props`。既有结果按原冻结输入复用，不为整理矩阵重跑。

## 第一关联批次：标准AG-UI消费与只读恢复

先确认并锁定官方标准客户端版本和公开接口，再接通真实网络夹具。现有GET端点只重放持久事件；客户端连接、断开、重连、恢复和游标错误均不得新建Run、恢复工具或追加收费。可以复用K06实际静态无模型图建立真实Run，再通过正式取消或独立审批形成真实事件；大历史/慢消费者专用构造数据明确标注为协议载荷，不冒充真实模型或大规模生产运行。

采用同一Run/可信主体/租户，记录实际接收顺序和游标。覆盖跨分页完整历史、断流后按最后确认游标继续、重复帧去重、单一终态、已终态重连、未来/失效/畸形游标和不连续历史。标准客户端扩展只能使用公开支持的接口；若其默认入口用于POST运行，明确使用公开传输扩展消费本项目既有GET，不更改为会触发执行的接口。

用真实慢速读取及取消检查有界资源释放、写入等待后权限复核和终止语义；不以MemoryStream或截断字符串单测代替实际网络。核对当前会话/权限在持续流中的失效行为；已开始SSE后不能混入ProblemDetails JSON或原始异常。任何发现的隔离或数据泄漏及时修复，保持公开事件静态闭包、幂等和未知结果语义。

### 真实 TCP 背压定向证据（2026-10-31）

新增真实 Linux Kestrel/回环 TCP 接收端验证，接收窗口设为 1024 字节且客户端不读取；正式 `AgUiStreamService` 面对有界合成历史载荷，实际 **10.040 秒**因写入期限返回未排空，测试 **1/1、零跳过**。客户端在结果返回前保持连接，没有通过主动取消制造结果；证据 `a04-backpressure-unit-v1.log` 和对应 TRX。此项使用内存来源夹具与真实网络，未验证数据库、真实 JWT/权限变化、Native API 或生产规模，不替代整组端到端验收。

持续 JWT 边界代码复查发现，Identity 原先只在会话和权限异步读取之前检查原 JWT 的 `exp`；若令牌在等待期间到期，当前调用可能仍返回授权。现于权限读取结束后再次检查同一 `exp`，并补充读取期间到期的确定性用例；Linux Identity 与测试构建退出 0，定向 Unit **17/17、零跳过**，证据 `a04-expiry-unit-v1.log` 和 TRX。该测试使用认证主体/受控时钟与模拟权威读取；新 Native 尚未验证，不将源码修改当作整组验收完成。

补充正式签名 JWT 的双库长连接：短期 JWT 实际到期后，Kestrel 真实 TCP 流先已交付快照，再发送安全拒绝码；原运行事件、日程意图和预算无新增。JIT Host 测试双库 **同轮 2/2、零跳过、2分38.274秒**；Identity/Integration PDB 分别 405/1031 源文档匹配，见 `a04-expiry-http-v1.log`、TRX 和 `a04-expiry-http-source-proof-v1.json`。这不覆盖 Tenant、实际 Native、运行中权限变更或读取等待期间签名令牌到期的数据库时序，不将整组标记通过。

JIT Host 新增双库真实 TCP 持续撤权 **4/4、零跳过**：正式角色权限变更会撤销会话，原流终止；独立数据库夹具只删除权限行、保留原会话及日程读取，也使原流拒绝继续。v1 因动作权限依赖页面权限而 400、v2 因误以为正式角色变更不撤销会话而断言失败，均保留；v3 见 `a04-revoke-http-v3.log`、TRX 及 `a04-revoke-http-source-proof-v3.json`。该项仅证明 JIT Host；数据库夹具不代表生产角色更新流程。

实际 Native API 首轮中双库 JWT 到期 **2 项通过**，撤权 **4 项失败**于夹具跨宿主使用不同签名环的令牌，原结果保留；改用 Native 正式登录后撤权补验 **双库同轮 4/4、零跳过**。两轮覆盖 6 个唯一场景，不能称同轮 6/6。新 ELF 为 `79E5382EE315325E88907FDE7A2364DA31098519871ECB488DA713CD8934D1AA`；门禁证据 `a04-admission-native-adoption-v1/v2.json`、进程证据 `a04-admission-native-process-v1/v2.json`、各自 TRX。仅 Host，未覆盖 Tenant/Worker/模型；独立权限行仍为数据库测试夹具。

## 第二关联批次：MCP精确故障组合

为每个未覆盖组合建立“输入、可信策略、预期拒绝点、是否允许派发、原始证据”行，避免同一根因重复扩张用例。Origin、session、audience按实际发行/验证链与协议固定预期，先核对现有安全实现，不能仅修改测试令牌或接受列表使其通过。

标准C#客户端与真实受控服务验证令牌刷新/撤销、不同用户/租户/会话交叉、服务令牌不可透传用户身份；连接复用不跳过当前Schema/批准/权限核证。恶意JSON/SSE、超限字节、Schema改变、反向RPC、提示注入、重定向、超时/断流使用内部隔离服务，保留正式DNS/证书/目的地及静默边界。分别记录SDK解码拒绝、宿主策略拒绝和实际派发结果，不将任一层的成功外推为整组通过。

正式 MCP 客户端和私网出站策略连接获准回环 TCP 服务，畸形 JSON、超限 JSON、超限 SSE、断流 **4/4 失败关闭且零远端工具调用**，实际握手 `2025-11-25`；超限 JSON 为 `HttpRequestException`，另三项为 SDK `McpException`。此批没有单独识别 SSE 异常的内部层，不宣称超时、TLS、正式身份或 Native 通过。证据 `a04-mcp-fault-unit-v1.log`、TRX、`a04-mcp-fault-source-proof-v1.json`。

修复远端工具消失/Schema 不可读取时 `null` 摘要被历史批准放行的问题，现统一按漂移失败关闭。真实 TCP + 正式 SDK + Handler：未变目录对照派发一次，变更及消失均零派发，连同策略测试 **7/7、零跳过**；实际协商 `2025-11-25`，PDB 见 `a04-mcp-schema-source-proof-v1.json`，日志/TRX 为 `a04-mcp-schema-unit-v1`。此项未覆盖完整统一执行器、双库或新 Native；不能外推 A04 整组通过。

补充 JIT 统一执行器 + 双库持久批准 + 真实受控 TCP：**同轮 2/2、零跳过**，每库不变/变更/消失三次校验仅派发正常一次，成功结果标为不可信；见 `a04-schema-executor-http-v1.log`、TRX 及 `a04-schema-executor-http-source-proof-v1.json`。测试主体由服务器令牌构造，没有经过 HTTP 认证中间件；不证明生产 OAuth、Native 或 A04 全部矩阵。

MCP Origin 首轮双库实际 TCP **0/2**：非法来源 POST 获 200，保留原始失败。现使用 Identity 既有浏览器精确白名单显式验证 MCP 路径来源，不因请求 Host 与 Origin 相同而绕过；JIT Unit **9/9**，真实签名 JWT + 双库 TCP **2/2**，无 Origin/许可 Origin 正常，非法 GET/POST/OPTIONS、未批准同 Host 与不透明 Origin 拒绝。证据 `a04-origin-unit-v2.log`、`a04-origin-http-v2.log`、TRX 与 `a04-origin-source-proof-v2.json`；此项不证明资源 audience 发行绑定。

补充实际 Native API 双库 **同轮 2/2、零跳过**，同样覆盖正向及非法 Origin；新 ELF `3910F2406BFF0B7D0DE8CD1621D6AC2F9C7F3FAE8B6C6EA4A3ACB1C0E6A0227F`，发布、源、5 模块消费者、规范身份和实际进程证据见 `a04-origin-native-adoption-v1.json`、`a04-origin-native-tests-v1.log`/TRX、`a04-origin-native-process-v1.json`。不证明资源 audience 或 OAuth 发行绑定。

### MCP版本与准入差集接续约束（尚未编码/执行）

连接复用核对发现：作用域内仅以连接 UUID 复用初始化任务，第二次传入不同端点或服务凭据时可能沿用第一次的连接。现已将已验证的精确端点与服务凭据 SHA-256 摘要绑定到该 UUID；初始化中或完成后配置变化均拒绝复用，失败的初始化仍可重新建立。摘要不记录到日志，不作为凭据发行或撤销的替代。Linux 定向单测 **6/6、零跳过**，包括初始化中变更端点/凭据与完成后变更凭据；证据 `a04-mcp-binding-unit-v1.log` 及对应 TRX。此项使用受控 HTTP handler，不据此关闭 A04。

另外以正式网络策略和 SDK 连接受控回环 TCP 服务，证明服务端撤销旧凭据返回 401、同作用域阻止新凭据复用旧客户端，以及新作用域使用新凭据可读取目录；v2 **1/1、零跳过**，实际 initialize 协商 `2025-11-25`，证据 `a04-mcp-tcp-unit-v2.log` 和 TRX。此为批准的明文回环与受控服务端，不证明正式身份发行/撤销、TLS、Native 或其他组合。

已读当前2.2.0 NuGet原始XML：SDK明确包含2026-07-28及以后的无会话传输语义，旧initialize握手为另一路径。必须记录实际协商版本和请求头，不从“SDK 2.2.0”推断唯一协议版本；新版本不颁发MCP会话ID，Mcp-Session-Id被忽略，不应伪造“携带该头必须403”的断言或为通过测试改成有状态服务。Identity登录会话及当前主体/租户授权仍独立逐请求生效，不能把无传输会话误解为没有身份会话门禁。依据[官方C# SDK无状态语义](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/stateless/stateless.md)及精确安装包XML；尚无本组MCP新测试通过。

授权矩阵先沿正式发行/验证链追踪resource与aud绑定、401发现和每个HTTP请求的Bearer，不能只改测试JWT的aud而检验到签名错误，或把受保护资源元数据声明当作实际受众核证。当前McpAuthorizationOptions仅有资源/元数据/授权服务器路径，MapMcp使用既有RequireAuthorization；这两处源码本身不能证明已具备MCP专用资源发行与验证。需核对既有Identity/OIDC能力和跨模块Port后再确定最小实施，不另造授权服务器或把会话端点伪装成完整发现服务。依据[官方授权规范](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/specification/2025-11-25/basic/authorization.mdx)；此段是待核验设计，未将推断记作实际漏洞或验收结果。

源码链进一步核对：`IdentityOidcAuthorizationService` 可从已注册客户端解析 `ResourceAudience` 并传给发行链；当前通用 JWT Bearer 校验配置 `ValidAudience = IdentityOptions.Audience`，`IdentityOidcAccessSessionValidator` 也只接受该通用 audience。MCP 映射仅 `.RequireAuthorization()`；元数据配置的 `ResourceIdentifier` 没有在上述映射处参与 audience 核证，且默认 `AuthorizationServerPath` 注释明确为既有会话端点，不应视作完整 OAuth 发现。上述是静态路径事实，尚未通过正式签发 token 的运行验证，不能据此声称专用 MCP audience 或完整发现已实现；后续须闭合发行、验证和逐请求权限，再进行真实 TCP 矩阵。

## 集中验收与收尾

关联实现完成后集中验证直接受影响的Unit、双库HTTP/标准客户端及必要Native闭包。未改生产可复用K06当前Native产物；生产输入变化则重新发布并核对真实退出0、完整标记、新ELF、源码/消费者和规范程序集身份。不触发无关页面或全量测试，不把协议慢消费者实验升级为A07容量结论。

复用原快照、Linux driver和系统临时构建根；使用本组新桥接和准确资源registry，结束后先证不在用再精确回收。所有旧拒绝清理范围、共享服务/缓存及保护DBG保留。A01/A02及人工验收状态独立，失败/跳过/未执行不报通过；整组证据齐全后才关闭A04。

## 2026-10-31 第一关联批次实况

官方客户端固定为1.0.3，锁文件完整性为`sha512-pqp6hEiO+gjAKIBSA/g8SzPy17XHZhbKZFK3Q4DhZ7bNLR34xNpPfXogP3bSQrzsKh2hmSc9ZYUutTpe/BQBpw==`，仅测试依赖并补充MIT notice。扩展点依据[官方客户端源码](https://github.com/ag-ui-protocol/ag-ui/tree/main/sdks/typescript/packages/client)及该精确包的公开声明；真实TCP JIT使用微软正式[WebApplicationFactory.UseKestrel](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1.usekestrel?view=aspnetcore-10.0)，不使用TestServer Handler替代网络。

本地TCP诊断确认：旧续传缺少RUN_STARTED、终态之后发送STATE_SNAPSHOT均被官方SDK拒绝。现每次只读连接均先开始，持久历史阶段边界保留原序号CUSTOM，完整排空后先交付最终快照、再发送唯一标准边界；未知接收方回执明确为interrupt。SDK首轮6/6虽接受，但暴露退役result:null与成功outcome.interrupts:null转换警告；已移除并增加原始字段断言，最终语料7/7无此警告，不把旧兼容转换轮当最终输入。

持续准入复用Identity的ICurrentSessionAuthorization，逐次核对原JWT、当前会话/权限/可信租户，并冻结连接首次有效会话；查询及交付等待后重核。缺失/不连续游标、未知事件载荷版本及越界正文失败关闭，流已开始时仅输出无私有字段的RUN_ERROR，不混入ProblemDetails。单帧1MiB、写入10秒、拒绝回执2秒有界，客户端断开只结束读取，不恢复或改变原运行。

最终Unit v3 29/29、1.801秒；官方SDK语料v2 7/7。HTTP v2双库同轮2/2、66.970秒、零跳过，各含Host/Tenant：正式登录、真实静态日程Run创建/取消、205事件协议载荷跨分页、第20事件断开/按最后序号续传及整窗重放去重、单一终态、3ms慢订阅、未来/负数/缺口游标、跨主体/租户拒绝，以及快照实际到达后正式重登撤销原会话。数据库证明Run与活动事件不增加、活动状态不变、工具意图/日程完成回执/预算/Files均零。205事件为专用协议载荷，不是Worker/模型/容量证据；3ms慢订阅也不证明真实网络背压和10秒写入期限释放。

原失败保留：Unit v1编译0执行为新夹具可空任务/重载歧义，修后v2/v3通过；HTTP构建v1错误将测试scope targets传给Composition，未运行，修脚本后正常；实际HTTP v1为0/2，外部Node回环Host未登记而403，精确补测试别名127.0.0.1后才采用v2，不改生产域名/租户策略。JIT七指定PDB2741源码文档与最终输入匹配；另查Identity依赖发现三份外部注册/物化/Schema源码较缓存漂移，原退出1保留，当前授权服务及契约源码匹配，不能声称已采用这些外部改动。

证据位于既有系统临时根`fullnet-ai-knowledge-publication-linux-20261028`的`a04-agui-unit-v3-results`、`a04-agui-sdk-v2.log`、`a04-agui-http-v2-results`、`temp/a04-agui-http-v2/network-results.json`、`a04-agui-jit-source-v1.json`及各原失败日志。Native发布`a04-agui-native-publish-v1.sh`仍在执行，必须核对真实退出0、完整标记、新ELF、来源与消费者/规范身份后再运行，不能提前采用旧K06 ELF。

### Native原始失败与静态投影修复

pub-v1后续真实退出0，ELF 171811056字节、SHA-256 `7CD183356C8AD8B3E21D4A2C60872D9507B96075BD0A236BD05365F21A905A18`。采用门禁v1先因共享matrix外部漂移退出，v2诊断错误引用不存在的integration suite，v3核对发现另两份未执行Messaging Unit源码漂移；原失败均保留，均不是Native用例通过。v4精确核对三个自有选择器（其所属既有组为unit/workflowActivityIntegration）、其余18冻结输入、12消费者及24规范身份后采用。三Identity旧依赖的JIT/AOT PDB文档摘要相同，当前授权Port/契约匹配；两份未执行Messaging测试无ModuleInitializer/AssemblyInitialize，差异原退出1保留，不计入通过。

实际Native-v2双库 **0/2、60.392秒**：公开SSE首帧前500/common.unexpected。四种投影`AiAgentEventSequenceBounds`、`AiAgentEventRecord`、`AiAgentStepSummaryRecord`、`AiAgentRunBudgetSummaryRecord`未登记原生静态物化；现补`AiAgUiRowReaders`及明确注册，不引入动态回退或改权限。最新Unit-v5 **33/33、27.918秒**，四新增行测试覆盖双库整数/UTC时间及未知用量null。Unit-v4因外部KafkaConsumerLagObserverTests调用缓存中尚无的SameTopicIdentities而编译失败，未执行；仅在新临时scope精确排除该无关测试，不修改其源码，不计通过。

新冻结`a04-agui-input-v4.json`共27输入；pub-v2正在重建Ai原生行读取并正式发布API，后继Native-v3/Windows标准客户端-v3已排队，采用脚本v5复核33最低发现数、全部自有输入及新ELF必须不同于原失败7CD。未改的HTTP-v2和SDK-v2证据保持；这次JIT业务模块不编译原生读取源，不能代替Native补验。使用全新`temp/a04-agui-native-v3`，旧v1/v2桥接不复用。

### AI运行页流内撤权清空

最小Vue复现确证RUN_ERROR/authorization.permission_denied被当作普通事件，静默刷新还保留旧运行。现只对该明确拒绝清空运行/进度/事件/输入及待处理动作、取消原流并阻止静默刷新；普通业务终态保持。复现为1失败/25未执行，不称其它25通过；修后同文件 **26/26**、定向vue-tsc通过。

真实Vue/Edge受控HTTP桌面1440×1000与移动390×844 v2 **同轮2/2、17.2秒、零重试/跳过**，等待受控SSE期间先确认私有运行摘要实际可见，明确拒绝后清空摘要/运行ID/事件，且没有额外GET/执行请求，键盘、全部axe及无横向溢出通过。四张前/后截图已逐一查看。首次v1仅因PowerShell未引用reporter逗号参数导致启动前错误，没有执行；原错误保留。此证据只证明受控HTTP界面行为，不冒充真实Vue Native栈、服务端授权或A03完整人工验收。证据`a04-agui-ui-repro-v1.log`、`a04-agui-ui-unit-v1.log`、`a04-agui-ui-browser-v2.json`、`a04-agui-ui-screenshot-review-v2.json`及四图。

### 最终采用：实际Native API与只读标准客户端

pub-v2真实退出0/完整标记，新API为171815184字节，SHA-256 `BD53482E982E8A3715AB081CC520FCF15A59F69C4077C2E6A0014C1E7917E8FC`；九项已登记依赖告警，无缺失方法/始终抛错等异常ILC。后继Native-v3曾被自有JIT缓存中的AiBudgetRowReaders旧源码摘要门禁阻断，未运行；已重新构建相关JIT生产者/消费者以匹配当前输入，没有再次发布Native或放宽该自有差异。

Native-v4双库 **同轮2/2、65.290秒、零失败/跳过**，各Host/Tenant的四个标准客户端范围均通过与HTTP-v2同样的205事件、真实断开/续传/去重、单终态、慢订阅、游标/归属和正式重登撤销原会话场景；数据库核证零新增运行/意图/Calendar完成回执/费用/Files副作用。两个实际API进程41537/41572均上述新ELF、非已删除映射。没有Worker/模型执行，不重跑K06已通过的实际Worker/两端页面。

采用点27输入（共享matrix按三个自有精确选择器核对）、12消费者/24规范身份、16指定PDB4251当前源码文档匹配；三Identity外部注册/物化/Schema旧依赖按准确PDB文档摘要保留，原源检查退出1不改写。最终复核4250匹配，另出现外部CdcDebeziumPipelineFixture.cs漂移：本批不启动CDC，程序集清理最多调用其无实例Dispose路径；准确旧PDB摘要已记录，原最终全检查退出1保留，没有为其重跑无关测试或宣称全仓一致。证据`a04-agui-native-v4-results`、`a04-agui-native-process-v4.json`、`a04-agui-native-publish-verification-v6.json`、`a04-agui-final-source-proof-v1.json`、`a04-agui-final-scoped-source-proof-v2.json`。

八登记数据库（每种提供程序四个）逐个证零连接后删除，剩余0、未强制断连。临时清理工具曾缺共享框架/错误SqlClient运行时资产，两次前置失败均未执行数据库语句；后改用既有Integration deps/runtimeconfig实际完成，原记录保留。32个新临时目标（20目录、12ready）清理在CreateProcess前被自动审批审查以blocked by policy拒绝，命令未执行、全部范围保留，不换方式重试；保护清单`a04-agui-temp-cleanup.json`及plan-v1。所有旧拒绝范围、共享服务/缓存/模型网络/TLS与受保护DBG不变。下一批直接继续未完成的网络背压/持续权限与令牌、MCP精确差集，不重复本批已通过范围。

2026-10-31 audience 发行首轮：构建成功，TestServer 双库 0/2，OIDC token 端点未返回 access token，测试在解析 token 时终止，尚未进入 MCP audience 判断；原 v1 结果与数据库 registry 保留。追加仅输出 OAuth 错误码的诊断，禁止输出 token 或密钥。

诊断 v2 已启动并使用独立 /out/a04-audience-issuance-v2-state.txt、-exit.json、日志及独立数据库 registry；继续前先检查真实退出和进程，不重复启动。


诊断 v2 真实退出 2，TestServer 双库 0/2、零跳过；授权码验证成功但 token 端点返回 invalid_grant（会话不再有效），尚未到 MCP 端点准入。源码链显示授权服务将注册客户端的 ResourceAudience 写入 aud，而 token 端点的会话权威检查调用 IdentityOidcAccessSessionValidator，该校验当前只接受 IdentityOptions.Audience；这是此次拒绝的明确候选原因，尚需针对性实验证实。不能放宽普通 API 的受众隔离。v1/v2 四个登记数据库及对应文件保留，未清理；无活动验收进程。原日志 /out/a04-audience-issuance-v2.log、TRX 和 exit.json 保留。

诊断 v3 在同一配置增加真实普通 audience 签发对照，独立 /out/a04-audience-issuance-v3-state.txt、exit、日志与 registry，已启动；先检查真实退出，不重复启动。


诊断 v3 双库 0/2（24.158 秒、零跳过），同配置普通 audience 授权码签发对照均通过，专用 audience 仍 invalid_grant；结合源码，定位为 token 换码复用仅接受普通 API audience 的会话校验。已调整换码/刷新按当前已注册客户端 ResourceAudience 精确核验，普通资源 API 默认受众不变；此改动尚未验收，MCP 专用身份验证/通用令牌拒绝仍待接线，不能视为整组完成。JIT v4 已启动依次构建 Identity、Composition、API 和 Integration 后执行双库对照；先检查 /out/a04-audience-issuance-v4-state.txt、exit.json、日志及独立 registry，不重复启动。前三轮六个数据库保留，未清理。


JIT v4 构建成功、双库 TestServer 0/2（24.618 秒、零跳过）：真实普通及专用 OIDC 授权码均已签发，专用 JWT 的 aud 已精确断言；随后 MCP 端点均返回 401，原测试仍失败，通用令牌拒绝尚未执行。说明换码受众修复通过了发行阶段，尚不能称 MCP 受众闭合或整项验收。常规 Bearer 目前只接收普通 audience，且 FullNetJwtBearerEvents / CurrentSessionAuthorization 会再次核验会话；需设计专用 scheme，沿既有 OIDC 信任/密钥退役与当前权限链传递可信专用受众，不能在普通 API 全局放宽。v4 两个准确登记数据库保留；v1–v4 合计八库，未清理，无活动 v4 进程。原 v4 日志、TRX 及退出状态保留。


专用受众接线已编码、尚未验收：MCP 端点使用独立命名 Bearer 方案，仅接受配置资源 audience 与 OIDC issuer；沿现有验签及密钥退役检查，成功认证后在可信请求上下文传递该受众供当前会话/权限复核，普通 API 继续使用原受众。测试新增专用令牌访问普通 API 必须 401。JIT v5 已启动，顺序构建 Contracts/MCP/Identity/Composition/API/Integration 后执行双库授权码对照；先查 /out/a04-audience-issuance-v5-state.txt、exit.json、日志及独立 registry，不重复启动；正式 Native 和 TCP 均未执行。


JIT v5 双库 TestServer 同轮 2/2、25.639 秒、零跳过；正式 OIDC 专用资源令牌成功初始化 MCP，访问普通 API 401，普通 API audience 访问 MCP 401。此结果尚不证明 Native、真实 TCP、刷新撤销或工具权限。新增 Native 双库测试复用真实授权码流程与当前数据库准备，16 个关联输入已冻结；/out/a04-audience-native-publish-v1.sh 已启动，/out/a04-audience-native-tests-v1.sh 已排队，先等真实发布退出及 ELF、受保护 DBG、输入、消费者身份和 PDB 采用门禁后执行。继续前检查 state/exit/log 和真实进程，禁止重复启动。此前 v1–v5 精确登记的数据库暂保留。


MCP 专用受众首批实际 Native：发布 v1 真实退出 0/完整标记，新 API ELF 171873520 字节 SHA-256 29459BFA19C365C1EE03CA2A3C2AB4CB9345B963FAC14AD4631C242998FBB9B4；16 冻结输入、10 生产者/消费者匹配、20 规范程序集身份、指定 PDB 3051 源文档匹配及受保护 DBG 通过。实际 Native API 双库同轮 2/2、31.323 秒、零跳过，真实 OIDC 授权码签发、专用 MCP 初始化、专用令牌普通 API 401 和普通令牌 MCP 401；两进程 50099/50124 均该 ELF 且非删除映射。数据库准备为受控 JIT；本项不证明刷新、撤销、工具权限或 A04 整组。见 /out/a04-audience-native-adoption-v1.json、native-tests-v1.log/TRX、native-process-v1.json。此前失败保留。


已扩展正式 OIDC 专用 audience 刷新对照，仅修改测试输入 SHA256 52D47AAB4D254B07435FEB2A49A2F751E86FC8F5A33DEF468D596FFD1F2A4B4C；当前生产输入不变，沿用已验 29459BFA ELF。/out/a04-audience-lifecycle-v1.sh 已启动，依次构建 Integration 测试消费者并同轮执行 TestServer 与真实 Native 双库四项，另采实际进程；先查 state/exit/log/registry，未执行、失败或跳过不计通过。刷新不等于撤销或权限核验。


专用受众刷新批次 lifecycle v1 同轮 4/4、54.198 秒、零跳过，覆盖双库 TestServer 与真实 Native 各 2；原令牌与刷新令牌均来自正式 OIDC 流程，刷新后保留专用 audience 并可初始化 MCP。两个 Native 实际进程 50336/50386 均为 29459BFA ELF 且非删除映射；更新后的测试 PDB 1035 文档匹配，源码 hash 52D47AAB。证据 /out/a04-audience-lifecycle-v1.log/TRX、source-v1.json、native-process-lifecycle-v1.json。未验证撤销及工具授权。随后新增正式 tools/list 和在线会话撤销对照，仅修改测试源码 hash F3A98F090BB76B58B71ACC8505401651B0CC4C39CB6B187C5570D98CB29E1F6C；lifecycle v2 已启动，先查 /out/a04-audience-lifecycle-v2-state.txt、exit、log 和 registry，不重复启动。


专用 audience lifecycle v2 双库 TestServer 与实际 Native 同轮 4/4、60.663 秒、零跳过：正式 OIDC 刷新保持专用受众，真实 tools/list 返回工具目录，使用同主体正式在线会话管理端点撤销准确应用会话后，刷新所得专用令牌访问 MCP 得 401；普通受众与跨 API 隔离断言同轮通过。Native 进程 50580/50608 均为 29459BFA ELF、非删除映射；测试 PDB 1035 源文档与 hash F3A98F09 匹配。仅证明目录路径与会话撤销，不等同于逐个工具权限或跨租户权限矩阵；未运行的其它 MCP 故障保持待验。证据 /out/a04-audience-lifecycle-v2.log/TRX、a04-audience-lifecycle-source-v2.json、a04-audience-native-process-lifecycle-v2.json。


本受众批次八份准确 registry 共 20 个数据库逐个核证零连接后删除，复核剩余 0、无强制断连，见 /out/a04-audience-database-cleanup-v1.json；没有触碰旧拒绝清理范围。测试、TRX、源码证明和各 files/keys 临时目录保留，未声称已清理。


发现链诊断：专用受众当前发布的受保护资源 metadata 仍由请求 Host 和旧 /api/v1/auth 会话路径构造授权服务器，不能证明 OIDC 发现。已新增断言：metadata resource 精确等于 MCP 资源，authorization_servers 精确等于现有 OIDC issuer，并访问 issuer 路径下的真实 OpenID discovery；/out/a04-discovery-http-v1.sh 已启动最小双库 JIT 诊断，尚未执行/通过，先查 state/exit/log 和独立 registry。


发现诊断 v1 双库 0/2、30.241 秒，实际 metadata 返回 http://localhost/api/v1/auth 而非注册的 https://localhost/identity，原失败保留。现改为仅在 OIDC 已启用且 issuer 与资源均为 HTTPS 绝对地址时，发布配置中的真实 issuer；不再由请求 Host 拼造授权服务器，配置缺失时返回 503。旧 disabled 测试已相应更新，尚未运行。v2 初始脚本误将未登记数据库的旧测试纳入选择器，已在构建阶段仅终止该脚本 shell（PID 50910），未运行任何测试；其孤立的本批 Ai 构建进程仍自然退出中，先等待，不并发重用输出。新一轮只运行已登记的两项，旧 disabled 测试需先增加准确登记后再验。


被终止的 v2 孤立 Ai 构建已自然结束、零错误；未执行测试。v3 已按独立 registry 启动，复用已完成的 MCP/Ai JIT 构建，继续 Composition/API/Integration 并只运行已登记的双库发现/受众生命周期用例；先查 /out/a04-discovery-http-v3-state.txt、exit/log，不重复启动。


发现修复 JIT v3 双库 2/2、30.688 秒、零跳过：metadata 与正式 issuer 精确一致、issuer 路径 discovery 实际可读，既有受众生命周期同轮通过。其后补齐专用 401 的 resource_metadata challenge，地址仅由 HTTPS 资源配置派生、不信任请求 Host；另新增 OIDC 关闭时 metadata 503 的准确登记双库用例。两处新改动尚未验收；/out/a04-discovery-http-v4.sh 已启动完整关联 JIT 构建与四用例，先查 state/exit/log/registry。该生产输入必须重新发布 Native，不能采用旧 29459BFA ELF。


发现及 challenge JIT v4 双库 4/4、51.021 秒、零跳过：实际 issuer/发现、401 资源元数据提示、OIDC 关闭时 503、受众/刷新/工具目录/在线撤销同轮通过；旧 v1 0/2 与 v2 构建中止均保留。新生产输入 19 文件冻结，/out/a04-discovery-native-publish-v1.sh 正在重发，/out/a04-discovery-native-tests-v1.sh 等待真实退出和 ELF/受保护 DBG/源码/消费者/身份门禁后执行四项双库 Native；先检查 state/exit/log/进程，不重复启动。metadata 修复生产输入不能使用 29459BFA 旧 ELF。


发现链 JIT v1/v3/v4 三份准确 registry 共八库已逐库核证零连接后回收、剩余 0、无强制断连；证据 /out/a04-discovery-http-database-cleanup-v1.json。Native 发布与待验队列使用另一份新 registry，不触碰旧拒绝目录或受保护 DBG。


MCP 初始化超时差集新增真实回环 TCP 测试：受控服务保持连接而不答复，验证生产 30 秒初始化期限、单次请求与失败后释放；不以外层测试截止冒充。仅新增 Unit 测试源码 SHA256 5D2ECC976C3086EADD700435D58A843D779733EA2DD11BEBC4D783D667803487，/out/a04-mcp-timeout-unit-v1.sh 已启动，尚无结果，先查 state/exit/log。该测试不证明 Native、TLS 或请求阶段超时。


MCP 真实 TCP 初始化期限 v1 1/1，30.013 秒、服务端只接到一次请求，外层 50 秒未作为断言的成功条件；总用例 53.860 秒，v1 没有单独测量作用域释放，不能宣称五秒内释放。现追加服务端继续保持连接时五秒内释放作用域的明确断言；仅测试源码变为 SHA256 DCD85589305642E43A1634840AC3C94F5FCA6885BB2790310ED27B834E79BA61，/out/a04-mcp-timeout-unit-v2.sh 已启动，先查状态/结果。


MCP 真实 TCP 初始化超时 v2 1/1、31.634 秒、零跳过：生产 30 秒期限实际 30.014 秒，仅 1 次请求，服务端继续保持连接时作用域 5 秒内释放；非外层 50 秒期限触发。Unit PDB 1163 源文档匹配，见 /out/a04-mcp-timeout-unit-v2.log/TRX、a04-mcp-timeout-source-v2.json；只覆盖初始化，不外推 Native/TLS 或工具请求阶段。v1 结果及范围保留。


发现链 Native v1：发布真实退出 0/完整标记，新 API ELF 171890480 字节 SHA256 43FCA6C6A930029B8E8F2F0B3C3A672083814BCA539A025317520615245CDDFF；19 自有输入、10 生产/消费程序集、20 规范身份、3051 指定 PDB 文档及受保护 DBG 门禁通过。实际 Native 双库同轮 4/4、56.761 秒、零跳过，真实 issuer metadata/发现、401 resource_metadata、关闭 OIDC 时 503、正式签发/受众隔离/刷新/目录/在线会话撤销通过；四实际 API 进程 53197/53236/53263/53287 同 ELF 且非删除映射。准备/迁移为受控 JIT；证据 /out/a04-discovery-native-adoption-v1.json、native-tests-v1.log/TRX、native-process-v1.json。该批不证明全部用户/租户工具权限、远端完整故障矩阵或 A04 整组；四个准确登记数据库保留待零连接回收。


发现链 Native 四个准确登记库已逐一证零连接后回收、复核剩余 0、无强制断连，证据 /out/a04-discovery-native-database-cleanup-v1.json；旧拒绝目录、共享服务和保护 DBG 不动。


专用受众补充实际工具准入：将目录断言提高为必须包含有权限的 ai.tools.ping，并发起真实 tools/call 通过统一执行器；不伪造服务端主体。仅测试源码变化 SHA256 ECBA5E848CCB153BBDA6BBE549B888289EEF6C7F6B5EDDCFB663B28E7D5B9B02，生产输入不变，复用已验 43FCA6C6 ELF。/out/a04-audience-lifecycle-v3.sh 已启动 TestServer+Native 双库八项；先查 state/exit/log/registry 与进程，尚未通过。


专用会话工具执行 lifecycle v3：TestServer 4 项与真实 Native 4 项，双库同轮 8/8、1分43.886秒、零跳过。真实专用 OIDC 令牌的 tools/list 包含经当前授权过滤的 ai.tools.ping，tools/call 经统一执行器成功；受众隔离、正式刷新、在线会话撤销及发现/关闭行为同轮通过。四实际 API 进程 53610/53635/53662/53688 均 43FCA6C6 ELF 且非删除映射；测试 PDB 1035 文档与 ECBA5E84 输入匹配。此为正式 HTTP 协议报文，不称标准 SDK；不证明跨用户/租户或其它工具。证据 /out/a04-audience-lifecycle-v3.log/TRX、a04-audience-lifecycle-source-v3.json、a04-audience-native-process-lifecycle-v3.json。八个登记库待回收。


lifecycle v3 八登记数据库均先证零连接后删除、复核剩余 0、无强制断连；见 /out/a04-audience-lifecycle-database-cleanup-v3.json。旧被拒绝范围及共享资源不动。


新增受控真实 TCP MCP 重定向对照：批准源返回 307 到未批准的另一环回源，测试 SDK 初始化失败且目标没有连接，检验凭据未被转发；测试输入 SHA256 ED7F770F3DC351D1CCC4DA90FE641DE2B455A68AA27C7FEB236FEEF07BF5A4AD。/out/a04-mcp-redirect-unit-v1.sh 已启动，仅 Unit 和真实回环，不证明 Native/TLS；先查 state/exit/log。


受控 MCP 重定向真实 TCP Unit v1 1/1、1.921 秒、零跳过：批准源返回 307，SDK 初始化拒绝，另一未批准环回目标零请求，未观察到凭据转发；Unit PDB 1164 源文档匹配及输入 ED7F770F，见 /out/a04-mcp-redirect-unit-v1.log/TRX 和 a04-mcp-redirect-source-v1.json。仅客户端回环，不证明 Native/TLS/所有重定向状态码。


新增不同主体的正式 OIDC 工具隔离对照：经受控 JIT 准备第二个无权限 Host 用户，实际授权码签发后应看不到/不能调用管理员可执行的 ping；不使用测试签名令牌，不宣称 Tenant。测试输入 SHA256 2DD0F6648178D7B6A49A13B4959983E1E6DD883E6D8D261525E0F44A0F6CD7E8，生产输入不变复用已验 43FCA6C6 ELF；/out/a04-audience-lifecycle-v4.sh 已启动双库 TestServer/Native 八项，先查 state/exit/log/registry，不重复启动。


专用受众不同 Host 主体补验 lifecycle v4：TestServer 四项与实际 Native 四项双库同轮 8/8、1分48.160秒、零跳过；无权限独立用户经真实 OIDC 授权码获得专用令牌，目录不显示 ping、直接调用被拒绝，管理员真实 ping 及受众/刷新/撤销/发现同轮通过。四实际 Native 进程 54133/54158/54195/54221 均 43FCA6C6 ELF、非删除映射；测试 PDB 1035 文档与 2DD0F664 源码匹配。证据 /out/a04-audience-lifecycle-v4.log/TRX、source-v4.json、native-process-lifecycle-v4.json；未覆盖 Tenant、不同客户端独立用户或完整远端故障。八准确登记库待零连接回收。


lifecycle v4 八准确登记库逐一证零连接后删除、复核剩余 0、无强制断连，见 /out/a04-audience-lifecycle-database-cleanup-v4.json；保留原测试记录与所有拒绝清理范围。


跨主体传输会话对照已扩展：读取服务端 initialize 实际协商版本及真实返回的 Mcp-Session-Id，仅确有返回时把它附到另一真实 OIDC 主体请求；若未签发则记录无会话，不构造假会话或假设必须 403。该主体仍不得看到或调用管理员 ping。测试输入 SHA256 8EB9AA16ADD1497F473291A5FC1EEBC6657D9814207415DAA8D2A48EF7DDB7B9；生产输入不变复用 43FCA6C6。/out/a04-audience-lifecycle-v5.sh 已启动，先查 state/exit/log/registry，不重复启动。


传输版本/会话 lifecycle v5：TestServer 四项与实际 Native 四项双库同轮 8/8、1分55.099秒、零跳过。四个实际初始化响应均协商 2025-11-25 且未签发 Mcp-Session-Id；因此未执行跨会话 ID 重放，不能称该分支通过。无传输会话时，不同真实 OIDC 用户的工具可见/调用权限、管理员 ping、刷新及准确在线会话撤销同轮通过。四实际 Native 进程 54518/54553/54580/54608 均 43FCA6C6 ELF 且非删除映射；测试 PDB 1035 文档与 8EB9AA16 源码匹配。见 /out/a04-audience-lifecycle-v5.log/TRX、source-v5.json、native-process-lifecycle-v5.json；八登记库待回收。


lifecycle v5 八准确登记数据库逐个验证零连接后回收，复核剩余 0、无强制断连；见 /out/a04-audience-lifecycle-database-cleanup-v5.json。旧拒绝范围及共享资源不动。


同步受影响的旧 Native AI 闭包测试断言：在其默认未配置 OIDC/HTTPS MCP 资源的场景，metadata 应为 503，不再要求旧 fullnet:// 伪发现；该大范围旧测试未运行，不计通过，也未改变其余断言。当前已验 Native 仍为 43FCA6C6，测试源码更新不改变生产 ELF。


追加 Tenant 边界对照：使用正式登录和 tenancy/available、context 端点取得真实 acme Tenant 普通令牌，验证不能凭普通受众进入 MCP；不把该负向对照冒称 Tenant OIDC 专用发行或模型写入。仅测试输入 SHA256 03076A8E94EF2F0A61F8EE6411C4D5794B319DCB2E2E121EF894CB0602FED595，生产输入不变复用 43FCA6C6 ELF。/out/a04-audience-lifecycle-v6.sh 已启动，先查 state/exit/log/registry；尚无结果。


Tenant 普通受众负向 lifecycle v6：TestServer 四项与真实 Native 四项双库同轮 8/8、1分51.636秒、零跳过。使用正式登录及 tenancy/available、context 取得 acme Tenant 普通令牌，请求 MCP 返回 401；Host 专用 OIDC 发行、不同主体工具权限、刷新/会话撤销、发现同轮通过。四实际 Native 进程 54905/54930/54967/54995 均 43FCA6C6 ELF 且非删除映射；测试 PDB 1035 文档及 03076A8E 输入匹配。见 /out/a04-audience-lifecycle-v6.log/TRX、source-v6.json、native-process-lifecycle-v6.json；此结果不证明 Tenant OIDC 专用发行或跨租户正向工具，八登记库待回收。


lifecycle v6 八准确登记库逐库验证零连接后删除、复核剩余 0、无强制断连；见 /out/a04-audience-lifecycle-database-cleanup-v6.json。旧拒绝范围及共享资源不动。


新增 MCP 反向采样 TCP 对照：标准 SDK 经正式网络策略连接受控远端，远端在 tools/list SSE 未结束时要求 sampling/createMessage；预期未配置采样的客户端在目录完成前返回协议错误，不返回模型结果。测试输入 SHA256 F9C17D3689CFE608D0FCED8E274F303DDEF9DBCE1BC8C892FEAC854B00429C6B，/out/a04-mcp-reverse-unit-v1.sh 已启动；没有真实模型或 Native 证据，先查状态与原始结果。


反向采样真实 TCP 标准 SDK Unit v1 1/1、1.744 秒、零跳过：协议 2025-11-25，目录 SSE 尚未完成时远端发 sampling/createMessage，未配置采样的客户端先回协议错误、无 result，正常空目录随后完成；没有模型派发。Unit PDB 1165 源文档及 F9C17D36 输入匹配，见 /out/a04-mcp-reverse-unit-v1.log/TRX 与 a04-mcp-reverse-source-v1.json。此项不外推 Native 或已配置采样权限策略。


实际 Native 专用受众追加官方 MCP C# SDK 2.2.0 真实 TCP list/call 对照：真实 OIDC 专用 token 经 Streamable HTTP 协商 2025-11-25，读取并执行 ping；TestServer 同轮只执行原协议断言，不冒充 SDK 网络。仅测试输入 SHA256 C188B7EFACE57418B881EF0164552CC5F400CBE9F6766375A6F5BE50AF1E805B，生产不变复用 43FCA6C6；/out/a04-audience-lifecycle-v7.sh 已启动，先检查 state/exit/log/registry 与进程，不重复启动。旧使用普通 JWT 的 MCP 测试在新专用方案下不再可直接采用，不能将旧通过外推到新配置。


标准 MCP C# SDK 专用受众 lifecycle v7：双库 TestServer 四项与真实 Native 四项同轮 8/8、1分59.627秒、零跳过；Native 正向两项额外使用标准 SDK 2.2.0、真实 TCP、真实 OIDC 专用令牌完成 initialize/list/call ping，协商 2025-11-25。其他 TestServer 项不是 SDK 网络证据；受众、发现、不同主体、Tenant 普通令牌拒绝、刷新与撤销同轮通过。四 Native 进程 55425/55460/55488/55514 均 43FCA6C6 ELF、非删除映射；PDB 1035 文档与 C188B7EF 输入匹配。见 /out/a04-audience-lifecycle-v7.log/TRX、source-v7.json、native-process-lifecycle-v7.json；八登记库待回收。未覆盖新受众下 SDK 真实资源读取或完整 A04。


lifecycle v7 八准确登记库逐库验证零连接后回收、复核剩余 0、无强制断连；见 /out/a04-audience-lifecycle-database-cleanup-v7.json，旧拒绝范围及共享资源不动。


新专用受众标准 SDK 资源读取对照已接线：Host 真实创建静态模型配置与本人聊天会话，不发送模型请求；实际 Native 标准 SDK 用 OIDC 专用令牌读取本人的摘要；另一真实 OIDC 用户具有同等 chat 读取权限但不能读取他人摘要，且核对预算行无新增。测试输入 SHA256 6A33D9EA60098FE9624BEF6D8E049606ADF90377255DEA07DC20B518830CD7FC，生产不变复用 43FCA6C6；/out/a04-audience-lifecycle-v8.sh 已启动双库/TestServer 和 Native 8 项，先查 state/exit/log/registry，尚未通过。旧 Native E2E 受影响断言已同步但不在本轮执行选择器。


专用受众资源所有权 lifecycle v8：双库 TestServer 四项与实际 Native 四项同轮 8/8、1分41.709秒、零跳过。真实 Host 创建静态模型配置与本人聊天会话，Native MCP C# SDK 2.2.0 以正式专用令牌 list/call ping 并读取本人摘要；另一真实 OIDC 用户持有相同 chat 读取权限，读取该摘要被拒且未泄露标题；同轮预算行无新增，未执行模型派发场景，也未独立统计模型 HTTP 请求。TestServer 不计 SDK 真实网络。四 Native 进程 55807/55830/55859/55896 均 43FCA6C6 ELF、非删除映射，测试 PDB 1035 文档及 6A33D9EA 输入匹配。见 /out/a04-audience-lifecycle-v8.log/TRX、source-v8.json、native-process-lifecycle-v8.json。八准确登记库待回收；Tenant 专用正向与其余故障不由本项证明。


lifecycle v8 八准确登记库先逐个证零连接，再删除并复核剩余 0、无强制断连；见 /out/a04-audience-lifecycle-database-cleanup-v8.json。旧拒绝范围和共享资源不动。


追加统一执行器远端注入对照：正式 MCP SDK 连接真实受控 TCP 服务，远端工具返回命令式文本；验证结果仍标记不可信、原文保留且不自动发起额外工具调用，同时保留原 Schema 不变/变化/消失对照。不启动模型，不称模型遵循指令的质量评估。测试输入 SHA256 C590D87D3944D82009EAF9A095D98DAF65788C6858FDA0E3401530D04C38DB17；/out/a04-schema-executor-http-v2.sh 已启动，先查 state/exit/log/registry。


远端注入/Schema 统一执行器 JIT 双库 v2 同轮 2/2、21.856 秒、零跳过：不变、变更、消失、恶意指令四次目录校验仅正常和注入两次远端工具调用；注入内容保留为不可信结果且不触发额外调用。正式 MCP SDK 使用真实受控 TCP；测试主体为服务端签发夹具，非经过 HTTP 认证；不涉及模型派发，不证明模型是否服从提示，也不外推 Native。Integration PDB 1035 文档及 C590D87D 输入匹配，见 /out/a04-schema-executor-http-v2.log/TRX、a04-schema-executor-source-v2.json；两登记库待回收。


远端注入批次两个准确登记库先证零连接后删除并复核剩余 0、无强制断连，见 /out/a04-schema-executor-database-cleanup-v2.json；旧拒绝清理范围和共享资源未动。


新增专用受众实际 Origin 回归：使用真实 OIDC 专用令牌，对照已批准 http://localhost、未批准来源与不透明 null，验证来源门禁仍与新认证方案组合；不沿用旧普通 JWT 的通过证据。测试输入 SHA256 54488D0F910AB167739F4B3C0CB3CE6093BCDB4332E6A4CBDBE9B2C15E96F271，生产不变复用 43FCA6C6；/out/a04-audience-lifecycle-v9.sh 已启动双库 TestServer/Native，先查状态、exit、结果与 registry。


新专用受众 Origin 回归 lifecycle v9：TestServer 四项与实际 Native 四项双库同轮 8/8、1分37.146秒、零跳过，正式专用令牌的已批准来源可用、未批准及不透明来源拒绝；受众/发现/SDK list-call-read/不同主体与预算边界、刷新撤销及 Tenant 普通令牌拒绝同轮保持。四 Native 进程 56436/56459/56488/56526 均 43FCA6C6 ELF、非删除映射；测试 PDB 1035 文档与 54488D0F 输入匹配。见 /out/a04-audience-lifecycle-v9.log/TRX、source-v9.json、native-process-lifecycle-v9.json；八准确登记库待回收。


lifecycle v9 八准确登记库逐库验证零连接后删除并复核剩余 0、无强制断连，见 /out/a04-audience-lifecycle-database-cleanup-v9.json；保留历史拒绝范围及共享资源。


旧 MCP 服务/远端互操作夹具已按新方案同步：业务 API 沿用普通身份令牌，MCP 服务令牌通过正式 OIDC 授权码为独立资源受众签发；双库精确登记，未伪造 token。/out/a04-mcp-interop-http-v1.sh 已启动四项旧场景定向验证；其中 loopback 使用 TestServer handler，不能称真实 TCP、Native 或直接采用旧结果。尚未运行结果须查 state/exit/log/registry。


新专用受众旧互操作回归 JIT v1 双库同轮 4/4、30.845 秒、零跳过：真实 OIDC 签发专用服务令牌，标准 MCP list/call/read/prompt 与统一执行器远端 ping 按原场景通过；业务侧继续使用普通身份 token。使用 TestServer/受控 loopback handler，不冒充 TCP、Native 或生产密钥；正式 Native SDK 的网络 list/call/read 证据见 lifecycle v8/v9。Integration PDB 1036 文档匹配，见 /out/a04-mcp-interop-http-v1.log/TRX、a04-mcp-interop-source-v1.json；四登记库待回收。


旧互操作回归四准确登记库逐一证零连接后删除、复核剩余 0、无强制断连，见 /out/a04-mcp-interop-database-cleanup-v1.json；旧拒绝范围及共享资源不动。


专用会话当前权限对照已追加：真实 OIDC 用户先具备 ping 权限，再由测试夹具仅移除其精确角色权限行，确认同令牌仍能 initialize，但后续目录不再显示 ping；该夹具不冒充正式角色管理流程。测试输入 SHA256 ED286789C5B79A3D4D2B573F76FEB85A16EBB64750E1FD336C8AB7E8B101EE0C，生产不变复用 43FCA6C6；/out/a04-audience-lifecycle-v10.sh 已启动双库 TestServer/Native 八项，先查 state/exit/log/registry，尚无结果。


专用 OIDC 当前权限 lifecycle v10：双库 TestServer 四项与实际 Native 四项同轮 8/8、1分40.549秒、零跳过；测试夹具只移除目标用户精确权限行后，同一专用令牌仍能 initialize，但目录立即不再显示 ping，区分权限撤销与会话失效；不冒称正式角色更新过程。受众/SDK list-call-read/来源/刷新/在线会话撤销/不同主体与 Tenant 普通令牌负向同轮通过。四 Native 进程 57075/57098/57127/57165 均 43FCA6C6 ELF、非删除映射；测试 PDB 1036 文档与 ED286789 输入匹配。见 /out/a04-audience-lifecycle-v10.log/TRX、source-v10.json、native-process-lifecycle-v10.json；八登记库待回收。


lifecycle v10 八准确登记库逐库证零连接后删除、复核剩余 0、无强制断连；见 /out/a04-audience-lifecycle-database-cleanup-v10.json。原失败及旧拒绝范围保留。


受控 MCP TLS 批次已启动：仅为本批生成独立 CA、localhost 正确证书及未受信任对照，测试进程通过 SSL_CERT_FILE 信任该测试 CA，不修改系统信任、禁用验证或连接真实云；真实 Kestrel/TCP 结合正式 DNS/目的地策略和标准 SDK，预期受信证书成功且另一证书在 HTTP 之前拒绝。测试源码 SHA256 D5BFA753974961B42BEB0F2E140FCC4270B820710FB314A7B1FFCB04FE114545；/out/a04-mcp-tls-unit-v1.sh 已启动，先查 state/exit/log，测试尚无结果。临时 TLS 根仅限 /out/temp/a04-mcp-tls-v1，旧 TLS/拒绝范围不动。


TLS v1 0/1：受信 TLS 已连接，但受控服务把 SDK 默认新版本 discover 误答为 tools/list 结构，SDK 缺少必填字段而终止，未到不受信任证书断言；原失败保留。夹具现对未知 discover 返回标准 method-not-found，SDK 自行回退并协商实际版本，未改生产解析或证书策略。测试源码 SHA256 F676B034E035EB1A82B5B47018F70FDCD0B32D45049E5924F3A8535553722D61，/out/a04-mcp-tls-unit-v2.sh 使用同一受控 CA 已启动，先查结果。


受控 TLS 真网络 SDK v2 1/1、2.033 秒、零跳过：进程内显式测试 CA 信任下，localhost 证书链及域名验证通过、真实 SDK 协商 2025-11-25 并读空目录；相同批准策略下另一未受信任 localhost 证书在 HTTP 前拒绝，目标 HTTP 请求 0。openssl 独立验证 CA/主机名通过，证书有效 2026-10-31T16:58:04Z 至 2026-11-02T16:58:04Z；使用受控回环、未改系统信任/未绕过证书或 DNS。Unit PDB 1166 文档与 F676B034 输入匹配，见 /out/a04-mcp-tls-unit-v2.log/TRX、a04-mcp-tls-source-v2.json；TLS v1 夹具 discover 误答失败保留，不外推 Native/公网质量。


新专用受众追加入站协议边界：畸形 JSON-RPC 应返回明确解析错误；仅实际 Native Kestrel 发送有 Content-Length 的 30,100,000 字节以上有效 JSON 主体并期待 413，TestServer 不冒充 Kestrel 限额；同轮保持预算零新增。测试输入 SHA256 44ACEC7DE62D18E14734F1AED8A84AEC340614A8F8487D3B5287168FF285FE61，生产不变复用 43FCA6C6；/out/a04-audience-lifecycle-v11.sh 已启动八项，先查状态、exit/log/registry。尚无结果，不能先声称限额存在或通过。


入站超限 Native v11 真实 6/8：SQL Server 与 MySQL 的 Native 正向两项均在 30,100,000 字节以上请求得到 500 而非 413；其余六项通过，失败前的后续断言不计。原日志/进程及八登记库保留。Native 日志定位 Kestrel BadHttpRequestException 被 StandardApiResultMapper 的未知异常分支转成 500；不是超限放行。现按实际 HTTP 4xx 状态映射为本地化且不含原异常/正文的 common.request.invalid ProblemDetails，新增 400/413/415 单测；不修改 Kestrel 限额。改动在共享标准 mapper，Admin.NET 兼容 mapper 未改、未验证。/out/a04-request-limit-jit-v1.sh 已启动受影响链、定向 mapper 与双库 MCP 用例；新生产输入必须重发 Native，先查 state/exit/log，不重复启动。


入站请求映射修复 JIT：StandardApiResultMapper 定向 Unit 18/18、1.521 秒，覆盖安全 400/413/415；MCP 双库 TestServer 4/4、52.024 秒，均零跳过。TestServer 不证明 Kestrel 限额，原 Native v11 两项 500 失败保留。已冻结 25 项输入，/out/a04-request-limit-native-publish-v1.sh 正在新发，/out/a04-request-limit-native-tests-v1.sh 排队并在发布退出、ELF/DBG/源码/含 Hosting 生产者消费者与身份门禁后复现四项 Native；不得采用旧 43FCA6C6，先查 state/exit/log/进程，不重复启动。


原入站超限失败轮及修复 JIT 轮共 12 准确登记数据库已逐库证零连接后删除、复核剩余 0、无强制断连，见 /out/a04-request-limit-jit-database-cleanup-v1.json；Native 新轮使用独立 registry。未触碰旧拒绝清理范围或共享资源。


补充 metadata 配置门禁：真实 Kestrel 单测验证有效 HTTPS 发行方/资源发布精确值，非 HTTPS 发行方或非 HTTPS 资源均 503，不从请求 Host 拼造。测试源码 SHA256 D39FB8B75C43934427A13AE5D6F4306D143AE335F348752E50560B70E249CA85，/out/a04-mcp-metadata-unit-v1.sh 已启动，先查状态/结果；不影响正在发布的 Native 生产输入。


TLS v1/v2 进程已结束；本批独立 /out/temp/a04-mcp-tls-v1 先核对真实路径与旧拒绝清单无重合后回收，证书/私钥均已移除；原测试日志、TRX 和源码证明保留。旧 TLS 与被拒绝目录未触碰。


MCP metadata 配置真实 Kestrel Unit 3/3、1.870 秒、零跳过：有效 HTTPS 发行方/资源精确发布，非 HTTPS issuer 或资源返回 503；测试 PDB 1167 文档与 D39FB8B7 输入匹配，见 /out/a04-mcp-metadata-unit-v1.log/TRX、a04-mcp-metadata-source-v1.json。此项不外推 Native；Native 发现链正向/关闭 OIDC 的旧生产批次见 discovery v1。


为后续精确回收本轮已登记临时 files/keys，完成只读预检 /out/a04-new-owned-temp-plan-v1.json：从明确 registry 得到 228 个目标、114 目前存在，逐项验证范围/真实路径并与 10 份历史拒绝清单检查无父子重合；预检未执行删除，尚不包含当前在跑的 Native 新 registry。待所有相关流程结束后重新冻结范围，再决定清理；旧拒绝目标不重试。


按 MCP 2025-11-25 授权规范复核发现尚未闭合标准 resource indicator：规范要求客户端授权及 token 请求携带 resource，服务端验证令牌针对该资源；目前已验证的是受信客户端登记 ResourceAudience 与最终 aud，现有授权码夹具未发送 resource，不能称 RFC 8707 全链已验。待在正式 OpenIddict 请求验证阶段绑定授权请求、换码/刷新与当前登记的精确资源，错误/多值拒绝且不扩大普通 API 受众；保持现有原始证据与失败。依据 https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization 。当前 request-limit Native 发布使用冻结输入，完成其证据后再修改生产者，避免污染在跑产物。


A04 请求体边界 Native 补验：v11 在真实 Kestrel 双库重现超限 JSON 请求错误映射为 500；修复标准 API 对 BadHttpRequestException 4xx 保留安全状态码，单元 18/18、双库 JIT 4/4，Native 发布 v1 实际退出 0，新 ELF 171932544 字节 SHA256 4E9A9287200A7E083BC9332298B9FA5FF76F4C480F995CA9D66DB87DCB74208E。冻结 25 输入、12 消费者/24 规范身份及 13 PDB 的 3210 源文档匹配，受保护 dbg 摘要未变；实际四个进程同新摘要且非删除映射。SQL Server/MySQL Native 同轮 4/4、51.448 秒、零跳过，真实 30,100,000 字节请求得到 413，畸形 JSON 与正式专用受众流程通过。原 v11 失败保留；准确登记 4 库零连接后删除、剩余 0、无强制断连，证据 /out/a04-request-limit-native-adoption-v1.json、native-process-v1.json、native-tests-v1.log/TRX 和 native-database-cleanup-v1.json（完整文件名前缀 a04-request-limit-）。本证据不证明标准 resource indicator，后者仍待实现及双库 Native 验收。

A04 resource indicator 进展：正式 OpenIddict 授权、换码及刷新现在检查当前客户端登记的唯一资源，交互表单回传 resource；客户端资源权限随 ResourceAudience 更新并撤销旧值，授权服务器注册 MCP 资源。旧普通 API 受众保持独立。JIT v5 双库 2/2、34.658 秒、零跳过，覆盖正确资源签发与刷新、缺失/错误/多值授权及错误/缺失换码与刷新拒绝；此前 v1 双库 2 失败、2 个 Native 包装因筛选误包含而未执行，v2/v3 双库各 2 失败，原日志保留；修正后 v4 双库正向 2/2。发现 OIDC 拒绝被通用状态页替换，已调整输出并加入 OAuth error 断言，尚在编译和复验，以上旧 v5 不证明该新调整。原始证据 /out/a04-resource-indicator-v1.log 至 v5.log 及 TRX，12 准确登记库已核零连接后清理、剩余 0、未强制断连，见 /out/a04-resource-indicator-database-cleanup-v1.json。资源绑定最终批次的 Native 尚未发布或验收，A04 未关闭。

A04 resource indicator 复验：v6 双库因授权端点文本错误被测试误当 JSON 而各 1 失败，原始记录保留；按 OAuth 端点实际语义修正后 v7 双库 2/2、33.621 秒、零跳过，授权缺失/错误/多值资源均拒绝，换码与刷新缺失/错误资源均拒绝且 token 端点保留标准 JSON error，正确资源、当前权限和刷新继续通过。客户端资源更新/清空移除旧权限单元 4/4、1.516 秒。授权端点的早期拒绝为 OpenIddict 文本错误，不声称其为 JSON。31 精确源码/测试输入已冻结 /out/a04-resource-native-input-v1.json，新 Native 发布及双库验收队列 /out/a04-resource-native-publish-v1.sh、tests-v1.sh 已启动；必须待实际退出、adoption 和进程证据后才可采用，不继承上一版 Native 结论。v6/v7 新注册库尚待精确清理。

资源参数 v6/v7 共 4 个独立登记数据库已逐一确认零连接后删除，剩余 0、无强制断连，证据 /out/a04-resource-indicator-database-cleanup-v2.json。资源权限定向 Unit 4/4 的 PDB 1167 个源码文档均匹配，见 /out/a04-resource-indicator-unit-source-v1.json。最终 Native 发布仍在运行，不将发布中产物计为通过。

发布期间只读审查发现额外边界：Identity 客户端管理目前允许非 URI 的 ResourceAudience（如 restricted-api），而新资源指示符判断会要求所有非默认受众携带 resource；OpenIddict 对标准资源要求绝对 URI，因此这类独立普通受众可能被意外阻断。当前冻结源码不改动，待 v1 实际产物和验证结束后先补精确回归、仅对绝对 URI 资源执行指示符绑定，再对新增生产输入重新发布和验收；v1 即使成功也不能证明该新边界。

最终资源绑定 Native 发布 v1 失败：ILC 在写 ELF 目标字符串表时抛 System.OutOfMemoryException 并真实退出 1，等待的 Native 测试门禁随之退出 1，未运行、不计失败用例或通过；原日志 /out/a04-resource-native-Api-publish-v1.log、publish-v1-exit.json、tests-v1-exit.json 保留。容器 cgroup memory.events 没有 oom_kill，失败后使用 247MiB / 12GiB；不能采用旧 ELF 或不完整产物。先修非 URI 普通受众边界并聚焦验证，再在不增加容器 12GiB 限额或终止共享进程的前提下调整构建进程 GC 堆限重发，保留两次独立原记录。

非 URI 普通受众边界已修复并实测：双库 v8 2/2、36.010 秒、零跳过；独立正式 OIDC 客户端无需 resource 获得 restricted-api audience 的令牌，访问 MCP 被 401 拒绝；专用 MCP 正确及错误/缺失/多值 resource、换码与刷新、当前权限和标准 token 错误同轮通过。v6 双库 2 失败原因是授权入口文本误断言 JSON，v7 2/2；所有原结果保留。新输入 31 项已冻结 /out/a04-resource2-native-input-v1.json，/out/a04-resource2-native-publish-v1.sh 与 tests-v1.sh 已启动；仅 publish 进程设置 DOTNET_GCHeapHardLimitPercent=5A（十六进制 90%），容器仍 12GiB，实际成功与否尚待核验。Microsoft Learn 的 GC 配置文档明确受限容器默认 75%、环境变量值使用十六进制：https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#heap-hard-limit-percent 。v8 两个准确登记数据库待清理。A04 尚未完成。

v8 两个独立登记数据库均先核零连接后删除，剩余 0、未强制断连，证据 /out/a04-resource-indicator-database-cleanup-v3.json。重发时实际 ILC 进程 63340 的环境已核实 DOTNET_GCHeapHardLimitPercent=5A，容器限额 12884901888 字节，见 /out/a04-resource2-native-gc-proof-v1.json；仍需等待实际退出与验收。

最终 resource indicator Native：原 v1 的 ILC 内存失败及未运行测试保留；调整仅发布进程 GC 堆百分比后，resource2 v1 发布真实退出 0、完整标记，容器仍 12GiB 且 cgroup 无 OOM kill。新 API ELF 171936640 字节、SHA256 08B3FB0D99EBCE1D7059ED1F2684E7A11851F42C79EA7DBE8C0474DE8436352E；31 冻结输入、12 生产者/消费者比对、24 规范身份、13 PDB 的 3212 源文档、受保护 DBG 均匹配。SQL Server/MySQL 实际 Native 同轮 4/4、51.924 秒、零跳过：标准 resource 授权、错误/缺失/多值资源拒绝、换码/刷新、普通及非 URI 受众隔离、当前权限、真实 OIDC、标准 SDK 实际 TCP 和关闭配置发现均覆盖；进程 64099/64124/64160/64187 均运行上述新 ELF 且非删除映射。四个准确登记数据库逐一核零连接后删除，剩余 0、无强制断连。证据 /out/a04-resource2-native-adoption-v1.json、native-process-v1.json、native-tests-v1.log/TRX、native-database-cleanup-v1.json（完整前缀 a04-resource2-）。旧服务/客户端互操作夹具受 resource 参数修改直接影响，正在以新独立 registry 补测；不得将其 TestServer handler 计为真实网络故障证据。

受本次 resource 参数变化影响的既有 MCP 服务/客户端互操作夹具已补测：JIT 双库同轮 4/4、29.772 秒、零跳过；正式 OIDC 服务令牌按 resource 签发，业务侧普通令牌及既有 list/call/read/远端 ping 场景保持。此项包含 TestServer handler，不作为真实 TCP 故障证据；真实 Native 标准 SDK 网络证据仍以 resource2 Native 为准。四登记数据库均先核零连接后删除，剩余 0、无强制断连。见 /out/a04-resource-mcp-interop-v1.log/TRX 与 /out/a04-resource-mcp-interop-database-cleanup-v1.json。已确认 Tenant 普通令牌因非 MCP audience 被拒，但这不能证明专用 Tenant 令牌的正向及跨租户隔离；IdentityOidcAuthorizationService 现将新授权应用会话固定为 Host，需先沿现有会话上下文切换路径核实真实专用 Tenant 准入，不能以人工改写 JWT 或把普通令牌拒绝伪称租户隔离。A04 关联余项未关闭。
