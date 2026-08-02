# CodeGeneration 生产 Helm 默认启用验证记录

## 结论

生产 Helm Chart 已提供 CodeGeneration Apply/Rollback 的默认启用路径并保持 `Build-verified`：当 `production: true` 且 `codeGeneration.apply.enabledWhenProduction: true`（Chart 默认）时，向 API/Worker ConfigMap 注入 `CodeGeneration__Apply__Enabled=true` 与工作区/保留/链长配置；**应用代码默认值仍为 `Enabled=false`**。启用时强制 RWX 工作区卷（`existingClaimName` 或经验证的 `persistence.create`）。

## Helm 键

- `codeGeneration.apply.enabledWhenProduction`（默认 `true`）
- `codeGeneration.apply.workspaceRoot`（默认 `/var/fullnet/codegeneration`）
- `codeGeneration.apply.distributedGateEnabled`（默认 `true`）
- `codeGeneration.workspace.existingClaimName` / `persistence.*`

## 新鲜验证证据

| 验证 | 结果 |
| --- | --- |
| `node scripts/testing/run-helm-contracts.mjs` | passed |
| `tests/deployment/helm-contract.test.mjs` | 随 CI 门禁 |

## 治理复盘

未命中规则或 Skill 升级触发条件；一行结论：无需规则/Skill 变更。