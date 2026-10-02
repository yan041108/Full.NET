# 私有知识库目录与模型处理审批

R04a 在 `Full.NET.Modules.Ai` 内提供私有目录及审批记录。当前不接收文件、不解析文档、不调用模型，也没有成员共享、检索或问答。文档授权与文件生命周期由 R04b 继续交付；R05/R06 的每次处理派发必须复核这里记录的审批及最新权威状态。

## 目录 API

基础路径为 `/api/v1/ai/knowledge-bases`。所有请求均使用已认证主体和可信租户上下文；请求体不能指定 TenantId、OwnerUserId 或其他未声明字段。拥有端点权限、Host 管理权或超级管理员身份，都不能直接读取其他所有者或其他租户的目录。越界读取和更新统一返回 404。

| 操作 | 方法与相对路径 | 精确权限 | 请求字段 |
| --- | --- | --- | --- |
| 本人目录分页 | `GET /` | `ai.knowledge_bases.read` | `page` 默认 1，`pageSize` 默认 20；每页 1–100，偏移不超过 100000 |
| 本人目录详情 | `GET /{knowledgeBaseId}` | `ai.knowledge_bases.read` | UUID 标识 |
| 创建私有目录 | `POST /` | `ai.knowledge_bases.create` | `name`、可空 `description` |
| 编辑目录 | `PUT /{knowledgeBaseId}` | `ai.knowledge_bases.update` | `name`、可空 `description`、`isEnabled`、`version` |
| 更新处理审批 | `PUT /{knowledgeBaseId}/policy` | `ai.knowledge_bases.policy_update` | `dataClassification`、两组可空模型标识/版本、目录 `version` |

名称去除首尾空格后须非空且不超过 200 字符，描述不超过 2000 字符。列表按创建时间与主键倒序，配合租户、所有者前缀索引；暂不支持任意排序和无界导出。

创建成功返回 201 与 Location；读取和更新成功返回 200。输入/字段错误返回 400，模型配置不可用返回 422，版本竞争返回 409，未认证/无精确权限分别返回 401/403。错误使用标准 ProblemDetails 与 `ai.knowledge.*` 稳定机器码，标题支持 `zh-CN`/`en-US`。

## 审批边界

新目录默认 `dataClassification=internal`、`isEnabled=true`，Embedding 与生成模型的标识和版本均为 null。`public`、`internal`、`restricted` 只是数据分类，任何分类都不自动批准模型处理。网络连通性、Provider 名称（包括 `ollama`）或目录编辑权限也不构成审批。

Embedding 使用 `embeddingModelConfigId` 和 `embeddingModelVersion`；生成使用 `generationModelConfigId` 和 `generationModelVersion`。一组审批必须同时为空（撤销），或同时提供非空 Guid 与正版本。批准时只能选择当前范围可用、已启用且版本完全匹配的模型配置；租户可以选择本租户模型或 Host 共享模型，不能批准其他租户模型。审批不授予模型管理权限，也不返回凭据和模型目的地。

目录编辑与审批共用 `version`，每次成功变更递增一次。普通编辑不修改审批字段；并发写入中只有匹配当前版本的请求能提交，失败结果由事务回滚。没有强制覆盖入口。

R05/R06 接入消费者时，还必须检查：知识库启用、精确审批模型及配置版本仍匹配、文档/版本未删除且仍授权、模型仍启用和可用、网络/预算/Provider 入口门禁全部允许。模型目的地变更、配置版本漂移或审批撤销后，旧审批不能授权下一次派发；尚未实现消费者前不能宣称数据外发链路已经验收。

## 双库与部署

迁移 `240_AiKnowledgeBase.sql` 在 SQL Server 与 MySQL 增加 `fn_ai_knowledge_base`，不改写已有业务表。目录仅保存本模块标识，不向 Identity 或 Tenancy 表建立外键。SQL Server 主键显式采用 UUID v7 聚集键；MySQL 使用 Binary16 网络字节序。

建表、后置索引与 SQL Server 注释支持未记账重放。暂停旧应用后可前滚迁移；如需应用回退，保留新增表即可，旧应用不读写该表，不需要删除目录数据。迁移不播种目录或默认审批。

验收证据与任务状态统一记录在[活动开发计划 R04](../superpowers/plans/2026-09-08-ai-agentic-web-alignment.md)。R04a API 无 Vue 页面入口；生成客户端、文档上传与页面按后续切片交付，整体 R04/RAG 和容量状态不能由目录验收推导。
