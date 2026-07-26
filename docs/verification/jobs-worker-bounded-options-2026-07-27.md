# Jobs Worker 有界配置验证（2026-07-27）

## 目标

将 Jobs Worker 的单轮领取数量与空轮询等待时间从代码硬编码收敛到
`Jobs:Worker` 配置，并确保越界配置在宿主启动期失败，不被 Runner 的防御性
`Math.Clamp` 静默修正。

本次不修改公共 API、OpenAPI、数据库对象、领取 SQL、租约、重试语义或双库行为。

## 配置契约

| 配置键 | 默认值 | 合法范围 | 越界行为 |
| --- | ---: | ---: | --- |
| `Jobs:Worker:BatchSize` | `10` | `1`～`50` | 启动校验失败 |
| `Jobs:Worker:PollMilliseconds` | `2000` | `100`～`60000` | 启动校验失败 |

配置只由 `JobsModule.AddBackgroundServices` 绑定。API Profile 使用的
`JobsModule.AddServices` 不注册 Worker Options 或 Hosted Service，继续保持运行角色隔离。

## 测试先行证据

- Options 回归先在缺少绑定时失败，错误为未注册
  `IOptions<JobsWorkerOptions>`；加入绑定、显式校验器和 `ValidateOnStart` 后通过。
- Processor 回归先因构造函数不接收 Options，且缺少可验证的单轮执行与等待边界而编译失败；
  实现后确认配置 `BatchSize = 7` 会原样传递给 Runner，`PollMilliseconds = 250`
  会转换为 250 毫秒等待。
- 两项聚焦回归在 Release 下 **2/2** 通过，失败 0、跳过 0；Unit 项目构建
  **0 warning / 0 error**。

## 隔离分支预验证

| 门槛 | 结果 |
| --- | --- |
| Full.NET.slnx Release | **0 warning / 0 error** |
| Unit / Compatibility / Architecture | **398/398** / **7/7** / **49/49**，失败 0、跳过 0 |
| Integration 分片发现 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** = **189** |
| Naming / Governance / Project Skill / Workspace | **23/23** / **11/11** / **52** 项 / 通过 |
| C# format / diff check | 通过 |

以上结果来自同步 Task 15 前的隔离分支：当时 main Unit 基线为 396，本任务新增 2 项。
最终 canonical 与合并证据以完成既定队列同步后的结果为准。

## 保持不变的边界

- 默认行为仍为单轮最多领取 10 条、每轮间隔 2 秒。
- `JobExecutionRunner` 的直接调用边界及防御性批大小限制保持不变。
- SQL Server/MySQL 使用同一现有领取契约；本次没有迁移、SQL 或 Integration 用例变更。
- 长任务主动续租、Cron/延迟调度、失败重试分类、运维重放与容量压测仍不在本次范围。

## 关联

- [实施计划](../superpowers/plans/2026-07-27-jobs-worker-bounded-options.md)
- [Jobs Host 任务定义验证](jobs-host-definitions-2026-07-26.md)
- [取消传播与批次故障隔离验证](jobs-cancellation-batch-failure-isolation-2026-07-27.md)
- [能力状态](../roadmap/capability-status.md)
