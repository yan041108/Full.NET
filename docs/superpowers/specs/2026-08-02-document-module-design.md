# Document 模块设计规格

**状态：** 原规格 Approved for implementation（Gate G4，2026-08-02）；2026-10-24 第一、二批关联实现与聚焦验收已完成；第三批关联实现及聚焦验收也已完成。
**日期：** 2026-08-02；最近修订 2026-10-24。
**适用范围：** `Full.NET.Modules.Document` 主项目与 Files 真实消费的最小 `Document.Contracts` 事件项目、Host 文档库、Vue 管理端、SQL Server/MySQL；租户文档库为后续独立需求，Layui 冻结。
**证据基线：** 2026-10-24 静态审查 `a16897a96ef4b5078e3cb879a053885f53b24400`；文档同步开始时 HEAD 为 `a77902afd87af1662af3b43206d85160eeec566b`，两者之间本次审查的 Document 模块与文档主列表代码未变化。静态审查阶段未执行运行测试；后续实施以同一 HEAD 为代码基线，当前聚焦运行证据见 §12.2、§12.3、§12.4。

**方案复核：** 2026-10-24 按用户“审查方案（有问题直接更新）”要求，补齐会话幂等、授权管理语义、状态转换、并发与文件释放交付规则；第一、二批目标已完成聚焦验收；第三批实现及收尾证据按以下分批状态区分。

## 1. 决策摘要

Admin.NET.Pro `Plugin.Document` 提供分类、标签、版本、分享、权限、预览、回收站与统计，但其把文件字节与业务元数据耦在插件表内，并依赖 SqlSugar/反射式权限。Full.NET 只吸收产品语义，不复制实现。

Document 模块拥有文档元数据、分类/标签、版本链、分享与 ACL；**文件字节唯一经 Files 模块 `FileId` 引用**，禁止 Document 直连 `fn_files_file`、禁止在业务表存储 Blob/Base64。当前产品目标是字段可信、权限明确、版本可靠、分享受控的 Host 文档中心；租户库、文件夹、全文检索、协同编辑和 Workflow/Ai 集成按真实需求独立建设，不因现有表含 TenantId 自动扩围。

当前能力矩阵保持既有 `Build-verified` 记录；第一、二批按聚焦范围留存本地运行证据，不据此将第三批或整个模块升级为 `Verified`、`Production-verified`；容量保持 `Capacity-not-verified`。实施按 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md) 与 [测试与验证](../../../rules/development-quality.md#11-测试与验证) 执行。

## 2. 目标与非目标

### 2.1 目标

- Host 文档：创建、元数据编辑、分类/标签、版本上传、列表/详情、软删除/恢复、分享链接（受限）、权限拒绝与审计。
- 通过 Files 公开上传、内容读取与引用 Claim 契约消费对象存储；Document 持久化 FileId 与版本元数据快照，模块间不共享本地写事务。
- Host 文档 `TenantId IS NULL`，由精确权限与文档 ACL 共同控制；未来租户库需要独立机构范围与隔离设计。
- 双库等价数据行为、标准 ProblemDetails API、Vue 页面与受影响场景的真实边界验收。

### 2.2 非目标（1.0 / 首切片）

- 在线协同编辑、全文检索集群和病毒扫描生产级引擎。Office 外部转换适配器已存在且默认关闭，本轮整改覆盖其任务和引用生命周期，不建设内置 Office 引擎。
- 任意用户自定义字段、动态 SQL 报表、Workflow/DataApproval 联动（后续模块通过显式契约集成）。
- 租户文档库与跨租户分享。Host 匿名分享已实现，本轮整改覆盖已有访问额度与状态约束，不再把匿名下载描述为无实现的非目标。
- 把 Document 文件存入业务表或绕过 Files Provider 路由。

## 3. 依赖与边界

| 依赖 | 边界 |
| --- | --- |
| **Files** | 仅通过 `Full.NET.Modules.Files.Contracts` 上传、内容、描述与 Claim 契约引用 FileId，禁止 JOIN Files 表。删除 Document 引用不等于删除共享文件；引用释放经 §8 的可靠交付完成，Blob 是否回收由 Files 决定。 |
| **Identity/Tenancy** | 登录路径消费可信身份、会话与精确 RBAC；文档 ACL 在 Document 解释，主体校验经 Identity 最小公开契约。匿名分享由 Document 验证口令、有效期及短期访问凭据。 |
| **Organization** | 租户文档读写在 SQL 层施加机构数据范围；Host 文档不使用机构过滤。 |
| **Auditing** | 元数据变更写 B1 行为审计（非 Outbox）；大文件字节不写审计正文。 |
| **字段投影** | 文档列表/详情可选暴露 `description` 等 Internal 字段；须注册语义资源目录后再做受限列，首切片可仅 Host 全字段。 |

## 4. 作用域模型

| 作用域 | `TenantId` | 数据范围 | 首切片 |
| --- | --- | --- | --- |
| Host 文档库 | `NULL` | 精确权限、文档 ACL 与生命周期共同控制 | 已有实现，状态及管理缺口见 §6 |
| 租户文档库 | 非空 | 机构树 + 用户隶属 + 文档 ACL | 后续切片 |

文档项等可恢复资源使用软删除；版本、分享、权限与日志按各自生命周期管理，不能概括为所有表统一软删除。活动名称冲突与恢复必须维持双库一致性。

## 5. 数据模型与整改不变量

命名遵循 [`naming-conventions.md`](../../../rules/naming-conventions.md)：`fn_document_*`，UUID v7 主键，PascalCase 列。

| 表 | 用途 |
| --- | --- |
| `fn_document_category` | 树形分类；`ParentId`、`Name`、`SortOrder`、作用域键 |
| `fn_document_tag` | 标签字典；租户/Host 分区 |
| `fn_document_tag_assignment` | 文档-标签多对多 |
| `fn_document_item` | 文档主记录：标题、描述、分类、当前版本指针、作用域 |
| `fn_document_version` | 文件版本：FileId、VersionNumber、ContentHash、SizeBytes、UploadedByUserId；文件名、MIME、扩展名和变更说明列已存在，写入待补齐 |
| `fn_document_permission` | 用户动作位集合，支持 read/write/delete/share 独立组合；角色与机构主体尚未实现 |
| `fn_document_share` | 随机分享码、口令哈希、有效期、额度、启停与并发版本 |
| `fn_document_preview_task` | 转换任务、源/输出 FileId、请求人、状态和领取版本；请求人可为空表示匿名 |
| `fn_document_access_log` / `fn_document_version_deletion_audit` | 访问事实与版本删除事实；永久删除时的保留策略待完善 |

第一批已新增分享访问会话 `fn_document_share_session`、版本幂等结果 `fn_document_version_operation` 与权限治理审计 `fn_document_permission_audit`，同步双库迁移、映射和清理。预览文件引用意图（见 §8.1）仍待第三批，由 Document 持有独立引用 Id、任务/尝试归属、源或输出用途、FileId 与完成状态。

**不变量：**

- `fn_document_version.FileId` 必填；目标为同一文档的版本号单调递增且永不复用，包括删除最高历史版本后继续追加。第一批已改为文档项持有 `LastVersionNumber` 高水位，与新版本及当前指针在根锁及本地事务内原子更新；独立操作表按文档、可信用户和 RequestKey 持久化幂等结果。
- 禁止 `FileId` 跨租户复用；Host 文档引用的 `FileId` 必须来自 Host Files 上传。
- 分类/标签名称在同一父节点 + 作用域内唯一（未删除行）。
- 分类更新不得形成祖先/子孙环；需要同时覆盖并发移动及删除分类与新增引用的竞争。
- 列表、详情、写入读取必须投影实际使用的字段；不得将缺列产生的默认值当作原值回写。编号、分类展示和当前版本大小需与契约一致，版本文件元数据来自 Files 权威契约。
- 元数据优先复用已有 `IHostFileDescriptorReader.GetReadyDescriptorAsync`；Claim 返回的 HostFileReference 只有 FileId、SizeBytes、ContentHash，不包含文件名/MIME。先通过 Claim 保留文件，再在 Document 写事务外取得描述并核对 FileId/大小/摘要；未知 MIME 或扩展名不得伪造。禁止为读取元数据下载完整文件或跨表 JOIN。

## 6. 版本、分享与权限

### 6.1 版本

- 新版本 = Files 就绪文件 + 独立 Claim + Document 本地事务分配编号、插入版本与更新当前指针 + Claim 确认/对账；提交结果未知时不得盲目释放引用。
- 回滚 = 将当前指针切到既有版本 **不删除** 较新版本行（Admin.NET 语义对齐）。
- 下载/预览始终解析当前版本或显式 `versionId`；无权限时 403，不存在 404。登录态原文、历史预览与转换输出在 Files 流打开后、移交前再次检查文档 ACL 和未删除状态；拒绝或查询异常均由本层释放流。首次不存在的任务直接拒绝，不能通过未授权的第二次读取取得并发新建任务。 转换输出使用同一个已授权快照判断成功状态并选取文件，移交前再对照权威状态；请求快照尚未成功时返回 `document.host_preview_task.not_ready`，不将状态切换拼接成资源不存在。

版本分配、回滚、手工/自动删除版本、状态转换、ACL 替换、恢复和永久删除都先锁定同一文档行，再按固定顺序操作子记录；锁内重查 ACL、状态、并发版本、当前版本与最小保留数，不能只在进入事务前授权。自动裁剪也须遵守此顺序，避免并发删除突破最少保留数或删除刚被回滚为当前的版本。跨文档操作按 Id 排序，涉及分享时锁顺序为文档→分享→会话/任务；Files/Identity 调用不进入本地事务。

版本新增按调用者、文档和请求幂等键记录已提交结果，同键同输入返回同一版本，同键不同输入返回 409；请求失败或响应丢失不得盲目上传和分配第二个版本。编号保证针对已提交版本，事务回滚的内部候选号不构成对外版本。初始化最大编号时同时考虑现存版本与删除审计；证据不足的数据不得凭猜测宣称历史编号未复用。

### 6.2 精确权限与 ACL

| 权限 | 说明 |
| --- | --- |
| `document.host_documents.read` / `download` | 列表、详情和版本列表 / 文件下载，分别授权 |
| `document.host_documents.create` / `update` / `add_version` | 创建、元数据更新、版本追加，分别授权 |
| `document.host_documents.delete` / `restore` / `rollback_version` / `delete_version` | 删除、恢复、回滚与历史版本删除，分别授权 |
| `document.host_permissions.read` / `set` | ACL 读取与整组替换 |
| `document.host_shares.read` / `create` / `update_status` | 分享管理 |
| `document.categories.read` / `create` / `update` / `delete` | 分类逐操作授权 |
| `document.tags.read` / `create` / `update` / `delete` | 标签逐操作授权 |

租户权限在租户切片时增加 `document.tenant_documents.*` 前缀，不得与 Host 权限混用。

上表同一行后续短名沿用首项前缀；完整目录以模块 Contracts 中权限常量和授权 Contributor 为准，不能把短名直接用作权限码。

第一批已使用显式 `host/restricted` 模式与用户动作集合，批量校验活动 Host 主体、重复主体和未知动作，按文档 Version 替换并递增访问修订。restricted 空名单拒绝内容访问；具有独立治理权限的用户可通过最小上下文重新授权，变更前后动作与操作者保存在独立治理审计表。普通文档 write 不授予治理权限。

整改采用以下确定语义：

- 文档可见模式为 `host` 或 `restricted`。前者仍需精确模块权限及状态允许，后者还需命中用户动作集合；restricted 空名单拒绝普通内容访问，不能自动回退 host。创建默认 host，Draft 的额外过滤仍生效；创建者身份只满足草稿可见条件，不是 restricted 或 Endpoint 授权旁路。
- 动作为 read/write/delete/share 的显式集合；write/delete/share 均包含 read，现有 manage 直接规范化为四项集合，不再表示 ACL 管理权。编辑与分享不隐含删除；文件下载仍须独立的 `document.host_documents.download`。
- `document.host_permissions.read/set` 明确为 Host 全局 ACL 治理权限，允许查看/替换受限文档 ACL，不要求先拥有该文档内容授权；它们不直接赋予下载或普通编辑权。普通文档创建者或 write/share 用户不能改 ACL。页面说明该治理权限可改变全库可见性，替换记录操作者、前后模式/动作与并发版本。治理权限本身仍受可信 Host、会话和精确 RBAC 约束。
- ACL 替换前通过已有 `IHostUserBatchSelectionDirectory.FindActiveHostUsersAsync` 一次批量校验活动 Host 用户；未知、停用、非 Host 主体拒绝。校验在 Document 事务外，提交时按文档版本 CAS，失败后重试必须重新校验；跨模块状态不是一致快照，实际访问仍由 Identity 当前会话/账号状态守卫。
- ACL、状态及当前版本改变时递增独立文档访问修订号；分享策略修改使用独立策略修订号。访问计数不修改这些修订号，不再用 Version 与 AccessCount 的差值推断策略版本。两种修订号用于撤销旧匿名会话，不缓存无期限授权结果。

### 6.3 分享

- 当前 Host 分享码由密码学随机源生成 12 位，口令仅存 PBKDF2 哈希；支持强制过期、停用与匿名 POST 限流，不返回长期 Files 直链。分享码与口令哈希是不同字段，不能把现状写成所有令牌只存摘要。
- 第一批已统一为持久化分享会话：`/access` 一次打开扣次，同键重试复用；原文件、预览创建、轮询与输出只接受固定版本会话，并逐次检查权威修订。会话 TTL 默认 15 分钟（范围 1～120 分钟），独立于 Office 转换预算；到期后额外保留 24 小时幂等窗口。
- 整改方向采用一次打开会话原子扣一次额度，签发绑定分享、文件版本和有效期的短期凭据；原文件、预览创建、轮询与结果读取统一验证。重新打开需新额度，撤销、过期、删除及不可分享状态必须拒绝。若业务需要按下载计数，应另设明确下载额度，不能让内部轮询重复计费。
- 未提供口令仍返回独立错误码；应让接口注释与公开错误策略一致，不能宣称全部失败同码。

会话协议及存续规则如下：

1. `/access` 先验证链接、口令及限流，再在 Document 本地事务内按固定锁序重查 Published、未删除、有效期和策略修订；修订必须与事务外口令验证快照一致，否则拒绝并要求重新验证。解析并固定当前 VersionId/FileId，原子扣次并插入会话。无可用当前版本时不创建会话、不扣次。ShareId 与随机请求键组合唯一；客户端为一次打开生成至少 128 位随机请求键，同键重试必须再次通过口令验证并只返回原会话，不重复扣次、不延长到期时间。幂等窗口持续至会话过期后 24 小时，窗口内过期或被撤销的同键请求拒绝，不能自动创建第二次访问；窗口外重用请求键视为新访问并重新扣次。
2. 会话持久化 Id、ShareId、DocumentId、VersionId、文档/分享修订号、创建与到期时间及请求键；短期凭据绑定会话和用途，由现有受保护凭据机制签发。多实例共用受保护密钥环及应用标识，不能要求粘性会话；凭据不放 URL、日志或持久化浏览器存储。请求键本身不是认证凭据。
3. 会话默认 15 分钟，可配置 1～120 分钟，最终到期不晚于分享到期；独立于转换器超时配置。原文件、预览创建、轮询和输出都使用同一会话，只读取绑定版本，不能重新解析最新版本。追加版本、回滚、ACL/状态变更、软删/恢复或分享策略修改会撤销旧会话；必须重新打开且消耗新额度。无论再次启用分享还是重新发布文档，旧会话均不复活。
4. 每次取内容都检查权威状态；获取 Files 流后、返回响应前复查修订号，失败则释放流。撤销阻止此后通过检查的新请求，已经开始传输的字节无法收回，不能宣称强制终止所有在途下载。会话是可转交的短期 bearer 凭据，不证明唯一自然人，也不承诺限制会话内的重复读取次数。
5. 轮询和文件 Range 请求不扣额外额度；一次会话的预览创建使用稳定操作 Id，不因重试重复建任务。会话过期不保证后台转换取消，但匿名访问必须拒绝，重新打开后按新会话重新授权。会话过期记录在过期后 24 小时分批清理；新会话始终先扣次，清理历史请求键不能形成免费访问。

### 6.4 状态与更新语义

- Draft 仅创建者及显式授权用户可见，不参与外部分享与正常发布统计；Published 正常参与查询、分享与统计；Archived 只读，拒绝元数据编辑、追加版本与回滚。恢复编辑需明确操作。
- 第一批已实现独立发布/归档/恢复编辑命令、草稿读取边界和归档写入/分享限制。第二批已完成当前可读 Published 范围的统计及聚焦验收。
- 元数据 `PUT` 采用完整替换：Title、Description、CategoryId、Thumbnail、Sort、TagIds 与 Version 必须全部出现；可空字段显式 null 表示清空，TagIds 空数组表示移除标签，缺失字段返回 400。页面先读取完整详情，再发送完整表单；用户仅改标题时保留其余表单值。状态从通用更新移出，使用独立转换命令；不引入反射式通用 Patch 或兼容旧缺字段请求。普通与 Native AOT 源生成 DTO 都要验证“字段出现但值为 null”与“字段缺失”的区别。

| 状态命令（整改目标） | 前置状态与结果 | 精确权限与文档动作 |
| --- | --- | --- |
| 发布 | Draft → Published，必须有就绪当前版本 | 新增 `document.host_documents.publish` + write |
| 归档 | Published → Archived，撤销全部旧分享会话并停用现有分享 | 新增 `document.host_documents.archive` + write |
| 恢复编辑 | Archived → Draft，不自动恢复分享 | 新增 `document.host_documents.unarchive` + write |

创建统一从 Draft 开始，上传就绪版本后再发布；不支持直接创建 Published/Archived 或用普通元数据更新绕过转换。新分享仅允许未删除、未进入永久清理的 Published 文档，Archived 可供有权登录用户只读查看/下载，不允许匿名访问；归档不等于法律保全，具有独立权限的软删、永久删除及版本保留仍按明确保留规则执行。状态冲突或陈旧 Version 返回 409，未知枚举或缺少字段返回 400；状态转换、分享停用与访问修订递增同事务提交。

## 7. API 与服务端查询

基路径 `/api/v1/document/host/...`，标准分页与 ProblemDetails。

- `GET/POST /items`，`GET/PUT /items/{id}`，`POST /items/{id}/delete`
- `POST /items/{id}/versions`（body：`fileId` 来自 Files 上传完成）
- `POST /items/{id}/restore`，`GET /items/{id}/versions`
- `GET/POST /categories`，`GET/POST /tags`
- `GET /permissions/by-document/{documentId}`，`POST /permissions`
- 匿名入口位于 `/api/v1/document/public/shares`，访问、内容和预览操作使用 POST。

OpenAPI 独立契约文件；JSON 使用源生成 Context。

第二批已完成服务端关键字、分类及子孙、状态、类型、标签组合筛选与分页，并通过聚焦验收；ACL 和状态同时约束计数与结果，排序包含稳定唯一键。页面必须使用服务端总数，不得把扩大固定拉取上限当作修复。

今日新建来自文档创建时间；今日访问、下载和预览来自访问日志的明确事件类型，以约定 UTC 日的半开时间区间聚合。匿名下载不得一律记成预览。当前版本大小/类型分布只连接当前版本；全部历史存储大小另设指标，明确单位及取整方式。

统计默认范围是调用者当前可读、未删除的 Published 文档，包含无版本文档的文档数量；类型/大小只计有当前版本者。Draft/Archived、回收站和保留审计不能混入同一总量。今日新建指当前统计范围内 CreatedAtUtc 落入 UTC 当日的文档，不是发布次数；ACL 或状态变化允许看板历史值随当前可见范围变化，若需不可变经营报表应另立指标。今日访问计成功逻辑访问事件，分享打开、下载和预览分项展示，不代表去重人数；轮询、幂等重试不重复记同一事件。下载指标命名为“下载请求”，记录通过授权并成功打开源流的逻辑请求，不宣称客户端已接收全部字节；Range 请求按同一逻辑操作归并。字节以整数累计，展示层再换算 KB，避免两库除法与逐行取整差异。

第二批接口约定：列表查询参数为 `page/pageSize/keyword/categoryId/status/documentType/tagId`；关键字最多 256 字符，按标题与编号执行文字包含匹配（通配符转义），`pageSize` 最大 100。分类选择包含自身和未删除子孙，排序含文档唯一键。非法枚举拒绝。

内容 GET 使用可选 UUID `requestKey`：一次逻辑操作的重试保持同键，带 Range 时必须提供非空键；省略键的普通 GET 每次作为独立操作。匿名原文使用请求的 `accessTypeKey=download|preview`（默认 download）；无内容请求键时按分享会话归并同一用途。Office 成功输出读取按任务与访问者/会话计预览，创建和轮询不计访问。唯一操作键持久化于日志，不因跨日重试重置首次事件时间。

分类树使用 `fn_document_category_guard` 的单一 Host 行锁串行化结构变化和文档分类引用写入，锁在事务首次读取前取得，随后才能锁文档根行。完整父链检查拒绝缺失父节点、间接环和超过 128 个节点的路径；回收站中的文档仍保有分类引用，必须永久清理或解除引用后才能删除分类。

## 8. 删除、恢复与保留

- 软删除文档：标记 `fn_document_item`；不立即删除 Files Blob；后台 Job 按保留策略清理 **无引用** 的 `FileId`（须与 Files 墓碑清理协调，禁止双写清理逻辑）。
- 恢复：清除删除标记；版本行保持不动。
- 当前按数量保留版本，手工删除和自动裁剪均受最少保留数约束；Runner 已取有效历史保留上限并限制尝试预算。按天清理回收站和法律保全尚未实现，不作为现有能力宣传。
- 永久删除由精确 purge 权限与文档 ACL 约束，管理端保留不可逆操作的明确确认；两库建表定义对分享和权限行配置级联删除。原始访问日志和版本删除审计仍按永久删除清理；第三批新增无父行级联关系的最小删除事实，默认保留90天，不能宣称完整原始审计不可删。
- Files 对账仍只扫描超龄 Pending。第一批已将版本删除、裁剪与永久删除的释放事实同 Document 本地删除原子写入 Outbox，由 Files 实际消费者按精确版本/文件身份幂等释放 Active Claim；不依赖提交后的同步回调。
- 整改在删除本地版本/派生引用的同一事务中写入事务 Outbox 释放请求，携带不可复用的版本引用 Id 与 FileId，并由固定契约推导确切 Claim 幂等键；Files 消费方经自己的事务与既有 Release 边界幂等处理。不得根据“查不到文档”直接删除共享 Blob。引用确认失败可由现有 Pending 对账收敛，Active 释放依赖持久化交付，不以一次 best-effort 调用替代。
- 释放事件归 Document 所有，只有 Files 真实编译期消费者需要时才抽取最小 Contracts 程序集，不放进 Identity.Contracts 或通用 BuildingBlocks。采用已有 Outbox/Inbox 交付能力，单一发布所有权；宕机、重复消息、未知提交、毒消息与重放均必须可追踪。大型永久删除按可恢复的有界批次记录释放请求；标记清理中的文档禁止恢复或新增引用，最终删除不能早于全部本地引用释放意图持久化。
- 永久删除在同一 Document 事务保留最小删除事实（资源 Id、操作者、发生时间、原因与数量摘要），不保留口令、正文或长期文件地址，不随父文档级联清除。保留期限由 Document 强类型策略定义，默认 90 天，清理可分批恢复；这只是开发默认值，不是法律保全承诺。访问原始日志与最小删除事实采用独立保留策略。

### 8.1 预览任务与派生文件

- 已实现超时重领、续租、领取版本校验和 MySQL 事务内 `FOR UPDATE SKIP LOCKED`；匿名任务请求人为空，派生文件归属文档创建者。本批已按实际改动及必要边界重新验收，证据见 §12.4。
- 第三批已实现：转换关闭时拒绝新任务；`GET /api/v1/document/public/preview-capabilities` 只公开启用状态、Provider 键、允许 MIME、大小与尝试上限，不包含密钥或路径。Vue 根据服务端启用状态、MIME 白名单和大小开放入口，任务页保留状态、次数、错误码、取消及精确授权的人工重试。
- 源文件在任务存续期间有明确保留规则，输出 PDF 建立 Document 侧引用与 Files Claim。上传成功但终态更新失败、租约失效、任务或文档被清除时必须可补偿/对账；检查终态更新行数，旧执行者不得提交结果。永久删除后清理派生引用，不影响其他合法引用。

具体闭环不能只增加一次 Claim 调用：

- 创建任务时固定 VersionId 和 SourceFileId，匿名任务来自会话绑定版本；源引用使用独立引用 Id，不能复用版本 Claim。先持久化引用意图，再在事务外 Claim，成功后通过本地状态转换使任务可领取，确认失败交 Pending 对账；尚未 Claim 的任务不能执行。
- 输出上传前持久化任务尝试和上传操作 Id。第一批已扩展 `IHostFileUploadWriter.UploadIdempotentAsync`：相同操作键复用就绪文件，pending/未知状态拒绝盲目新建。第三批已接入源/输出引用意图，并在上传前预留输出 UUID v7 和操作键；`FindUploadAsync` 按操作键和可信所有者恢复就绪收据，pending/未知状态拒绝盲目新上传。“上传成功但尚未保存 FileId 就宕机”重领后恢复同一文件，不再调用转换器，也不查 Files 表或猜测文件名。
- 一个转换任务（含崩溃重领）固定同一逻辑上传操作和独立输出引用，避免非确定性转换结果在恢复时换键生成第二份 PDF；每次领取另存 `preview_attempt` 事实。取得输出后申请该意图的 Claim，再以任务领取版本 CAS 提交成功；陈旧执行者的结果永远不能覆盖新结果。Document 的精确引用 Probe 和保留贡献点同时覆盖版本、源/输出意图；Probe 不得把有效的准备中引用误报为不存在。任务/引用取消必须使迟到确认失效并产生可靠释放请求，引用 Id 不复用。
- 输出失败、失去租约或取消后按 §8 持久化释放；任务重试不会无限生成 PDF。默认最多 3 次尝试（含崩溃重领），有界退避，确定性错误直接终止，人工重试使用 `document.host_preview_tasks.retry` 并建立新任务、新意图及后续领取记录；取消使用 `document.host_preview_tasks.cancel`、文档 write 与任务 Version。读取源、转换、上传及提交共用全链路超时，租约多保留60秒；`LeaseExpiresAtUtc` 在领取/续租时固定，配置热更新不提前回收旧租约。排队任务执行前续租，续租与永久删除共用文档根锁；清理程序不得删除仍有有效租约的尝试资源。

## 9. SQL Server / MySQL 索引与容量

- 列表：`(TenantId, IsDeleted, UpdatedAtUtc DESC, Id)` 非聚集 + 覆盖索引按查询路径裁剪。
- 分类树：`(TenantId, ParentId, SortOrder)`。
- 版本：`(DocumentItemId, VersionNumber)` 唯一；`(FileId)` 非唯一索引用于引用计数查询。
- ACL 已使用 DocumentId、UserId 与 Actions；SQL Server 使用 Host 过滤唯一索引，MySQL 在当前 Host 专属表采用文档/用户唯一键。286 迁移先映射并验证旧级别，再移除旧列；非空数据及部分 DDL 未记账重跑由双库恢复用例覆盖。
- 原草案的“单租户 10 万文档、每文档 200 版本”仅保留为未来容量评估输入，不是当前 Host 产品的已验证限制；实际版本保留按配置执行。分页强制 `pageSize <= 100`；大列表禁止 `SELECT *`。

双库迁移必须成对、可恢复、幂等；迁移号在实施前现场确认。

## 10. Vue 与受影响场景验收

- 路由：`/document/host-items`（列表+详情抽屉）、`/document/categories`、`/document/tags`（可与 Admin.NET 信息架构对齐但非像素级复制）。
- Vue `ui/admin` 是唯一持续交付线，遵守客户端契约与逐操作授权；Layui 冻结，本规格不构成恢复其功能交付的授权。
- 按实际变更选择页面交互与 API 边界场景；不能用模拟响应的页面测试替代 SQL、授权或文件引用的真实行为验证。

## 11. 许可、成本、内容安全与退出条件

| 主题 | 决策 |
| --- | --- |
| **许可** | 不引入 GPL/AGPL 文档预览库；Office/PDF 预览若用商业组件须单独许可证登记。 |
| **对象存储成本** | 当前版本条数可配置；回收站按天清理尚未实现。统计返回聚合值，索引与聚合成本按实际数据评估，静态 SQL 不能证明无全表扫描。 |
| **病毒/内容安全** | 当前未提供原草案中的 IDocumentContentInspector 上传接入；有真实需求时与 Files 就绪状态机共同设计扫描、水印或审批，不在 Document 另建文件生命周期。 |
| **退出条件** | 相关范围本地实际通过即可验收，不以 S3 生产验证代替或阻塞本地功能证据。第三方 SaaS 涉及数据驻留时单独评审；生产与容量结论必须有相应证据。 |

## 12. 验收门禁（实现阶段）

以下按批次记录范围；第一批已实现并通过本批聚焦验收，实际证据见 §12.2。第二批关联实现与聚焦验收已完成；第三批预览运行闭环已实现并通过聚焦验收。

| 批次 | 整改范围 | 关键验收场景 |
| --- | --- | --- |
| 第一批：正确性与访问边界 | 字段/版本快照、完整 PUT、状态转换、ACL 治理与访问修订、分享会话、版本串行化、现有版本 Claim 可靠释放 | 字段缺失 400、显式 null 清空、只改标题不丢其他值；restricted 空名单拒绝内容但治理权限可恢复；草稿/归档分享拒绝；同键并发/响应丢失只扣一次、跨实例凭据有效、跨版本或撤销后拒绝；并发回滚/裁剪保护当前版本和最少保留数；删除最高历史版本后编号不复用；永久删除后 Active Claim 可可靠释放 |
| 第二批：查询与管理体验 | 服务端分页筛选、统计、分类防环、第一批管理流程的页面完善 | 第 101 条可检索、分类包含子孙；用户结果与计数均受 ACL/状态约束；跨日/Range/重试事件口径正确、历史版本不重复计入当前大小；分类并发移动不成环；页面明确权限治理与清空语义 |
| 第三批：预览与清理 | Files 幂等上传结果恢复、源/输出意图与 Claim、能力判断、重领上限、审计保留 | 关闭转换不积压任务；上传成功后宕机可找回同一文件；尚未 Claim 不执行；旧执行者不能提交；取消与迟到确认竞态可收敛（包括释放消息先于 Claim 被消费后的可靠补偿）；重复释放不误伤其他引用；删除后最小审计按策略保留 |

测试范围遵守 [测试与验证](../../../rules/development-quality.md#11-测试与验证)：只测实际改变的代码和必要边界，数据变化覆盖对应 SQL Server/MySQL 场景，API/AOT 映射与 Vue 变化覆盖各自受影响路径。功能批次完成后集中验证；文档更新只做结构、链接和差异检查，不自动运行全量测试。

批次是优先级与依赖安排，不是三个可独立宣称完整交付的产品版本：分享与 ACL/状态修订必须在第一批一起闭合；第三批复用第一批的释放事件与会话，不另建一套匿名凭据。已发现的引用泄漏不得等到第三批才处理现有版本删除路径。对本次改变的迁移覆盖半完成恢复、非空数据与约束收紧；Host 唯一键不能依赖两库 NULL 唯一性恰好一致。仅文档审查不运行上述未来行为测试。

### 12.1 已有修复的证据入口

- ACL 查询与操作检查：[DocumentPermissionSql](../../../src/Modules/Full.NET.Modules.Document/Persistence/DocumentPermissionSql.cs)、[DocumentAccessPolicy](../../../src/Modules/Full.NET.Modules.Document/Security/DocumentAccessPolicy.cs)。
- 任务领取恢复：[DocumentPreviewTaskSql](../../../src/Modules/Full.NET.Modules.Document/Persistence/DocumentPreviewTaskSql.cs)、[DocumentPreviewTaskRunner](../../../src/Modules/Full.NET.Modules.Document/PreviewTasks/DocumentPreviewTaskRunner.cs)。
- 保留预算：[DocumentVersionRetentionRunner](../../../src/Modules/Full.NET.Modules.Document/Retention/DocumentVersionRetentionRunner.cs)。
- Claim 对账边界：[HostFileReferenceClaimSql](../../../src/Modules/Full.NET.Modules.Files/Persistence/HostFileReferenceClaimSql.cs)、[PendingHostFileReferenceClaimReconciliationRunner](../../../src/Modules/Full.NET.Modules.Files/Reconciliation/PendingHostFileReferenceClaimReconciliationRunner.cs)；两者只处理超龄 Pending，不是 Active 释放兜底。
- 现有跨模块读取：[IHostFileDescriptorReader](../../../src/Modules/Full.NET.Modules.Files.Contracts/IHostFileDescriptorReader.cs)、[IHostUserBatchSelectionDirectory](../../../src/Modules/Full.NET.Modules.Identity.Contracts/IHostUserBatchSelectionDirectory.cs)；复用这些真实消费者契约，不另建空抽象。
- 现有授权/领取集成测试源码：[ProjectReviewRemediationAssertions](../../../tests/Full.NET.IntegrationTests/Document/ProjectReviewRemediationAssertions.cs)。测试源码存在不代表本轮执行通过，未来应选择直接受影响场景，避免因共享测试文件触发无关模块验收。

### 12.2 第一批实施与聚焦证据（2026-10-24）

- 代码基线为本计划记录的 HEAD；新增迁移 286/287 成对交付。实现入口和精确命令见[第一批计划](../plans/2026-10-24-document-batch-one.md)。
- 已通过：本批契约/授权/保留预算 Unit、双库业务流程及非空迁移恢复、双库运行时 OpenAPI 导出、相关 Vue/共享契约和 Edge 桌面/移动交互、受影响架构边界、Host.Api 完整闭包 AOT/Trim 静态分析。各项测试数量以测试矩阵为机器事实源，实际执行结果在计划中记录。
- 最终追加的关联日志分批删除、回滚/裁剪竞争、共享引用精确释放及流故障释放均已通过对应检查；未运行全量测试、原生发布或原生外部进程测试。浏览器使用受控 HTTP，不替代双库 Integration；无生产或容量状态升级。

### 12.3 第二批实施与聚焦证据（2026-10-24）

- 第二批已完成：服务端组合筛选和分类子孙分页、Published 当前可读范围统计、当前/历史字节分离、UTC 逻辑事件与 Range/重试去重、分类树及引用的事务闸门、完整元数据清空、Vue 服务端总数与标准操作组及移动布局。
- 288 双库迁移和未记账半完成恢复、真实双库业务及必要回归、运行时 OpenAPI 导出、生成零漂移、相关 Unit/客户端用例与 Document 模块 AOT/Trim 分析通过；命令、实际数量和限制见[第二批计划](../plans/2026-10-24-document-batch-two.md)。
- Office 输出计数已通过现有双库用例的直接补验：生产 Runner 配合受控 PDF 转换器，匿名会话及已认证访问者分别重复读取输出，各记一次 preview；转换引用生命周期由第三批闭环处理，见 §12.4。
- Edge 桌面/移动使用受控 HTTP，包含服务端分页参数、清空、键盘确认及相关生命周期菜单回归；不作为真实浏览器到数据库全链路证据。未执行全量、原生发布、生产或容量验收；后续第三批证据见 §12.4。

### 12.4 第三批实现与聚焦证据（2026-10-24）

- 迁移289成对新增 `preview_reference`、`preview_attempt`、`deletion_fact` 与任务领取字段，结构及状态约束独立探测，支持未记账的半完成重跑。升级前缺少引用身份的排队任务终止为需重新创建，不猜测旧引用；其原文件仍按已有任务/版本保留，用户可显式新建任务。
- 任务创建先固定实际 VersionId、SourceFileId、文档创建者及源/输出独立引用，源 Claim 成功才进入 pending。准备中意图同时参与精确 Probe 和文件保留；确认失败交 Files Pending 对账。输出先固定文件 UUID 与上传键，恢复 ready 收据后直接提交，陈旧领取版本不能提交。
- 复审补充：输出在确认前、确认后均于根锁内检查任务领取版本、持久租约与独立引用意图；先消费释放的迟到 Claim 不能被确认成 Active。已进入释放流程则重发可靠释放事实；确认前宕机保留可对账 Pending，确认后宕机仍有已建立 Claim 可供可靠释放。Vue 的活动文本、独立 SVG/XHTML 预览按纯文本展示；SVG 图片仅在受限 img 模式使用。下载通过无 opener 的链接导航且延迟回收对象 URL，匿名 Office 超限时不提交转换。
- API/Worker 使用同一转换配置与 Provider 注册入口，Worker 独立装配可解析完整预览链路；转换关闭仍执行引用及审计维护，已通过对应聚焦 Unit。
- 领取默认最多3次，含崩溃；瞬时转换失败按次数有界退避，确定性错误终止。每次领取保存事实，过期尝试写入原因。人工重试使用新任务及新引用；源保留到任务取消或永久删除，成功输出保留到文档永久删除。
- 取消、失败和永久删除在本地事务持久化释放。有效租约内先保存 `release_pending` 与截止，并写入带 NotBefore 的 Outbox 事实；提前交付只确认收悉，不消耗失败预算。Document 维护在到期后以本地状态 CAS 再次写 Outbox，即使父任务已清理也能恢复；重复释放不影响原版本或其他任务。转换关闭仍维护引用与审计。
- 最小删除事实保存资源、操作者、时间、原因及开始删除时的版本/任务数量，无口令、正文或地址，默认90天；原始日志仍可随删除清理。实际代码、命令、运行证据和未验证边界见[第三批计划](../plans/2026-10-24-document-batch-three.md)。模块整体不升级为 Verified、Production-verified 或容量通过。

- 第三批的真实双库流程、迁移半完成恢复、既有匿名输出回归、续租/清理竞争、Worker 单独装配、运行时契约与生成、相关 Vue/客户端、Edge 桌面/移动及两模块 AOT/Trim 静态分析均已通过；未运行全量测试。具体实际结果、失败夹具修正和限制见第三批计划。

## 13. 批准记录

| 角色 | 决定 | 日期 |
| --- | --- | --- |
| Gate G4 评审 | Approved for implementation | 2026-08-02 |
| 当前用户 | 要求按静态审查建议更新相关文档；本次同步现状、整改方向与验收场景，未授权本轮代码实施 | 2026-10-24 |
| 当前用户 | 要求复核方案并直接修正文档问题；补齐协议、状态、授权、可靠释放与失败恢复语义，代码仍待实施 | 2026-10-24 |
| 当前用户 | 授权按更新方案实施第一批，关联功能集中完成后仅做本次修改和必要边界的聚焦验收，后续多次要求继续 | 2026-10-24 |
| 当前用户 | 授权继续第二批，关联功能集中完成后仅做本次修改和必要边界的聚焦验收，不跑全量测试 | 2026-10-24 |
| 当前用户 | 授权继续第三批，关联功能集中完成后仅做本次修改和必要边界的聚焦验收，不跑全量测试 | 2026-10-24 |

本次修订替代旧稿中的双端交付、旧权限码/表名、匿名分享与 Office 预览完全未实现、已删版本必须保留行及 S3 生产验证前置等过时描述；原 2026-08-02 批准记录和历史验证事实保留。未实现的整改目标不能作为当前能力证据，三批均已获当前用户明确实施授权，并按修改与必要边界完成聚焦验收。
