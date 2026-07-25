# Identity API Key 认证验证记录（2026-07-26）

## 状态

**Build-verified**（双库 Integration 依赖 CI Testcontainers）

## 交付摘要

| 区域 | 证据 |
| --- | --- |
| 迁移 | `031_IdentityApiKey.sql`（SQL Server + MySQL） |
| 认证 | `SmartAuthenticationDefaults` + `ApiKeyAuthenticationHandler` |
| 管理 API | `ManageHostApiKeys` 创建/列表/禁用 |
| OpenAPI | `identity-host-api-keys-v1.json` + Scalar `ApiKey` scheme |
| client-contracts | `host-api-keys.ts` + Vitest **69/69** |
| Integration 双库 | `Host_api_keys_follow_contract` SQL Server/MySQL **2/2** → **168 → 170** |
| Unit | **352/352**（含权限目录快照更新） |
| 四处 canonical 门槛 | **352/7/40/170** |

## 本地验证

- Release 构建：通过
- Unit：**352/352**
- client-contracts：**69/69**
- Integration 170：未在本地执行（需 Testcontainers）

## 已知限制

- 仅 Host 作用域；无 Vue/Layui 管理 UI
- API Key 权限在创建时固定，不继承用户角色
- `/api/v1/me` 等依赖会话 Claim 的端点不支持 ApiKey 主体
