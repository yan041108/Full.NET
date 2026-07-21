# Seed 双库契约验证记录

- 日期：2026-07-21
- 切片：Seed 计划 Task 6 — Development/Test/Production 双库纵向契约

## 交付范围

| 层级 | 内容 |
|---|---|
| 测试 | `DevelopmentSeedTests`（SQL Server / MySQL）：Development 首次/重跑、租户冲突、Production Overlay 拒绝、Test Profile 隔离 |
| 夹具 | `TestOnlySeedContributor`（仅 Integration 程序集，不进发布物） |
| 迁移修复 | MySQL `014`/`015` UUID 列改为 `BINARY(16)`；SQL Server `015` 将 `UPDATE DataScopeKind` 包入 `EXEC` 以避免批处理解析失败 |

## 门槛（本切片后）

| 套件 | 数量 |
|---|---|
| UnitTests | **322**（不变） |
| Integration 双库 | **103**（+2 Development Seed 契约） |
| Compatibility | **7**（不变） |
| Architecture | **26**（不变） |

## 本地验证

| 命令 | 结果 |
|---|---|
| `dotnet build` IntegrationTests | **通过** |
| `dotnet test --filter FullyQualifiedName~DevelopmentSeedTests` | **2/2 通过**（约 5m33s，共享 Testcontainer） |
| UnitTests `--minimum-expected-tests 322` | 未在本切片重跑；门槛不变 |

## 覆盖场景

1. Development 首次：Baseline 管理员 + `tenancy.local_tenant` + 可选 E2E viewer；`local` 租户与 TenantProvisioned Outbox 各 1 条
2. Development 重跑：管理员密码哈希不变；租户/Outbox 仍各 1；审计 run 增至 2
3. Identifier=`local` 但 Domain 冲突：返回 `seeding.data.conflict`，不覆盖租户
4. Production：Baseline 成功；Development/Demo/Test 返回 `seeding.profile.not_allowed` 且不新增拒绝 profile 的成功 run 计数漂移（run 仍为 Baseline 的 1）
5. Test Profile：执行 Baseline + `testing.profile_contract_marker`，不执行 Development Overlay（无 `local` 租户）

## 非目标（仍开放）

- Production Secret 注入与运维验收 Runbook
- 超级管理员 MFA / 强认证 Provider
