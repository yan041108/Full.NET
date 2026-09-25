# 执行清单 78 — GoView 首个大屏项目保存/发布/只读预览 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **78**；Host 域 `fn_goview_project` / 发布版本表；`/api/v1/goview/projects` CRUD + `publish` + `versions` + `preview`；`GoViewCanvasValidator` 结构边界；`ui/admin` 项目列表 / JSON 画布编辑 / 只读预览（**非**独立 GoView OSS 二进制客户端）；预览仅返回已发布 `canvasJson` 快照，**无**大屏直连业务库或绕过 API 的查询端点。

## 选定切片

| 项 | 值 |
|----|-----|
| 权限 | `goview.projects.read` / `.create` / `.update` / `.publish` / `.preview` |
| 管理 API | `GET/POST /api/v1/goview/projects`；`PUT` 草稿；`POST …/publish`；`GET …/versions` |
| 预览 API | `POST …/preview` → `GoViewProjectPreviewResponse`（`GeneratedAtUtc` + 快照 JSON） |
| 画布 | 默认 `GoViewCanvasPolicy.DefaultCanvasJson`；最大 2MB；`width`/`height`/`components[]` |
| 数据范围 | `SqlDataScope.HostOnly` |
| Vue | `GoViewProjectsView`；`GoViewEditorView`（草稿保存/发布）；`GoViewPreviewView`（`goview-preview-stage`） |
| 契约 | `packages/client-contracts/src/goview-projects.ts`；`ui/admin/src/api/goview-projects.ts` |

## 交付锚点

| 层 | 位置 |
|----|------|
| 服务 | `GoViewProjectManagementService`；`GoViewProjectPreviewService` |
| 校验 | `GoViewCanvasValidator`；`GoViewCanvasPolicy` |
| 单元 | `GoViewCanvasValidatorTests`；`GoViewAuthorizationContributorTests`（合计 **5/5** GoView 过滤器） |
| Vitest | `GoViewProjectsView.test.ts`；`GoViewEditorView.test.ts`；`GoViewPreviewView.test.ts` |

## 清单 78 验收结论

- **已有**：发布版本不可变快照；未发布时 preview 422；启用校验。
- **本槽**：`phase-c-78-goview-projects-publish-preview.spec.mjs`；`goview-real-stack.mjs`。
- **未验**：素材上传/背景图批量、独立 GoView 设计器二进制、动态数据源绑定；K3Cloud 见 **79** closeout。

## 停止边界

- **79**：K3Cloud 固定单据认证/提交适配。
- 禁止在预览 API 暴露 SQL/Reporting 端口或租户业务表只读旁路。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~GoView` | **5/5**（`d40de4e6`） |
| `pnpm exec vitest run` `GoViewProjectsView` + `GoViewEditorView` + `GoViewPreviewView` | **3/3** |
| OpenAPI | `goviewListProjects` / `goviewPublishProject` / `goviewPreviewProject` |
| real-stack | `phase-c-78-goview-projects-publish-preview.spec.mjs`（需 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
