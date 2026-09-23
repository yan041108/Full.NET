# Gate 0 修复包 — 本地验证记录（2026-09-23）

**目的**：与清单 closeout / E2E 增量**分轨**；仅登记 main CI 红因修复，不冒充 integration-matrix / real-stack fresh 绿。

## 变更文件（建议单独 PR / commit）

| 路径 | 说明 |
|------|------|
| `package.json`, `pnpm-lock.yaml` | `adm-zip` → 0.6.1（GHSA-7q85-xj36-vmfc） |
| `src/Modules/Full.NET.Modules.Tenancy/...` | `TenantEntitlementEnforcementRecord.EntitlementEnforcementPhase` |
| `src/Modules/Full.NET.Modules.Identity/Authorization/PermissionClaimEvaluator.cs` | Host 专属权限要求 Host 请求上下文 |
| `tests/Full.NET.IntegrationTests/Identity/IdentityOidcMultiInstance*.cs`, `NativeApiOidcE2EAssertions.cs` | 多实例 OIDC 共享密钥 |
| `docs/verification/2026-09-23-gate0-ci-triage-main-d40de4e6.md` | RCA |
| `docs/verification/2026-09-23-gate0-fix-bundle-verification.md` | 本文件 |

工作区另有大量 **清单 B/C/D E2E** 与 tracker 文档未列入上表；合 main 时建议 **勿与 Gate0 修复混为单 commit**，便于 CI 归因。

## 本地已执行（2026-09-23）

| 命令 / 套件 | 结果 |
|-------------|------|
| `pnpm audit:clients` | 通过 |
| `pnpm test:governance` | 55/55 |
| `pnpm test:openapi` | 175/175 |
| `pnpm test:aot:analyzers` | 通过 |
| `FullNetPermissionHandlerTests` + SqlScopeGuard 相关（MTP） | 29/29 |
| IntegrationTests / UnitTests Release **build** | 通过 |

## 本机集成 spot-check（2026-09-23，Docker Desktop 已启动）

`dotnet …IntegrationTests.dll`（Release），filter 含：

- `TenantEntitlement_enforcement_phase_round_trip`（SqlServer + MySql，**2**）
- `Host_templates_follow_contract_with_sql_server`
- `Oidc_context_switch_tokens_validate_on_peer_instance_with_sql_server`

**结果：4/4 通过**，墙钟约 **3m**。

追加 spot（同会话）：

- `Oidc_multi_instance_revoke_all_propagates_with_sql_server`
- `Oidc_context_switch_disabled_client_fails_closed_across_instances_with_sql_server`

**2/2 通过**（约 26s）。合计集成 spot **6/6**（`d40de4e6` + Gate0 工作区补丁）。

## 本机仍未执行

| 项 | 原因 |
|----|------|
| 全量 `pnpm test:integration:api:sqlserver` | 未跑完整 shard |
| `api-native-aot-linux` OIDC 8 项 | 依赖 CI 发布物 + Linux 作业 |

集成测试入口（仓库约定）：

```text
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll
  --filter "FullyQualifiedName~…" --minimum-expected-tests N --timeout 20m
```

## Push 后观察（G0-1）

1. `client-build-test` → `pnpm audit:clients`
2. `integration-shard` → `api-sqlserver` / `api-mysql`（Tenancy entitlement、CodeGen 403、OIDC 多实例）
3. `api-native-aot-linux` → Native OIDC E2E

**出口**：上述 fresh 绿 + G0-3 RBAC merge 登记后，再更新 [program-tracker](2026-09-23-execution-checklist-program-tracker.md) G0-1 为满足。
