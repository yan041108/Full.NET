# Jobs 显式失败重试与 Worker Host Context 验证（2026-07-30）

## 状态与范围

- 状态：`Build-verified`
- 任务基线：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 任务快照：`jobs-retry-classification-20260730`
- 范围：Jobs 显式可重试失败、固定延迟、尝试上限、SQL Server/MySQL
  到期领取、037 半完成迁移恢复，以及 Worker 轮询 Host Context。
- 不包含：Cron、通用延迟调度、指数退避/抖动、人工重放、容量 A/B 和
  完整 Integration 全量执行。

指数退避与抖动已由后续独立切片补齐；本记录继续作为显式失败分类、037 与 Worker
Host Context 的原始证据，新增行为见
[Jobs 有界重试退避与抖动验证](jobs-retry-backoff-jitter-2026-07-30.md)。

## 行为结论

1. 只有 `RetryableJobException` 会进入重试；普通异常、缺失 Handler 和宿主取消
   保持原终态或传播语义。
2. `Jobs:Worker:MaxAttempts` 默认 `1`、范围 `1..10`，因此未显式配置的部署仍是
   首次失败即终止；`RetryDelaySeconds` 默认 `30`、范围 `1..86400`。
3. 可重试且尚有次数时，执行回到 `pending`，清理租约与完成时间，并写入
   `NextAttemptAtUtc`；领取 SQL 在到期前不会再次取得该执行。
4. 达到总尝试次数后进入 `failed`，清理 `NextAttemptAtUtc` 和租约。
5. Worker 每轮轮询在自身 Scope 内建立 Host Context，并在成功或异常后清理，避免
   Host-only 领取语句触发 `HostContextRequiredException`。

## 数据库与恢复

- SQL Server：`NextAttemptAtUtc datetimeoffset(7) NULL`，使用带
  `Status = 'pending'` 过滤条件的
  `IX_fn_jobs_execution_PendingNextAttemptLease`。
- MySQL：`NextAttemptAtUtc datetime(6) NULL`，使用同名四列索引。
- 037 会在列已存在、旧索引已移除或新索引缺失的未记账半完成状态下重新收敛。
- `eng/testing/test-matrix.json` 已登记 037 的双库恢复选择器，受影响测试不再降级为
  完整 migrations 分片。

## 自动化证据

| 验证 | 结果 |
| --- | --- |
| Options RED | 新配置属性尚不存在时，聚焦测试出现预期编译失败 |
| Runner RED | 可重试失败仍调用终态 SQL，9 项中 1 项预期失败 |
| Worker Context RED | 领取时观测 `IsHost = false`，专项 1/2 失败 |
| Worker Context GREEN | 专项 2/2，通过并确认每轮结束后 Context 已清理 |
| Release 构建 | Unit、Integration、Architecture 三个测试项目均 0 警告、0 错误 |
| 全量 Unit | 654/654，失败 0，跳过 0 |
| 全量 Architecture | 49/49，失败 0，跳过 0 |
| Jobs SQL Server | 1/1，失败 0，跳过 0 |
| Jobs MySQL | 1/1，失败 0，跳过 0 |
| 037 双库半完成恢复 | 2/2，失败 0，跳过 0 |
| Integration 分片发现 | API SQL Server 38、API MySQL 38、migrations 70、infrastructure 79；合计 225，无重复或遗漏 |
| Naming | 23/23 |
| SQL safety | 5/5 |
| Governance | 16/16 |
| 测试工具契约 | 31/31 |

双库 Jobs 聚合并行运行曾在同项目另一个窗口同时占用 Docker 时出现 1 项通过、另 1 项
在 6 分钟后被外层命令中止，期间没有测试失败。释放其它窗口容器后按 Provider 串行
复跑，SQL Server 与 MySQL 均通过；交付结论采用串行新鲜结果。

## 未验证项

- 本地没有执行 225 项完整 Integration；完整集合仍由 `main` CI 的互斥分片门禁运行。
- 尚未验证多副本 Worker 在持续可重试失败下的容量、抖动和数据库热点。
- 尚未提供运维人工重放、重试队列观测指标或管理端操作入口。
- 规则演进检查未命中重复失败、高风险新类别或规则冲突；本次不新增规则候选。
- Skill 演进检查未发现项目 Skill 缺口；本次不修改 Skill。
