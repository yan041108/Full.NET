# 测试数量门槛核对记录

- 日期：2026-07-19
- 类型：门槛核对与新鲜验证
- 状态：已完成
- 代码基线：`7894c8d`（`docs: converge agents baselines to rules pointers`）
- 范围：四套 .NET 测试程序集的 `--minimum-expected-tests` 门槛与 README、CI、`getting-started.md`、`delivery-map.md` 声明是否一致，并在真实双库环境执行 Integration 全套
- 方法：Release 构建后按 Microsoft Testing Platform 直接运行测试 DLL；Integration 使用 Testcontainers 拉起 SQL Server 2022 与 MySQL 8.0

## 声明门槛与文档来源

| 套件 | 声明门槛 | 权威来源 |
| --- | ---: | --- |
| `Full.NET.UnitTests` | 291 | `README.md`、`docs/development/getting-started.md`、`.github/workflows/ci.yml`、`.agents/skills/fullnet-module-delivery/references/delivery-map.md` |
| `Full.NET.CompatibilityTests` | 5 | 同上 |
| `Full.NET.ArchitectureTests` | 24 | 同上 |
| `Full.NET.IntegrationTests` | 58 | 同上 |

核对结论：四处 canonical 门槛完全一致，无文档内部漂移。历史计划/评审快照（如 `.superpowers/sdd/*` 中的 `277/287/20/22`）属于过程留档，不作为当前门槛。

## 新鲜自动验证

环境：Windows 10、.NET SDK `10.0.400-preview.0.26322.102`、Docker Desktop（Linux containers）、Testcontainers `ryuk:0.14.0`。

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Release 构建 | `dotnet build Full.NET.slnx -c Release` | 0 警告、0 错误 |
| Unit Tests | `--minimum-expected-tests 291` | **291/291** 通过，约 7s |
| Compatibility Tests | `--minimum-expected-tests 5` | **5/5** 通过，约 3s |
| Architecture Tests | `--minimum-expected-tests 24` | **24/24** 通过，约 9s |
| Integration Tests | `--minimum-expected-tests 58 --timeout 45m` | **58/58** 通过，25m 54s；Workers=2，真实 SQL Server + MySQL |

Integration 执行前曾终止一次孤儿宿主进程并干净重跑；完整摘要见仓库根目录临时日志 `integration-run.log`（未纳入版本控制）。

## 结论

- 声明门槛 **291/5/24/58** 与当前代码基线实测数量 **完全一致**。
- 四套测试在声明门槛下 **全部通过**，无需调整 `--minimum-expected-tests` 数字。
- 本记录 **不能** 将任意能力矩阵项整体提升为 `Verified`；各能力仍须满足自身规格中的跨端、人工或生产验收条件。

## 未验证项

- 客户端工作区：`pnpm test:clients`、`pnpm test:e2e`、`pnpm test:e2e:uniapp` 未在本核对中重跑。
- 治理校验：`pnpm test:governance` 在基线 `7894c8d` 已落地，但不在本次四套 .NET 核对范围内。
- 基准测试：`benchmarks/Full.NET.Benchmarks` 序列化基线未重跑。
- 本记录绑定提交 `7894c8d`；后续未提交工作区变更不在本证据范围内。

## 增补（2026-07-19，基线 `84ab8f5` 之后）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **58 → 60** | 新增 `Session_refresh_and_context_switch_races_are_linearized`（SQL Server + MySQL 各 1） |
| UnitTests 门槛 **293 → 294** | 新增 `E2eHostViewerSeedContributorTests` |
| 新鲜验证 | `pnpm test:e2e:real` **16/16**（SQL Server）；`pnpm test:e2e:real:mysql` **16/16**（MySQL）；新增 `permission-denied`、`session-cross-tab`；CI `real-stack-e2e-mysql` |
| client-contracts | `session-refresh-coordinator` 无 Web Locks 时 `sessionStorage` 互斥回退，单测 **27** 项 |

四处 canonical 门槛已同步为 **295/5/26/62**。

## 增补（2026-07-19，基线 `9760590` 之后）

| 变更 | 说明 |
| --- | --- |
| Integration 门槛 **60 → 62** | 新增 `GuidPrimaryKeyApplicationTests`（SQL Server + MySQL 各 1） |

## 关联文档

- [当前能力状态矩阵](../roadmap/capability-status.md)
- [本地开发与运行指南](../development/getting-started.md)
