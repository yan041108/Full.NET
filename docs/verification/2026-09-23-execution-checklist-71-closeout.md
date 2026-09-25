# 执行清单 71 — Printing 模板版本 / 数据绑定 API 与预览打印页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **71**（首种固定表单 **`printing.tenant_profile_card`**；模板草稿/发布版本 API；`PrintingFormBindingService` 服务端绑定；`POST …/preview` 生成 HTML；`PrintingPreviewView` + DOMPurify + `window.print`；**无**硬件打印服务）。

## 选定切片

| 项 | 值 |
|----|-----|
| 表单 Schema | `GET /api/v1/printing/form-schemas`（字段固定，客户端不可扩展） |
| 模板 API | `GET/POST/PUT /api/v1/printing/templates`；`publish`；`versions` |
| 预览 | `POST …/templates/{id}/preview`（租户上下文绑定 + `PrintingHtmlRenderer`） |
| 布局边界 | `PrintingLayoutPolicy.MaxLayoutHtmlLength`（64 KiB） |
| 绑定源 | `IPrintingTenantProfileBindingSource`（租户名称/编码/域名 + 打印人/时间） |
| Vue | `PrintingPreviewView`（创建并发布、预览、浏览器打印；净化 HTML） |
| 权限 | `printing.form_schemas.read`；`printing.templates.*`（含 `.preview`） |

## 交付锚点

| 层 | 位置 |
|----|------|
| 目录 | `PrintingFormSchemaCatalog` / `BrowseFormSchemas` |
| 管理 | `PrintingTemplateManagementService` / `PrintingTemplateQueryService` |
| 预览 | `PrintingTemplatePreviewService` / `PrintingFormBindingService` |
| 渲染 | `PrintingHtmlRenderer`（占位符 `{{fieldKey}}`） |
| 持久化 | `PrintingTemplateSql`（`SqlDataScope.HostOnly`） |
| 契约 | OpenAPI `PrintingFormSchemas` / `PrintingTemplates` / `PrintingPreviews` |
| 单元 | `PrintingFormSchemaCatalogTests`、`PrintingHtmlRendererTests`、`PrintingAuthorizationContributorTests` |
| Vitest | `PrintingPreviewView.test.ts`（DOMPurify 剥离可执行标记） |

## 清单 71 验收结论

- **已有**：未知 Schema **404**；未发布模板预览 **422**；无租户上下文绑定失败。
- **本槽**：`phase-c-71-printing-preview.spec.mjs`；`printing-real-stack.mjs`。
- **未验**：real-stack 对已发布种子模板的完整 preview **200**（依赖种子模板）；多表单 Schema 扩展另编号。

## 停止边界

- **72**：AI 模型配置/连通性/额度（与 Printing 无耦合）。
- PDF 服务端渲染、硬件打印机驱动不在本槽。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~Printing` | **4/4**（`d40de4e6`） |
| `pnpm exec vitest run` `PrintingPreviewView.test.ts` | **2/2** |
| OpenAPI | `OpenApiOperationIdentityRulesTests` 登记 printing 路径 |
| real-stack | `phase-c-71-printing-preview.spec.mjs`（预览 API 需租户上下文） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
