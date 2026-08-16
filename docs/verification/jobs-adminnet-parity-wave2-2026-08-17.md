# Jobs Admin.NET 对标第二波验证（Build-verified）

**日期：** 2026-08-17  
**快照：** `jobs-overlap-control-20260817` 及后续切片  
**状态：** Build-verified（未标 Verified；无 admin-real-stack E2E）

## 切片 1：按任务禁止重叠执行

- 迁移 `096_JobsDefinitionAllowConcurrentExecutions`（默认 `AllowConcurrentExecutions=false`）
- Acquire gate（双库 `ROW_NUMBER` + 有效 running 检查）与 Schedule 物化 gate
- API/契约/Vue 开关；`JobsOverlapControlAssertions` 双库 Integration

## 切片 2：执行历史

- `GET /api/v1/jobs/host-executions` 扩展过滤（definition/schedule/status/from/to）
- `GET /api/v1/jobs/host-executions/{id}`；`HostJobExecutionsView` 独立页

## 切片 3：Cron 解释

- `HostJobScheduleCronPreviewResponse`：`humanDescription` + `nextOccurrencesUtc[]`
- Cron 宏预处理（`@daily` 等）；`HostJobSchedulesView` 宏下拉与下 N 次列表

## 切片 4：只读健康

- 迁移 `097_JobsWorkerHeartbeat`；Worker 轮询 upsert 心跳
- `GET /api/v1/jobs/host-health`（`jobs.health.read`）；`HostJobHealthView`

## 验证命令与结果

```text
pnpm test:inner -- --snapshot jobs-overlap-control-20260817   # 通过（2026-08-17）
pnpm test:slice -- --snapshot jobs-overlap-control-20260817   # 通过（6 项 Jobs Integration）
dotnet test tests/Full.NET.IntegrationTests --filter "Name~Host_job_definition|Name~allow_concurrent|Name~worker_heartbeat"
dotnet test tests/Full.NET.UnitTests --filter "FullyQualifiedName~JobScheduleCalculatorTests"
```

## 排除项（与 Spec 一致）

- 脚本/HTTP 任务、Furion 全局 pause、调度主节点选举
- 不提高默认 `Jobs:Worker:MaxConcurrency`
