# 执行清单 32 — Files 批量上传/删除 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **32**（批量上传逐条结果、文本预览、批量删除与逐项反馈；引用中文件删除失败见集成断言）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `POST …/batch-upload`、`POST …/batch-delete`、`GET …/preview` |
| 服务 | `HostFileManagementService.BatchUploadAsync` / `BatchDeleteAsync` |
| Vue | `HostFilesView.vue`（多选上传、`host-files-batch-delete`、预览对话框） |
| 集成 | `FilesHostFileManagementAssertions.VerifyBatchUploadDeleteAndPreviewAsync` |
| 单元 | `HostFileBatchOperationsTests` |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`HostFileBatchOperationsTests` | 批量边界与预览 MIME |
| real-stack | `host-files.spec.mjs` 增补清单 32 |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
