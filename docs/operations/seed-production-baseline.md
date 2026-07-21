# 生产 Baseline Seed 与 Bootstrap Secret 运维 Runbook

## 1. 目的与边界

本 Runbook 约束 Production 环境首次或补齐 Baseline Seed 时如何注入宿主管理员 Secret、如何验收门禁，以及失败时如何排障。它不授权跳过 Production Overlay 拒绝规则，也不替代 MFA/强认证 Provider。

适用范围：

- `Full.NET.Host.Migrator --seed baseline`
- 环境名 `Production`（或宿主 `IHostEnvironment.IsProduction() == true`）
- 配置键 `Identity:Bootstrap:Username` / `Identity:Bootstrap:Password`（及对应环境变量）

不在范围：

- Development/Demo/Test Overlay（Production 必须拒绝）
- API/Worker 启动时播种（禁止）
- 远程超级管理员授予/撤销（Production 在强认证 Provider 到位前保持关闭）

双库契约证据见 [seed-dual-database-contract-2026-07-21.md](../verification/seed-dual-database-contract-2026-07-21.md)。

## 2. Secret 注入方式

Production **禁止**把 Bootstrap 密码写入仓库、镜像层、日志或 `appsettings*.json`。只允许从 Secret 管理器在运行时注入：

| 键 | 环境变量示例 | 要求 |
|---|---|---|
| `Identity:Bootstrap:Username` | `Identity__Bootstrap__Username` | 非空，3–128 字符 |
| `Identity:Bootstrap:Password` | `Identity__Bootstrap__Password` | 满足 Identity 密码策略（至少 12 位且含大小写、数字和特殊字符） |
| `Identity:Bootstrap:DisplayName` | `Identity__Bootstrap__DisplayName` | 可选；缺省为「系统管理员」 |

Aspire 本地开发继续用 AppHost Parameter / user-secrets（见 [getting-started.md](../development/getting-started.md) §3）；生产部署必须改用平台 Secret（Kubernetes Secret、Azure Key Vault、环境变量注入等），且 Migrator 与 API 不得共享可写的明文配置文件。

缺失用户名或密码时，Baseline Contributor 必须以稳定码 `seeding.bootstrap.secret_missing` 失败；日志与 CLI 输出不得包含密码原文。

## 3. 推荐执行顺序

1. 确认目标库已完成 DbUp 迁移（Migrator 无 `--seed` 或先迁移后 Seed）。
2. 注入连接串与 Bootstrap Secret；确认 `ASPNETCORE_ENVIRONMENT=Production`。
3. 执行：

```powershell
dotnet Full.NET.Host.Migrator.dll --seed baseline
```

4. 退出码必须为 `0`；标准错误流不得出现 `seeding.profile.not_allowed`、`seeding.bootstrap.secret_missing` 或其他 Seed 错误码。
5. 验收查询（只读，不输出 Secret）：

- `fn_seed_run` 最新一行：`Profile=baseline`、`Status=Succeeded`、`EnvironmentName` 含 Production
- `fn_identity_user` 存在约定用户名的 Host 账号且 `IsActive=1`
- `fn_identity_role` 存在 `host-administrator` 且 `IsSuperAdministrator=1`
- 不存在 Development Overlay 租户（例如 Identifier=`local`）除非业务另行开通

6. 故意用错误 Profile 验收门禁：

```powershell
dotnet Full.NET.Host.Migrator.dll --seed development
```

必须失败，稳定码 `seeding.profile.not_allowed`，且不得新增成功的 Overlay 审计项。

## 4. Go/No-Go

全部为「是」才允许在生产执行 Baseline Seed：

1. 备份已完成且恢复演练可定位；
2. Migrator 镜像与目标提交一致；
3. Secret 由平台注入，仓库与 CI 日志无明文密码；
4. 仅计划 Baseline，无 Development/Demo/Test；
5. 值班人员已知 `seeding.bootstrap.secret_missing` / `seeding.profile.not_allowed` 的处置；
6. 首次 Seed 后立即轮换或封存 Bootstrap 密码的运维流程已确认（Seed **不覆盖**已存在账号密码）。

任一为「否」则 No-Go。

## 5. 失败处置

| 稳定码 | 含义 | 处置 |
|---|---|---|
| `seeding.bootstrap.secret_missing` | 未注入成对 Username/Password | 修复 Secret 注入后重跑；不改代码放宽 |
| `seeding.profile.not_allowed` | Production 请求了 Overlay | 改回 `--seed baseline` |
| `seeding.data.conflict` | 自然键存在但状态不一致 | 人工核对，禁止覆盖用户数据 |
| `seeding.lock.timeout` | 并发 Seed 争用 | 等待或排查僵死会话后重跑 |
| `migrator.migration.failed` | 迁移未成功 | 先修迁移，禁止带失败库播种 |

重跑策略：Contributor 幂等补齐/跳过；**不删除、不重置密码、不覆盖显示名称**。不得把 `fn_seed_run` 当作业务幂等开关绕过 Contributor。

## 6. 与账号保护的关系

- Host 用户管理提供禁用，不提供硬删除；禁用最后一名有效超级管理员必须返回 `identity.super_administrator.last_remaining`。
- Production 远程超级管理员写操作保持关闭，直至 MFA/强认证 Provider 落地（见超级管理员计划）。
