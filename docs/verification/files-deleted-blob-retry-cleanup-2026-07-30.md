# Files 软删除 Blob 重试清理验证记录

## 结论

- 状态：`Integration-verified`
- 范围：Host 文件软删除墓碑、Worker Profile 清理器、本地 Blob 与 SQL Server/MySQL。
- 基线：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 任务快照：`files-deleted-blob-retry-20260730`

Files Worker Profile 新增默认关闭的有界清理循环。Runner 按 `DeletedAtUtc + Id` 游标读取 Host
软删除墓碑，先执行幂等 Blob 删除，再精确硬删除仍处于软删除状态的墓碑。单个 Blob 失败只保留
墓碑并继续本轮后续候选；数据库失败和宿主取消向上传播。

## RED 与修复

生产类型落地前，`DeletedHostFileBlobCleanupTests` 因缺少 Cleanup Options、Runner、Record 与
SQL Statement 编译失败，确认行为契约先于实现。

首次 SQL Server 真栈运行暴露首批查询把 `Guid.Empty` 作为未启用游标的参数传入 Dapper，
`AssignedGuidTypeHandler` 以“持久化标识必须由应用预先分配”拒绝该值。修复后首批 `AfterId`
传 `NULL`，只有后续页才传数据库返回的有效 UUID；Unit 同步断言首批参数不得携带空 UUID。

## 新鲜验证

2026-07-30 在共享工作区串行执行：

| 验证 | 结果 |
| --- | --- |
| Files SQL Server API 聚焦真实栈 | 通过，1/1，约 32 秒 |
| Files MySQL API 聚焦真实栈 | 通过，1/1，约 57 秒 |
| `dotnet build tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-restore --nologo` | 通过，0 警告、0 错误 |
| Unit runner：`FullyQualifiedName~Full.NET.UnitTests.Files` | 通过，10/10 |
| `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore --nologo` | 通过，0 警告、0 错误 |
| 三个改动项目的精确文件 `dotnet format --verify-no-changes` | 通过 |
| `pnpm test:naming` | 通过，23/23 |
| `git diff --check` | 通过 |

双库测试都先通过 HTTP 上传和软删除，再根据数据库墓碑重建“Blob 仍存在”的同步删除失败残态，
随后验证清理器删除真实文件、清除数据库墓碑，并在第二次运行返回零处理。SQL Server、MySQL
与 Ryuk teardown 后确认 `docker ps` 为空，再将 Docker 正式释放给 Jobs 窗口。

## 影响集与并行边界

`pnpm test:integration:affected:plan -- --snapshot files-deleted-blob-retry-20260730 --phase inner`
识别 `CodeGeneration, Files, integration-tooling, Realtime, smoke`。这是共享快照之后多个窗口并行
变更的合并影响；本窗口只执行 Files 双库聚焦，不接管 CodeGeneration、Realtime、工具链或 Smoke。

本窗口未修改 Worker `Program.cs`/`appsettings.json`、迁移、共享路线图或
`eng/testing/test-matrix.json`。测试数量由最终 discovery 负责窗口统一收口。

## 运维边界

- 清理默认关闭；只有 Worker 与 API 共享同一有效 `Files:Local:RootPath` 时才能启用。
- 单轮最多处理 `BatchSize * MaxBatchesPerRun` 个墓碑。
- 自动任务只覆盖数据库已知的 Host 软删除对象；无墓碑历史孤儿和租户文件不在本切片范围。
- 没有新增数据库对象或迁移，现有软删除列与主键顺序足以支持当前有界批处理。
