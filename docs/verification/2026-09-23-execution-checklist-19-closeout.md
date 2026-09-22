# 执行清单 19 — 角色复制 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **19**（复制权限/数据范围/字段授权；系统标记不继承）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `POST /api/v1/identity/roles/{sourceRoleId}/copy`，`identityCopyHostRole`，`identity.roles.copy` |
| 服务 | `HostRoleManagementService.CopyAsync` |
| Vue | `RolesView.vue`：`data-testid="roles-action-copy"`、`copyHostRole` |
| OpenAPI 门禁 | `OpenApiOperationIdentityRulesTests` |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`HostRoleManagementServiceTests` | 13/13 通过 |
| real-stack | `host-roles.spec.mjs` 新增「可通过 API 复制角色」；全套件待 CI `real-stack-e2e*` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
