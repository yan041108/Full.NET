# Outbox Typed Command Plan 端到端 A/B 证据

## 范围

- 任务基线：`b8d570a5439bec7f8ee4f97e53e5c6fc166d3a2c`
- Typed Plan 初始实现：`a8e3d92049eb00f585744cad88bbc95defde0670`
- 交错与审查修正：`25365e2977660b289f1184933f0753875228f818`、`431e2b52cb7147edaa427093f4452cd9d9722f85`
- 最终 A/B 运行基线：`431e2b52cb7147edaa427093f4452cd9d9722f85`
- 分支：`main`
- 任务快照：`outbox-typed-plan-ab-20260828`
- 环境：Windows 10 `10.0.19045`、.NET runtime `10.0.11`、20 logical processors、Docker Desktop `29.6.2`
- Provider：SQL Server `16.0.4135.4`（`2022-CU14`）与 MySQL 8 Testcontainers
- 容量状态：`Capacity-not-verified`

本轮实现并验证一个仅限 Outbox、封装在 `Full.NET.Data.Dapper` 内部的强类型命令计划候选。业务边界仍只暴露 `IOutboxWriter`；生产 DI 继续显式选择 `StaticRegistry`，没有切换默认路径，也没有把 Factory 或 Provider 命令对象暴露给业务代码。

## 决策

**候选保留，生产切流 No-Go。**

Typed Plan 的分配收益方向稳定：12 个配对场景的每写入分配全部下降，范围为 **3.0%–21.9%**。但按全部成功写入加权只下降约 **4.6%**（52,061 B/write → 49,656 B/write），低于 5% 门槛。CPU/write 加权约下降 **7.9%**（599.4 μs → 552.0 μs），但场景间不稳定，MySQL Legacy concurrency 1 与 SQL Server AppendOnly concurrency 8 分别回退约 34.7% 与 6.4%。它值得保留为 Outbox-only 内部候选，但资源证据不足以支持默认切流。

但本轮没有满足“无双库 P99/错误率回退”的切流门槛：

- MySQL 6 个场景中 5 个 P99 回退，范围约 **5.7%–10.7%**；只有 Legacy concurrency 32 改善约 1.7%。
- SQL Server 两条路径都出现 SQL failure；Typed 为 45/209,997 attempts，Registry 为 42/198,131 attempts，当前样本不能证明错误率不劣。
- 两次重复只能用于聚焦候选比较，不足以给吞吐和尾延迟建立稳定置信区间。

因此当前正确边界是：保留 Typed Plan、保留 A/B 入口、继续默认 `StaticRegistry`。只有在隔离数据库抖动、增加重复数并证明双库 P99/错误率不回退后，才能提出 Outbox-only 默认切换；本证据不支持扩展到整个 Dapper AOT 执行框架。

## 实现边界

- `DapperTypedCommandPlan<TArgs>` 使用固定 SQL、固定参数 ordinal，并为 SQL Server/MySQL 各保留一个有界空闲 `DbCommand` 槽。
- 回收前清空参数值并脱离 `Connection` / `Transaction`；在用 command 不共享，槽已满或异常路径直接释放。
- Legacy Outbox 使用 8 参数计划，Append-only Outbox 使用 12 参数计划。
- Typed 执行仍经过现有 `DbSession`、事务、命令超时、异常映射、Telemetry 与受影响行检查。
- `ServiceCollectionExtensions` 显式注册 `StaticRegistry`；Typed 只由测试和 Profile harness 显式选择。
- Native AOT 路径只使用闭合泛型与显式绑定，没有反射、匿名参数形状或运行时类型发现。

## A/B 方法

命令：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release --no-build -- outbox-write-profile `
  --providers sqlserver,mysql `
  --concurrency 1,8,32 `
  --targets legacy,append `
  --command-paths registry,typed `
  --payload-size 256 `
  --repetitions 2 `
  --warmup-seconds 5 `
  --duration-seconds 10 `
  --output BenchmarkDotNet.Artifacts/outbox-typed-plan-ab-corrected-20260828
```

每个 Provider × target × concurrency × repetition 都包含 Registry/Typed 配对。奇数轮按 Registry → Typed，偶数轮反转为 Typed → Registry，以削弱时间顺序偏差。JSON 共 48 条结果，每个 cell 两轮且无缺失配对。

原始产物（本地、未提交）：

`BenchmarkDotNet.Artifacts/outbox-typed-plan-ab-corrected-20260828/outbox-write-profile.json`

首次非交错运行只用于发现顺序偏差，不参与裁决。第二次交错运行被独立审查发现 CPU/分配快照包含 warmup，同样不参与裁决。下表只使用 `431e2b52` 上修正 measurement 窗口后的第三次运行：warmup 完成后清表、重置 Telemetry/连接池指标，再捕获进程资源起点。

## 配对结果

以下百分比均为 Typed 相对 Registry；分配/CPU/P50/P99 为负表示改善，吞吐为正表示改善。

| Provider | Target | 并发 | 吞吐 | P50 | P99 | 分配/write | CPU/write | Registry/Typed errors |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| MySQL | AppendOnly | 1 | +0.4% | 0.0% | +8.6% | -21.9% | -0.4% | 0 / 0 |
| MySQL | AppendOnly | 8 | -1.1% | +1.2% | +5.7% | -12.6% | -4.0% | 0 / 0 |
| MySQL | AppendOnly | 32 | -0.7% | +0.7% | +7.5% | -11.2% | -9.5% | 0 / 0 |
| MySQL | Legacy | 1 | -2.2% | +0.5% | +9.2% | -19.8% | +34.7% | 0 / 0 |
| MySQL | Legacy | 8 | -1.3% | +1.9% | +10.7% | -12.6% | -15.8% | 0 / 0 |
| MySQL | Legacy | 32 | +1.5% | -2.3% | -1.7% | -10.4% | -0.5% | 0 / 0 |
| SQL Server | AppendOnly | 1 | +0.5% | +0.1% | -5.4% | -15.2% | -8.8% | 0 / 0 |
| SQL Server | AppendOnly | 8 | +7.6% | -11.5% | -8.2% | -3.9% | +6.4% | 1 / 0 |
| SQL Server | AppendOnly | 32 | +10.2% | -9.7% | -5.7% | -6.0% | -8.7% | 21 / 22 |
| SQL Server | Legacy | 1 | +1.2% | -2.0% | +6.4% | -13.8% | -18.1% | 0 / 2 |
| SQL Server | Legacy | 8 | +10.8% | -18.1% | -8.4% | -3.0% | -11.4% | 0 / 0 |
| SQL Server | Legacy | 32 | +12.1% | -11.4% | -4.3% | -4.6% | -13.6% | 20 / 21 |

跨并发聚合只用于描述资源规模，不作为延迟裁决：

| Provider | Target | Path | Writes | Errors | Allocated B/write | CPU μs/write |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| MySQL | AppendOnly | Registry | 39,651 | 0 | 26,895 | 719.2 |
| MySQL | AppendOnly | Typed | 39,385 | 0 | 23,673 | 666.9 |
| MySQL | Legacy | Registry | 39,020 | 0 | 25,246 | 620.7 |
| MySQL | Legacy | Typed | 39,252 | 0 | 22,397 | 602.7 |
| SQL Server | AppendOnly | Registry | 59,175 | 22 | 72,003 | 519.9 |
| SQL Server | AppendOnly | Typed | 64,372 | 22 | 67,877 | 498.1 |
| SQL Server | Legacy | Registry | 60,243 | 20 | 66,406 | 584.9 |
| SQL Server | Legacy | Typed | 66,943 | 23 | 63,406 | 506.5 |

## 正确性与回归验证

- Typed Plan 生命周期：顺序复用、参数 ordinal 更新、连接/事务脱离、Provider 隔离、在用实例不共享。
- Writer 路由：默认 Registry、显式 Typed、非 Dapper executor 失败关闭、受影响行语义保持。
- Profile 合约：CLI path 解析、JSON path 标识、偶数轮路径顺序反转。
- 独立审查修正：包装连接只向 Provider command 绑定真实 inner connection；Guid/UTC 参数不变量与既有 TypeHandler 对齐；进程资源窗口排除 warmup；控制台和 JSON 输出稳定小写 path token。
- 聚焦 Unit：20/20，通过。
- `pnpm test:inner -- --snapshot outbox-typed-plan-ab-20260828`：14/14，通过；包含 MySQL Outbox/smoke。
- SQL Server Outbox 精确筛选：3/3，通过；覆盖 schema、append-only 字段持久化和事务回滚。
- `pnpm test:aot:analyzers`：通过，0 警告/0 错误；覆盖 `FULLNET_AOT_COMPILE` 下的 MySQL wrapper 实现。
- `pnpm test:aot:publish:linux`：最终 HEAD 发布通过；warning gate 接受 9 个已登记第三方告警，生成 72,269,968 B 的 Linux x64 原生 Host.Api。
- API Native AOT Architecture：49/49，通过。
- Governance：52/52，通过。
- Native AOT E2E Windows 发现门禁：发现 5 项、0 失败、5 项按设计跳过；这不等价于 Linux 原生进程执行。
- 独立代码复审：修正 4 项发现后，无剩余 Critical/Important finding。
- Naming：29/30；唯一失败是本任务未修改、任务基线已存在的 migration 100 静态扫描 `FNSQL003`（4 处 unsupported DDL），不计为本候选通过。
- SQL Server 首次精确筛选的 2 个迁移失败不计为代码失败：复用测试容器累计约 1,200 个测试数据库并在 recovery 中退出（`OOMKilled=false`、exit 255）。删除无挂载的已退出测试容器后，同一测试集 3/3 通过。

## 后续门槛与回退

若继续推进，应在干净、固定资源的双库环境中把每格重复提高到至少 5 次，并将 P99 与错误率作为硬门槛。只有所有 Provider/target/concurrency cell 都无显著回退，才允许将 Outbox DI 默认从 `StaticRegistry` 改为 `TypedPlan`。

回退不需要数据库迁移或业务契约变更：保持或恢复 `DapperOutboxCommandPath.StaticRegistry` 即可。整个 Dapper AOT 框架继续使用现有静态 Registry 设计。
