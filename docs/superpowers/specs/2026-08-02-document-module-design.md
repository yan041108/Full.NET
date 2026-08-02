# Document 模块设计规格

**状态：** Approved for implementation（Gate G4，2026-08-02）
**日期：** 2026-08-02
**适用范围：** `Full.NET.Modules.Document`（单主项目垂直切片）、Host/Tenant 文档库、双管理端、SQL Server/MySQL

## 1. 决策摘要

Admin.NET.Pro `Plugin.Document` 提供分类、标签、版本、分享、权限、预览、回收站与统计，但其把文件字节与业务元数据耦在插件表内，并依赖 SqlSugar/反射式权限。Full.NET 只吸收产品语义，不复制实现。

Document 模块拥有文档元数据、分类/标签、版本链、分享与 ACL；**文件字节唯一经 Files 模块 `FileId` 引用**，禁止 Document 直连 `fn_files_file`、禁止在业务表存储 Blob/Base64。首个切片以 Host 文档库为主，租户文档库在 Host 切片 `Build-verified` 后再开独立纵向切片。

批准本规格只把能力从 `Mapped` 提升到 `Planned`；实现仍须遵守 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md) 与双库/双端门禁。

## 2. 目标与非目标

### 2.1 目标

- Host 文档：创建、元数据编辑、分类/标签、版本上传、列表/详情、软删除/恢复、分享链接（受限）、权限拒绝与审计。
- 通过 Files 上传/下载 API 或模块内注入的 **Files 只读服务契约** 消费对象存储；Document 持久化 `FileId` + 可选 `ContentHash` 快照。
- 组织数据范围：租户文档默认受 `Organization` 机构树与用户-机构隶属过滤；Host 文档 `TenantId IS NULL`。
- 双库等价迁移、恢复测试、标准 ProblemDetails API、Vue/Layui 同步页面与真实栈 E2E。

### 2.2 非目标（1.0 / 首切片）

- 在线协同编辑、全文检索集群、Office 转 PDF 预览服务、病毒扫描生产级引擎（仅定义接入点与退出条件）。
- 任意用户自定义字段、动态 SQL 报表、Workflow/DataApproval 联动（后续模块通过显式契约集成）。
- 租户文档库、跨租户分享、公开匿名下载（无账户分享链接若做，必须限 Host 且独立 Decision Gate）。
- 把 Document 文件存入业务表或绕过 Files Provider 路由。

## 3. 依赖与边界

| 依赖 | 边界 |
| --- | --- |
| **Files** | 仅通过 `Full.NET.Modules.Files.Contracts` 公开类型与 Host 文件 API/应用服务端口引用 `FileId`；Document SQL 不得 JOIN `fn_files_file`。上传走 Files 已有 pending→ready 状态机；删除文档版本时按策略调用 Files 软删除或保留引用计数。 |
| **Identity/Tenancy** | 权限码、会话、租户解析与现有 RBAC；分享令牌校验走 Identity 会话或一次性签名链接（首切片可选仅登录用户 ACL）。 |
| **Organization** | 租户文档读写在 SQL 层施加机构数据范围；Host 文档不使用机构过滤。 |
| **Auditing** | 元数据变更写 B1 行为审计（非 Outbox）；大文件字节不写审计正文。 |
| **字段投影** | 文档列表/详情可选暴露 `description` 等 Internal 字段；须注册语义资源目录后再做受限列，首切片可仅 Host 全字段。 |

## 4. 作用域模型

| 作用域 | `TenantId` | 数据范围 | 首切片 |
| --- | --- | --- | --- |
| Host 文档库 | `NULL` | 全 Host 可见性由文档 ACL + `document.host_documents.*` 权限控制 | **交付** |
| 租户文档库 | 非空 | 机构树 + 用户隶属 + 文档 ACL | 后续切片 |

逻辑删除：所有用户可见表含 `IsDeleted`、`DeletedAtUtc`、`DeletedById`；唯一约束在 **未删除** 行上生效（过滤唯一索引或等价双库模式，与现有 Organization/Files 模式一致）。

## 5. 数据模型（草案）

命名遵循 [`naming-conventions.md`](../../../rules/naming-conventions.md)：`fn_document_*`，UUID v7 主键，PascalCase 列。

| 表 | 用途 |
| --- | --- |
| `fn_document_category` | 树形分类；`ParentId`、`Name`、`SortOrder`、作用域键 |
| `fn_document_tag` | 标签字典；租户/Host 分区 |
| `fn_document_tag_assignment` | 文档-标签多对多 |
| `fn_document_item` | 文档主记录：标题、描述、分类、当前版本指针、作用域 |
| `fn_document_version` | 不可变版本行：`FileId`、`VersionNumber`、`ContentHash`、`SizeBytes`、`UploadedById` |
| `fn_document_acl` | 主体（用户/角色）对文档的 `read`/`write`/`share`/`delete` 授权 |
| `fn_document_share_link` | 可选：限时分享令牌摘要、过期、最大访问次数（首切片可推迟） |

**不变量：**

- `fn_document_version.FileId` 必填；同一 `DocumentItemId` 的 `VersionNumber` 单调递增且唯一（含软删版本行的处理策略在迁移中显式定义：已删版本保留行、不参与“当前版本”指针）。
- 禁止 `FileId` 跨租户复用；Host 文档引用的 `FileId` 必须来自 Host Files 上传。
- 分类/标签名称在同一父节点 + 作用域内唯一（未删除行）。

## 6. 版本、分享与权限

### 6.1 版本

- 新版本 = 新 Files 上传 + 插入 `fn_document_version` + 原子更新 `fn_document_item.CurrentVersionId`。
- 回滚 = 将当前指针切到既有版本 **不删除** 较新版本行（Admin.NET 语义对齐）。
- 下载/预览始终解析当前版本或显式 `versionId`；无权限时 403，不存在 404。

### 6.2 权限码（草案）

| 权限 | 说明 |
| --- | --- |
| `document.host_documents.read` | Host 文档列表/详情/下载 |
| `document.host_documents.write` | 创建/更新元数据/上传新版本 |
| `document.host_documents.delete` | 软删除/恢复 |
| `document.host_documents.share` | 管理 ACL 与分享链接 |
| `document.categories.manage` | 分类维护 |
| `document.tags.manage` | 标签维护 |

租户权限在租户切片时增加 `document.tenant_documents.*` 前缀，不得与 Host 权限混用。

### 6.3 分享

- 首切片：仅登录用户 ACL（用户/角色授予）。
- 外链分享：须单独 ADR；令牌只存哈希、可撤销、强制过期与审计；禁止在 URL 携带长期 Files 直链。

## 7. API 形状（草案）

基路径 `/api/v1/document/host/...`，标准分页与 ProblemDetails。

- `GET/POST /items`，`GET/PUT/DELETE /items/{id}`
- `POST /items/{id}/versions`（body：`fileId` 来自 Files 上传完成）
- `POST /items/{id}/restore`，`GET /items/{id}/versions`
- `GET/POST /categories`，`GET/POST /tags`
- `GET/PUT /items/{id}/acl`

OpenAPI 独立契约文件；JSON 使用源生成 Context。

## 8. 删除、恢复与保留

- 软删除文档：标记 `fn_document_item`；不立即删除 Files Blob；后台 Job 按保留策略清理 **无引用** 的 `FileId`（须与 Files 墓碑清理协调，禁止双写清理逻辑）。
- 恢复：清除删除标记；版本行保持不动。
- 保留类别：纳入 Auditing 既有保留框架的 `Document` 类别（与 Outbound 类似）；默认保留天数可配置，启动期校验。
- 硬删除：仅 Host 管理员 + 强确认；写审计；Files 删除在事务外 best-effort。

## 9. SQL Server / MySQL 索引与容量

- 列表：`(TenantId, IsDeleted, UpdatedAtUtc DESC, Id)` 非聚集 + 覆盖索引按查询路径裁剪。
- 分类树：`(TenantId, ParentId, SortOrder)`。
- 版本：`(DocumentItemId, VersionNumber)` 唯一；`(FileId)` 非唯一索引用于引用计数查询。
- ACL：`(DocumentItemId, PrincipalType, PrincipalId)` 唯一。
- 容量目标（设计级，非认证）：单租户 10 万文档项、每文档 200 版本为软上限；分页强制 `pageSize <= 100`；大列表禁止 `SELECT *`。

双库迁移必须成对、可恢复、幂等；迁移号在实施前现场确认。

## 10. Vue / Layui 与 E2E

- 路由：`/document/host-items`（列表+详情抽屉）、`/document/categories`、`/document/tags`（可与 Admin.NET 信息架构对齐但非像素级复制）。
- 双端共用 `@fullnet/client-contracts` 导航与 API 适配器；权限、403、409、ProblemDetails 一致。
- E2E：真实栈至少覆盖创建→上传版本→列表→下载元数据→软删→恢复→无权限 403；SQL Server + MySQL 各一 spec。

## 11. 许可、成本、内容安全与退出条件

| 主题 | 决策 |
| --- | --- |
| **许可** | 不引入 GPL/AGPL 文档预览库；Office/PDF 预览若用商业组件须单独许可证登记。 |
| **对象存储成本** | 版本保留与回收站天数必须可配置；统计 API 只返回聚合计数/字节，不扫描全表。 |
| **病毒/内容安全** | 首切片不内置扫描；预留 `IDocumentContentInspector` 端口与 `document.content.rejected` 错误码；生产启用须 ADR + 双库集成测试。 |
| **退出条件** | 若 Files 未 `Build-verified` 或 S3 Provider 未在生产验证，不得宣称 Document `Verified`；若预览依赖第三方 SaaS，须数据驻留评审。 |

## 12. 验收门禁（实现阶段）

实现完成需证明：双库迁移恢复、权限拒绝、机构范围（租户切片）、Files 引用不直连表、版本单调性、软删恢复、保留清理、Vue/Layui 对等、OpenAPI 契约、真实栈 E2E、Architecture 不引入 EF/通用 Repository。

## 13. 批准记录

| 角色 | 决定 | 日期 |
| --- | --- | --- |
| Gate G4 评审 | Approved for implementation | 2026-08-02 |

批准前：路线图状态保持 **`Mapped`**。批准后：更新为 **`Planned`** 并创建实施计划，不得跳过批准直接编码。