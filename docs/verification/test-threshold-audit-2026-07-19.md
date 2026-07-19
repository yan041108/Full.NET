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

四处 canonical 门槛已同步为 **296/6/26/66**。

## 增补（2026-07-19，基线 `9760590` 之后）

| 变更 | 说明 |
| --- | --- |
| UnitTests 门槛 **295 → 296** | `FullNetJsonOptionsTests` 验证 Guid 小写连字符序列化 |
| Compatibility 门槛 **5 → 6** | `AdminNetApiResultMapperTests` 验证 Guid 包络 JSON |
| Integration 门槛 **64 → 66** | 新增 `UuidExternalContractIntegrationTests`（SQL Server + MySQL 各 1） |

## 增补（2026-07-19，基线 `b7ff745` 之后，P0 UUID Task 5 Step 5）

环境：Windows 10、.NET SDK `10.0.400-preview.0.26322.102`、Docker Desktop（Linux containers）、Testcontainers。

| 验证 | 命令要点 | 结果 |
| --- | --- | --- |
| Unit 聚焦 | `--filter "FullyQualifiedName~Guid\|FullyQualifiedName~FullNetJson"`，`--minimum-expected-tests 6` | **9/9** 通过，约 0.4s |
| Integration 聚焦 | `--filter "FullyQualifiedName~Guid\|FullyQualifiedName~IdentityApi\|FullyQualifiedName~TenancyApi\|FullyQualifiedName~Outbox\|FullyQualifiedName~MultiResult"`，`--minimum-expected-tests 20` | **26/26** 通过，约 8m 32s |
| Integration 全量 | `--minimum-expected-tests 66 --timeout 45m` | **66/66** 通过，27m 59s；Workers=2，真实 SQL Server + MySQL |
| Architecture 全量 | `--minimum-expected-tests 26` | **26/26** 通过 |
| Compatibility 全量 | `--minimum-expected-tests 6` | **6/6** 通过 |

结论：声明门槛 **296/6/26/66** 与实测一致；UUID v7 应用持久化、读取路径与外部 JSON 契约相关新增测试在双库聚焦与 Integration 全量下均通过。本记录不将 UUID 能力整体提升为 `Verified`（生产维护窗口与 Runbook 实跑仍缺）。

## 关联文档

- [当前能力状态矩阵](../roadmap/capability-status.md)
- [本地开发与运行指南](../development/getting-started.md)
