# Full.NET 测试矩阵与本地门禁

权威数字与分片定义只在 [`test-matrix.json`](test-matrix.json)。本文只描述**如何少误跑**。

## 日常漏斗

| 阶段 | 何时 | 命令 |
| --- | --- | --- |
| inner | 每次改代码 | `pnpm test:integration:affected:plan -- --snapshot <id> --phase inner` 然后 `pnpm test:integration:affected ...` |
| slice | 纵向功能切片关闭 | 同上，`--phase slice` |
| merge | PR / 合并候选 | 同上，`--phase merge` |
| main CI | 合入受保护分支 | 五个互斥 Integration 分片（含 `messaging-heavy`） |

工作区脏或跨窗口时先 `pnpm test:task:start -- <task-id>`，后续一律 `--snapshot <task-id>`。

## 不要做的事

- 本地禁止 `pnpm test:integration:full`（完整 585 项只留给 main CI）。
- 改文档 / 纯前端 / 纯 `benchmarks/` 不必跑 Integration。
- merge 默认不跑 `messaging-heavy`；Kafka/CDC/Capacity 变更在 slice 验证，完整重测交给 main CI 或 `pnpm test:integration:messaging-heavy`。
- 需要本地 merge 也跑重测时，追加 `--include-heavy`。

## 按变更选门禁

| 变更 | 最低验证 |
| --- | --- |
| 单模块 CRUD（不动 SQL/租户/认证） | Unit + affected inner/slice |
| SQL / 事务 / 租户 / 迁移 | 同场景 SQL Server **与** MySQL（slice/merge） |
| Messaging / Kafka / CDC / Connect | Unit + affected slice（含 `messaging-heavy`） |
| 共享宿主 / Composition / Identity / Tenancy | inner 立即跑登记聚焦集；merge 追加 Smoke |
| 测试矩阵 / 选择器 | `pnpm test:integration:partitions` + `pnpm test:governance` |

## inner 与双库

`inner` 聚焦测试只强制 **MySQL** Provider，用于加快内循环；`slice` 与 `merge` 仍要求双库。SQL/迁移/租户相关变更不得在 inner 阶段单独宣布完成。

## 慢测与分片

- `pnpm test:integration:durations`：分析 TRX，找 Top 慢测。
- `pnpm test:integration:messaging-heavy`：Kafka/CDC/Capacity Docker 重测（51 项，与 infrastructure 分离）。
- 新增 Integration 前先读 [`rules/development-quality.md`](../../rules/development-quality.md) §11.2。

## 推荐内循环

```powershell
pnpm test:task:start -- my-feature-20260816
dotnet build Full.NET.slnx -c Release
pnpm test:dotnet:unit -- --no-build --filter FullyQualifiedName~YourArea
pnpm test:integration:affected:plan -- --snapshot my-feature-20260816 --phase inner
pnpm test:integration:affected -- --snapshot my-feature-20260816 --phase inner
```

功能切片关闭后再 `--phase slice`；PR 前 `--phase merge` 与 `pnpm test:governance`。
