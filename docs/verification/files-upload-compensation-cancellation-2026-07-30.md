# Files 上传取消补偿删除验证记录

## 范围

- 上传 Blob 已落盘、数据库事务随后取消时，补偿删除不再继承已取消的请求令牌。
- 外层 `OperationCanceledException` 继续传播。
- 不修改数据库对象、迁移、API/JSON、配置、Worker、前端、路线图或测试矩阵。

## TDD 证据

RED：

```text
Upload_cancellation_after_blob_save_still_removes_uncommitted_blob
expected: 0
actual:   1
总计: 1，失败: 1
```

GREEN：

```text
HostFileManagementServiceTests：1/1 通过
Full.NET.UnitTests.Files：11/11 通过
共享全 Unit 新鲜发现与执行：744/744 通过
Full.NET.UnitTests Release build：0 warning，0 error
Full.NET.Modules.Files Release build：0 warning，0 error
生产文件与测试文件精确格式检查：通过
切片文件 git diff --check：通过
```

## 行为结论

事务失败后的 Blob 补偿删除使用 `CancellationToken.None`，因此请求已取消也不会在物理删除前退出；补偿仍由既有 best-effort 边界保护，不会覆盖原始事务异常。

## Affected 计划

任务快照的 slice 计划命中 `CodeGeneration`、`Files`、`Realtime`。当前 Docker 依次由 CodeGeneration、Jobs 独占，本窗口按共享工作区队列等待二者 teardown 后再执行，避免容器与 Integration 输出互相污染。

CodeGeneration 窗口已在本变更落盘后完成一次共享工作区新鲜 affected slice：

```text
tooling：39/39 通过
smoke：8/8 通过
CodeGeneration + Files + Realtime：28/28 通过
Integration Release rebuild：0 warning，0 error
teardown：RUNNING_COUNT=0，SQL Server/MySQL/Ryuk/Testcontainers residual=0
```

该结果证明当前共享源状态已覆盖 Files 组合验证。

Jobs 释放队列后，本窗口又执行了本任务快照的 slice 门禁：

```text
pnpm test:integration:affected -- --snapshot files-upload-compensation-cancellation-20260730 --phase slice
Integration Release build：0 warning，0 error
CodeGeneration + Files + Realtime：28/28 通过
双 Provider 发现门禁：通过
teardown：RUNNING_COUNT=0，SQL Server/MySQL/Ryuk/Testcontainers residual=0
```

## 最终结论

上传落盘后事务取消的回归已由真实本地存储 Unit 覆盖；补偿删除不再继承已取消请求令牌，Files Unit、模块构建、格式、affected 双 Provider 组合验证与容器 teardown 均通过。
