# Outbox 旧版本退役扫描验证

日期：2026-07-27
状态：已完成并合入清理

## 1. 目标与边界

本切片为 Worker 增加一次性只读退役扫描，用于确认某个已注册 Handler 的规范路由和全部历史别名，在指定 `SchemaVersion` 下是否仍存在未处理消息。扫描不领取、修改或重放消息，也不输出载荷、租户、消息标识、异常文本或连接字符串。

本次未修改数据库迁移、消息写入/领取/重试/死信语义、公共 HTTP API、客户端或 Docker 编排。相邻版本自动升级、生产发布平台持续门禁、真实压力基准、指标告警和人工重放自动化仍是后续工作。

`IOutboxBacklogReader` 公共数据抽象新增
`ReadVersionRetirementAsync(...)` 与只读快照类型。这是本能力的显式公共契约变化；
已有 backlog 方法及运行语义保持不变，外部自定义实现需要同步实现新增方法。

## 2. 行为契约

- 两个专用参数必须各出现一次，消息类型不得为空，SchemaVersion 必须是正整数。
- 专用参数在创建通用 Host Builder 前被剥离，不会污染配置参数。
- 请求路由必须唯一匹配当前注册的 Handler；扫描范围固定为规范路由优先、随后是按 Ordinal 去重的全部历史别名。
- SQL Server 与 MySQL 都只聚合目标路由、精确版本且 `ProcessedAtUtc IS NULL` 的记录。
- 普通待处理与死信分别计数，任一非零都返回阻塞；已处理、其他路由或其他版本不阻塞。
- `0` 表示安全排空，`1` 表示命令或路由错误，`2` 表示仍有阻塞消息；报告使用 `outbox.version_retirement.*` 稳定机器码。
- 扫描时不启动 `OutboxProcessor`，但仍启动 Worker Host 以执行现有配置、数据库与路由唯一性启动门禁。

## 3. 测试先行证据

| 切片 | RED | GREEN |
| --- | --- | --- |
| 命令解析 | Unit 构建因命令类型不存在而失败 | 聚焦 Unit **2/2**，Unit 项目 Release 构建 **0 warning / 0 error** |
| 双库只读快照 | Integration 构建因读取契约与快照类型不存在而失败 | Integration 项目 Release 构建 **0 warning / 0 error**；SQL 安全门禁 **5/5** |
| Scanner 与 Worker 接线 | Unit 构建因 Scanner/Report 不存在而失败 | Scanner 聚焦 **3/3**，完整退役扫描 Unit **5/5** |

开发基线上的非 Docker 预验证：

```text
dotnet build Full.NET.slnx -c Release --no-restore
  0 warning / 0 error

dotnet Full.NET.UnitTests.dll --filter FullyQualifiedName~OutboxVersionRetirement
  5/5

dotnet Full.NET.UnitTests.dll
  413/413

pnpm test:sql-safety
  5/5

dotnet format <owned projects> --verify-no-changes
  passed

git diff --check
  passed
```

上述 `413/413` 只属于开发分支旧基线；最终 canonical 以同步前序任务后的新鲜发现与执行结果为准。

## 4. 双库场景

SQL Server 与 MySQL 各新增一个同语义用例。每个用例在新迁移数据库中写入：

- 一个目标规范路由的普通待处理消息；
- 一个目标历史别名的死信；
- 一个已处理的目标消息；
- 一个其他消息类型；
- 一个其他 SchemaVersion。

预期快照为普通待处理 `1`、死信 `1`，最老时间取两个未处理目标消息中的最早值。
Docker Engine READY 后从干净状态运行，SQL Server/MySQL **2/2** 通过，失败
**0**、跳过 **0**，耗时 **1m07s**。

## 5. 最终门禁

DatabaseOptions 与 Caching 已完整合入清理，本分支无冲突同步到
`main@216475cf266b9b1005e7ba07a28bcafb0dfb4f5b`。新鲜发现结果为：

| Suite | 发现数量 |
| --- | ---: |
| Unit | 416 |
| Compatibility | 7 |
| Architecture | 49 |
| Integration | 191 |

已同步 README、getting-started、CI、Integration 分片定义与项目 Skill delivery-map。
Release solution build 已通过，结果为 **0 warning / 0 error**。

最终分支门禁：

| 门禁 | 结果 |
| --- | --- |
| Release solution build | **0 warning / 0 error** |
| Unit | **416/416**，失败 0、跳过 0 |
| Compatibility | **7/7**，失败 0、跳过 0 |
| Architecture | **49/49**，失败 0、跳过 0 |
| 退役快照 SQL Server/MySQL | **2/2**，失败 0、跳过 0，**1m07s** |
| 完整 Integration | MTP 摘要 **191/191**，失败 0、跳过 0，**34m26s** |
| 完整 Integration TRX | `outcome=Completed`，total/executed/passed **191/191/191**；failed/error/timeout/aborted/inconclusive/notExecuted 全 0，墙钟 **34m25.907s** |
| Integration stderr | **0 bytes**，无 `ELIFECYCLE` |
| Integration 分片 | API SQL Server **35** + API MySQL **35** + Migrations **62** + Infrastructure **59** = **191**，无遗漏或重复 |
| Governance / Skill / SQL safety | **11/11** / **52 checks** / **5/5** |
| Integration tooling / workspace | **4/4** / 通过 |
| owned C# format / `git diff --check` | 通过 / 通过 |
| Docker / Integration 进程 | 完整套件结束后 **0 / 0** |

完整套件由 detached wrapper 启动；wrapper 没有保留可在事后读取的 runner
句柄，因此未直接观测数值 ExitCode。这里不声称捕获到 exit 0，而是以 MTP
“已通过”摘要、完整 TRX counters、stderr 0 和无 `ELIFECYCLE` 共同证明成功路径。

执行期间第一次聚焦运行因 Docker Desktop 未启动，在任何数据库断言前以
`DockerUnavailableException` 失败；Engine READY=29.6.1 后已从干净状态重跑
并通过。一次受管沙箱后台尝试同样因无 Docker 命名管道权限失败，且一个重复
后台 run 被按 PID 精确终止；它们都没有计入产品结果，也没有替代上述权威完整
套件。

最终 canonical 为 **416/7/49/191**。功能与门槛提交已 fast-forward 到
`main@995e45bc45b132b791658fc23450739531d45567`。主线新鲜复验结果为 Release
solution build **0 warning / 0 error**、Unit **416/416**、Compatibility **7/7**、
Architecture **49/49**、Governance **11/11**、Skill **52 checks**、Integration
分片发现 **191**、workspace 与 SQL safety **5/5** 均通过。构建首次在受管沙箱
内因其空 NuGet 缓存无法解析 SDK，改用已包含所需 SDK 的本机 NuGet 缓存执行同一
`--no-restore` 命令后通过；该环境失败未计入产品结果。

`codex/outbox-version-retirement-scan` 分支、关联 worktree 与其中的
`node_modules` junction 已删除。最终复核 Docker 容器 **0**、实际
`dotnet.exe` Integration runner **0**；主检出区仅保留用户已有的 `.cache/`
与 `.tmp/art-design-pro/`。

## 6. 规则与 Skills 复盘

- 规则：无变化。本次命令筛选、双库一致语义、敏感信息最小化、公共契约披露、真实验证和 Git 清理均已被现有规则覆盖；MTP 过滤器与 Windows 行尾问题属于已有执行边界，没有新的重复或高风险证据。
- Skills：无变化。本切片首次形成旧版本退役扫描闭环，尚未出现跨模块重复的稳定交付流程；现有 `fullnet-module-delivery` 没有暴露缺口，也不应为单一命令创建新 Skill。
