# 架构复核缺口 Program Merge 验证（2026-08-08）

## 范围

Tasks 1–7 全部完成后，以 `3f45047c`（计划登记基线）到 `HEAD` 为边界执行 program merge 门禁。

## 门禁结果（新鲜输出）

| 门禁 | 结果 |
|------|------|
| `git diff --check` | 通过 |
| `pnpm test:governance` | 26/26 通过 |
| `pnpm test:openapi` | 81/81 通过 |
| `pnpm test:naming` | 24/24 通过（含 083–085 迁移命名债务登记） |
| `pnpm test:sql-safety` | 5/5 通过 |
| `pnpm test:dotnet:architecture` | 91/91 通过 |
| `pnpm test:dotnet:unit` | 1144/1144 通过 |
| `pnpm test:integration:affected -- --base 3f45047c --phase merge` | 165/165 通过（约 29 分钟） |

## 修复项（merge 期间补齐）

1. **模块契约引用**：Identity 消费 `Organization.Contracts` 做机构投影同步，但不能声明 `Organization` 模块依赖（会与 Organization→Identity 形成环）。架构测试新增“反向已声明依赖”豁免。
2. **Tenancy 夹具**：`TenantProvisioningTests` 注册 `IOrganizationUnitProjectionCatalog` 测试替身，避免 `ValidateOnBuild` 解析回填服务失败。
3. **命名债务**：`contracts/naming/naming-debt.json` 登记 083–085 迁移的精确命名债务。
4. **Identity 注册快照**：`IdentityModuleRegistrationTests` 对齐机构投影服务与集成事件处理器注册。

## 结论

Tasks 1–7 与 program merge 门禁均已完成；工作区干净。

## Task 8

按计划在真实 Integration Event 非加法变更 Spec 获批前不启动。