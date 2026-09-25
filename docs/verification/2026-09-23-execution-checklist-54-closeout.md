# 执行清单 54 — 授权备份执行器 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **54**（任务目录、运行结果查询、成功产物受控下载；**不**含生产自动恢复）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `/api/v1/platform/backup-executor`（`status` / `tasks` / `runs` / `runs/{id}/download`） |
| 权限 | `platform.backup_tasks.read`；`platform.backup_runs.read`；`platform.backup_runs.download` |
| 下载安全 | `BackupRunArtifactService`（仅 `succeeded` + 安全文件名 + 产物根目录内解析；拒绝 `..`/路径段/重解析点） |
| 配置 | `PlatformBackupExecutorOptions`（产物根目录；由部署期执行器写入） |
| Vue | `BackupExecutorView`（状态条、任务表、运行筛选、详情抽屉、受控下载按钮） |
| 契约 | `tests/openapi/platform-backup-crypto-mqtt-observability-contract.test.mjs` §backup-executor |
| 数据 | Platform 模块备份任务/运行表（迁移见 `BackupExecutorSql`） |

## 清单 54 验收结论

- **已有**：只读目录 + 分页运行列表；下载与 DB 恢复解耦；无 restore API。
- **本槽**：`phase-c-54-backup-executor.spec.mjs`（status/tasks/runs、缺失 run/download fail-closed、页面冒烟）。
- **未验**：真实执行器写产物 + 大文件 Range 下载（需部署备份 Job 与磁盘/OSS 前置）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`BackupRunArtifactServiceTests` | **1/1**（`d40de4e6`） |
| `node --test` `platform-backup-crypto-mqtt-observability-contract.test.mjs` | **4/4**（含备份执行器夹具） |
| real-stack | `phase-c-54-backup-executor.spec.mjs`（需本地 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
