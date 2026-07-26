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
完成 Task 15、OpenAPI schema 与客户端损坏锁记录的既定队列同步后，Release 构建再次保持
**0 warning / 0 error**，Unit/Compatibility/Architecture 为
**400/400** / **7/7** / **49/49**；最终 canonical 为 **400/7/49/189**。

## 最终门禁

| 门槛 | 结果 |
| --- | --- |
| Full.NET.slnx Release | **0 warning / 0 error** |
| Unit / Compatibility / Architecture | **400/400** / **7/7** / **49/49**，失败 0、跳过 0 |
| Jobs SQL Server/MySQL 聚焦 | **2/2**，失败 0、跳过 0，**41.9s** |
| Integration 全量 | **189/189**，失败 0、跳过 0，**27m16.8s**，stderr 0 |
| Integration 分片发现 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **57** = **189** |
| OpenAPI / breaking | **58/58** / **25/25** |
| Naming / Governance / Project Skill / Workspace | **23/23** / **11/11** / **52** 项 / 通过 |
| C# format / diff check | 通过 |
| Docker | SQL Server、MySQL、Ryuk 容器均已退出 |

完整 Integration 首轮为 **188/189**：SQL Server 多 Worker 夹具在一次
`READPAST` 锁窗口内只处理 24/32 条。该夹具原先错误地把“每个 Worker 单次轮询
恰好领取 8 条”当作原子领取不变量；生产 Worker 实际会继续轮询。夹具按同一批大小
有界重复轮询后，双库聚焦与完整 189 均通过，总数、`AttemptCount = 1`、最终状态和租约
清理强断言保持不变；生产 Runner 与领取 SQL 未修改。详细过程见
[多 Worker 原子领取验证](jobs-multi-worker-claim-2026-07-27.md)。

## 保持不变的边界

- 默认行为仍为单轮最多领取 10 条、每轮间隔 2 秒。
- `JobExecutionRunner` 的直接调用边界及防御性批大小限制保持不变。
- SQL Server/MySQL 使用同一现有领取契约；本次没有迁移或 SQL 变更，Integration
  总数保持 189，仅校正既有多 Worker 夹具的轮询模型。
- 长任务主动续租、Cron/延迟调度、失败重试分类、运维重放与容量压测仍不在本次范围。

## 关联

- [实施计划](../superpowers/plans/2026-07-27-jobs-worker-bounded-options.md)
- [Jobs Host 任务定义验证](jobs-host-definitions-2026-07-26.md)
- [取消传播与批次故障隔离验证](jobs-cancellation-batch-failure-isolation-2026-07-27.md)
- [能力状态](../roadmap/capability-status.md)
