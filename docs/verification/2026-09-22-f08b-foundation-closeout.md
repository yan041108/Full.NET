# F08a/F08b 底座收口（Lane C）

**日期**：2026-09-22

## F08b

- Spec：[`2026-09-22-f08b-tenant-invitation-seat-orchestration.md`](../superpowers/specs/2026-09-22-f08b-tenant-invitation-seat-orchestration.md)
- 实现：`AcceptTenantInvitationService` 席位预留/确认移出 Identity 本地事务；确认失败补偿
- 债务：`contracts/architecture/module-local-transaction-debt.json` **entries: []**

## F08a

- 对账 API：`POST /api/v1/tenancy/quota/reservations/reconcile-metric-ids`（`dryRun` 默认 true）
- 权限：`tenancy.tenant_quota.reconcile_metric_ids`
- 绑定规则：按租户 + `MetricCode` + `default` 周期回填 `MetricId`

## 验证

```bash
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~TenantInvitationRollbackTests"
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter "FullyQualifiedName~ModuleLocalTransactionBoundaryTests"
```
