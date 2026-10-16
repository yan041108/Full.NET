# 知识库目录、文件版本、用户与角色授权及模型处理审批

## DOCX 独立解析工具（R08d）

`src/Tools/Full.NET.Ai.DocxParser` 是独立进程工具。R08d 交付正文解析子集，R08g 增加受信隔离启动、原会话规划及内部单批执行，R08h 增加完整代次、原子激活和完成恢复；R08i 接入上传、可信配置、Worker、当前读取者证明及逻辑引用。运行时索引默认关闭，须显式启用。

工具不接受命令行参数，从标准输入读取完整 DOCX 字节，成功时向标准输出写单个 JSON；失败返回退出码 2、标准错误固定 `fn_ai_docx_*` 机器码且无部分解析结果。调用方取消继续传播，输入和输出流归调用方所有。协议为 `protocolVersion=1`、`parserVersion=docx-ooxml-body-v1`，包含原始字节小写 SHA-256、字节大小、正文 `text` 与 `blocks`。区间按 UTF-16 计数，结构分隔符不计入块长度；段落序号从 1 开始，块序号从 0 开始。表格单元格保留表/行/列序号、首段序号、段数和表头标志；段落用换行、单元格用制表符分隔，不生成页码。

只支持普通正文段落、文本/制表符/换行及基本表格，超链接仅提取可见文字。宏、嵌入对象、字段、修订、隐藏文字、自动编号、页眉页脚/脚注尾注、嵌套或合并表格等未支持内容拒绝解析；样式等 XML 部件按内容类型和扩展名预检，部件名比较遵守大小写等价，DTD、外部实体和 XML 网络解析禁用。不执行 Office、不获取外链、不将 ZIP 解压到文件系统，也不演绎 Word 排版或 OCR。

输入上限 5 MiB；ZIP 最多 256 项，单项实际解压不超过 8 MiB、合计不超过 16 MiB，内容类型与根关系各不超过 256 KiB。XML 深度不超过 32、每部件节点与属性合计不超过 65536、单节点属性不超过 64；文本和属性值总量、最终正文分别不超过 2 Mi UTF-16 字符。段落最多 8192、表格 128、表格行合计 4096、单元格合计 8192、每行 64 格，块最多 8192，完整 JSON 最多 16 MiB。预算依据实际读取量，不以 ZIP 声明大小替代验证。

Windows/JIT 与 Linux/AOT 依赖分别锁定在工具目录的 `packages.lock.json` 和 `linux.packages.lock.json`。Linux 先以 `dotnet restore` 对项目执行 `-r linux-x64 -p:PublishAot=true -p:NuGetLockFilePath=linux.packages.lock.json --locked-mode`，再以同一属性执行 `dotnet publish -c Release --no-restore`；SDK/编译器图变化须显式更新锁文件并重验。镜像使用该工具目录的 `Dockerfile` 和包含原生二进制及 `licenses/` 的发布目录构建。镜像以 UID/GID 65532 运行，入口强制 30 秒超时。消费方必须另外设置无网络、只读文件系统、禁用 capabilities/提权、进程数量和内存/CPU 约束；本地原生验证使用 `--network none --read-only --cap-drop ALL --security-opt no-new-privileges --pids-limit 32 --memory 256m --cpus 1`。这些约束不会由直接执行 CLI 自动提供，接入 Worker 时仍需验证可信启动与取消清理。

R08e 增加模块内的完整输出验收、模型分块和批次计划算法。验收要求可信原始摘要及大小、固定解析版本、精确且不重复的全部字段、有效 Unicode、连续完整的正文区间和段落/表行列坐标；空段落仍保留，单元格声明的段落数须有足够内部结构换行。任何未验收或部分结果均不能进入规划。验收只校验协议自洽，实际来源授权、原字节绑定、可信工具身份、进程隔离与退出清理仍由后续运行器证明。

分块使用精确获批且作用域匹配的官方模型配置及真实 `cl100k` 词表，在单个段落或单元格内形成普通文本窗口，保留全局 UTF-16 位置，不跨逻辑块重叠或解释文档指令。空白块不生成模型输入，但完整文档仍保留它们及其坐标。低层分块的临时 UTF-8 摘要只绑定本地文本片段，完整 DOCX 的原始摘要、大小和坐标由外层冻结文档独立保存。全局块数、重复字符和 Token 预算不会因换段落或单元格重置。

内部 `knowledge-docx-plan-v1` 仅接收默认规划预算和完整非空逻辑块覆盖；逐块复核原文切片、代理对边界和实际 Token，再冻结有界批次。全文及全部坐标、原始来源摘要/大小、知识库/模型/租户精确版本、词表、算法预算和批次内容共同进入稳定摘要。计划没有费用、向量保存或 ready 证明。实际原生工具输出已在本地串联到这些 JIT 算法，不把它报告为 Native Worker DOCX 索引已完成。

R08f 增加内部持久化组件。`knowledge-docx-manifest-v1` 最多 8 MiB，完整保存一次正文、所有空白及非空逻辑块、模型与来源身份；恢复时重建原计划并核对真实 Token 和摘要。`knowledge-docx-vector-batch-v1` 单批最多 512 KiB，保存指定批次的原文范围、原逻辑块与小端 IEEE 向量，保持负零等有限值。两个格式中的 GUID 均采用规范网络字节序。载荷摘要来自实际捕获的原字节，未知格式、截断、尾部、坏编码、错批次和不合法向量均拒绝恢复。

保存及读取每次复核当前可信租户、原入队会话的五项精确权限、来源摘要及大小、模型批准和有效租约。向量还须关联同批请求、完整清单和成功结算的操作账本；唯一写入竞争只恢复完全相同的原载荷，取消或到期在短事务内回滚。全代次证明复核所有批次与前后实际字节、账本回执，它是瞬时证据，后续激活仍须独立事务重验。受控双库测试不代表真实云模型派发或收费。

迁移 `255_AiKnowledgeDocxPersistence.sql` 在双库新增 AI 自有 `fn_ai_knowledge_docx_manifest`，扩展向量格式守卫并保留既有文本/PDF载荷；新守卫必须校验并启用后才移除旧守卫，支持未记账及说明缺失重放。回退应用保留表和扩展格式；已有 DOCX 行时不能自动收缩旧约束或删除载荷。

既有删除及退役扫描也纳入 DOCX 清单，能够发现尚无向量的单独清单。派生数据删除沿用同一任务锁、租户、资源归属及后继代次守卫；保留任务、原会话和费用账本。

R08g 的私有执行链只接受宿主可信 Node/CLI 绝对路径及完整不可变镜像 ID。每份文件在无网络、无挂载、无凭据、只读非 root 容器中执行，512 MiB/零 swap/2 CPU/32 PID，并有独立硬时限。CLI `framed-v1` 按权威长度读取正文，保持 stdin 至完整退出；EOF、额外字节或管道错误表示取消。只有完整输出、退出 0、零 stderr、归属核对、确认停止和删除全部通过才交付逻辑文档。失败不返回部分或完整正文。

原会话规划在 25 秒内取得可信 Files 流，验证精确 MIME/大小/原始 SHA、释放流、隔离解析并生成完整获批模型计划。Host/Tenant 均经 Files/Identity 最小 Port，来源、原会话五项权限、租约、批准模型与目的地/组织指纹在等待之后复核。后续模型复核不再次打开正文。清单和向量组件只对事务外读取使用有界取消边界；写事务保留原调用者完整等待，不在后台放弃后提交。

内部单批消费者先冻结清单再预留预算，仅一次新预留允许模型派发；失败和取消沿原范围独立结算，从可能计费的调用开始未知用量不得推断为零。当前已结算且原载荷完整的单批直接恢复，不打开原文或重复调用模型。丢失向量、未知操作或不同请求不得免费重发；单批成功不代表整代次 ready。恢复末次权限等待也核对完整目的地和分类投影，精确版本相同不能隐藏漂移。

R08h 的私有整代次消费者按完整清单执行全部批次，总派发期限 15 分钟、每阶段 25 秒；原会话续租中纯读取使用独立有界清理，续租命令和本地事务必须直接等待完成或回滚。已收费操作沿原范围独立结算；缺失载荷的未知/既有操作不免费重派。全部实际向量、成功结算回执及当前获批目的地复核后，在短 AI 本地事务内共同提交完成态与活动指针。已完成原租约重放重新验证原会话五项权限、实际完整证明、当前来源和指针，不打开正文或增加费用。最后授权等待之后再以短事务严格复核来源、模型目的地、原绑定及活动指针；向量/结算证明对应前一个完整锁事务，不声称 Identity 与 AI 联合原子。

同一次完整证明内复用从捕获原字节重建的不可变完整计划；每次清单查询仍复制当前字节并复算 SHA，授权、来源和模型继续新鲜读取，匹配实际字节后才复用。Windows x64、i7-12700H、约 64 GB、SDK 10.0.401，测试进程 2 个处理器、GC 堆 2 GiB，同一受控查询夹具先后各 3 次预热、20 次采样：2048 个短正文块/128 批完整证明 p50 从 2050 ms 到 230 ms、p95 从 2605 ms 到 296 ms、p99 从 2792 ms 到 300 ms；授权调用仍为每样本 3250 次、向量读取 256 次、回执读取 2 次。此证据仅为受控查询的完整证明成本，不含真实数据库/网络，未覆盖最大正文字符数、最大 Token 或全部空白块布局。

R08i 的配置节为 `FullNet:Ai:KnowledgeDocx`：`Enabled` 默认 `false`；启用时设置可信 `NodeExecutablePath`、`CliScriptPath` 绝对路径和完整不可变 `ImageId=sha256:…`。API 只注册完成读取证明，Worker 注册隔离规划和付费消费者；Worker 还校验本机文件存在。API 与 Worker 使用一致的部署启用策略，格式开关只决定新入队、人工重试和扫描，不使既有完整代次失去当前读取资格。

上传接受 `.docx` 与规范 MIME `application/vnd.openxmlformats-officedocument.wordprocessingml.document`，声明和实际流均限制 5 MiB，先验证有界 ZIP 文件头；完整 OOXML 安全验收在隔离解析阶段执行。上传完成只表示原文件和不变版本已经保存，不承诺索引已就绪。Vue 支持 DOCX 选择和原文件下载，原文不在浏览器中执行或转换成 HTML。

读取、问答和历史独立验证当前读取者权限及知识库、文档两层关系，并锁读实际完整清单、付费回执和向量；原索引写入人离线或失去派发权限不影响已完成数据的合法读取。模型仍只能选择服务器引用标识。DOCX 引用增加可选 `docxLocation`，包含逻辑块、段落及完整表/行/单元格坐标，`pageNumber` 为 null；既有 TXT/PDF 引用字段保持兼容。含 DOCX 依赖的持久化答案使用 lineage v2，拒答或未引用的来源也保留完整依赖；旧记录沿用 v1，未知版本与畸形字段失败关闭。

墓碑清理删除活动指针及派生清单/向量；退休清理在当前后继完整证明的同一事务内删除旧载荷，错误归属、损坏后继和取消不能提交删除。费用、原会话、任务与原文件不随派生数据清理删除。取消时已开始的 DOCX 本地证明事务必须结束，外层清理期限不能使其在后台迟到提交。本批验证范围以活动计划 R08i 的实际结果为准；最大正文的真实数据库恢复成本及容量保持 `Capacity-not-verified`。

R04a 提供私有目录及审批记录，R04b1 增加明确用户成员与立即撤权，R04b2a 提供文档草稿元数据及独立明确用户授权，R04b2b 接入上传、不可变来源版本及持久化导入恢复，R04b2c 提供共享生成客户端，R04b3a 增加角色授权候选查询，R04b3b 接入角色授予及两层权威读取，R04b3c 补齐角色授予共享客户端，R04b3d 提供当前租户活动机构候选目录及两个候选共享客户端，R04b3e 接入机构授予与权威成员读取。管理与入队 API 不解析正文、不调用模型；R05 索引 Worker 的消费边界见下文。R05/R06 的每次处理派发必须复核这里记录的审批及最新权威状态。

## 目录 API

基础路径为 `/api/v1/ai/knowledge-bases`。所有请求均使用已认证主体和可信租户上下文；请求体不能指定 TenantId、OwnerUserId 或其他未声明字段。所有者可读本人目录；明确成员仅可读当前范围内启用的目录，且仍须精确读取权限。Host 管理权或超级管理员身份不绕过范围与所有权。越界读取和更新统一返回 404。

| 操作 | 方法与相对路径 | 精确权限 | 请求字段 |
| --- | --- | --- | --- |
| 获授权目录分页 | `GET /` | `ai.knowledge_bases.read` | `page` 默认 1，`pageSize` 默认 20；每页 1–100，偏移不超过 100000 |
| 获授权目录详情 | `GET /{knowledgeBaseId}` | `ai.knowledge_bases.read` | UUID 标识 |
| 创建私有目录 | `POST /` | `ai.knowledge_bases.create` | `name`、可空 `description` |
| 编辑目录 | `PUT /{knowledgeBaseId}` | `ai.knowledge_bases.update` | `name`、可空 `description`、`isEnabled`、`version` |
| 更新处理审批 | `PUT /{knowledgeBaseId}/policy` | `ai.knowledge_bases.policy_update` | `dataClassification`、两组可空模型标识/版本、目录 `version` |
| 所有者读取成员 | `GET /{knowledgeBaseId}/members` | `ai.knowledge_bases.members_read` | UUID 标识 |
| 所有者整量替换成员 | `PUT /{knowledgeBaseId}/members` | `ai.knowledge_bases.members_update` | `userIds`、目录 `version` |

名称去除首尾空格后须非空且不超过 200 字符，描述不超过 2000 字符。列表按创建时间与主键倒序，配合租户、所有者前缀索引；暂不支持任意排序和无界导出。

创建成功返回 201 与 Location；读取和更新成功返回 200。输入/字段错误返回 400，模型配置不可用返回 422，版本竞争返回 409，未认证/无精确权限分别返回 401/403。错误使用标准 ProblemDetails 与 `ai.knowledge.*` 稳定机器码，标题支持 `zh-CN`/`en-US`。

## 审批边界

新目录默认 `dataClassification=internal`、`isEnabled=true`，Embedding 与生成模型的标识和版本均为 null。`public`、`internal`、`restricted` 只是数据分类，任何分类都不自动批准模型处理。网络连通性、Provider 名称（包括 `ollama`）或目录编辑权限也不构成审批。

Embedding 使用 `embeddingModelConfigId` 和 `embeddingModelVersion`；生成使用 `generationModelConfigId` 和 `generationModelVersion`。一组审批必须同时为空（撤销），或同时提供非空 Guid 与正版本。批准时只能选择当前范围可用、已启用且版本完全匹配的模型配置；租户可以选择本租户模型或 Host 共享模型，不能批准其他租户模型。审批不授予模型管理权限，也不返回凭据和模型目的地。

目录编辑、审批与成员替换共用 `version`，每次成功变更递增一次。普通编辑及成员替换不修改审批字段；并发写入中只有匹配当前版本的请求能提交，失败结果由事务回滚。没有强制覆盖入口。

R05/R06 接入消费者时，还必须检查：知识库启用、精确审批模型及配置版本仍匹配、文档/版本未删除且仍授权、模型仍启用和可用、网络/预算/Provider 入口门禁全部允许。模型目的地变更、配置版本漂移或审批撤销后，旧审批不能授权下一次派发；尚未实现消费者前不能宣称数据外发链路已经验收。

## 成员边界

`userIds` 是完整名单，最多 100 个非空且不重复的 UUID，不能包含所有者。所有者权限来自目录所有权，空名单撤销全部明确成员，不会撤销所有者。请求 `version` 必须为正数且匹配目录当前版本；成员响应包含知识库标识、排序后的名单与最新版本。

Host 候选须为活动 Host 用户；Tenant 候选须为当前可信租户的活动成员，单纯拥有 Host 账号不足以入选。AI 通过 Identity 最小目录 Port 核对，不查询 Identity 表；跨模块核对在 AI 本地写事务之外完成。无效名单返回 400 `ai.knowledge.members_invalid`，候选不可用返回 422 `ai.knowledge.member_unavailable`。

成员管理、目录编辑和审批始终仅限所有者，即使成员持有相应端点权限也不能写入。读取直接核对本模块权威表，不缓存授权；替换事务提交后，同一成员令牌的下一次读取立即得到新授权结果。禁用目录时成员读取返回 404，重新启用后当前名单恢复只读访问。后续文档/派发消费者还须独立复核文档授权，不能把目录成员资格视为文件访问授权。

## 组织授权候选

知识库 `GET /api/v1/ai/knowledge-bases/{knowledgeBaseId}/organization-candidates` 使用 `ai.knowledge_bases.organization_candidates_read`；文档 `GET /api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/organization-candidates` 使用 `ai.knowledge_documents.organization_candidates_read`。两条端点均仅供资源所有者调用，当前会话权限每次从权威源复核，明确用户或角色成员不能凭读取资格枚举目录。

候选仅包含当前可信租户的活动机构，返回 `id`、`code`、`name` 与分页元数据，按编码、标识稳定排序。`pageSize` 为 1..100，最大偏移 100000；Host 资源返回空候选，查询字符串 `tenantId` 不能切换范围。机构禁用立即从候选消失，子机构按自己的活动状态独立列举。知识库所有者可为已禁用目录查询候选，文档候选只允许启用知识库内未删除的文档。查询不修改资源版本，也不授予组织成员读取资格。

共享客户端已导出 `aiListKnowledgeOrganizationCandidates` 与 `aiListKnowledgeDocumentOrganizationCandidates`，候选读取不改变资源授予。

## 组织授予与权威读取

以下路径基于 `/api/v1/ai/knowledge-bases`。

| 操作 | 方法与相对路径 | 精确权限 |
| --- | --- | --- |
| 所有者读取知识库机构 | `GET /{knowledgeBaseId}/organizations` | `ai.knowledge_bases.organizations_read` |
| 所有者替换知识库机构 | `PUT /{knowledgeBaseId}/organizations` | `ai.knowledge_bases.organizations_update` |
| 所有者读取文档机构 | `GET /{knowledgeBaseId}/documents/{documentId}/organizations` | `ai.knowledge_documents.organizations_read` |
| 所有者替换文档机构 | `PUT /{knowledgeBaseId}/documents/{documentId}/organizations` | `ai.knowledge_documents.organizations_update` |

维护仅接受所有者的有效交互会话，每次权威核对精确权限。知识库维护允许禁用目录，文档维护要求目录启用且文档未删除。非所有者、跨范围或不存在资源返回 404；Host 没有机构目录，只允许空授予。

PUT 正文为 `organizationUnitIds` 与资源 `version`，最多 100 个唯一非空 UUID；版本须为正数。空集合只清除机构授予。未知字段在 JSON 绑定时拒绝并返回 400；重复、空标识、超限或无效版本返回 400 `ai.knowledge.organization_grants_invalid`。不存在、禁用或其他租户机构返回 422 `ai.knowledge.organization_unavailable`。响应包含 `resourceId`、排序后的 `organizationUnitIds` 和版本；GET 保留已失效机构标识供所有者清理，不表示仍可读取。

机构候选核对在 AI 本地事务之外，经 Organization 最小 Port 完成。替换与对应资源共用 Version CAS，409 不能强制覆盖；插入失败回滚旧授予和版本。用户、角色与机构接口分别维护自己的授予，文档软删除同时清理三种授权。

每次读取获取当前可信租户、当前主体的完整活动机构关系，机构和隶属关系均须活动且属于当前租户；不缓存或按候选页大小截断。只匹配明确机构，授予父机构不会包含子机构。每层按所有者、明确用户、活动角色或活动机构匹配，知识库与文档两层均须满足；两层可用不同类型交叉匹配。分页总数和结果使用相同过滤。调离、机构禁用及授权撤销后旧令牌下一次读取立即失去该路径资格。

来源流打开后重新核对凭据、权限、角色、机构和两层资源状态；拒读、权威依赖失败或取消异常都释放流。后端四个 Operation 与共享生成客户端均已交付。

## 文档草稿与授权

基础路径为 `/api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents`。所有者只能维护启用知识库中的文档；文档标题、描述、列表数量和详情也受文档授权保护。读取必须同时具备 `ai.knowledge_bases.read` 与 `ai.knowledge_documents.read`，每层还须分别满足所有权、明确用户、权威活动角色或活动机构授予中的一种。知识库读取权、目录成员资格、模型批准都不能单独授予文档访问。

| 操作 | 方法与相对路径 | 精确权限 | 请求字段 |
| --- | --- | --- | --- |
| 获授权草稿分页 | `GET /` | `ai.knowledge_documents.read`，另需知识库读取权 | `page`、`pageSize`，边界同知识库目录 |
| 获授权草稿详情 | `GET /{documentId}` | `ai.knowledge_documents.read`，另需知识库读取权 | UUID 标识 |
| 所有者创建草稿 | `POST /` | `ai.knowledge_documents.create` | `title`、可空 `description` |
| 所有者编辑元数据 | `PUT /{documentId}` | `ai.knowledge_documents.update` | `title`、可空 `description`、文档 `version` |
| 所有者软删除 | `DELETE /{documentId}` | `ai.knowledge_documents.delete` | JSON 正文中的文档 `version` |
| 所有者读取文档授权 | `GET /{documentId}/members` | `ai.knowledge_documents.members_read` | UUID 标识 |
| 所有者整量替换文档授权 | `PUT /{documentId}/members` | `ai.knowledge_documents.members_update` | `userIds`、文档 `version` |

草稿标题须非空且最多 200 字符，描述最多 2000 字符；响应状态固定为 `draft`，不代表已上传、解析或索引就绪。未知 `fileId`、`tenantId`、`ownerUserId`、`status` 等字段返回 400。创建返回 201，成功软删除返回 204；删除后的读取及跨范围访问返回 404。版本竞争返回 409 `ai.knowledge.document_version_conflict`。

DELETE 响应丢失时，当前所有者可在同一可信范围内、持有效删除权限重发原请求版本；墓碑版本恰好为原版本加一时仍返回 204，不再次递增版本或刷新删除时间，也不重新登记清理意图。已删除文档的其他正版本返回 409，非正版本返回 400；非所有者、错误知识库或不存在资源仍返回 404，缺少删除权限返回 403。知识库禁用不阻止确认既有删除，存活文档仍要求启用目录及版本匹配。204 只确认删除意图已提交，不能据此判断文件与索引已全部清除。

文档名单最多 100 个唯一非空 UUID，不包括所有者，且只能选择当前知识库明确成员。任一用户不属于名单返回 422 `ai.knowledge.document_member_unavailable`，整次替换的旧名单、版本及此前插入均回滚。文档元数据、授权和删除共用文档版本，独立于知识库版本。空名单只撤销文档明确用户授予，已有角色或机构授予仍可满足该层。撤销目录明确用户后，仍须按该层全部当前授予判断；所有可用路径都被撤销或目录禁用时才拒绝文档读取。目录撤权不删除独立文档授权记录，重新授予目录读取资格后仍按文档当前用户、角色或机构授予判断。软删除同时写入墓碑并清理文档用户、角色与机构授权，无法通过重新授权打开已删除文档。权威查询每次联查本模块状态，不等待缓存、索引或文件清理。

## 不可变来源版本与导入恢复

以下相对路径基于 `/api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}`。上传、导入查询及恢复均限文档所有者；持有目录或来源下载权的成员不能上传或管理导入操作。

| 操作 | 方法与相对路径 | 精确权限 | 输入 |
| --- | --- | --- | --- |
| 上传新版本 | `POST /versions` | `ai.knowledge_documents.upload` | 查询字段 `version`、`requestKey`、`fileName`；正文为文件原始流 |
| 获授权版本分页 | `GET /versions` | `ai.knowledge_documents.versions_read`，另需知识库和文档读取权 | `page`、`pageSize`，边界同草稿目录 |
| 获授权来源下载 | `GET /versions/{versionId}/source` | `ai.knowledge_documents.source_read`，另需知识库和文档读取权 | 服务端版本 UUID |
| 所有者查询导入 | `GET /imports/{operationId}` | `ai.knowledge_documents.imports_read` | 服务端操作 UUID |
| 所有者恢复导入 | `POST /imports/{operationId}/retry` | `ai.knowledge_documents.imports_retry` | 无正文 |
| 所有者重试文档清理 | `POST /imports/cleanup` | `ai.knowledge_documents.imports_retry` | 无正文；每次最多 25 个操作 |

每个文件最多 8 MiB，按实际读取字节检查，不信任声明长度。仅接收 `.txt`/`text/plain`、`.md`/`text/markdown`、`.pdf`/`application/pdf`；文本必须为严格 UTF-8，PDF 必须有 `%PDF-` 文件头，文件名不得包含路径、控制字符或冒号。格式检查不等于正文解析。空文件、未知上传字段及不匹配的格式返回 400；请求不能指定 FileId、租户、所有者或处理状态。

上传返回 202 和导入操作 Location。同一文档的非空 `requestKey` 绑定首次内容摘要、大小、文件名、类型及预期文档 `version`，完全相同的重放复用操作；不同内容或元数据返回 409。上传前独立登记操作和服务端版本 UUID；文件发布、AI 版本提交及引用确认分别执行，不借用跨模块事务。成功的版本提交递增一次文档版本；历史版本只插入不覆盖，按版本号倒序分页。版本状态固定为 `pending_processing`，草稿元数据仍为 `draft`，均不表示解析或索引就绪。

导入阶段为 `awaiting_upload`、`file_uploaded`、`pending_confirmation`、`completed`、`cleanup_pending`、`failed`；每次推进使用五分钟有界租约及令牌 CAS。Host 通过 Files Claim → AI 本地提交 → Confirm 维护稳定版本引用；确认失败或 AI 提交结果未知时保留恢复凭据，不释放可能已提交的引用。Tenant 采用 Ai 模块和服务端版本资源所有权；响应丢失后仅从该资源的 ready 文件核对名称、类型、实际摘要和大小，复用原文件。

请求中断且租约清理失败时，遗留租约仍在到期前阻止原上传重放及导入重试，返回 409 `ai.knowledge.import_busy`。到期后用同一操作重试，或保留原 `requestKey`、版本、文件名及字节重放上传；并发请求只有一个能领取新租约，旧令牌不能写入状态或清除新租约。恢复继续使用日志中的同一文件和版本，不能通过换键重复上传来绕过竞争。租约清理失败不会触发文件补偿释放，也不会覆盖主要结果。

Host 在 Claim 前中断或 Pending 已提交但响应丢失时，保留 `file_uploaded` 日志且不发布 AI 版本。重放原始请求键、内容和元数据后继续使用同一文件、版本标识和 Claim 幂等键；已存在的 Pending Claim 经确认转为 Active。此时来源才可读取，完成后重复请求不再创建 Claim 或版本。

Tenant 文件只有配额确认后才成为 ready。未知确认保留 pending，Worker 用原文件操作键幂等确认；CAS 冲突不会当作确定拒绝。尚未绑定 FileId 的有效上传意图也受 Ai Owner 保护。清理先撤销读取资格，存储删除或配额释放失败保留墓碑；清理端口每次最多处理 25 个所属文件，剩余项保留 `cleanup_pending`。超时旧上传晚到时独立复核权威状态，仅补偿已经撤销或缺失的对象。

来源下载每次同时核对可信范围、启用知识库及知识库/文档授权，打开 Files 流后再次复核。响应为附件 `application/octet-stream`，设置 `no-store` 和 `nosniff`，不返回文件存储键、公共 URL 或任意 FileId。目录禁用、目录/文档撤权及软删除立即拒绝下一次来源读取。删除先在 AI 本地事务中写墓碑、清除授权并登记清理，提交后尝试有界索引清理，再在事务外释放文件；失败不恢复读取，所有者可继续查询和重试已删除文档的操作。

`POST /imports/cleanup` 同时推进已删除文档的派生索引清理。每次最多处理 25 个索引任务，每个任务独立短事务：锁定任务、取消 queued/running 租约，删除活动代次指针、向量和分块清单。任务、原会话、批次及费用凭据保留；缺少历史会话也不妨碍所有者清理。存活文档不会清除索引。知识库禁用不阻止所有者恢复已删除文档的清理。

AI 清理异常回滚当前任务，后续请求继续剩余任务；Files 释放失败或尚有有效导入租约时保留 cleanup_pending，到期后可再次恢复。响应整数仍只统计本次完成的文件补偿操作数，0 不代表整个文档已经清理完毕。公开索引状态在文件补偿或派生载荷残留时为 deleting，全部清理后才为 deleted。重复调用清理幂等；后台自动恢复见下方独立 Worker。成功重放 DELETE 也会尝试原有有界清理，有效租约不抢占，异常不撤销墓碑或改变 204 响应。

文件引用不存在、Host 文件元数据已软删除、Tenant 文件已 released 或所属模块／资源错配时，来源入口返回 404 `ai.knowledge.source_unavailable`。元数据仍为 ready、实际存储对象丢失时，当前 Local Provider 抛出存储异常，由统一管道返回 500 `common.unexpected` ProblemDetails；这表示存储状态不一致，应定位服务端日志并恢复对象，不能当作正常删除或重新导入成功。两类拒读响应均不返回正文、对象存储路径或内部异常；不会改写历史来源版本。

## 角色授权候选

| 操作 | 路径 | 精确权限 |
| --- | --- | --- |
| 知识库角色候选 | `GET /api/v1/ai/knowledge-bases/{knowledgeBaseId}/role-candidates` | `ai.knowledge_bases.role_candidates_read` |
| 文档角色候选 | `GET /api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/role-candidates` | `ai.knowledge_documents.role_candidates_read` |

两个权限独立，只允许当前资源所有者的有效交互会话调用；成员不能枚举候选，API Key 不支持这项操作。每次请求在 Endpoint 策略之后再次从权威源核对会话、账号、租户和精确权限；会话仍有效但权限已撤销时，旧令牌返回 403 `authorization.permission_denied`，会话同时失效时由认证管线返回 401。所有权不匹配、跨范围或资源不存在返回 404；禁用知识库仍允许其所有者查询知识库候选，文档候选要求目录启用且文档未删除。

候选来自 Identity 最小只读目录，只包含当前可信 Host/租户范围的活动普通角色 `id`、`code`、`name`，排除超级管理员角色，不携带角色成员和权限明细。`page` 从 1 开始，`pageSize` 为 1..100，偏移不超过 100000；按编码和标识固定排序，总数与列表使用相同过滤。请求中的租户参数不能切换范围。候选查询不会修改资源版本或授予访问。

## 角色授予与权威读取

以下路径基于 `/api/v1/ai/knowledge-bases`。

| 操作 | 方法与相对路径 | 精确权限 |
| --- | --- | --- |
| 所有者读取知识库角色 | `GET /{knowledgeBaseId}/roles` | `ai.knowledge_bases.roles_read` |
| 所有者替换知识库角色 | `PUT /{knowledgeBaseId}/roles` | `ai.knowledge_bases.roles_update` |
| 所有者读取文档角色 | `GET /{knowledgeBaseId}/documents/{documentId}/roles` | `ai.knowledge_documents.roles_read` |
| 所有者替换文档角色 | `PUT /{knowledgeBaseId}/documents/{documentId}/roles` | `ai.knowledge_documents.roles_update` |

维护入口只接受所有者的有效交互会话，每次核对权威精确权限；API Key 不具备维护资格。知识库角色维护允许禁用目录，文档角色维护要求目录启用且文档未删除。非所有者、跨范围和不存在资源统一返回 404。

PUT 正文为 `roleIds` 与对应资源的 `version`：最多 100 个唯一非空 UUID，空集合撤销全部角色授予；版本必须为正数。未知字段在 JSON 绑定时拒绝并返回 400；版本不为正、重复、空 UUID 或超限返回 400 `ai.knowledge.role_grants_invalid`。非活动普通角色、其他范围角色或不存在角色返回 422 `ai.knowledge.role_unavailable`。响应含 `resourceId`、排序后的 `roleIds` 和当前 `version`；GET 保留已禁用角色标识供所有者撤销，不表示该角色仍可读取。

角色替换与对应目录或文档共用版本，并在 AI 本地事务中原子执行；版本竞争返回 409，插入失败回滚旧名单和版本。角色接口只替换角色，旧 `members` 接口只替换明确用户，互不清空。Identity 候选校验及用户活动角色读取均通过最小 Port 在 AI 事务之外完成，AI SQL 只访问自身表。

每次读取从权威源取得当前主体在可信范围中的完整活动普通角色集合，不使用 JWT 角色声明或授权缓存。账号、角色、关系与租户活动成员资格均须有效。知识库层与文档层独立判断，再同时满足两层；两层可以分别匹配明确用户与角色，也可以匹配不同角色。分页总数与列表使用相同过滤。角色禁用、关系移除或租户成员停用后，同一旧令牌的下一次读取立即失去对应角色资格。

读取入口还核对当前会话或 API Key 的权威精确权限；API Key 禁用、到期、账号状态、安全戳或权限变更均会拒绝后续读取。来源流打开后再次核对凭据、精确权限、角色、机构及两层资源状态，拒绝时关闭已打开的流。当前 AI 入口未开放 Signature 认证，父子机构推导仍未实现。

## 共享客户端

`@fullnet/client-contracts` 的 `ai-knowledge` 入口导出知识库、文档、成员、文件版本、导入恢复、角色候选、角色授予、组织候选及组织授予的 32 个生成 Operation、参数类型、DTO 与响应读取器；根入口也导出同一组 API。协议来自 SQL Server/MySQL 真实 Host 的一致 OpenAPI 快照，不维护第二套手写路径或 DTO。

上传 Operation 为 `aiUploadKnowledgeVersion(http, parameters, signal?)`：`parameters` 包含 `knowledgeBaseId`、`documentId`、`version`、`requestKey`、`fileName`、`contentType` 和原始 `Blob` 正文 `body`。`contentType` 必须显式选择 `application/pdf`、`text/markdown` 或 `text/plain`，不能传 JSON 或 FormData。重放同一 `requestKey` 时保留首次的版本、文件元数据及字节，不重新生成键或更换文件。

`aiDownloadKnowledgeSource` 使用统一认证 Blob 客户端；`aiDeleteKnowledgeDocument` 接受 204 空响应。JSON 结果经过生成响应守卫，拒绝损坏 DTO 和整数精度丢失。Access Token、Cookie、语言、并发刷新、ProblemDetails 与取消继续由 `createHttpClient` 处理，Operation 不复制认证或租户状态。

`aiGetKnowledgeRoles` / `aiSetKnowledgeRoles` 维护知识库角色，`aiGetKnowledgeDocumentRoles` / `aiSetKnowledgeDocumentRoles` 维护文档角色。PUT 接受 `roleIds` 与当前资源 `version`，401 刷新只重试一次并保留原始正文及版本；409 继续作为 ProblemDetails 交给调用方重新读取和处理，不能自动覆盖并发修改。响应校验 UUID、角色集合及整数版本，兼容服务端整数字符串线编码。

`aiGetKnowledgeOrganizations` / `aiSetKnowledgeOrganizations` 维护知识库机构，`aiGetKnowledgeDocumentOrganizations` / `aiSetKnowledgeDocumentOrganizations` 维护文档机构。PUT 正文包含 `organizationUnitIds` 和当前资源 `version`；Host 使用空名单，401 刷新保留原始名单与版本并只重试一次。403/409 保留 ProblemDetails，由调用方处理权限或重新读取版本；生成响应守卫校验资源 UUID、机构集合及整数版本。语言和取消信号沿用统一 HTTP 客户端。

## 原会话索引入队

`POST /api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index` 的 OperationId 为 `aiEnqueueKnowledgeIndex`，不接受请求体或查询字段。只允许可信范围内的 Refresh/OIDC 原会话，API Key 与签名身份不能入队；所有者、租户、来源版本和模型批准均由服务端读取。

调用者须同时具备 `ai.knowledge_bases.read`、`ai.knowledge_documents.read`、`ai.knowledge_documents.source_read`、`ai.knowledge_documents.upload` 和独立的 `ai.knowledge_documents.index_enqueue` 权限。部署升级时，普通角色须显式授予新增权限；超级管理员仍受原会话、租户隔离和资源所有权约束。后台续租、模型派发、清单/向量持久化及激活也复核该权限，撤销后停止下一阶段。

仅接受当前最新、导入完成且模型批准仍有效的 TXT/Markdown 版本，以及分别显式启用对应运行时后的 PDF/DOCX 版本。202 响应只含 `taskId`、`versionId`，证明任务与原会话的稳定绑定；重放保留已有任务和实际收费，不重置状态、不发放免费重试，也不证明 queued 或 ready。新登录不能替换原绑定，旧无绑定任务返回冲突；状态查询见下节，Vue 文件版本入口使用共享客户端执行入队。

字段错误返回 400 `ai.knowledge.input_invalid`，权限拒绝返回 403；非所有者、跨范围或失效来源统一返回 404。原绑定或等待后权威状态冲突返回 409 `ai.knowledge.index_conflict`，未启用 PDF 或其他不支持的索引格式返回 422 `ai.knowledge.index_format_unsupported`。错误使用 ProblemDetails 与稳定机器码，标题支持 `zh-CN`/`en-US`。API 不解析文件，轮询仍由 Worker 的显式配置控制。

## 索引状态与当前完整性

`GET /api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index` 的 OperationId 为 `aiGetKnowledgeIndexStatus`，不接受请求体或查询字段。调用者须同时具备 `ai.knowledge_bases.read`、`ai.knowledge_documents.read` 与独立的 `ai.knowledge_documents.index_read`；普通角色须显式授予新增权限。所有者可以读取，其他当前主体须同时满足知识库和文档两层用户、活动角色或活动机构授权。跨范围或不可见资源返回 404，精确权限失效返回 403。

响应仅含 `versionId`、可空 `taskId`、`state` 和可空 `failureCode`。未入队为 `pending_processing`，任务排队/运行对应 `queued`/`processing`；失效来源、失败任务或不完整证明返回 `failed`，失败码统一为 `ai.knowledge.index_unavailable`，不返回内部异常、会话、租约、正文或向量。证明期间资源发生变化时返回 409 `ai.knowledge.index_conflict`，调用方可重新读取。

`ready` 每次复核当前来源、模型批准、活动指针、完整清单和批次回执，并在数据库中重新计算实际向量载荷摘要。创建任务的原会话离线或失去写入权限，不撤销其他当前读者对完整索引的读取资格；原写入重试仍要求原会话授权。读取不调用 Files 或模型、不修改任务或收费；返回值只表示本次判断，不可作为后续检索的授权缓存。

删除后仅所有者能读取精确版本墓碑：仍有文件清理待办或派生指针/清单/向量时为 `deleting`，这些内容清理完后为 `deleted`。任务与收费审计历史可以保留。该入口不执行清理，自动恢复由下述独立 Worker 承担；既有版本列表的 `pending_processing` 字段保持兼容。

## 所有者索引对账与恢复提示

`GET /api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index/reconciliation` 的 OperationId 为 `aiGetKnowledgeIndexReconciliation`，不接受正文或查询字段。调用者须同时具备知识库读取、文档读取与 `ai.knowledge_documents.index_read`，且必须是当前范围的资源所有者；用户、角色或机构成员的索引读取资格不能用于对账。不可见资源返回 404，未认证/权限失效返回 401/403，证明或权限等待期间观察到来源、任务或范围变化时拒绝旧提示；资源变化返回 409。

响应为 `versionId`、可空 `taskId`、`state`、`reason`、`recoveryAction`、当前 `documentVersion` 与 `knowledgeBaseVersion`。`state` 使用 TXT/PDF/DOCX 各自的完整就绪证明，分类不提升状态；关闭 PDF/DOCX 新处理开关也不撤销已经完整的代次读取。导入记录与版本的内容摘要、大小或类型不一致时返回 `source_unavailable/none`，不能通过重建绕过失效来源。

| reason | recoveryAction | 当前含义 |
| --- | --- | --- |
| `ready` / `deleted` | `none` | 完整代次可读 / 删除清理已完成 |
| `not_indexed` / `binding_stale` | `enqueue` | 尚无任务 / 文档、知识库或模型批准版本已变化，需要新的当前绑定 |
| `queued` / `processing` / `lease_expired` / `cleanup_pending` | `wait` | 等待排队、处理、原自动租约恢复或清理 Worker |
| `task_failed` / `lease_exhausted` | `retry` | 失败任务或第三次运行租约到期；仍须原会话及全部写入权限 |
| `task_cancelled` / `generation_unavailable` | `rebuild` | 当前绑定的任务已取消、冻结身份失配或完整代次损坏，需独立重建流程 |
| `source_unavailable` / `format_unsupported` | `none` | 来源/批准失效或格式未启用，先修复相应权威配置或来源 |

该 GET 不写任务、抢租约、解析文件、调用模型或收费。提示是本次核对结果，不是执行许可；enqueue/retry 必须经过已有写入端点的当前会话、权限、预算及幂等检查。`rebuild` 表达需要独立新代次，执行入口及启用要求见下文；不能重开已完成任务、接管旧会话或利用旧账本免费重发。共享生成客户端提供同名操作与响应校验。

Vue 知识文件的「文件版本 → 版本索引」提供「所有者索引对账」。默认仍读取普通索引状态，对账由用户显式触发；成员可读索引不等于拥有对账资格，所有权由服务端复验。界面以中文/英文展示原因、恢复建议、任务和文档/知识库快照版本，进入对账后刷新继续读取对账，不自动轮询。

仅已知且一致的原因/动作组合可以显示已有入队或重试按钮，按钮仍要求来源读取、上传及独立写入权限。重试使用本次读取的精确任务，原会话限制继续由服务端核验；等待及无需恢复只提供说明；重建须具备独立权限并显式勾选新增费用确认。拒绝、冲突、损坏或错版本响应会清除旧状态与操作资格，不回退旧状态或自动重发；关闭、资源或会话切换取消请求并丢弃迟到结果。202 只显示任务已接受，需显式刷新重新读取权威状态。

## 所有者显式独立重建（R08g）

本批已实现接口、Vue 和双库迁移，验收进度见活动开发计划。默认关闭 `FullNet:Ai:KnowledgeIndexRebuild:Enabled`。先暂停旧索引写入，执行迁移 `260_AiKnowledgeIndexRebuild.sql`，将全部 API/Worker 升级到识别多代次的版本后再显式开启；不能与仍按旧来源唯一性判断的程序混跑。关闭开关仅停止新增重建，不清除已保存的任务、费用或活动指针。应用回退须保留多代次读取和写入栅栏的兼容版本及新增列/索引，不删除历史数据来恢复旧唯一约束。

`POST /api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index/rebuild` 的 OperationId 为 `aiRebuildKnowledgeIndex`。请求只接受 `clientRequestId`、`expectedTaskId`、`expectedDocumentVersion`、`expectedKnowledgeBaseVersion`、`confirmAdditionalCost`。前四项来自客户端新请求键及最新对账快照，确认字段必须为 true；租户、所有者、来源和模型均由服务端确定。禁止附加 JSON 字段或查询参数。

所有者须具备原入队的五项权限、`ai.knowledge_documents.index_read` 和独立 `ai.knowledge_documents.index_rebuild`。API Key 不能发起重建。新 Refresh/OIDC 登录可以创建独立任务，不能认领旧请求或覆盖旧任务会话；相同请求键、载荷及原会话只返回同一新任务。网络结果未知时，Vue 保留请求键和完整快照供用户显式重试，不自动生成新键补发。刷新出现不同任务或快照、资源或会话切换后须重新确认。

仅当前来源与模型批准匹配、最新任务为 failed/cancelled/completed、所有历史索引费用均已结算且用量已知时允许新代次。queued/running、过期快照、不同载荷/会话或未知费用返回冲突；关闭开关返回业务拒绝。成功 202 只确认任务与冻结会话已保存，不表示 ready。后台续租、来源读取、模型派发和文本/PDF/DOCX 激活继续检查重建附加权限，撤权后停止后续阶段。提交后响应前撤权仍拒绝成功响应，已保存绑定保留供权威状态查询。

重建使用新的 UUID v7 任务和独立收费操作，不改写旧任务、批次、清单、向量和账本。新任务排队、处理或失败期间，旧完整活动代次继续按当前读取者授权提供读取；新任务通过全部清单、向量和结算证明后才在同一事务完成并切换指针。旧任务随后不可再次领取、人工恢复或用迟到租约覆盖新指针。来源或批准失效、删除及读取者撤权仍立即阻断旧代次读取。

## 原会话失败索引重试

`POST /api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index/{taskId}/retry` 的 OperationId 为 `aiRetryKnowledgeIndex`，不接受请求体或查询字段。调用者必须是原任务冻结的 Refresh/OIDC 会话，具备入队的五项权限及独立的 `ai.knowledge_documents.index_retry`；普通角色须显式授予该权限。新登录即使属于同一所有者，也不能接管旧任务。API Key、签名身份与旧无会话任务不能恢复。

只接受仍为最新的 TXT/Markdown 或已启用 PDF 来源、当前所有者与模型批准有效的任务；`failed` 或第三次运行租约已经到期可恢复为 `queued`，重置尝试计数、失败码和租约。排队中、活动租约、尚可自动恢复的低于三次过期运行、完成或取消状态返回 409 `ai.knowledge.index_conflict`；请求重复到达时不会重置已排队或运行的任务。权限不足返回 403，不可见或失效来源返回 404，未启用的 PDF 格式返回 422。条件写入再次核对可信 PDF 启用标志。

202 只含 `taskId`、`versionId`，确认本次恢复已接受；Worker 可能已经领取，不保证响应时仍排队。响应前撤权或来源变化仍拒绝成功响应，已提交的恢复不回滚；调用方应重新读取状态，不能无条件循环重试 409。

恢复保留原会话、模型绑定、清单、向量、批次与预算账本。已存完整付费批次由 Worker 复用；已付费但向量丢失、未知用量或其他不可安全恢复状态不会因人工重试获得免费模型调用。原会话到期/撤销后不能借新登录恢复，可由所有者使用当前交互会话按下述独立代次流程创建新任务；本端点不承诺修复所有失败原因，也不执行派生索引删除。

## PDF 可信运行时

`FullNet:Ai:KnowledgePdf` 独立于索引轮询开关，API 与 Worker 应使用一致的启用策略。上传 PDF 不自动启用解析或索引。

| 配置 | 默认值 | 约束 |
| --- | --- | --- |
| `Enabled` | `false` | 启用后允许 PDF 入队、人工重试和 Worker 扫描 |
| `NodeExecutablePath` | 空 | 可信 Node 可执行文件的绝对路径 |
| `CliScriptPath` | 空 | 受部署保护的隔离控制器脚本绝对路径 |
| `ImageId` | 空 | `sha256:` 加 64 位小写十六进制摘要，禁止可变标签 |

启用时启动校验全部配置形状；Worker 另要求两个本机文件存在，API 不需要本机解析器文件或 Docker 控制权限。路径和镜像来自可信部署配置，不能来自上传文件名、请求或模型输出；控制器使用仓库的 [pdf-sandbox-cli.mjs](../../scripts/ai/pdf-sandbox-cli.mjs)。Worker 仍须具备既有隔离控制器所需的 Docker 与固定解析镜像运行条件；仅配置路径存在不证明解析可用。

关闭 PDF 开关后不再接受 PDF 入队、人工重试或扫描；已完成 PDF 的状态读取仍按当前读取者授权和完整代次证明判断。读取不要求历史写入会话继续有效，不访问解析器或模型。

## 知识索引 Worker 的内部消费边界

Worker Profile 注册文本与 PDF 的 scoped 索引消费者，按来源格式分流；API Profile 不启动索引轮询。配置节为 `FullNet:Ai:KnowledgeIndexWorker`：

| 配置 | 默认值 | 约束 |
| --- | --- | --- |
| `Enabled` | `false` | 显式开启才扫描 TXT/Markdown 队列；PDF 另需启用可信运行时 |
| `BatchSize` | `4` | 每轮候选 1–32 个，按创建时间和主键排序 |
| `PollMilliseconds` | `1000` | 未满批轮询间隔 250–60000 毫秒 |

仅消费已经原子冻结原会话、所有者和租户匹配且来源/模型批准仍有效的任务；旧无会话任务不会补造身份或自动接管。每个候选在独立租户作用域竞争原有租约，再由对应完整代次消费者重验原会话、精确权限、Files 来源与预算。权限目录同时在 Worker 注册，普通用户的精确权限不会因后台入口而被过滤。

执行拒绝或阶段故障使用当前租约保护的 `ai.knowledge.index_unavailable`，原收费和未知用量记录保留。宿主关闭取消保留运行租约供到期恢复；已有持久向量不再收费，已付费而向量缺失的操作也不能免费重发。原会话 API 入队与当前 ready 查询见上文，检索问答按当前读取者重新证明来源；真实云模型整代次联动仍待验证，`pending_processing` 不因启用配置变成 ready。

## 删除与旧代次清理 Worker

Worker Profile 通过 `FullNet:Ai:KnowledgeCleanupWorker` 自动恢复已提交删除意图，API 不启动该循环。此开关独立于 `KnowledgeIndexWorker`，不会派发模型请求，也不要求原登录会话仍有效；禁用知识库仍可完成删除清理。

| 配置 | 默认值 | 约束 |
| --- | --- | --- |
| `Enabled` | `false` | 显式开启自动删除恢复 |
| `BatchSize` | `4` | 每轮 1–32 个索引任务或文件补偿操作 |
| `PollMilliseconds` | `1000` | 每轮结束均等待 250–60000 毫秒 |
| `RetiredGenerationCleanupEnabled` | `false` | 另行显式开启旧完成代次清理；同时要求 `Enabled=true` |
| `RetiredGenerationRetentionHours` | `168` | 24–8760 小时；从当前活动指针最近一次切换时间计算 |

扫描只读取 AI 自有删除意图、旧代次候选及最小操作标识，按创建时间、操作类型和主键分页。每项在独立可信租户作用域重新校验所有权，使用 30 秒取消预算；失败仍推进游标，扫描末尾回绕重试，重启从头扫描不会丢失持久意图。并发实例通过既有任务锁与导入租约协调；持续故障没有自动成功或清理完成承诺，应结合 `deleting` 状态与只含操作标识、类型的日志定位。若新增积压持续超过处理吞吐，回绕和旧失败项重试可能延后，本切片未验证容量或承诺清理时限。

索引清理取消排队／运行任务并删除活动指针、向量和索引清单，保留任务、会话、批次及收费凭据。文件释放沿既有 `cleanup_pending`、五分钟租约和版本幂等键执行，始终在 AI 事务外；有效导入租约不会抢占，到期后再恢复。两类操作各自收敛，文档在墓碑提交时已不可读取。手动 `imports/cleanup` 仍可使用，其返回整数只计完成的文件补偿。DELETE 的原版本重放语义见文档元数据一节。

旧代次清理采用独立开关，默认不改变现有保留行为。开启后只处理仍启用、未删除文档中被当前完成代次替换的旧完成任务，保留窗口以当前指针最近一次切换为准；连续切换会保守延后清理，不代表每个旧任务拥有独立的退役时间。排队、运行、无后继或保留期未到的任务不处理。

扫描结果仅是候选。每项恢复可信所有者与租户，TXT/PDF/DOCX 完整代次证明在同一短事务内锁定当前来源、模型、历史绑定、清单、向量、批次与收费凭据，再核对当前指针并删除旧向量和清单。后继损坏、来源过期、指针恢复或范围变化时保留旧载荷；异常与取消回滚，失败推进游标后回绕重试。该过程不派发模型，不要求原写入会话仍在线，也不删除活动指针、原文件、任务、会话、批次或账本。

启用前按恢复需求设置保留时间。清理后的旧载荷不能仅靠切回旧指针恢复，也不能利用保留的收费记录免费重发；需要后续独立重建流程。关闭旧代次清理开关并重启 Worker 后停止新的旧代次扫描，已在途项仍由事务及取消语义收敛。此能力不承诺积压清理时限，未进行容量实测。

## 本人回答反馈与撤回

知识答案（包括拒答）支持固定评价：`helpful` 对应 `accurate/useful`，`unhelpful` 对应 `incomplete/unsupported/outdated/other`。最多选八个该回答真实引用，可不选；不接收自由文本、评审声明或模型指令。反馈始终待审核，不自动进入评估集或模型输入，也不产生模型调用或收费。

路径前缀为 `/api/v1/ai/chat/sessions/{sessionId}/messages/{messageId}/feedback`，仅交互用户可操作本人可信租户中的已完成知识答案。除精确反馈权限外，服务复核当前 `ai.chat.sessions.read`；API Key 与 Signature 不可代替本人交互会话。

| 操作 | 精确权限 | 语义 |
| --- | --- | --- |
| GET 前缀 | `ai.knowledge_answer_feedback.read` | 读取固定评价及引用；完整当前来源证明失效时返回 404 |
| PUT 前缀 | `ai.knowledge_answer_feedback.submit` | 同样复核来源；版本零首次创建，同版本同载荷重放幂等，不同旧载荷返回 409 |
| GET 前缀 `/state` | `ai.knowledge_answer_feedback.withdraw` | 只返回本人消息标识、状态和当前版本；来源撤权后仍可取清除版本，无评价或引用 |
| DELETE 前缀 `?version=N` | `ai.knowledge_answer_feedback.withdraw` | 按当前版本清空评价、原因、引用与依赖摘要，返回递增版本的 `withdrawn` 墓碑；同原版本重放幂等 |

非法评价、版本或伪造引用使用标准 400 ProblemDetails，版本竞争使用 409；不可读资源使用 404。

角色授权树在 AI 对话页登记反馈读取、提交和撤回动作；`Chat.Read` 覆盖宿主和租户，不授予其他用户会话或来源读取权。Vue 在未知结果或 409 后保留选择并要求显式刷新；来源失效时清除反馈载荷，独立取清除版本后允许撤回。来源仍可读时可按墓碑版本重新评价。

迁移 `256_AiKnowledgeAnswerFeedback.sql` 成对创建 AI 自有反馈表，消息唯一约束防重复，会话锁与行版本防并发覆盖。外部权威授权在事务外，锁内复核本人会话及原消息绑定；不承诺跨模块授权原子性。物理删除消息级联清理反馈，应用回退保留新增表；未记账重放恢复约束和说明，不覆盖已有反馈。结构化标签审核和统计导出见下节；Prompt 发布见 [Prompt 运营说明](ai-prompts.md)。

## 结构化反馈审核与标签统计

路径前缀 `/api/v1/ai/knowledge-answer-feedback` 仅支持交互用户。三个独立动作挂在现有 AI 对话授权页，服务还复核当前 `ai.chat.sessions.read`；这些权限不授予其他用户的会话或回答正文读取权。

| 方法与路径 | 精确权限 | 行为 |
| --- | --- | --- |
| `GET /reviews?after=...` | `ai.knowledge_answer_feedback.review_read` | 最多扫描25项；返回获来源资格的固定标签、引用数量及双版本，可能出现空页与继续游标 |
| `PUT /reviews/{feedbackId}` | `ai.knowledge_answer_feedback.review` | `DecisionKey=approved/rejected`，同时提交当前 `FeedbackVersion` 与 `ReviewVersion`；禁止本人审核本人 |
| `GET /export` | `ai.knowledge_answer_feedback.export` | 固定最前64项当前批准窗口，仅输出评价、原因、引用数量与计数 |

反馈来源绑定按服务器 JOIN 证明的原所属人解码；权限、角色、机构和两层知识库/文档授予始终按当前评审者判断。完整来源证明后重新取得评审者授权参数，最终单次 SQL 同时核对反馈绑定、所有依赖的当前 ACL、文档/知识库版本、文件、活动任务、租约和完成摘要。评审者只有目录授予或只有文档授予均不可读取标签；来源撤权、删除、升级或派生证明失效会隐藏队列并排除统计。外部权威端口只在事务外调用，不承诺跨模块授权的原子快照。

队列游标由 Data Protection 保护并绑定当前评审者和 Host/租户，十五分钟有效。游标只表示内部扫描位置，不能用反馈 UUID 替代；失效、篡改、跨主体或跨范围游标返回400。被过滤行的标识不会通过游标交付。

首次审核版本为零，写入后递增；同评审者、同反馈版本、同决定的响应丢失重放保持版本不变，不同载荷或旧版本返回409。反馈修改或撤回令旧批准/拒绝绑定失效，审核版本仍保留，重新提交不会复活旧批准。会话锁与本人反馈写入共用，失败结果、取消或写后绑定改变会回滚。迁移 `258_AiKnowledgeAnswerReview.sql` 成对创建独立审核表；删除反馈级联删除审核，回退保留数据，未记账重放恢复约束和说明且不重置审核版本。

导出规则版本为 `feedback-labels-v1`，`IsPartial` 始终为真，不宣称总体完整。每格至少五名不同提交者，单人重复反馈不能补足阈值；计数为满足该阈值的反馈条数。输出不包含主体、反馈、消息、会话、引用标识、时间或正文，也无客户端筛选维度。实时精确计数仍存在跨次差分推断风险，**小样本抑制不保证匿名化**。此统计仅供评估选题，不是答案样例，不自动进入评估集、训练或模型输入，不产生模型调用或费用。原始问答审核与正式评估集准入仍待后续交付。

Vue AI 对话页提供“反馈审核”入口，按独立读取与导出权限显示队列、统计和下载；审核按钮还要求审核权限。队列仅展示固定标签、引用数量和状态，支持密文游标前后翻页。批准/驳回须确认当前标签并冻结反馈、审核双版本；409 或未知写入结果后暂停审核，显式刷新取得当前版本再操作。失效游标刷新回第一页；关闭、卸载、身份、租户或权限变化会取消在途请求并清空载荷，迟到响应不恢复旧上下文。

统计预览与 JSON 下载分别重新请求当前统计。共享客户端和页面均只投影固定字段，下载文件为 `ai-feedback-labels.json`，含规则版本、部分窗口声明，以及各格的评价、原因、引用数量、计数。界面提供中英文范围说明，批准不等于正式评估集准入。Vue 当前为 `Implemented / Build-verified`，真实栈页面验收在 AI 模块范围全部实现后集中执行，不能据此宣称页面已完成真实多用户验收。

离线人工审核候选准入工具及评估入口见 [评估语料说明](../../tests/ai-evaluation/README.md#人工脱敏反馈候选准入r09g)。操作人另行准备脱敏案例、受控来源许可与双人审核记录，工具保留全部原基线并追加独立版本，不直接消费标签统计或读取生产问答。审核标签为 `operator_attested`，不认证身份，不替代生产授权、撤权失效或在线样本准入；这些服务端能力仍待完成。

## PDF 外部 I/O 容量与拒绝

PDF 来源、规划、授权和模型端口通过进程内专用后台线程进入，普通操作与清理分别使用 4 个线程、8 个额外准入位置，每条通道最多 12 个未完成操作。排队、同步阻塞及异步等待都占用该额度，操作最终结束后才释放；普通通道满载不占用独立清理通道。线程随首次 PDF 边界调用建立，在进程生命周期内复用。

规划器在 Files 打开来源之前、模型批次在预算预留之前先保留清理额度；未使用的保留位置和迟到工作持有的引用也占用容量，实际清理提交一次后持续占用到其完成。未发生资源接管或收费时释放未使用位置；迟到来源由原保留位置释放，外层超时不撤销它。

容量耗尽立即返回 `fn_ai_pdf_io_capacity_exhausted` I/O 失败，不排入无界等待、不交付未确认结果。单次取消及既有清理期限保持有效；预算结算、晚到流释放和已开始的来源证明事务仍遵循原有确认规则。反复遇到该失败时应检查阻塞端口与未完成清理，不能将准入额度当作业务吞吐保证。

任意 Stream 或端口实现若永久阻塞，无法由线程调度强制终止，会持续占用对应额度；清理通道本身也有相同上限。该边界只约束宿主资源，PDF 解析仍依赖原有独立进程/容器沙箱。容量实测与真实云模型整代次验收仍待集中执行，保持 `Capacity-not-verified`。

## 双库与部署

迁移 `240_AiKnowledgeBase.sql` 增加 `fn_ai_knowledge_base`，`241_AiKnowledgeMember.sql` 增加 `fn_ai_knowledge_member`，`242_AiKnowledgeDocument.sql` 增加 `fn_ai_knowledge_document` 与 `fn_ai_knowledge_document_member`，`243_AiKnowledgeFileVersion.sql` 增加 `fn_ai_knowledge_version` 与 `fn_ai_knowledge_import`，`244_AiKnowledgeRoleGrant.sql` 增加知识库及文档角色授予表；均有 SQL Server 与 MySQL 实现，不改写已有业务表。外键仅关联 AI 自有父记录，不向 Files、Identity 或 Tenancy 表建立外键。SQL Server 主键显式采用 UUID v7 聚集键；MySQL 使用 Binary16 网络字节序。

建表、后置索引与 SQL Server 注释支持未记账重放。暂停旧应用后可前滚迁移；如需应用回退，保留新增表即可，旧应用不读写该表，不需要删除目录数据。迁移不播种目录或默认审批。

迁移 `245_AiKnowledgeOrganizationGrant.sql` 增加 AI 自有知识库及文档机构授予表，只有 AI 父资源外键，不向 Organization 建外键；同样成对实现、支持恢复重放且不播种默认授权。

验收证据与任务状态统一记录在[活动开发计划](../superpowers/plans/2026-09-08-ai-agentic-web-alignment.md)。目录、授权、文件引用与导入恢复、共享客户端、检索问答及 Vue 文件入口已接入；R08i 合批扩展 DOCX 上传、Worker、当前读取者证明、逻辑引用与历史、双库恢复和派生数据清理，验收进度见计划。PDF/DOCX 原生 API 用例验证真实 HTTP 入队、重试和已结算代次读取；受控恢复由 JIT Coordinator 执行，不能据此证明 Native Worker 的真实文件解析或云模型整代次派发。完整知识库/RAG、模型质量与容量状态不能由局部验收推导，容量保持 `Capacity-not-verified`。

## 最新来源的增量意图

迁移 `261_AiKnowledgeIncrementalIndex.sql` 增加 AI 自有的 `fn_ai_knowledge_index_change`。导入完成和 `fullnet.ai.knowledge_source.confirmed` v1 事件同事务写入 Outbox，逻辑 Topic 为 `ai.knowledge-source-confirmed.v1`，保持 LegacyPolling 单一投递所有权。Files 文件不可变；本系统替换来源通过 AI 发布新的不可变文件版本。

Worker 从可信事件信封建立独立 Host/Tenant 作用域，重新核对当前文档、知识库和最新已确认来源，只保留每个文档一条单调递增的来源意图。重复、乱序消息不会覆盖较新来源；墓碑及禁用状态仍由权威表控制。文档删除在同一事务移除意图，迟到事件不能重建。该步骤不读取正文、不请求模型、不保存会话，也不产生新的索引费用。

版本列表仅向具备 `ai.knowledge_documents.index_read` 的用户区分 `pending_processing`、`awaiting_index_confirmation`、`index_admitted`，后台分别显示等待处理、待确认索引、索引任务已创建；权限在查询完成后权威核对，其余用户保留原有的中性待处理状态。三者均不证明活动索引 ready；实际读取、对账和恢复仍沿现有索引入口。执行需要当前会话、精确权限、来源和模型批准；显式重建继续要求费用确认，默认开关保持关闭。

部署须先应用所用提供程序的迁移，再完成全部 Worker 升级，最后升级 API 生产者。旧 Worker 不理解新事件，不能与开始发布新事件的 API 混用；误投递须在新 Worker 就绪后沿现有 Outbox 死信恢复入口处理。回退保留新增表与 Outbox 记录，不删除业务来源或重写旧费用、任务、活动指针。

Outbox 写入失败会回滚导入完成状态，沿原上传重试键或导入恢复入口重新确认即可；未知提交先读取权威导入日志。事件不会自行回填历史来源或已经在停用期间处理过的记录，可使用后文的显式恢复或默认关闭的自动补偿。文档元数据事件和后台有费用的自动派发仍属后续能力；索引收费继续显式确认。来源事件的实现与验收范围见[增量意图计划](../superpowers/plans/2026-10-16-ai-incremental-index-intents.md)。

## 增量意图恢复

来源确认增量意图启用之前的历史导入、或知识库停用时被消费跳过的来源，可由所有者在文档页使用“恢复增量意图”。需要独立 `ai.knowledge_documents.index_recover` 以及知识库/文档/版本和索引读取权限；普通成员即使具有操作权限也不能恢复别人的目录。

对应 `POST /api/v1/ai/knowledge-bases/{knowledgeBaseId}/index-changes/recover`，请求仅含 `afterDocumentId`（首批为空）和 `pageSize`（1..50）。结果 `scanned` 表示本页复核来源数，`updated` 只统计新增或推进的意图，`nextDocumentId` 非空时继续下一批。UI 一次提交一批，不自动循环；失败后清空游标、从头幂等重扫。游标按当前数据库文档 ID 排序，不能跨知识库或提供程序复用；并发新增或确认来源可在本轮后重新扫描，不保证全库原子快照。

恢复逐文档重查最新已确认版本、当前可信范围、启用状态与墓碑；不会恢复已删除文档或跳过未确认来源。既有更新的意图不回退，同版本重放返回 `updated=0`。每文档独立授权准入并提交；撤权不追溯取消已准入的当前事务，下一文档前再检查并拒绝继续。取消会传递给数据库，失败时当前条可能已经提交或提交状态未知，已提交意图保留，从头幂等重扫可收敛。

此操作不创建收费任务，不复用旧用户会话、不派发模型、不切换索引代次；后续仍使用现有显式索引准入。无需新增迁移，须先部署包含 261 迁移及来源事件消费者的上一批版本。

## 增量意图自动补偿

Worker 可独立开启 `FullNet:Ai:KnowledgeIncrementalRecoveryWorker:Enabled=true`（默认 false），自动补齐历史来源与停用期间遗漏的意图。`BatchSize` 默认4、允许1..32；`PollMilliseconds` 默认10000、允许1000..60000，配置修改后重启 Worker。满批或失败后均等待，每条文档在30秒时发出取消；该期限依赖数据库驱动响应取消，不强制终止忽略令牌的实现。

扫描只读取 AI 自有的知识库、文档、最新完成版本及可信租户锚点，不读取正文；每条在全新 Host/Tenant 范围内用原文档锁复核启用、墓碑和最新已确认来源。未确认的较新版本不会挤掉最近已确认来源；重复扫描不回退意图。关闭目录会跳过，重新启用后在后续绕回时补齐。此后台内部元数据补偿不代表租户可用、所有者仍有派发权限或模型获批，所有收费入口继续独立核对当前用户、会话及授权。

进程内游标串行推进，单项故障不挡住后续文档，末页或空页回到开头；取消会停止，重启从头幂等重扫。多个 Worker 可以重复扫描，最终由同一文档事务串行收敛；没有全库原子快照、固定补偿时限或容量保证。无需额外迁移，保留来源事件与 Outbox 原处理记录，不新增、重写或重投这些收据。

该开关独立于收费索引 Worker、删除清理和显式恢复入口，不创建任务、用户会话、预算或模型调用；源意图恢复后仍须用户显式确认索引操作。功能验收状态与边界见[自动补偿计划](../superpowers/plans/2026-10-16-ai-incremental-auto-recovery.md)。
