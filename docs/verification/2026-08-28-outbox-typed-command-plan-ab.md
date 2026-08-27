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

## 当前结论

**五次重复复核后仍为生产切流 No-Go。** Typed Plan 的整体分配与 CPU 收益已经超过原资源门槛，但逐 cell 的 P99 与 SQL error rate 仍不能证明不回退。最新证据和裁决位于[五次重复复核](#五次重复复核2026-08-28)；下面先保留两次重复阶段的原始结论，避免覆盖实验演进过程。

## 两次重复的初始决策

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
- `ServiceCollectionExtensions` 在 Production 及所有非 Testing 环境强制注册 `StaticRegistry`；只有 Native 外部进程测试可通过内部键 `Testing:OutboxCommandPath=TypedPlan` 显式选择候选，未知测试值失败关闭。
- Native AOT 路径只使用闭合泛型与显式绑定，没有反射、匿名参数形状或运行时类型发现。

## Native AOT 运行闭环（2026-08-28）

- 验证输入：`1235a4df01eca5962b0bde7df93934e2d2ae4bf6` 加本任务工作区差异；任务快照 `outbox-typed-plan-native-aot-20260828`。
- `pnpm test:aot:publish:linux`：通过；warning gate 仅接受 9 个已登记第三方告警，生成 `linux-x64` 原生 Host.Api，大小 72,269,952 B，publish manifest 位于 `artifacts/native-aot/linux-x64/publish-manifest.json`。
- Windows discovery：`pnpm test:aot:native:notifications:e2e` 发现 2 项并按平台规则跳过；该结果仅证明可发现，不作为运行证据。
- Linux 原生执行：在 `mcr.microsoft.com/dotnet/sdk:10.0` 容器中挂载仓库与 Docker socket，并设置 `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`，直接运行已构建 Integration 程序集及新发布的 ELF。SQL Server/MySQL Notifications Typed Outbox 流程 2/2 通过、0 失败、0 跳过，耗时 2m22s；TRX 为 `artifacts/native-aot/linux-x64/test-results/Full.NET.IntegrationTests-native-aot-notifications-linux-local.trx`。
- 两个 Provider 都完成登录、Announcement 与 Inbox 事务写入、HTTP/JSON/SignalR 返回及原生进程优雅停止；成功的 mutation response 依赖包含 Typed Outbox INSERT 的业务事务提交，因此证明 Legacy Outbox Typed Plan 在真实 Native AOT Provider connection/transaction 链路可执行。
- Notifications 服务在事务提交后直接发布 SignalR，本门禁只启动 Host.Api、未启动 Host.Worker；因此 SignalR 成功只验证独立的 realtime/JSON 闭包，不证明 Outbox row 已被后台领取或消费。
- 本轮原生门禁证明提交路径，不单独构造“Outbox 已插入后再强制回滚”的原生故障点；事务回滚仍由既有双库 Integration/Typed Plan 生命周期门禁覆盖，禁止把本条证据扩大解释为全部原生故障矩阵。
- Production 默认没有改变，A/B 的生产切流结论仍为 No-Go，容量状态仍为 `Capacity-not-verified`。

Linux 本地门禁命令：

```powershell
docker run --rm --add-host host.docker.internal:host-gateway `
  -e CI=true `
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal `
  -v "G:\wwwroot\github_fork\Full.NET:/repo" `
  -v //var/run/docker.sock:/var/run/docker.sock `
  -w /repo mcr.microsoft.com/dotnet/sdk:10.0 `
  dotnet /repo/tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll `
  --no-ansi --progress off --timeout 45m `
  --filter "FullyQualifiedName~NativeApiNotifications" `
  --minimum-expected-tests 2 `
  --results-directory /repo/artifacts/native-aot/linux-x64/test-results `
  --report-trx `
  --report-trx-filename Full.NET.IntegrationTests-native-aot-notifications-linux-local.trx
```

本地执行注意：Docker Linux publish 后，Windows inner 首次因 `obj/project.assets.json` 暂时指向容器 NuGet 路径 `/root/.nuget/packages/` 而在编译前失败；宿主包缓存实际完整。执行 `dotnet restore Full.NET.slnx` 恢复 Windows assets 后，同一 `pnpm test:inner -- --snapshot outbox-typed-plan-native-aot-20260828` 原样重跑 14/14 通过。该环境恢复不涉及产品代码。

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
- `pnpm test:aot:publish:linux`：本轮 Native 闭环发布通过；warning gate 接受 9 个已登记第三方告警，生成 72,269,952 B 的 Linux x64 原生 Host.Api。
- API Native AOT Architecture：49/49，通过。
- Governance：52/52，通过。
- Native AOT 核心 E2E Windows 发现门禁：发现 5 项、0 失败、5 项按设计跳过；这不等价于 Linux 原生进程执行。Typed Outbox 的 Notifications 专用 Linux 原生门禁已在上节单独完成双库 2/2。
- 独立代码复审：修正 4 项发现后，无剩余 Critical/Important finding。
- Naming：29/30；唯一失败是本任务未修改、任务基线已存在的 migration 100 静态扫描 `FNSQL003`（4 处 unsupported DDL），不计为本候选通过。
- SQL Server 首次精确筛选的 2 个迁移失败不计为代码失败：复用测试容器累计约 1,200 个测试数据库并在 recovery 中退出（`OOMKilled=false`、exit 255）。删除无挂载的已退出测试容器后，同一测试集 3/3 通过。

## 原后续门槛与回退

初始证据要求在固定宿主双库环境中把每格重复提高到至少 5 次，并将 P99 与错误率作为硬门槛。该门槛已由下一节执行；由于仍有多个 Provider/target/concurrency cell 回退，Outbox DI 不得从 `StaticRegistry` 改为 `TypedPlan`。

回退不需要数据库迁移或业务契约变更：保持或恢复 `DapperOutboxCommandPath.StaticRegistry` 即可。整个 Dapper AOT 框架继续使用现有静态 Registry 设计。

## 五次重复复核（2026-08-28）

### 复核结论

**生产切流继续 No-Go。** 本轮 5 次重复确认 Typed Plan 的分配收益是真实且稳定的：12/12 个 cell 的分配中位数全部下降，全部成功写入加权后由 52,834 B/write 降至 49,877 B/write，改善 **5.60%**，首次超过 5% 资源门槛；CPU/write 加权由 578.1 μs 降至 546.9 μs，改善 **5.39%**。

但资源门槛不是唯一门槛：P99 中位数只有 5/12 个 cell 改善，7/12 回退；MySQL AppendOnly concurrency 32 的 P99 中位数回退 7.59%，5 轮只有 1 轮改善。SQL Server 的错误总体接近，但 Legacy concurrency 32 的 Typed 错误率高于 Registry，AppendOnly concurrency 8 也明显更差，不能证明逐 cell 正确性不劣。因此不修改 Production DI，Typed Plan 继续只作为 Testing/benchmark 候选。

### 固定宿主边界与方法

- 代码基线与 JSON 内嵌 commit：`f75e150ca04e6c859eb866232412186b5acc8fab`。
- Windows 10 `10.0.19045`、.NET `10.0.11`、20 logical processors、约 64 GB 主机内存。
- Docker Desktop `29.6.2`，Linux engine，20 CPUs、31.22 GB memory、overlayfs。
- SQL Server `16.0.4135.4`（`2022-CU14`）；MySQL `8.0.46`（`mysql:8.0`）。两个 benchmark Provider 容器顺序创建、独立迁移并在各自 60 个样本后销毁。
- 开始时另有两个仓库共享测试容器空闲运行，采样前观察合计约 1.5% CPU、约 3.5 GiB memory；本轮没有并行构建或其它 benchmark。该背景负载是本地证据限制之一。
- 参数：2 providers × 2 targets × 3 concurrency × 2 paths × 5 repetitions = 120 samples；每样本 warmup 5 s、measurement 10 s、payload 256 B，奇偶轮反转路径顺序。
- 原始工件（本地、未提交）：`BenchmarkDotNet.Artifacts/outbox-typed-plan-ab-5x-20260828/outbox-write-profile.json`，288,086 B，SHA-256 `51F882E4C406E1D504DDAB115808F8528E75EB151B75F23DEF61FA5AEAA68C08`；120 条记录、120 个唯一场景键，12 个 cell 均完整包含 repetition 1..5 的 Registry/Typed 配对。
- 全程 31m54s；0 connection timeout、0 duplicate attempt、数据库诊断采集 0 error。MySQL 60/60 个样本具有 connection-wait 分布，SQL Server 60/60 个样本的 `ConnectionWait` 为 null，因此 SQL Server 连接等待 P50/P95/P99 未验证；0 timeout 不能替代缺失的等待分布。仍属于固定宿主聚焦 A/B，不是生产等价容量认证，状态保持 `Capacity-not-verified`。

命令：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release --no-build -- outbox-write-profile `
  --providers sqlserver,mysql `
  --concurrency 1,8,32 `
  --targets legacy,append `
  --command-paths registry,typed `
  --payload-size 256 `
  --repetitions 5 `
  --warmup-seconds 5 `
  --duration-seconds 10 `
  --output BenchmarkDotNet.Artifacts/outbox-typed-plan-ab-5x-20260828
```

### 每 cell 配对中位数

百分比为 Typed 相对 Registry；吞吐为正表示改善，其余指标为负表示改善。“改善轮数”为 5 个同 repetition 配对中 Typed 更优的轮数。

| Provider | Target | 并发 | 吞吐中位数/改善轮数 | P99 中位数/改善轮数 | 分配中位数/改善轮数 | CPU 中位数/改善轮数 | Registry/Typed errors | Registry/Typed error ppm |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| MySQL | AppendOnly | 1 | +2.13% / 5 | -2.88% / 4 | -22.06% / 5 | -2.89% / 3 | 0 / 0 | 0.0 / 0.0 |
| MySQL | AppendOnly | 8 | +0.11% / 3 | -3.14% / 4 | -13.50% / 5 | +4.82% / 2 | 0 / 0 | 0.0 / 0.0 |
| MySQL | AppendOnly | 32 | +0.34% / 3 | +7.59% / 1 | -11.68% / 5 | +0.86% / 2 | 0 / 0 | 0.0 / 0.0 |
| MySQL | Legacy | 1 | -0.72% / 2 | +2.54% / 2 | -19.88% / 5 | +12.60% / 1 | 0 / 0 | 0.0 / 0.0 |
| MySQL | Legacy | 8 | +0.14% / 3 | -0.98% / 4 | -12.23% / 5 | +11.18% / 0 | 1 / 0 | 45.6 / 0.0 |
| MySQL | Legacy | 32 | -1.07% / 1 | +2.82% / 2 | -10.93% / 5 | -6.77% / 3 | 0 / 0 | 0.0 / 0.0 |
| SQL Server | AppendOnly | 1 | +1.07% / 4 | +0.30% / 2 | -15.15% / 5 | -22.00% / 4 | 1 / 1 | 114.2 / 109.1 |
| SQL Server | AppendOnly | 8 | +5.55% / 4 | -4.73% / 3 | -3.22% / 5 | -23.99% / 4 | 1 / 10 | 19.3 / 181.9 |
| SQL Server | AppendOnly | 32 | -3.09% / 1 | +3.11% / 1 | -5.83% / 5 | -9.97% / 4 | 62 / 45 | 644.9 / 478.6 |
| SQL Server | Legacy | 1 | +0.05% / 4 | +1.63% / 1 | -13.79% / 5 | +1.30% / 2 | 1 / 2 | 129.1 / 256.8 |
| SQL Server | Legacy | 8 | -0.87% / 2 | +7.26% / 2 | -2.47% / 5 | -10.51% / 4 | 7 / 0 | 125.5 / 0.0 |
| SQL Server | Legacy | 32 | +3.87% / 5 | -4.71% / 3 | -4.01% / 5 | -7.75% / 4 | 35 / 51 | 373.3 / 517.8 |

次级延迟指标同样没有形成全 cell 稳定优势：

| Provider | Target | 并发 | P50 中位数/改善轮数 | P95 中位数/改善轮数 | SQL P99 中位数/改善轮数 | 连接等待 P99 中位数/改善轮数 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| MySQL | AppendOnly | 1 | -1.87% / 5 | -0.96% / 5 | -7.81% / 5 | -0.26% / 3 |
| MySQL | AppendOnly | 8 | -0.93% / 4 | -1.77% / 4 | -5.74% / 5 | -0.33% / 3 |
| MySQL | AppendOnly | 32 | -1.29% / 3 | +0.92% / 1 | +24.91% / 1 | +7.06% / 2 |
| MySQL | Legacy | 1 | +0.32% / 2 | -0.49% / 3 | +1.34% / 2 | +1.41% / 2 |
| MySQL | Legacy | 8 | -0.44% / 3 | +0.97% / 2 | -0.06% / 3 | +1.76% / 1 |
| MySQL | Legacy | 32 | +0.57% / 2 | +0.06% / 2 | +23.37% / 1 | -0.44% / 3 |
| SQL Server | AppendOnly | 1 | -1.54% / 4 | -0.21% / 3 | -5.47% / 3 | n/a |
| SQL Server | AppendOnly | 8 | -2.46% / 4 | -6.45% / 3 | -3.98% / 4 | n/a |
| SQL Server | AppendOnly | 32 | -1.64% / 3 | +6.44% / 2 | +10.29% / 1 | n/a |
| SQL Server | Legacy | 1 | -0.25% / 3 | +0.79% / 2 | -1.60% / 5 | n/a |
| SQL Server | Legacy | 8 | -3.38% / 3 | +7.76% / 2 | -5.22% / 5 | n/a |
| SQL Server | Legacy | 32 | -6.71% / 5 | -6.99% / 5 | -23.04% / 5 | n/a |

表中的 SQL Server connection-wait `n/a` 表示 Provider 指标未产出分布，不表示零等待；本轮只能确认其 connection timeout 计数为 0。

### 加权资源与错误结果

| Provider | Target | Path | Writes | Errors | Error ppm | Allocated B/write | CPU μs/write |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| MySQL | AppendOnly | Registry | 96,979 | 0 | 0.0 | 27,007 | 633.8 |
| MySQL | AppendOnly | Typed | 97,173 | 0 | 0.0 | 23,621 | 634.2 |
| MySQL | Legacy | Registry | 97,003 | 1 | 10.3 | 25,305 | 607.7 |
| MySQL | Legacy | Typed | 95,345 | 0 | 0.0 | 22,410 | 623.2 |
| SQL Server | AppendOnly | Registry | 156,759 | 64 | 408.1 | 72,031 | 546.6 |
| SQL Server | AppendOnly | Typed | 158,129 | 56 | 354.0 | 68,000 | 478.1 |
| SQL Server | Legacy | Registry | 157,262 | 43 | 273.4 | 66,606 | 556.9 |
| SQL Server | Legacy | Typed | 163,802 | 53 | 323.5 | 63,944 | 517.3 |

全部场景合并仅用于衡量资源规模：Registry 为 508,003 writes / 108 errors / 212.6 ppm / 52,834 B/write / 578.1 μs CPU/write；Typed 为 514,449 writes / 109 errors / 211.8 ppm / 49,877 B/write / 546.9 μs CPU/write。整体错误率近似不代表逐 cell 不回退，也不能覆盖 P99 门槛。

### 下一步与停止条件

当前五次重复证据已经足够支持本轮裁决：**不切**；不再为本次决策追加本地样本。若未来 Outbox 命令准备重新成为生产 Profile 的显著瓶颈，应在隔离、固定 CPU/memory 的 Linux 性能环境中重新建模，并让 runner 记录 SQL failure 的异常类别、稳定错误码与采样窗口归属；当前 JSON 只有失败计数，原因仍未分类，不能预设为窗口边界噪声。在此之前保持 `StaticRegistry`，不再扩大 Typed Factory 范围。

## Profile 可观测性闭环（2026-08-28）

本节只修复后续证据工具，不重算前述 120 样本，也不改变 **No-Go**。根因已经从源码确认：MySqlConnector 发布 `db.client.connections.wait_time`，而 SqlClient EventCounters 没有对应等待直方图，原 SQL Server 监听器因此按设计返回 `ConnectionWait = null`；同时 Outbox Profile worker 的最终异常分支只有 `Errors++`，异常类别、Provider 错误码和测量窗口归属会丢失。

修复后保留两层不同语义：

- `ConnectionWait` 继续表示 Provider 驱动公开的池内部等待；SQL Server 缺失时仍为 `null`，不伪造为零。
- `ConnectionAcquisition` 监听生产同源的 `fullnet.data.connection_pool/fullnet.db.connection.wait`，记录 `DbSession` 从进入准入边界到连接打开成功或失败的等待，因此 SQL Server/MySQL 都可比较。样本缓冲在测量前固定分配 131,072 项，热路径只做数组写入与固定原子计数；溢出会增加 `DroppedSamples` 并令 `EvidenceComplete=false`，避免监听器逐写入分配污染 A/B 资源指标或静默截断。
- `SqlFailureReasons` 与 `SqlCancellations` 保留 Dapper 低基数失败原因和取消计数；`AttemptFailures` 记录稳定原因、Provider 数字错误码、`WindowOwned` 与计数；`WindowCanceledAttempts` 单独记录窗口按期结束时取消的在途尝试。工件不包含异常消息、原始 SQL 或参数。

双库最小 smoke 命令：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release --no-build -- outbox-write-profile `
  --providers sqlserver,mysql `
  --concurrency 8 `
  --targets legacy `
  --command-paths registry `
  --payload-size 256 `
  --repetitions 1 `
  --warmup-seconds 1 `
  --duration-seconds 5 `
  --output BenchmarkDotNet.Artifacts/outbox-profile-observability-smoke-20260828
```

原始工件（本地、未提交）：`BenchmarkDotNet.Artifacts/outbox-profile-observability-smoke-20260828/outbox-write-profile.json`，7,007 B，SHA-256 `7B7B807EA47B369635592832BE8E1B79516C55AAF5B9F33AF88E47F334DF20F2`。

| Provider | Writes | Errors | SQL failures | SQL cancellations | Window cancellations | Acquisition captured/dropped | Acquisition P50 | Provider wait |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| SQL Server | 4,402 | 0 | 0 | 1 | 8 | 4,410 / 0 | 0.0164 ms | unavailable |
| MySQL | 2,837 | 0 | 0 | 1 | 2 | 2,839 / 0 | 0.8534 ms | available |

两库 `AttemptFailures` 均为空并与 `Errors = 0` 一致，连接获取样本均为 `EvidenceComplete=true` 且零丢弃。窗口取消与 SQL 取消不要求相等：前者统计每个 worker 在窗口结束时被取消的在途尝试，后者只统计取消发生在 Dapper SQL 计量边界内的执行。该结果证明采集链路有效，不是性能 A/B，也不用于重开 Typed Plan 决策；状态仍为 `Capacity-not-verified`，Production 仍为 `StaticRegistry`。

验证结果：聚焦 Profile 合约测试 8/8；Benchmark Release 构建 0 警告/0 错误；受影响 Integration inner 选择器判定无目标；Governance 52/52；项目 Skill 合同通过；独立复审修复了监听器逐写入采样可能污染分配指标的问题，修复后无剩余 Critical/Important finding。Naming 仍为 29/30，唯一失败是任务基线已有的 migration 100 `FNSQL003` 四处 unsupported DDL，与本次 benchmark/docs 影响集无关。`git diff --check` 无错误，仅有工作区行尾转换提示。

## SQL Server 窗口错误归类复核（2026-08-28）

本轮只回答前述 SQL Server 错误是否发生在有效测量窗口内。范围固定为此前错误较多的 concurrency 32：2 targets × 2 paths × 2 repetitions，共 8 个交错样本；每样本 warmup 3 s、measurement 10 s。它不是新的性能决策矩阵。

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release --no-build -- outbox-write-profile `
  --providers sqlserver `
  --concurrency 32 `
  --targets legacy,append `
  --command-paths registry,typed `
  --payload-size 256 `
  --repetitions 2 `
  --warmup-seconds 3 `
  --duration-seconds 10 `
  --output BenchmarkDotNet.Artifacts/outbox-sqlserver-failure-classification-20260828
```

原始工件（本地、未提交）：`BenchmarkDotNet.Artifacts/outbox-sqlserver-failure-classification-20260828/outbox-write-profile.json`，27,186 B，SHA-256 `46F2C5AB5A2276001F24327538FCE21760A9B4BBDEBD879FEFDD1FAA06EED00D`。

| Target | Path | Writes | Errors / SQL failures | SQL cancellations | Window cancellations | code 0 / code 3980 | Non-window failures | Max acquisition P99 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| AppendOnly | Registry | 40,365 | 26 / 26 | 22 | 24 | 17 / 9 | 0 | 0.0386 ms |
| AppendOnly | Typed | 41,364 | 17 / 17 | 20 | 33 | 16 / 1 | 0 | 0.0351 ms |
| Legacy | Registry | 34,292 | 14 / 14 | 2 | 23 | 6 / 8 | 0 | 0.0498 ms |
| Legacy | Typed | 38,333 | 26 / 26 | 25 | 27 | 22 / 4 | 0 | 0.0405 ms |

结论：全部 83 个业务错误的 `WindowOwned` 均为 `true`，没有测量窗口运行期间的非取消失败。SQL Server 官方错误目录将 3980 定义为批次已中止，可能由客户端 abort signal 或同一会话仍忙导致；本轮 22 个 3980 全部与窗口取消同时发生，和该定义一致。其余 61 个 Provider code 0 不根据数字本身推断具体根因，只能由本轮 `WindowOwned=true` 证明它们发生在窗口取消之后。SqlClient 可能以 `SqlException` 而非 `OperationCanceledException` 呈现异步取消，因此 `Errors`/`SqlFailures` 与 `SqlCancellations` 不要求逐项相等。

8 个连接获取快照全部 `EvidenceComplete=true`、`DroppedSamples=0`；acquisition P99 最大为 0.0498 ms。这批错误不是连接获取饱和证据，也没有证明 Typed 或 Registry 的运行期正确性差异。它关闭了“错误来源未分类”这一证据缺口，但此前 7/12 cell 的 P99 中位数回退仍然成立，所以 Production 继续 `StaticRegistry`、Typed Plan 继续 Testing/benchmark-only，状态保持 `Capacity-not-verified`。

参考：[SQL Server Database Engine errors 3000–3999](https://learn.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors-3000-to-3999)、[SqlClient cancellation behavior discussion](https://github.com/dotnet/SqlClient/issues/26)。
