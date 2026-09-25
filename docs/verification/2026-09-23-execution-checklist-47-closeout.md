# 执行清单 47 — Host Document 版本回滚 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **47**（历史版本回滚为新的当前版本/受控指针变更；权限、并发守卫；历史版本行不静默覆写、回滚不重新上传文件）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `POST /api/v1/document/host/items/{itemId}/versions/{versionId}/rollback` |
| 服务 | `HostDocumentItemManagementService.RollbackVersionAsync` / `RollbackVersionCoreAsync`（仅切换 `CurrentVersionId` + 乐观锁 `Version`） |
| 权限 | `document.host_documents.rollback_version`（迁移 `146` 自 update 角色幂等授予） |
| 错误码 | `document.host_document.version_already_current`（重复回滚 **409**） |
| Vue | `HostDocumentItemsView`（版本历史、`host-document-item-version-rollback`、确认框） |
| 客户端 | `rollbackDocumentVersion`（`host-document-items.ts`） |
| 集成 | `DocumentHostItemAssertions.VerifyRollbackVersionAsync`（双库 `DocumentHostItemAssertions`） |
| Files | 回滚 **不** 触发新 Claim；新版本上传仍走既有 `versions/upload` 与 Claim 边界（同夹具 `DocumentFilesReferenceClaimAssertions`） |

## 核验结论

- **已有**：回滚只移动当前指针，保留全部版本行与内容哈希；与「删除版本」路径分离（删除才释放 Claim）。
- **本槽**：新增 real-stack `host-document-version-rollback.spec.mjs`（API 双版本回滚 + 重复冲突；UI 版本历史回滚）；`document-real-stack.mjs` 辅助上传/回滚。
- **未验**：跨实例 Files 存储灾备与大规模版本表性能（容量类，非本槽）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`DocumentAuthorizationContributor` | **1/1** |
| real-stack | `host-document-version-rollback.spec.mjs`、`host-documents.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。

**Phase B（19–47）**：本编号为 B 区串行最后一槽（23 已关闭跳过）。
