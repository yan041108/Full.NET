# 知识库目录、文件版本、用户与角色授权及模型处理审批

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

仅接受当前最新、导入完成且模型批准仍有效的 TXT/Markdown 版本，以及显式启用 PDF 运行时后的 PDF 版本。202 响应只含 `taskId`、`versionId`，证明任务与原会话的稳定绑定；重放保留已有任务和实际收费，不重置状态、不发放免费重试，也不证明 queued 或 ready。新登录不能替换原绑定，旧无绑定任务返回冲突；状态查询见下节，共享 TypeScript/Vue 入队入口留待后续切片。

字段错误返回 400 `ai.knowledge.input_invalid`，权限拒绝返回 403；非所有者、跨范围或失效来源统一返回 404。原绑定或等待后权威状态冲突返回 409 `ai.knowledge.index_conflict`，未启用 PDF 或其他不支持的索引格式返回 422 `ai.knowledge.index_format_unsupported`。错误使用 ProblemDetails 与稳定机器码，标题支持 `zh-CN`/`en-US`。API 不解析文件，轮询仍由 Worker 的显式配置控制。

## 索引状态与当前完整性

`GET /api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index` 的 OperationId 为 `aiGetKnowledgeIndexStatus`，不接受请求体或查询字段。调用者须同时具备 `ai.knowledge_bases.read`、`ai.knowledge_documents.read` 与独立的 `ai.knowledge_documents.index_read`；普通角色须显式授予新增权限。所有者可以读取，其他当前主体须同时满足知识库和文档两层用户、活动角色或活动机构授权。跨范围或不可见资源返回 404，精确权限失效返回 403。

响应仅含 `versionId`、可空 `taskId`、`state` 和可空 `failureCode`。未入队为 `pending_processing`，任务排队/运行对应 `queued`/`processing`；失效来源、失败任务或不完整证明返回 `failed`，失败码统一为 `ai.knowledge.index_unavailable`，不返回内部异常、会话、租约、正文或向量。证明期间资源发生变化时返回 409 `ai.knowledge.index_conflict`，调用方可重新读取。

`ready` 每次复核当前来源、模型批准、活动指针、完整清单和批次回执，并在数据库中重新计算实际向量载荷摘要。创建任务的原会话离线或失去写入权限，不撤销其他当前读者对完整索引的读取资格；原写入重试仍要求原会话授权。读取不调用 Files 或模型、不修改任务或收费；返回值只表示本次判断，不可作为后续检索的授权缓存。

删除后仅所有者能读取精确版本墓碑：仍有文件清理待办或派生指针/清单/向量时为 `deleting`，这些内容清理完后为 `deleted`。任务与收费审计历史可以保留。该入口不执行清理，自动恢复由下述独立 Worker 承担；既有版本列表的 `pending_processing` 字段保持兼容。

## 原会话失败索引重试

`POST /api/v1/ai/knowledge-bases/{knowledgeBaseId}/documents/{documentId}/versions/{versionId}/index/{taskId}/retry` 的 OperationId 为 `aiRetryKnowledgeIndex`，不接受请求体或查询字段。调用者必须是原任务冻结的 Refresh/OIDC 会话，具备入队的五项权限及独立的 `ai.knowledge_documents.index_retry`；普通角色须显式授予该权限。新登录即使属于同一所有者，也不能接管旧任务。API Key、签名身份与旧无会话任务不能恢复。

只接受仍为最新的 TXT/Markdown 或已启用 PDF 来源、当前所有者与模型批准有效的任务；`failed` 或第三次运行租约已经到期可恢复为 `queued`，重置尝试计数、失败码和租约。排队中、活动租约、尚可自动恢复的低于三次过期运行、完成或取消状态返回 409 `ai.knowledge.index_conflict`；请求重复到达时不会重置已排队或运行的任务。权限不足返回 403，不可见或失效来源返回 404，未启用的 PDF 格式返回 422。条件写入再次核对可信 PDF 启用标志。

202 只含 `taskId`、`versionId`，确认本次恢复已接受；Worker 可能已经领取，不保证响应时仍排队。响应前撤权或来源变化仍拒绝成功响应，已提交的恢复不回滚；调用方应重新读取状态，不能无条件循环重试 409。

恢复保留原会话、模型绑定、清单、向量、批次与预算账本。已存完整付费批次由 Worker 复用；已付费但向量丢失、未知用量或其他不可安全恢复状态不会因人工重试获得免费模型调用。原会话到期/撤销后不能借新登录恢复，应等待后续明确代次重建能力；本端点不承诺修复所有失败原因，也不执行派生索引删除。

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

执行拒绝或阶段故障使用当前租约保护的 `ai.knowledge.index_unavailable`，原收费和未知用量记录保留。宿主关闭取消保留运行租约供到期恢复；已有持久向量不再收费，已付费而向量缺失的操作也不能免费重发。原会话 API 入队与当前 ready 查询见上文，真实外部联动与检索问答尚待后续切片；`pending_processing` 不因启用配置变成 ready。

## 删除清理 Worker

Worker Profile 通过 `FullNet:Ai:KnowledgeCleanupWorker` 自动恢复已提交删除意图，API 不启动该循环。此开关独立于 `KnowledgeIndexWorker`，不会派发模型请求，也不要求原登录会话仍有效；禁用知识库仍可完成删除清理。

| 配置 | 默认值 | 约束 |
| --- | --- | --- |
| `Enabled` | `false` | 显式开启自动删除恢复 |
| `BatchSize` | `4` | 每轮 1–32 个索引任务或文件补偿操作 |
| `PollMilliseconds` | `1000` | 每轮结束均等待 250–60000 毫秒 |

扫描只读取 AI 自有墓碑及最小操作标识，按创建时间、操作类型和主键分页。每项在独立可信租户作用域重新校验所有权，使用 30 秒取消预算；失败仍推进游标，扫描末尾回绕重试，重启从头扫描不会丢失持久意图。并发实例通过既有任务锁与导入租约协调；持续故障没有自动成功或清理完成承诺，应结合 `deleting` 状态与只含操作标识、类型的日志定位。若新增积压持续超过处理吞吐，回绕和旧失败项重试可能延后，本切片未验证容量或承诺清理时限。

索引清理取消排队／运行任务并删除活动指针、向量和索引清单，保留任务、会话、批次及收费凭据。文件释放沿既有 `cleanup_pending`、五分钟租约和版本幂等键执行，始终在 AI 事务外；有效导入租约不会抢占，到期后再恢复。两类操作各自收敛，文档在墓碑提交时已不可读取。手动 `imports/cleanup` 仍可使用，其返回整数只计完成的文件补偿。DELETE 的原版本重放语义见文档元数据一节。

## 双库与部署

迁移 `240_AiKnowledgeBase.sql` 增加 `fn_ai_knowledge_base`，`241_AiKnowledgeMember.sql` 增加 `fn_ai_knowledge_member`，`242_AiKnowledgeDocument.sql` 增加 `fn_ai_knowledge_document` 与 `fn_ai_knowledge_document_member`，`243_AiKnowledgeFileVersion.sql` 增加 `fn_ai_knowledge_version` 与 `fn_ai_knowledge_import`，`244_AiKnowledgeRoleGrant.sql` 增加知识库及文档角色授予表；均有 SQL Server 与 MySQL 实现，不改写已有业务表。外键仅关联 AI 自有父记录，不向 Files、Identity 或 Tenancy 表建立外键。SQL Server 主键显式采用 UUID v7 聚集键；MySQL 使用 Binary16 网络字节序。

建表、后置索引与 SQL Server 注释支持未记账重放。暂停旧应用后可前滚迁移；如需应用回退，保留新增表即可，旧应用不读写该表，不需要删除目录数据。迁移不播种目录或默认审批。

迁移 `245_AiKnowledgeOrganizationGrant.sql` 增加 AI 自有知识库及文档机构授予表，只有 AI 父资源外键，不向 Organization 建外键；同样成对实现、支持恢复重放且不播种默认授权。

验收证据与任务状态统一记录在[活动开发计划 R04/R05](../superpowers/plans/2026-09-08-ai-agentic-web-alignment.md)。R04 的目录、授权、文件引用与导入恢复已完成收尾验收，共享生成客户端已交付；R05 已接入文本与显式启用的 PDF Worker、原会话 HTTP 入队、公开索引状态及原会话失败重试、删除后的有界索引清理及自动恢复，分项验收见活动计划。PDF 原生用例验证真实 HTTP 与已结算代次恢复，不能据此证明 Native Worker 的真实文件解析或云模型整代次派发；检索问答及 Vue 页面入口仍待后续切片，容量尚未验证。完整知识库/RAG、模型质量与容量状态不能由这些局部验收推导。
