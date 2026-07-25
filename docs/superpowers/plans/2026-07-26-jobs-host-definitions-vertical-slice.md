# Jobs Host 任务定义纵向切片（2026-07-26）

## 目标

交付 Host 作用域任务定义 CRUD、手动触发执行、内置 `jobs.ping` 处理器、Worker 轮询与双管理端 UI。

## 清单

1. [x] 迁移 `030_JobsDefinitionAndExecution.sql`（SQL Server + MySQL）
2. [x] `Full.NET.Modules.Jobs`：定义/执行持久化、处理器注册、`JobExecutionRunner`、API Endpoint
3. [x] Worker `JobExecutionHostedProcessor` 后台轮询
4. [x] Integration **160 → 162**（`Host_job_definition_and_trigger` SQL Server/MySQL）
5. [x] OpenAPI 夹具 + client-contracts
6. [x] Vue/Layui `host-jobs` 双端 UI
7. [x] `shell-parity`「任务调度列表与触发」× 双端 → **60 → 62**
8. [x] 路线图与验证记录

## 范围外（后续）

- Cron/周期调度
- 租户作用域任务
- 更多业务处理器
