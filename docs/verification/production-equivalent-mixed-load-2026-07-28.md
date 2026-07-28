# 生产等价混合负载与性能预算验证

- 日期：2026-07-28
- 状态：正式 V3 已冻结；V2 保留为诊断数据
- 基线提交：`db290c1de68024f9a8bc3e885decc0a67dfe24fa`
- 范围：真实 API Host 请求链、JWT、API Key、读写请求、Audit 与事务 Outbox 入队

## 1. 结论与边界

Task 20 已建立可重复的 SQL Server/MySQL 混合负载驱动。驱动为每个
Provider/并发单元创建独立 Testcontainers 数据库和 `WebApplicationFactory` API
Host，执行正式迁移、管理员引导、租户准备、JWT 登录与 API Key 创建；预检全部场景
后才进入预热和采样。

本记录最终只冻结同一开发机的回归门槛，不是生产 SLA，也不用于比较两个 Provider 的
绝对性能。HTTP 请求经过真实应用 Host、中间件、认证授权、Dapper 与数据库，但
`WebApplicationFactory` 使用进程内 TestServer，未包含外部负载均衡、TLS、真实网络和
独立 Kestrel 进程成本。正式采样未启动 Worker，因此 Outbox 指标表示可靠入队的积压
增长与最老年龄，不表示发布吞吐。

## 2. 冻结 workload

随机种子固定为 `20260728`；每个并发 Worker 使用独立租户和乐观并发版本，避免测试
驱动自身制造同租户版本冲突。权重总和为 100：

| 场景 | 权重 | 认证 | 类型 | 预期状态 | 额外覆盖 |
| --- | ---: | --- | --- | ---: | --- |
| `jwt-read` | 25 | JWT | Read | 200 | Dashboard 查询 |
| `jwt-write-outbox` | 15 | JWT | Write | 200 | Tenant 更新与 Outbox |
| `api-key-read` | 25 | API Key | Read | 200 | 用户列表 |
| `api-key-write-outbox` | 15 | API Key | Write | 200 | Tenant 更新与 Outbox |
| `audit-list` | 10 | JWT | Read | 200 | AccessLog cursor |
| `validation-failure` | 10 | JWT | Write | 400 | 可预期参数验证失败 |

契约测试同时冻结默认 Provider、`1/4/16/32` 并发、30 秒预热、每档 600 秒稳态、
随机种子、最大非预期错误率以及必需指标；400 验证失败属于预期响应，不计入错误率。

## 3. 指标与预算

每档记录请求原始样本、状态码、P50/P95/P99/最大值、Dapper Statement 次数与失败、
Host 进程 CPU/分配/GC、驱动层连接池峰值与等待/超时、数据库会话与锁等待、
AccessLog 写入，以及 Outbox pending 增量和最老消息年龄。SQL Server 连接池使用
`Microsoft.Data.SqlClient.EventSource`，MySQL 使用 `MySqlConnector` Meter；数据库
会话另通过 SQL Server DMV 与 MySQL `SHOW GLOBAL STATUS` 观测。正式结果必须同时满足
场景覆盖、Dapper、数据库资源和连接池证据完整门禁，否则工件保留但命令以非零退出。
MySQL 直接观测 active/idle/pending/timeout/wait；SqlClient 不公开 pending/timeout，
因此 SQL Server 冻结替代门禁：active 峰值不得超过 `MaxPoolSize` 的 90%，且 Dapper
失败必须为 0，报告中保留 `null` 表示不可直接观测，禁止伪造为零。
数据库容器 CPU 按 Docker Engine 的系统总 CPU 增量归一化，并单独记录平均/峰值与
内存峰值；Host 进程 CPU 与数据库容器 CPU 不混为一个预算。

预算在代码和契约测试中按 Provider 独立冻结：

| Provider | P95 | P99 | 非预期错误率 | Host 进程 CPU | 数据库容器 CPU |
| --- | ---: | ---: | ---: | ---: | ---: |
| SQL Server | ≤ 750 ms | ≤ 2500 ms | ≤ 0.5% | ≤ 85% | ≤ 85% |
| MySQL | ≤ 1000 ms | ≤ 3000 ms | ≤ 0.5% | ≤ 85% | ≤ 85% |

预算用于发现明显回归，不能据此承诺固定 QPS。若运行环境的 CPU、容器资源、数据库
版本或网络拓扑变化，必须另建同环境基线，禁止混合样本直接覆盖当前门槛。

## 4. 早期正确性与矩阵预检（不作为 V3 基线）

基准硬化早期先运行每档 2 秒预热、10 秒采样的双库矩阵。该轮只验证场景和双库链路，
未使用最终 V3 的完整遥测与长窗参数，不作为性能基线：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release --no-build -- mixed-load `
  --providers sqlserver,mysql --concurrency 1,4,16,32 `
  --warmup-seconds 2 --duration-seconds 10 `
  --output .tmp/mixed-load-matrix-smoke-v2
```

| Provider | 并发 | 请求 | QPS | P95 ms | P99 ms | 非预期错误 | Dapper 失败 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| SQL Server | 1 | 828 | 82.74 | 29.326 | 35.751 | 0 | 0 |
| SQL Server | 4 | 2539 | 253.47 | 36.628 | 43.859 | 0 | 0 |
| SQL Server | 16 | 4528 | 450.86 | 82.152 | 116.261 | 0 | 0 |
| SQL Server | 32 | 6059 | 602.69 | 139.393 | 180.259 | 0 | 0 |
| MySQL | 1 | 652 | 65.18 | 44.561 | 54.979 | 0 | 0 |
| MySQL | 4 | 2045 | 204.12 | 58.659 | 70.057 | 0 | 0 |
| MySQL | 16 | 5689 | 566.14 | 87.030 | 104.372 | 0 | 0 |
| MySQL | 32 | 8651 | 859.63 | 116.987 | 136.672 | 0 | 0 |

8 个单元均生成汇总、manifest 和独立 NDJSON 原始样本；数据库资源采集错误为 0。
AccessLog 增量与请求数逐档相等，Outbox pending 随成功写场景增长，说明全请求审计和
事务 Outbox 入队均进入被测链路。

## 5. V3 10 分钟正式基线

正式 V3 从已提交源码 `db290c1de68024f9a8bc3e885decc0a67dfe24fa` 构建并运行，
源码版本由工件记录为
`1.0.0+db290c1de68024f9a8bc3e885decc0a67dfe24fa`。命令为：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release --no-build -- mixed-load `
  --output BenchmarkDotNet.Artifacts/mixed-load/formal-20260728-v3
```

运行环境为 .NET `10.0.9`、Windows `10.0.19045`、20 个逻辑处理器。SQL Server
镜像为 `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04`、版本
`16.0.4135.4`；MySQL 镜像为 `mysql:8.0`、版本 `8.0.46`。

| Provider | 并发 | 请求 | QPS | P50 ms | P95 ms | P99 ms | 非预期错误 | Host CPU | DB CPU 平均/峰值 | 预算 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| SQL Server | 1 | 33,783 | 56.30 | 16.624 | 30.389 | 36.437 | 0 | 0.95% | 1.72% / 6.69% | PASS |
| SQL Server | 4 | 91,444 | 152.37 | 25.353 | 46.525 | 64.017 | 0 | 2.60% | 6.94% / 12.43% | PASS |
| SQL Server | 16 | 145,956 | 243.22 | 52.992 | 156.820 | 255.720 | 0 | 4.95% | 20.39% / 32.95% | PASS |
| SQL Server | 32 | 173,126 | 288.47 | 75.538 | 315.410 | 477.453 | 0 | 6.23% | 35.55% / 51.67% | PASS |
| MySQL | 1 | 20,827 | 34.71 | 24.194 | 51.434 | 83.146 | 0 | 0.67% | 1.29% / 1.86% | PASS |
| MySQL | 4 | 63,387 | 105.64 | 34.338 | 65.637 | 90.801 | 0 | 1.96% | 4.27% / 8.04% | PASS |
| MySQL | 16 | 126,867 | 211.41 | 66.751 | 166.615 | 259.247 | 0 | 4.33% | 18.75% / 34.39% | PASS |
| MySQL | 32 | 122,873 | 204.66 | 103.323 | 479.225 | 1066.218 | 0 | 4.08% | 25.39% / 46.25% | PASS |

V3 总计 778,263 个完整响应请求，非预期错误 0、Dapper 失败 0、数据库资源采集错误 0；
8 档场景、Dapper、数据库、连接池与容器资源证据门禁全部 PASS。8 个 raw 文件共
161.48 MiB。各档 AccessLog 增量与请求数严格相等，Outbox pending 增量与成功写场景
数量一致。`summary.json`、`README.md` 和最后原子替换的 `manifest.json` 共享
`ReportId=57e3e97f-7718-4837-beeb-b493840fe4d3`。

### 5.1 连接池、锁与 GC 证据

| Provider | 并发 | Pool active 峰值 | Pending 峰值 | Pool timeout | Pool wait P95 ms | 锁等待 ms | Gen2 GC | Outbox Δ |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| SQL Server | 1 | 1 | 不可直接观测 | 不可直接观测 | 不可直接观测 | 0 | 6 | 10,059 |
| SQL Server | 4 | 4 | 不可直接观测 | 不可直接观测 | 不可直接观测 | 117,780 | 6 | 27,386 |
| SQL Server | 16 | 16 | 不可直接观测 | 不可直接观测 | 不可直接观测 | 1,603,373 | 6 | 43,695 |
| SQL Server | 32 | 32 | 不可直接观测 | 不可直接观测 | 不可直接观测 | 5,147,185 | 174 | 51,799 |
| MySQL | 1 | 1 | 1 | 0 | 2.224 | 0 | 3 | 6,138 |
| MySQL | 4 | 4 | 4 | 0 | 2.037 | 26 | 6 | 19,065 |
| MySQL | 16 | 16 | 13 | 0 | 4.215 | 201 | 6 | 37,963 |
| MySQL | 32 | 33 | 22 | 0 | 15.717 | 510 | 6 | 36,698 |

SQL Server 从 c=16 到 c=32 的 QPS 仍增长 18.6%，不能再沿用 V2 的“吞吐下降”结论；
但 P95 增长 101.1%，锁等待时间增至约 3.21 倍，Gen2 GC 从 6 次升至 174 次。
MySQL 同区间 QPS 下降 3.2%，P95 增长 187.6%，P99 增长 311.3%，连接池等待 P95
增至约 3.73 倍。这说明 c=32 虽未越过宽松回归预算，却已不是当前混合链路的推荐默认值。
后续 A/B 统一以 c=16 为主要参考点，同时保留 c=32 作为压力退化档；不得把本机数值解释为
生产容量承诺。

### 5.2 热路径与后续顺序

c=32 时 SQL Server 最热场景是 `jwt-read`，P95/P99 为 `441.5/658.1ms`；
`audit-list` 为 `189.8/304.7ms`。MySQL c=32 的两类 Outbox 写最热：
`api-key-write-outbox` 为 `738.5/1407.0ms`，`jwt-write-outbox` 为
`758.2/1453.3ms`；`jwt-read` 为 `495.2/896.8ms`。结合既有 100,000 行 contains
证据，后续顺序冻结为：

1. Task 21 限制 Audit contains 的无界扫描风险；
2. Task 22 用同一 workload 做 Audit 同步写入开关 A/B，量化 SQL Server 锁竞争与两库尾延迟占比；
3. Task 24 独立压测 Outbox 消费容量与慢 Handler，不依据本轮入队 QPS 提高默认并发；
4. Task 25 在冻结撤销时效与 fail-closed 语义后评估认证请求链缓存。

正式工件目录未纳入 Git，包含 `manifest.json`、`summary.json`、`README.md` 与
`raw/*.ndjson`；验证记录冻结其关键结果和可重复命令。

## 6. V2 10 分钟稳态（诊断数据，不作为正式基线）

正式命令使用默认冻结参数：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release --no-build -- mixed-load `
  --output BenchmarkDotNet.Artifacts/mixed-load/formal-20260728-v2
```

代码评审发现 V2 使用 `ResponseHeadersRead` 后在大多数读取场景没有消费完整响应体，
计时在响应头到达时提前结束；同时只记录数据库会话近似值，没有采集驱动连接池。
因此下表的延迟、QPS、预算 PASS 和饱和结论全部降级为诊断线索，不得用于回归门槛、
容量承诺或后续 A/B 验收。修复已加入完整响应体消费、真实连接池遥测、证据完整门禁和
数据库容器周期采样、原子工件写入；正式 V3 必须从已提交且可定位的源码重新运行全部八档。

首轮长窗在 SQL Server c=1 完成后主动终止：审查发现 Runner 会把已完成单元的全部请求
对象保留到整个矩阵结束，可能让前档样本污染后档 GC/内存基线。该轮只作为诊断，不纳入
正式结论。新增检查点 RED/GREEN 后，v2 从头运行；每档完成即完整写入原始样本文件、
释放内存引用并刷新汇总。

正式环境为 .NET `10.0.9`、Windows `10.0.19045`、20 个逻辑处理器；Docker Desktop
报告 31.22 GiB 内存。SQL Server 镜像为
`mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04`、版本 `16.0.4135.4`；
MySQL 镜像为 `mysql:8.0`、版本 `8.0.46`。

| Provider | 并发 | 请求 | QPS | P50 ms | P95 ms | P99 ms | 非预期错误 | CPU | 预算 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| SQL Server | 1 | 50,384 | 83.97 | 8.808 | 26.549 | 32.412 | 0 | 1.17% | PASS |
| SQL Server | 4 | 123,907 | 206.50 | 20.526 | 41.417 | 52.276 | 0 | 2.83% | PASS |
| SQL Server | 16 | 192,904 | 321.45 | 43.230 | 147.816 | 225.450 | 0 | 5.12% | PASS |
| SQL Server | 32 | 156,412 | 260.62 | 80.215 | 405.979 | 685.834 | 0 | 5.06% | PASS |
| MySQL | 1 | 30,992 | 51.65 | 11.468 | 49.429 | 59.470 | 0 | 0.90% | PASS |
| MySQL | 4 | 96,651 | 161.08 | 14.500 | 59.584 | 77.659 | 0 | 2.31% | PASS |
| MySQL | 16 | 203,430 | 339.01 | 37.686 | 126.565 | 160.622 | 0 | 5.83% | PASS |
| MySQL | 32 | 229,350 | 382.13 | 68.651 | 230.276 | 288.115 | 0 | 7.22% | PASS |

V2 总计 1,084,030 个诊断请求，非预期错误 0、Dapper 失败 0、数据库资源采集错误 0。
8 个 raw 文件共 224.72 MiB；各档 AccessLog 增量与请求数严格相等，Outbox pending
增量与成功写场景数量一致。

### 6.1 资源与饱和证据

| Provider | 并发 | 会话前→后 | 锁等待次数 | 锁等待 ms | Gen2 GC | Outbox Δ |
| --- | ---: | --- | ---: | ---: | ---: | ---: |
| SQL Server | 1 | 5→8 | 4,669 | 14,561 | 6 | 15,014 |
| SQL Server | 4 | 13→13 | 63,908 | 263,282 | 6 | 37,006 |
| SQL Server | 16 | 40→35 | 299,909 | 2,365,520 | 6 | 57,872 |
| SQL Server | 32 | 71→75 | 522,678 | 7,326,388 | 79 | 46,759 |
| MySQL | 1 | 6→7 | 0 | 0 | 4 | 9,191 |
| MySQL | 4 | 20→22 | 1 | 1 | 6 | 28,942 |
| MySQL | 16 | 51→52 | 5 | 36 | 6 | 61,048 |
| MySQL | 32 | 101→100 | 11 | 142 | 13 | 68,562 |

V2 曾观察到 SQL Server c=32 的吞吐比 c=16 下降 18.9%，P95 增长 174.6%，锁等待时间达到约
3.10 倍且 Gen2 GC 从 6 次升至 79 次；本机混合写链路已在 c=16 后饱和。不能把
“错误率仍为 0”解释为可继续增加并发。MySQL c=32 吞吐仍增长 12.7%，但服务端会话
已经达到约 100，继续提高并发前必须先冻结连接池预算并验证等待/超时，不能直接把
Worker 或请求并发提高到 64。上述判断仅作为 V3 需要复核的假设。

### 6.2 热路径方向

V2 的 c=32 场景 P95/P99 显示 JWT 读链可能最热：SQL Server 为
`608.7/966.9ms`，MySQL 为 `271.9/318.8ms`；两类 Outbox 写为 SQL Server
约 `293–298/527–537ms`、MySQL 约 `201/283–291ms`。无 contains 的 Audit cursor
在 MySQL 保持 `20.2/27.7ms`，SQL Server 则随全请求 AccessLog 写锁竞争升至
`255.9/460.5ms`。这些数据只提供待 V3 复核的后续顺序：

1. Task 21 先限制 Audit contains 的无界扫描风险；
2. Task 22 量化同步 Audit 写入对尾延迟和 SQL Server 锁竞争的占比；
3. Task 25 在冻结撤销时效与 fail-closed 语义后评估 JWT 请求链缓存；
4. Task 24 的 Outbox Worker 容量必须独立压测，不能依据本轮入队 QPS提高默认并发。

工件目录包含：

- `manifest.json`：固定参数、workload、必需指标和预算；
- `summary.json`：环境、Provider、数据库版本、各档汇总与预算判定；
- `README.md`：便于人工检查的结果摘要；
- `raw/*.ndjson`：逐请求原始样本。

`summary.json`、`README.md` 与 `manifest.json` 共享同一 `ReportId`；manifest 最后原子
替换，作为该轮汇总已完整落盘的完成标记。

## 7. 自动化验证

本 Task 新增 13 个契约测试，Unit canonical 从 461 提升到 474。除 workload 和预算外，
测试还锁定完整响应体消费、连接池采集、证据缺失失败门禁，以及“每档先原子写入原始
样本、再释放内存引用”，避免长矩阵累积样本污染后续 GC/内存基线。完成前执行：

```powershell
dotnet build Full.NET.slnx -c Release --no-restore
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll `
  --no-ansi --progress off --minimum-expected-tests 474
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll `
  --no-ansi --progress off --minimum-expected-tests 7
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll `
  --no-ansi --progress off --minimum-expected-tests 49
git diff --check
```

新鲜结果：

| 门禁 | 结果 |
| --- | --- |
| Release build | 0 warning、0 error |
| Unit / Compatibility / Architecture | 474/474、7/7、49/49；失败 0、跳过 0 |
| Project Skills | `fullnet-module-delivery` 52 项、`fullnet-performance-hardening` 33 项通过 |
| Governance / Naming / Performance governance | 11/11、23/23、3/3 通过 |
| 双库正式矩阵 | V3 共 778,263 请求；8/8 证据与预算 PASS，非预期错误 0、Dapper 失败 0 |
| Git whitespace | `git diff --check` 通过 |

本 Task 只增加隔离基准、测试与文档，不修改生产 API、数据库结构、认证撤销语义、
Audit 可靠性或 Outbox Worker 并发默认值。后续 Task 21–27 的优化必须复用同一
workload 做单变量 A/B；收益不足、预算退化或可靠性回归时拒绝落地。

## 8. 规则与 Skill 复盘

本轮在首个正式单元后发现“跨档保留逐请求对象会污染后续 GC/内存基线”，已补充
逐档检查点和 RED/GREEN 契约。该遗漏属于现有
`rules/performance-engineering.md` 与 `fullnet-performance-hardening` 已覆盖的
实验隔离和自干扰门禁，没有形成新的重复规则缺口，因此不新增规则标识。

Skill 本体不需要新增步骤；其性能地图已补充 `mixed-load` 可重复入口、默认矩阵、
TestServer/生产 SLA 边界和 Outbox 入队/消费容量边界，并同步最新 Unit canonical。
