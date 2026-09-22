# B1-1b Identity 用户与自助收口（Lane A）

**日期**：2026-09-22
**范围**：执行清单 13–18、07、23

## 清单 13–18、23（已具备）

| ID | 能力 | 证据 |
|----|------|------|
| 13 | 掩码/揭示 | `HostUserProfileMapper`、`identity.users.reveal_*`、`UsersView` / `UserEditorDialog` |
| 14–16 | `/me` 改密、解锁、强制改密 | `ChangePassword`、`PasswordChangeRequirementEvaluator`、`SecuritySettingsView` |
| 17–18 | 自助资料/头像 | `SelfServiceProfile`、`ProfileSettingsView`、Files Claim |
| 23 | Grid 列偏好 | `UsersView` → `grid-preferences` |

## 清单 07（本切片）

- 策略：[`2026-09-22-host-user-retire-policy.md`](../superpowers/specs/2026-09-22-host-user-retire-policy.md)
- API：`POST .../host/users/{id}/retire`、迁移 `233_IdentityUserRetiredAtUtc`、Vue `users-action-retire`

## 验证（本地）

```bash
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~TenantInvitationRollbackTests"
pnpm test:e2e:real -- tests/e2e/admin-real-stack/tests/host-users.spec.mjs
```

**状态**：Build-verified；B2 升 Verified 仍待 Wave 2 全矩阵。
