# 执行清单 64 — Office→PDF 转换 Provider 与预览任务页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **64**（可插拔 **Office→PDF** Provider、`external_http` / `external_process` / `disabled`；Host **预览任务** API + Worker + `DocumentPreviewTasksView`；**MaxInputBytes**、超时、隔离临时目录；**无** Tenant 文档作用域）。

## 选定 Provider

| ProviderKey | 实现 |
|-------------|------|
| `external_http` | `ExternalHttpDocumentOfficePreviewConversionProvider` |
| `external_process` | `ExternalProcessDocumentOfficePreviewConversionProvider` |
| `disabled` | `DisabledDocumentOfficePreviewConversionProvider`（任务入队后快速失败/不转换） |

配置节：`Document:OfficePreviewConversion`（`Enabled`、`ProviderKey`、`MaxInputBytes`、`TimeoutSeconds`、`TempRootPath` 等）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `POST/GET /api/v1/document/host/preview-tasks`（+ `/{id}`、`/content`） |
| 分享匿名 | `POST …/public/shares/{shareCode}/preview-task` |
| Worker | `DocumentPreviewTaskHostedProcessor` / `DocumentPreviewTaskRunner` |
| 策略 | `DocumentOfficePreviewSourcePolicy`（Office MIME 白名单） |
| Vue | `DocumentPreviewTasksView`（筛选、提交转换、打开 PDF） |
| 权限 | `document.host_preview_tasks.read` / `.create` |
| 契约 | `document-host-preview-tasks-v1.json` |
| 集成 | `DocumentAdminNetParityAssertions.VerifyPreviewTasksAsync` |

## 清单 64 验收结论

- **已有**：非 Office 源 **422** `document.office_preview.unsupported_source`；默认 Provider 常为 `disabled`（real-stack 不声称真实 PDF 产出）。
- **本槽**：`phase-c-64-document-office-preview-tasks.spec.mjs`；`document-real-stack.mjs` Office 上传与任务 API 辅助。
- **未验**：生产字体/许可证、真实 LibreOffice 或 HTTP 转换网关端到端（需运维 Provider 与外网）。

## 停止边界

- **DC05**：Tenant 文档 **不** 在本槽扩展；仅 Host 路由。
- 不与 FL02 通用 Files 预览合并；MVP 直读预览仍走 `items/{id}/preview`（非 Office PDF）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`DocumentOfficePreview` + `HostDocumentPreview` | **3/3**（`d40de4e6`） |
| `node --test` `document-host-preview-tasks-contract.test.mjs` | **1/1** |
| real-stack | `phase-c-64-document-office-preview-tasks.spec.mjs`（需 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
