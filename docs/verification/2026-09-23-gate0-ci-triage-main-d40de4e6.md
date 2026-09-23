# Gate 0 CI 根因摘录（main `d40de4e6`，2026-09-23）

**纪律**：摘录来自 GitHub Actions 失败日志与本地复现；**不**宣称 main 已绿。工作区修复见文末「本回合补丁」。

## 参考 Run

| Run ID | 工作流 | SHA 语境 |
|--------|--------|----------|
| [35762392723](https://github.com/yan041108/Full.NET/actions/runs/35762392723) | `ci` | push `docs(verification): checklist 21 tenant lifecycle closeout` |
| [35762256621](https://github.com/yan041108/Full.NET/actions/runs/35762256621) | `api-native-aot-linux` | 同时间段 main |

## G0-1：`client-build-test` — Audit client dependencies

- **失败步骤**：`pnpm audit:clients`（`scripts/audit-client-dependencies.mjs`）
- **直接原因**：`Unreviewed high advisory: GHSA-7q85-xj36-vmfc`（`adm-zip` &lt; 0.6.1，ZIP 声明解压尺寸导致 DoS）
- **本地复现**：`pnpm audit:clients` exit 1（`npm_config_registry` 须指向 `https://registry.npmjs.org`，与 CI 策略一致）
- **修复方向**：将 pnpm override `@dcloudio/uni-cli-shared>adm-zip` 升至 **0.6.1**（优于长期例外登记）

## G0-1：`integration-shard`（例：`api-sqlserver`）

- **规模**：大量契约/OIDC/租户类用例失败；部分为 **500 InternalServerError** 而非预期 **403 Forbidden**（例：`Host_templates_follow_contract_with_sql_server`）
- **已定位一条 tenancy 根因**（日志内联异常）：
  - 语句：`tenancy.settings.get_enforcement_phase`
  - Dapper 物化 `TenantEntitlementEnforcementRecord` 失败：列名 `EntitlementEnforcementPhase` 与 record 参数名 `Phase` 不一致（非 AOT 路径走反射构造）
  - 连带：`TenantEntitlement_enforcement_phase_round_trip` 及依赖 entitlement 设置端点的用例
- **修复方向**：record 属性改名为 `EntitlementEnforcementPhase` 与 SQL 列对齐
- **根因（工作区）**：租户内超级管理员经 `ResolvePermissionScopeMask` 误获 **Host 专属**权限，Endpoint 授权通过但 `HostOnly` SQL 抛 `HostContextRequiredException` → **500**（例：`Host_templates_follow_contract` 期望 **403**）
- **修复**：`PermissionClaimEvaluator.HasPermission` 对 `AuthorizationScope.Host` 专属权限要求 `effectiveScope == Host`；`FullNetPermissionHandlerTests` **7/7**

## G0-1：`api-native-aot-linux` — Native AOT OIDC E2E

- **失败**：8 项（SqlServer + MySQL 各 4）
- **断言**：`Assert.IsFalse(string.IsNullOrWhiteSpace(exchanged.AccessToken))` — 授权码交换后 **AccessToken 为空**
- **位置**：`NativeApiOidcE2EAssertions.cs`（双实例 exchange、context switch、governance、center restart）
- **根因（工作区）**：双实例场景未配置共享 `EncryptionKeyBase64`（Native `BuildOidcSettings`）；集成多实例断言仍用 `AllowDevelopmentEphemeralSigningKey=true` 的 per-instance 临时密钥，对端 `/api/v1/me` 校验 JWT 失败
- **修复方向**：`NativeApiOidcE2EAssertions` 写入 `SharedEncryptionKeyBase64`；`IdentityOidcContextSwitch*MultiInstance*` / `IdentityOidcMultiInstanceGovernanceAssertions` 经 `UsingConfiguredPairAsync` 共享签名与 DataProtection
- **状态**：**待** push 后 CI 复跑 native + integration shard

## G0-1：其它 `ci` 作业（未逐行摘录）

- `real-stack-e2e` / `real-stack-e2e-production-totp`：失败（需 Docker/Testcontainers + Host；本机 Docker 不可用未复现）
- `integration-gate`：随 shard 失败级联

## G0-3 RBAC merge

- 仍 **待 CI**：`integration-shard` 绿后再以 merge E2E 登记

## 本回合工作区补丁（2026-09-23）

| 项 | 变更 |
|----|------|
| adm-zip | `package.json` override → `0.6.1`；`pnpm install` 更新 lockfile |
| Tenancy Dapper | `TenantEntitlementEnforcementRecord.EntitlementEnforcementPhase` + 调用点 |
| OIDC 多实例测试 | `UsingConfiguredPairAsync`；Context switch / governance / **revoke-all** 多实例断言；Native 双实例 `EncryptionKeyBase64` |
| 验证 | `pnpm audit:clients` 通过；Tenancy + IntegrationTests **Release build** 通过 |
| 本机集成 spot | **6/6**（[fix-bundle 验证](2026-09-23-gate0-fix-bundle-verification.md)） |

## 建议合入批次（Gate0 最小修复集）

1. `package.json` + `pnpm-lock.yaml`（adm-zip 0.6.1）
2. Tenancy `EntitlementEnforcementPhase` Dapper 对齐
3. `PermissionClaimEvaluator` Host 专属权限与请求上下文对齐
4. Integration OIDC 多实例测试配置 + Native `EncryptionKeyBase64`
5. 本 triage 文档

合入后观察：`client-build-test`、`integration-shard`（`api-sqlserver` / `api-mysql`）、`api-native-aot-linux`（OIDC E2E 8 项）。

**下一步**：合入并推送 → 观察 `client-build-test` 与 `integration-shard`；若 OIDC native 仍红，专开 OIDC native 调查项（不占用清单 88+ 编号）。
