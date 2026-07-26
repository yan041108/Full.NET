# Jobs 长任务主动续租验证（2026-07-27）

## 目标

当 Handler 运行时间超过领取时写入的初始租约时，Jobs Worker 必须持续延长仍由当前
`LeaseId` 持有的 `running` 执行，避免其他 Worker 把健康长任务当作过期任务重复领取。

本切片复用 `fn_jobs_execution.LeaseId` 与 `LeaseExpiresAtUtc`，不新增迁移、表列、公开
HTTP/JSON 契约、权限码或 Handler API。

## 配置契约

| 配置键 | 默认值 | 合法范围 | 额外约束 |
| --- | ---: | ---: | --- |
| `Jobs:Worker:LeaseSeconds` | `300` | `30`～`3600` | 初始领取与每次续租使用相同时长 |
| `Jobs:Worker:LeaseRenewalSeconds` | `60` | `5`～`1200` | 不得大于 `LeaseSeconds / 2` |

Worker Profile 绑定配置并执行 `ValidateOnStart`。API Profile 不注册 Hosted Service，
但手动触发所使用的 Runner 可解析同一配置类型的安全默认值，保持原先 5 分钟初始租约。

## 所有权与故障语义

续租使用同一条 Provider-neutral 参数化 SQL：

```sql
UPDATE fn_jobs_execution
SET LeaseExpiresAtUtc = @LeaseExpiresAtUtc
WHERE TenantId IS NULL
  AND LeaseId = @LeaseId
  AND Status = @RunningStatus
```

- 同一批次尚未完成的记录共享领取时生成的 `LeaseId`，续租不会触碰其他 Worker、
  Tenant 任务或已完成记录。
- 批次完成后取消并等待续租循环；不会在作用域释放后留下后台数据库操作。
- UPDATE 返回 0 或数据库续租抛异常时，Runner 取消传给 Handler 的 linked token，
  等待协作式退出后传播原始租约故障；不会误写成功或业务失败终态。
- 宿主取消继续传播 `OperationCanceledException`，停止续租并保留当前租约，由既有
  过期恢复路径重新领取。

## 测试先行证据

1. Options RED 先因 `LeaseSeconds`、`LeaseRenewalSeconds` 不存在而编译失败；实现后
   锁定 300/60 默认值、两个绝对范围与半租期关系。
2. Runner RED 先因构造函数不接收租约配置、缺少 `JobSql.RenewExecutionLease`
   而编译失败；实现后阻塞 Handler 会等待首次续租，再允许任务成功完成。
3. 所有权丢失 RED 暂时没有“UPDATE 0 即失败”分支时，在 3 秒测试上限内超时；
   恢复最小分支后，Handler 收到取消且 Runner 抛出稳定的所有权丢失异常。
4. 终态竞态 RED 用可控 Command Executor 让最后一个 Handler 成功写入终态后，续租返回 0；
   修正前 Runner 误抛所有权丢失，修正后返回已处理 1 条且不写入失败终态。

## 隔离分支非 Docker 预验证

| 门槛 | 结果 |
| --- | --- |
| Full.NET.slnx Release | **0 warning / 0 error** |
| Unit / Compatibility / Architecture | **404/404** / **7/7** / **49/49**，失败 0、跳过 0 |
| Jobs 新增与既有聚焦 Unit | **7/7**，失败 0、跳过 0 |
| Integration 项目 Release 编译 | **0 warning / 0 error** |
| Integration 分片发现 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** = **189** |
| Naming / Governance / Project Skill / Workspace | **23/23** / **11/11** / **52** 项 / 通过 |
| `git diff --check` | 通过 |

以上证据来自基线 `main@c828a6579fb36761581f62246ff83bef48d59635` 的隔离分支，
本任务新增 4 项 Unit；Docker 由前序 Outbox 全量占用，因此本节不把双库夹具写成已通过。

## 最终双库与合并门禁

待 Outbox → admin-real-stack E2E → session lease-horizon 依次合并清理并释放 Docker 后，
本节补录最新 main 上的 Jobs SQL Server/MySQL **2/2**、最终 canonical、静态门槛、
合并提交与分支/工作树/容器清理证据。

## 保持不变的限制

- 本切片只防止健康长任务因租约到期被重复领取，不提供 exactly-once 业务副作用保证；
  Handler 仍须幂等并协作响应 `CancellationToken`。
- Cron/延迟调度、失败重试分类、运维重放和真实大规模多 Worker 压力基准仍未交付。
- 租约续租失败不自动改写为 `failed`，避免把基础设施所有权丢失误报为业务失败。

## 关联

- [实施计划](../superpowers/plans/2026-07-27-jobs-active-lease-renewal.md)
- [Worker 有界配置验证](jobs-worker-bounded-options-2026-07-27.md)
- [多 Worker 原子领取验证](jobs-multi-worker-claim-2026-07-27.md)
- [能力状态](../roadmap/capability-status.md)
