# Pre-v1 协议名称迁移说明

本文档记录 Task 4 对 **错误码**、**审计 ResultCode** 与 **Statement ID** 的规范化变更。机器清单以 [`PreV1NameMapV1`](../../contracts/naming/pre-v1-name-map.json) 的 `protocol` 段为准。

## 破坏性变更（标准 API）

自 `1.0.0-pre-v1-switch` 起，ProblemDetails / 标准错误响应的 `code` 字段只输出 **canonical** 值：

| Legacy | Canonical |
| --- | --- |
| `tenancy.domain-exists` | `tenancy.domain_exists` |
| `tenancy.host-not-found` | `tenancy.host_not_found` |
| `tenancy.identifier-exists` | `tenancy.identifier_exists` |
| `tenancy.not-found` | `tenancy.not_found` |
| `identity.bootstrap.invalid-password` | `identity.bootstrap.invalid_password` |
| `identity.bootstrap.invalid-profile` | `identity.bootstrap.invalid_profile` |

登录审计写入的 `ResultCode` 由 `identity.login-succeeded` 改为 `identity.login_succeeded`。历史行仍保留 legacy 值；新写入只使用 canonical。

所有 [`PreV1NameMapV1`](../../contracts/naming/pre-v1-name-map.json) 中登记的 Statement ID 已改为下划线形式（例如 `outbox.acquire.sql_server`、`tenancy.find_by_identifier`）。

## 兼容窗口

### 服务端

- [`PreV1ProtocolCompatibility`](../../src/BuildingBlocks/Full.NET.Hosting/Api/PreV1ProtocolCompatibility.cs) 集中维护 legacy ↔ canonical 映射。
- [`ResourceErrorMessageLocalizer`](../../src/BuildingBlocks/Full.NET.Hosting/Api/ResourceErrorMessageLocalizer.cs) 在 canonical 资源键缺失时可回退到 legacy 资源键（迁移期保险）。
- Admin.NET 兼容层可通过 `IPreV1LegacyErrorCodeProfile.EmitLegacyErrorCodes = true` 在包络 `code` 字段回退 legacy 值；默认关闭。

### 客户端

- [`@fullnet/client-contracts`](../../packages/client-contracts/src/pre-v1-protocol.ts) 提供 `normalizePreV1ErrorCode` 与 `areEquivalentPreV1ErrorCodes`，迁移期应同时识别旧/新 error_code。
- Vue、Layui 与 uni-app 在比较 API 错误码时应使用上述规范化函数，禁止硬编码永久双码分支。

## 退役计划

- **M1.0（011 Contract 后）**：移除 `EmitLegacyErrorCodes` 回退、客户端 legacy 识别与 `PreV1ProtocolCompatibility` 中已排空条目的映射。
- 禁止在无版本说明的情况下永久维持双码；兼容仅服务于已发布客户端的过渡窗口。

## 验证

```powershell
dotnet build Full.NET.slnx -c Release
dotnet test tests/Full.NET.UnitTests --filter "Naming|ErrorCode" -c Release
dotnet test tests/Full.NET.CompatibilityTests --filter "Naming|ErrorCode" -c Release
pnpm test:naming
pnpm test:clients
```
