# Files 上传取消后的补偿删除实施计划

**Goal:** Blob 已成功落盘而数据库事务因请求取消失败时，仍完成不可取消的本地补偿删除，避免生成没有数据库墓碑、后台清理器也无法发现的物理孤儿。

**Task snapshot:** `files-upload-compensation-cancellation-20260730`

## 根因链

1. `HostFileManagementService.UploadAsync` 在进入数据库事务前先保存 Blob。
2. 数据库事务失败后，上传路径把原请求 `CancellationToken` 继续传给 `TryDeleteBlobAsync`。
3. `LocalHostFileBlobStorage.DeleteAsync` 在文件删除前调用 `cancellationToken.ThrowIfCancellationRequested()`。
4. 请求令牌已取消时，补偿删除在 `File.Delete` 前退出；异常被 best-effort 边界吞掉，物理文件保留。
5. 该文件没有成功插入的元数据或软删除墓碑，因此 `DeletedHostFileBlobCleanupRunner` 无法枚举和修复。

同一服务的删除路径已经在数据库软删除提交后使用 `CancellationToken.None` 删除 Blob，证明补偿动作不应再受已经结束的请求生命周期控制。

## 范围

- 修改 Files 上传服务与 Files Unit 测试。
- 不修改 API/JSON、数据库、迁移、配置、Worker、前端、路线图或测试矩阵。
- 外层上传调用仍必须传播原始取消；只让回滚 Blob 的内部补偿不再被已取消请求阻断。
- 补偿仍保持 best-effort；文件系统删除自身失败时不覆盖原始事务异常。

## TDD 步骤

- [x] 新增 `HostFileManagementServiceTests`，使用真实 `LocalHostFileBlobStorage` 和临时目录。
- [x] 记录型事务在 Blob 保存完成后取消请求令牌并抛出 `OperationCanceledException`。
- [x] 运行聚焦 Unit，确认旧实现传播取消但临时目录仍残留一个最终 Blob。
- [x] 只把上传事务失败后的补偿调用改为 `CancellationToken.None`。
- [x] 复跑聚焦 Unit，确认原取消继续传播且临时目录无最终文件或 `.uploading` 文件。
- [x] 复跑全部 Files Unit。
- [x] 完成精确格式检查、Files 模块 Release 构建与 `git diff --check`。
- [x] 等 Docker 队列释放后完成基于任务快照的 affected slice 验证。

## 验收边界

- 成功路径、输入校验、数据库插入和下载契约不变。
- 不增加后台扫描或重试机制；无墓碑历史孤儿仍按运维差异清单处理。
- 本切片只阻止新产生的“保存成功、事务取消、补偿令牌也已取消”孤儿。
