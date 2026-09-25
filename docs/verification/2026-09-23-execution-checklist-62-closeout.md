# 执行清单 62 — Document 版本保留策略与授权删除 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **62**（**依赖 47** 回滚已 closeout）；Host 文档 **历史版本保留策略**（配置 + Worker 自动裁剪）、**授权删除**非当前版本、删除审计与 Files **Claim** 一致性；**当前版本不可删**、未引用版本才释放 Claim。

## 选定能力

| 项 | 值 |
|----|-----|
| 策略 API | `GET/PUT /api/v1/document/host/version-retention` |
| 手动删除 | `POST …/items/{itemId}/versions/{versionId}/delete` + 乐观锁 `Version` |
| 权限 | `document.host_documents.delete_version`；策略更新复用 `document.host_documents.update` |
| 策略域 | `DocumentVersionRetentionPolicy`（最小保留数、历史上限） |
| Worker | `DocumentVersionRetentionHostedProcessor` / `DocumentVersionRetentionRunner` |
| 审计 | `fn_document_version_deletion_audit`（`InsertDeletionAudit`） |
| Files | `DocumentHostFileRetentionContributor`（版本行存在则仍引用） |
| Vue | `DocumentStatisticsView`「版本保留」页签；`HostDocumentItemsView` 历史删除按钮 |
| 错误码 | `version_already_current`（删当前 **409**）；`version_minimum_retained`（触底 **422**） |

## 与 47 的分工

| 编号 | 能力 |
|------|------|
| **47** | 回滚指针，**不**删版本行、**不**释放 Claim |
| **62** | 删历史版本行 + 审计 + Claim 释放；自动裁剪按策略 |

## 清单 62 验收结论

- **已有**：UI 仅对非当前版本展示删除；集成 `DocumentHostItemAssertions.VerifyDeleteVersionAsync`（双库 Host 切片）。
- **本槽**：`phase-c-62-document-version-retention-delete.spec.mjs`；`document-real-stack.mjs` 增补 delete/retention 辅助。
- **未验**：大规模版本表下 Worker 裁剪耗时与跨存储灾备（容量类）。

## 停止边界

- 不回滚式「删除」；47 已覆盖指针回滚。
- DC02 显式回滚 UI 与 47 重叠；本槽聚焦 **保留/删除/审计**，非重复立项回滚。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`DocumentVersionRetentionPolicy` + `DocumentAuthorizationContributor` | **4/4**（`d40de4e6`） |
| `node --test` `document-host-version-retention-contract.test.mjs` | **1/1** |
| 集成 | `DocumentHostItemAssertions`（含删除版本，随 integration-matrix） |
| real-stack | `phase-c-62-document-version-retention-delete.spec.mjs`（需 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
