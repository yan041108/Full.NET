# 执行清单 21 — 租户生命周期 closeout（2026-09-23）

**范围**：租户重新启用、受控停用、管理员入口（清单 §7.B-21）。

## 交付锚点

| 层 | 位置 |
|----|------|
| 权限 | `tenancy.tenants.enable` 等于 [TenancyAuthorizationContributor](src/Modules/Full.NET.Modules.Tenancy/TenancyAuthorizationContributor.cs) |
| API | Host 租户管理 Endpoint（enable/disable 与成员/管理员复用 Identity Port） |
| Vue | `host-tenants` 相关视图（real-stack：`host-tenants.spec.mjs`） |

## 验证

| 项 | 状态 |
|----|------|
| 双库 Integration | 随 `integration-shard` / Tenancy 套件 |
| real-stack | `tests/e2e/admin-real-stack/tests/host-tenants.spec.mjs`（CI 待绿） |

**状态**：Build-verified（源码）；Verified 待 D-83 / Gate0 CI。
