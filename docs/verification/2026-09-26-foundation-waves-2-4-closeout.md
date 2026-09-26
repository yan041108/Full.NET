# Foundation 产品化 Wave 2–4 收口（工作区）

**基线提交**：以 `git rev-parse HEAD` 为准；本摘要描述当前工作区实现与已跑通的本地验证。

## Wave 2（Identity 生命周期）

| 能力 | 实现 | 验证 |
|------|------|------|
| MFA 恢复码 | `ManageMfaRecoveryCodes` + 迁移 238 | 集成 `MfaRecoveryCodeAssertions`（需 Docker DB）；MySQL 238 使用 `BINARY(16)` 与 `fn_identity_user.Id` 对齐 |
| 成员自行退出 | `POST .../tenant-members/me/leave` | 扩展 `TenantMembershipAssertions`（含 owner 不可退出） |
| 邀请撤销 vs 接受 | `RevokeInvitationAsync` | 同上 |
| 租户暂停写保护 | 租户解析 `IsActive=false` | `TenantLifecycleAssertions` 阻断邀请（断言 `identity.tenant_members.tenant_inactive`） |

- **F06 暂停写保护**：Identity 成员写路径 + Files 上传 + Realtime 租户 Hub（`ITenantActivityReadPort` / `FullNetNotificationHub`，连接后 inactive 租户 `Context.Abort()`）；Jobs/OpenAccess 为 Host 作用域，暂停后 Host 管理面仍可用；`TenantLifecycleAssertions` 覆盖邀请/Files/Hub/Host 消费者（Hub：允许 `StartAsync` 成功但 500ms 内不得保持 `Connected`）
- **Admin Vue**：`/account/security` MFA 恢复码再生；租户上下文页「退出当前租户」（`GET tenant-members/me` + leave）；文案键 `mfaRecovery.*`、`tenant.leave*` 已写入 `packages/admin-i18n`
- **修复（集成）**：`RemoveMemberAsync`/`me/leave` 使用 `TenantMemberListRow` 对齐 `FindMemberById` 投影；商业恢复测试在订阅绑包后刷新租户 `Version` 再 suspend
- **OpenAPI / client-contracts**：`tenant-members-v1` 含 `/me` 与 leave；新增 `identity-mfa-recovery-codes-v1.json`；`LeaveTenantMembershipRequest` + `isTenantMember`；Vue coverage 登记 `mfaRecoveryCodes.ts`

## Wave 3（Tenancy 商业）

| 能力 | 实现 | 验证 |
|------|------|------|
| 权益兼容回填 | `TenantEntitlementBackfillService` dry-run + apply | dry-run / apply 幂等集成测试 |
| 席位用量基线 | `POST /api/v1/tenancy/quota/usage-baseline` + `ITenantActiveMemberCountPort` | 单元 `TenantQuotaUsageBaselineServiceTests`；集成 `VerifySeatUsageBaselineDryRunAndApplyAsync`（双库 TenancyApi） |
| 存储用量基线 | 同上 + `ITenantResourceFileStorageUsagePort`（ready 文件 SizeBytes 求和） | 单元（storage 分支）；集成 `VerifyStorageUsageBaselineAfterUploadAsync`（上传确认后 dry-run 已对齐）；缺失指标行：`VerifyMissingStorageMetricProvisionedViaUsageBaselineAsync` |
| 文件存储配额 | `ITenantFileStorageQuotaPort` + `TenantResourceFileStore` | 单元 `TenantResourceFileStoreTests` + `TenantFileStorageQuotaPortTests`；集成 `TenantResourceFileStorageQuotaAssertions`（限额 0 时 store 上传 → `files.storage_quota.exceeded`） |
| Identity 席位配额 | `ITenantMemberSeatQuotaPort` + provision/accept | 集成 `TenantMemberSeatQuotaAssertions`（满额拒绝 provision 与接受邀请） |
| 开通默认配额 | `ProvisionTenant` 写入 `identity.seats` + `files.storage_bytes` 指标 | `TenantProvisioningTests` 断言双指标行 |

**未闭合**：F01 仍待 CI real-stack。**Enforced + `feature.workflow`**：Baseline 种子、`WorkflowTenantFeatureEntitlementGate`（租户表单/定义变更、实例启动）、双库集成 `Enforced_feature_workflow_binding_gates_tenant_start_*`。存量租户缺失配额指标行：`POST /api/v1/tenancy/quota/usage-baseline` 会补建默认 Limit 并对齐 UsedValue（`identity.seats` / `files.storage_bytes`）。

## Wave 4

| 能力 | 实现 | 验证 |
|------|------|------|
| TestChannel 订阅履约 | `TestChannelTenantSubscriptionPaymentFulfillmentPort` | 既有 Tenancy 订阅集成路径 |

**F09–F11 样例**：`samples/enterprise-request/` + `EnterpriseRequestAssertions`（CRUD、审批提交、CSV 导入）双库 `EnterpriseRequestApi*Tests`；Playwright 桩 `tests/e2e/admin-real-stack/tests/enterprise-request.spec.mjs`。

## F02 补充

- 模板 `package.json` 含 `diagnose:development` / `diagnose:production`（`tests/templates/created-app.test.mjs`）
- Host CRUD 预览确定性：`CodeGenerationPreviewAssertions.VerifyDeterministicPreviewAsync`

## 建议命令

```bash
pnpm test:templates
node --test tests/openapi/foundation-api-contract.test.mjs tests/openapi/vue-client-contract-coverage.test.mjs tests/openapi/identity-mfa-recovery-codes-contract.test.mjs tests/openapi/tenant-members-foundation-contract.test.mjs
dotnet test tests/Full.NET.UnitTests --filter "FullyQualifiedName~TenantFileStorageQuotaPort|TenantCommercialReactivateGate|TenantResourceFileStore|TenantMembershipActiveTenantGuard|TenantQuotaUsageBaseline|TenantFeatureEntitlementPort"
# 集成（需 Docker）：
dotnet test tests/Full.NET.IntegrationTests --filter "FullyQualifiedName~identity_seats_at_capacity|storage_quota_exhausted|TenantCommercial_reactivate|Tenant_membership|Tenant_lifecycle|TenantEntitlement_|Enforced_feature_workflow_binding_gates_tenant_start|TenantQuota_missing_storage_metric"
# F01 real-stack（需 Docker + 干净工作区/commit bundle 输入；本地 `FULLNET_RUN_TEMPLATE_REAL_STACK=1`）：
# set FULLNET_RUN_TEMPLATE_REAL_STACK=1 && node --test tests/templates/created-app-real-stack.test.mjs
```

## 提交前门禁（Foundation 批次）

1. 仅暂存本批次源码/契约/测试/文档；排除 `bin/`、`obj/`、`.tmp/`。
2. 本地：`pnpm test:templates` + 上列 OpenAPI 子集 + UnitTests filter（脏树时 bundle/real-stack 相关用例会 **skip**，CLI `build-source-bundle` 仍拒绝脏树）。
3. 推送后确认 CI：`template-created-app-real-stack`、Tenancy/Identity 集成作业、`pnpm test:openapi`（含相对 HEAD 兼容检查）。
4. F01 仅在 **real-stack 作业绿** 后更新 `docs/verification/2026-09-26-f01-created-app-real-stack-closeout.md` 的 CI run id。

## 建议 `git add` 范围（Foundation 批次）

**基线 HEAD**：`63b5e4c17863f2d6fdc5dab1d4a4926f181acdf6`（提交前再 `git rev-parse HEAD`）。

**整批纳入**（实现 + 契约 + 测试 + 文档 + CI/模板，不含 `bin/`/`obj/`）：

- `.github/workflows/ci.yml`
- `contracts/openapi/`（含 `identity-mfa-recovery-codes-v1.json`、`tenant-members-v1.json`、`vue-client-coverage-v1.json`）
- `docs/development/first-crud-from-template.md`
- `docs/operations/application-upgrade.md`
- `docs/verification/2026-09-26-f01-created-app-real-stack-closeout.md`
- `docs/verification/2026-09-26-foundation-waves-2-4-closeout.md`
- `eng/testing/test-matrix.json`
- `packages/admin-i18n/src/messages.ts`
- `packages/client-contracts/src/`（`mfa-recovery-codes.ts`、`tenant-members.ts`、`index.ts`）
- `scripts/templates/`（含 `migration-script-modules.mjs`、`upgrade-framework.mjs` 及已改 build/verify 脚本）
- `src/BuildingBlocks/Full.NET.Abstractions/Tenancy/ITenantActivityReadPort.cs`
- `src/BuildingBlocks/Full.NET.Realtime.SignalR/FullNetNotificationHub.cs`（F06 租户 Hub 活动态门禁）
- `src/BuildingBlocks/Full.NET.Migrations.DbUp/`（238 双库迁移、`FrameworkManifestMigrationScope.cs`、Runner/DI）
- `src/Modules/Full.NET.Modules.Files*`、`Files.Contracts`（配额 Port、TenantResourceFileStore、错误码）
- `src/Modules/Full.NET.Modules.Identity*`（MFA 恢复码、成员 me/leave、ActiveTenantGuard、AOT/JSON/resx）
- `src/Modules/Full.NET.Modules.Tenancy*`（回填、TestChannel 履约、FileStorageQuotaPort、权益/订阅 SQL、`TenantFeatureEntitlementPort`）
- `src/Modules/Full.NET.Modules.Tenancy.Contracts/`（`ITenantFeatureEntitlementPort`、`TenantEntitlementCatalogCodes`；Workflow 跨模块只读引用）
- `src/Modules/Full.NET.Modules.Workflow*`（`WorkflowTenantFeatureEntitlementGate`：租户表单/定义变更与实例启动前 `feature.workflow` 权益门禁）
- `templates/fullnet-app/`
- `tests/Full.NET.IntegrationTests/`、`tests/Full.NET.UnitTests/`（本批次新增/扩展断言）
- `tests/openapi/`（foundation、vue coverage、MFA/tenant-members 契约测试）
- `tests/templates/`（含 `created-app-real-stack.test.mjs`、`support/created-app-real-stack.mjs`）
- `ui/admin/src/api/`（`mfaRecoveryCodes.ts`、`tenant-members.ts`）
- `ui/admin/src/views/`（`SecuritySettingsView.vue`、`TenantContextView.vue`）

**提交前人工核对**：

- `src/Modules/Full.NET.Modules.Identity/Features/Login/Handler.cs` — 若与 Foundation 无关，**不要**纳入本批或单独 revert。
- 勿 `git add` 任何 `bin/`、`obj/`、`.tmp/`、本地密钥或 `.env`。

**提交后 CI 必看**：`template-created-app-real-stack`、Tenancy/Identity/Files 集成、`pnpm test:openapi`（相对 HEAD 兼容）。

## 状态

**Build-verified（工作区）**；集成与 F01 real-stack 仍依赖 CI / 本地 Docker。

**本地门禁（2026-09-26，续跑）**：`pnpm test:templates` **30** 通过 / **7** 跳过（脏树 bundle + real-stack）；OpenAPI 子集 **5/5**；Foundation UnitTests filter（Release）**26/26**（含 `TenantQuotaUsageBaselineServiceTests` 6 项）；`Full.NET.Composition` Release 构建通过。集成已本地绿：`Tenant_lifecycle_returns_standard_contract`（双库）、`Enforced_feature_workflow_binding_gates_tenant_start_*`（双库）、`TenantQuota_missing_storage_metric_provisioned_via_usage_baseline`（双库）。`created-app-real-stack.test.mjs` 本地默认 **skip**（需 `FULLNET_RUN_TEMPLATE_REAL_STACK=1` + bundle 输入无未提交变更；CI 自动跑 F01）。`git diff --check --cached` 通过。HEAD `63b5e4c17863f2d6fdc5dab1d4a4926f181acdf6`；领先 origin **9** 提交；Foundation 批次已全部 staged，**未 commit**。
