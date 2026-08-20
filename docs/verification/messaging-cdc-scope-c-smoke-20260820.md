# Messaging Scope C kafka-capacity `scope_c_smoke` 验证记录（2026-08-20）

- **日期：** 2026-08-20
- **基线：** `main@e008f64eecf896c55894d7511483c6703fe8b353`
- **快照：** `kafka-capacity-scope-c-smoke`
- **范围：** GitHub 工作流 Profile `scope_c_smoke`（`transaction_outbox_cdc` + `--host-parity-mode worker`）、契约测试与运维文档；**不**包含生产等价矩阵/Soak，**不**开启 DeliveryCutover

## 1. 方法

1. 在 `.github/workflows/kafka-capacity.yml` 增加 `scope_c_smoke`：强制 `--scope transaction_outbox_cdc` 与 `--host-parity-mode worker`；`--drain-seconds 45`。
2. 缺少 `KAFKA_CAPACITY_MYSQL_CONNECTION_STRING` 或 `KAFKA_CAPACITY_CONNECT_BASE_URI` 时 `exit 0` 安全跳过，日志只打印 Secret **名**，不回显连接串或 Connect 端点值。
3. 更新 `tests/performance/kafka-capacity-workflow-contract.test.mjs` 与 `docs/operations/kafka-capacity-runner.md`。
4. 本机未对专用 `kafka-capacity` Environment / self-hosted Runner 发起真实 `workflow_dispatch`；条件 CDC E2E（SQL Server Agent + MySQL ROW Binlog + Connector/Consumer 重启）未在本任务窗口执行。

## 2. 结论

| 项 | 状态 |
| --- | --- |
| 工作流 Profile 与契约测试 | 已落地（见下方自动化） |
| 容量认证状态 | **`Capacity-not-verified`**（硬保持） |
| `Messaging:DeliveryCutover:Enabled` | **保持 `false`**；本任务未修改宿主配置，也不得因 smoke Profile 存在而切流 |
| 正式矩阵 / Soak / N+1 | **未执行** |
| 专用 Environment 真实 `scope_c_smoke` 运行 | **未执行**（见环境缺口） |

本记录只证明「官方工作流入口与安全跳过契约」就绪，**不能**作为生产等价容量或切流证据。

## 3. 自动化证据

```text
node --test tests/performance/kafka-capacity-workflow-contract.test.mjs
# 2026-08-20：5/5 pass（含 scope_c_smoke 契约）
```

期望：全部断言通过，含 `scope_c_smoke` 强制 `transaction_outbox_cdc` + `worker`、Secret 缺失跳过文案、禁止 `echo` 连接串。

## 4. 环境缺口（未验证项）

下列缺口任一未关闭时，不得把 Scope C 升为 Verified / Capacity-certified，也不得开启 DeliveryCutover：

| 缺口 | 影响 |
| --- | --- |
| GitHub Environment `kafka-capacity` 未配置 `KAFKA_CAPACITY_MYSQL_CONNECTION_STRING` | Profile 安全跳过，无 CDC 样本 |
| 未配置 `KAFKA_CAPACITY_CONNECT_BASE_URI` | Profile 安全跳过，无 Connect 注册 |
| 可选 `KAFKA_CAPACITY_CONNECT_INTERNAL_BOOTSTRAP` / MySQL Connector 凭据覆盖未齐 | Connect 在隔离网络下可能预检失败（Runner 失败关闭） |
| 预迁移容量库缺少 Outbox/Inbox/所有权与 `CurrentOwner=CdcKafka` | 预检失败关闭 |
| 本机/CI 无 Docker + Debezium Connect + SQL Server CDC Agent | SQL Server CDC 路径仍可能 `Inconclusive`（见 [`sqlserver-cdc-ci-debt.md`](sqlserver-cdc-ci-debt.md)） |
| 无 `[self-hosted, linux, x64, kafka-capacity]` Runner 或非 `main` 触发 | Job 不运行 / 被 `if` 门禁拒绝 |
| 未跑 Connector/Consumer 重启、Broker 短中断、重复投递场景 | 可靠性门禁仍开 |

## 5. 规则与边界

- ADR-0006 影子运行与切流门禁不变。
- 规则演进：未命中新增规则或 Skill 触发条件。
