# Identity TOTP 强认证 Provider 验证记录

- 日期：2026-07-21
- 切片：Production TOTP 强认证 Provider（ADR-0004）
- ADR：[ADR-0004](../architecture/adr/ADR-0004-production-super-admin-strong-reauth.md)

## 交付范围

| 层级 | 内容 |
|---|---|
| 决策 | ADR-0004：Production 解锁三条件（远程写开关 + TOTP Provider + 操作者已登记 TOTP） |
| 数据 | 双库 `016_IdentityUserTotp` → `fn_identity_user_totp` |
| 契约 | Grant/Revoke 可选 `totpCode`；`/api/v1/identity/me/mfa/totp` begin/confirm/status |
| Provider | `PasswordReauthenticationProvider`（Dev/Test）；`TotpStrongReauthenticationProvider`（Production 合格） |
| 修复 | `LockSuperAdministratorRole*` SELECT 补齐 `DataScopeKind`，恢复 Dapper 投影 |

## 门槛（本切片后）

| 套件 | 数量 |
|---|---|
| UnitTests | **331**（+9：Validator/Management/TOTP 算法） |
| Integration 双库 | **107**（+2：`TotpStrongReauthTests`） |
| Compatibility / Architecture | **7 / 26**（不变） |

## 本地验证

| 命令 | 结果 |
|---|---|
| Unit：`IdentityOptionsValidatorTests\|SuperAdministratorManagementServiceTests\|TotpAlgorithmTests` | **18/18 通过** |
| Integration：`TotpStrongReauthTests` | **2/2 通过** |
| `pnpm test:naming` | **通过** |
| Unit 全量 `--minimum-expected-tests 331` | **331/331 通过** |

## 明确仍开放

- Vue/Layui TOTP 登记与超管写操作确认 UI
- Production TOTP 强制路径真实栈（Development 超管页授予/撤销见[真实栈验证](./identity-super-admin-real-stack-2026-07-21.md)）
- WebAuthn / 其他因子（须新 ADR）
