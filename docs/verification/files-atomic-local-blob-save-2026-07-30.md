# Files 本地 Blob 原子保存验证记录

## 结论

- 状态：`Build-verified`
- 范围：Files 本地 Blob 保存失败路径；不涉及数据库、迁移、公共 API、Worker 或容器。
- 基线：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 任务快照：`files-orphan-blob-cleanup-20260730`

`LocalHostFileBlobStorage` 先把上传内容写入最终对象同目录的唯一 `.uploading` 暂存文件，
完整复制、刷新并关闭句柄后，再以不覆盖方式移动到最终路径。取消或复制失败不再把零字节或部分
内容发布为最终对象；最终键已存在时仍抛出冲突异常且不覆盖原内容。

## 根因与 RED

原实现直接用 `FileMode.CreateNew` 打开最终路径，再把输入流复制进去。最终文件在复制完成前已经
可见，预取消令牌会在留下零字节最终文件后抛出 `OperationCanceledException`。

生产代码修改前运行：

```text
tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.exe
  --filter FullyQualifiedName~LocalHostFileBlobStorageTests
  --minimum-expected-tests 1
  --progress off
```

结果：`1/1` 失败；取消场景期望根目录文件数为 `0`，实际为 `1`。失败来自旧实现留下的最终
文件，不是编译或环境错误。

## GREEN 与回归

2026-07-30 在共享工作区执行以下无容器验证：

| 验证 | 结果 |
| --- | --- |
| `dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo` | 通过，0 警告、0 错误 |
| `Full.NET.UnitTests.exe --filter FullyQualifiedName~Full.NET.UnitTests.Files --minimum-expected-tests 1 --progress off` | 通过，4/4 |
| 两个改动 C# 文件的 `dotnet format --verify-no-changes` | 通过 |
| `pnpm test:naming` | 通过，23/23 |

Files 聚焦回归覆盖：

1. 成功保存只发布内容完整的最终对象，不残留暂存文件；
2. 最终对象已存在时不覆盖原内容，不残留本次暂存文件；
3. 上传取消时不留下最终对象或暂存文件；
4. 既有 Files 上传输入校验继续通过。

## 影响集与并行边界

`pnpm test:integration:affected:plan -- --snapshot files-orphan-blob-cleanup-20260730 --phase inner`
发现快照之后共享工作区同时出现 CodeGeneration、测试矩阵与 Realtime 变更，因此计划显示
`CodeGeneration, Files, integration-matrix, Realtime`，inner 执行阶段选择
`CodeGeneration, integration-matrix, Realtime`。本窗口未执行这些并行窗口的影响集，也未启动
Docker。

共享测试矩阵已由负责收口的窗口更新为 Unit `708`，Integration
`228/82/38/38/70`，并包含 Files 新增的 3 个测试；本窗口未修改该矩阵。

## 未覆盖边界

- 本切片不扫描或删除历史孤立 Blob。
- 软删除后物理删除失败仍按现有只读差异清单处理。
- 文件系统拒绝删除暂存文件时仍可能需要运维处理。
- 本切片不把 Files 能力状态提升为 `Verified`。
