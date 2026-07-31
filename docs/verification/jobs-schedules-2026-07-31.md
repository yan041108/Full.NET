# Jobs 计划调度验证记录（2026-07-31）

## 结论

Admin.NET 的“一项任务可配置多个触发器、暂停恢复、下一次运行时间和执行历史”业务目标已按 Full.NET 边界吸收为 Host-only Jobs 计划调度纵向切片，状态为 `Build-verified`。

本次没有复制 Admin.NET 的动态 C# 编译、SqlSugar 持久化、进程内调度真源或任意延迟等待。`fn_jobs_schedule` 是持久化真源；Worker 在同一事务内锁定到期计划、创建执行记录并推进计划，再复用既有领取、租约、续租、重试和终态语义。

## 已交付能力

- 触发类型：`manual`、`one_time`、五段 `cron`。
- 误触发策略：`skip` 与 `fire_once`；多次遗漏不会无界补发。
- 时区：输入接受 IANA/Windows ID，持久化规范 IANA ID，执行时刻统一 UTC；覆盖 DST 跳跃与重叠。
- Host API：计划分页、详情、创建、更新、暂停、恢复；读写权限分别为 `jobs.schedules.read`、`jobs.schedules.write`。
- 并发：计划写入使用版本号；到期选择稳定按 `NextExecutionAtUtc, Id` 排序。
- 原子性：计划物化与游标推进共用数据库事务，受影响行不为一时整批回滚。
- 历史：`fn_jobs_execution` 增加可空 `JobScheduleId` 与 `ScheduledForUtc`，手动执行保持兼容。
- 双库：成对迁移 `040_JobsSchedules.sql` 创建 `fn_jobs_schedule` 并恢复缺失列/索引。

## 依赖决策

采用 Cronos 0.13.0，仅用于 Cron 表达式与时区/DST 计算。依赖为 MIT 许可、纯托管实现，已加入中央版本与第三方声明。动态代码执行仍明确禁止。

## TDD 证据

- Calculator RED：缺少计划模型与计算器；GREEN `JobScheduleCalculatorTests` **8/8**。
- Service RED：缺少 Host 计划服务；GREEN `HostJobScheduleServiceTests` **7/7**。
- Dispatcher RED：缺少原子物化器；GREEN `JobScheduleDispatcherTests` **4/4**。
- Worker RED：轮询未读取计划；GREEN `JobExecutionHostedProcessorTests` **3/3**。
- Migration RED：SQL Server 计划表计数为 0；实现后 SQL Server/MySQL 恢复各 **1/1**。
- API RED：`POST /api/v1/jobs/host-schedules` 返回 404；实现后 SQL Server/MySQL Jobs API 各 **1/1**。

本切片新增 .NET Unit 测试方法 **19**。

## 新鲜验证

| 验证 | 结果 |
|---|---:|
| Jobs Unit | **68/68** |
| 全量 Unit | **861/861** |
| Architecture | **50/50** |
| OpenAPI 静态合同 | **2/2** |
| 命名治理 | **24/24** |
| affected 工具链 | **39/39** |
| governance | **16/16** |
| affected smoke | **8/8** |
| Jobs + migration-040 双 Provider | **4/4** |
| Integration 分片发现 | SQL Server API **43**、MySQL API **43**、migrations **80**、infrastructure **83**，合计 **249** |
| Release solution build | **0 warning / 0 error** |
| Docker teardown | running **0**、相关 residual **0** |

关键命令：

```powershell
pnpm test:naming
node --test tests/openapi/jobs-host-definitions-contract.test.mjs
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 861 --timeout 10m
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 50 --timeout 10m
pnpm test:integration:affected -- --snapshot adminnet-jobs-schedules-task5-20260731 --phase slice
dotnet build Full.NET.slnx -c Release --no-restore
```

## 保留边界

- 当前仅 Host 作用域，不开放租户自助计划。
- 不支持动态 C#、HTTP 脚本、运行时程序集加载或秒级 Cron。
- 运维重放、计划删除/归档、管理端 Vue/Layui 页面、生产指标导出和告警仍属后续切片。
- 状态保持 `Build-verified`；在双管理端同场景 E2E 与生产运维演练完成前不标记 `Verified`。
