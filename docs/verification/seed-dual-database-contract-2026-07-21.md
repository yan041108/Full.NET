# Seed 双库契约验证记录

- 日期：2026-07-21；2026-07-22 补充 Task 3A 发布物隔离复核
- 切片：Seed 计划 Task 6 — Development/Test/Production 双库纵向契约；架构硬化 Task 3A — E2E 场景数据移出发布物

## 交付范围

| 层级 | 内容 |
|---|---|
| 测试 | `DevelopmentSeedTests`（SQL Server / MySQL）：Development 首次/重跑、租户冲突、Production Overlay 拒绝、Test Profile 隔离 |
| 夹具 | `TestOnlySeedContributor`（仅 Integration 程序集，不进发布物） |
| 迁移修复 | MySQL `014`/`015` UUID 列改为 `BINARY(16)`；SQL Server `015` 将 `UPDATE DataScopeKind` 包入 `EXEC` 以避免批处理解析失败 |
| Task 3A 隔离 | Identity 只注册生产 Baseline Contributor；受限查看者改由真实栈 API 健康后执行的 `provision-viewer.mjs` 幂等创建 |

## 门槛（本切片后）

| 套件 | 数量 |
|---|---|
| UnitTests | **332**（删除 1 项发布程序集内 E2E Contributor 单测） |
| Integration 双库 | **109**（Development Seed 仍为 SQL Server/MySQL 2 项） |
| Compatibility | **7**（不变） |
| Architecture | **28**（新增 1 项发布物测试场景类型/配置节门禁） |

## 2026-07-21 本地验证

| 命令 | 结果 |
|---|---|
| `dotnet build` IntegrationTests | **通过** |
| `dotnet test --filter FullyQualifiedName~DevelopmentSeedTests` | **2/2 通过**（约 5m33s，共享 Testcontainer） |
| UnitTests `--minimum-expected-tests 322` | 未在本切片重跑；门槛不变 |

### 2026-07-22 Task 3A 新鲜验证

| 命令 | 结果 |
|---|---|
| UnitTests `--minimum-expected-tests 332` | **332/332 通过** |
| ArchitectureTests `--minimum-expected-tests 28` | **28/28 通过**；发布程序集测试场景类型/配置节零违规 |
| API / Worker / Migrator Release `dotnet publish` 后扫描 | **零命中**：E2E 类型名、`e2e-viewer`、`Identity:E2eViewer` |
| SQL Server / MySQL 真实栈 | **未验证**：当前机器没有可用容器运行时，Testcontainers 在数据库启动前失败 |

## 覆盖场景

1. Development 首次：Baseline 管理员 + `tenancy.local_tenant`；`local` 租户与 TenantProvisioned Outbox 各 1 条；受限查看者不再属于 Seed Contributor
2. Development 重跑：管理员密码哈希不变；租户/Outbox 仍各 1；审计 run 增至 2
3. Identifier=`local` 但 Domain 冲突：返回 `seeding.data.conflict`，不覆盖租户
4. Production：Baseline 成功；Development/Demo/Test 返回 `seeding.profile.not_allowed` 且不新增拒绝 profile 的成功 run 计数漂移（run 仍为 Baseline 的 1）
5. Test Profile：执行 Baseline + `testing.profile_contract_marker`，不执行 Development Overlay（无 `local` 租户）
6. 真实栈查看者：API 健康后经 Host 用户/角色/权限/用户角色 API 创建；连续执行两次准备脚本复用同一角色代码和用户名

## 非目标（仍开放）

- MFA / 强认证 Provider
