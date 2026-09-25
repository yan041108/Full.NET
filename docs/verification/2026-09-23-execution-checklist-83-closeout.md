# 执行清单 83 — Identity/租户/组织/设置页面验收 closeout（2026-09-23）

**范围**：清单 §7.D 编号 **83**（所选批次末尾任务：Identity、角色、菜单、租户、组织、设置类页面的 real-stack 与逐页验收证据汇总）。

## 验收策略

- **深度路径**：各模块已有独立 `host-*.spec.mjs`（403 直调、导航裁剪、核心 CRUD/API），含 Phase B 补强项（租户品牌、枚举目录、职位导入等）。
- **本槽补强**：`phase-d-83-identity-org-settings-nav.spec.mjs` — Host 管理员一次登录串联主导航可达性与页面标题（Vue）；租户组织页在 Development 租户上下文中验收。
- **双 Provider**：业务 API 双库行为以各模块 Integration 快照与 **87** `integration-matrix` fresh 为准；本槽不重复跑 Testcontainers。
- **人工/axe**：按 [D 区审计](2026-09-23-execution-checklist-d-zone-audit.md) 登记为 **87 前** 可选补项，不记 Verified。

## 页面 → 证据索引

见 [2026-09-23-execution-checklist-d-zone-audit.md §83](2026-09-23-execution-checklist-d-zone-audit.md)。

## 本机验证

| 命令 | 结果 |
|------|------|
| real-stack（深度，抽样） | `host-users.spec.mjs`、`host-roles.spec.mjs`、`host-tenants.spec.mjs`、`host-org-units.spec.mjs`、`host-config-entries.spec.mjs`（需本地 Host） |
| real-stack（83 汇总） | `phase-d-83-identity-org-settings-nav.spec.mjs` |

**状态**：Build-verified（页面证据汇编 + 导航冒烟）；升 **Verified** 受 **87**、Gate0 CI fresh 与 B2 手册约束。
