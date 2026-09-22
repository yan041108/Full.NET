# 执行清单 20 — 角色生命周期与成员 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **20**（启用/禁用/删除、角色成员页签）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `enable` / `disable` / `DELETE`、`GET/PUT .../members` |
| Vue | `RolesView` disable/enable/delete、成员抽屉 |
| real-stack | `host-roles.spec.mjs`（列表、授权树、禁用按钮可见性） |

## 本机验证

| 命令 | 结果 |
|------|------|
| `HostRoleManagementServiceTests` | 含启停/复制场景，13/13 通过（与 19 共用套件） |

**状态**：Build-verified；成员 UI 全路径在 Phase D-83 逐页验收。
