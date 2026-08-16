# Full.NET Capacity Certification Harness

Dedicated-cluster k6 harness for high-concurrency certification. **Do not** run these profiles against a developer laptop or shared CI as an automatic capacity gate.

## Rules

- Keep `Capacity-not-verified` until a dedicated production-equivalent environment completes certification.
- Never treat k6 VU count as actual in-flight requests. Always record `actual active requests`.
- Run both closed-loop (stability) and open-loop (arrival / queueing / coordinated omission) models.
- Execution order gate: **2K → 5K → 10K → Soak**.
- Certify **SQL Server** and **MySQL** separately with the same profile set.
- Missing any required evidence checklist item ⇒ result = `Incomplete`.

## Profiles

| Profile | Target in-flight | Notes |
|---|---|---|
| `2k` | 2000 | Entry gate |
| `5k` | 5000 | Mid gate |
| `10k` | 10000 | Design target; still Capacity-not-verified until certified |
| `soak` | 5000 | Long soak after 10k gate |

## Scenarios

- `read-heavy`
- `mixed-write`
- `cache-recovery`
- `audit-logging`
- `outbox-jobs-backlog`

## Local validation only

```bash
pnpm test:load-profiles
```

This validates profile contracts. It does **not** start a load run against local Aspire/AppHost.

## Dedicated cluster run (manual)

```bash
export FULLNET_BASE_URL=https://api.example
export FULLNET_DB_PROVIDER=SqlServer   # or MySql
export FULLNET_LOAD_PROFILE=2k
export FULLNET_LOAD_MODEL=closed_loop  # or open_loop
k6 run eng/load/k6/scenarios/read-heavy.js
```

Kubernetes Job template: `deploy/load/k6-test-run.yaml`.

Certification evidence template: `docs/verification/high-concurrency-capacity-certification-template.md`.

## 专用环境认证 checklist（未执行）

下列项必须在**专用生产等价环境**完成并归档后，方可讨论移除 `Capacity-not-verified`；Scope B/C Kafka 集成测试、本地 k6 profile 校验 **不等于** 本清单完成。

- [ ] Data Protection Key Ring 多实例共享与轮换演练
- [ ] 全局限流 / 租户限流与 Files 多实例一致性
- [ ] 1 / 2 / 4 / 8 实例模块化单体矩阵（读重、混合写、缓存恢复、审计、Outbox/Jobs 积压）
- [ ] SQL Server 与 MySQL 分别完成 2K→5K→10K→Soak 门禁
- [ ] 滚动升级、连接耗尽、Broker/DB 故障注入与恢复对账
- [ ] 运维 Runbook 与告警阈值在专用环境实测

当前状态：**未执行**；所有对外表述保持 `Capacity-not-verified`。
