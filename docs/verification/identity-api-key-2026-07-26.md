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
| Integration 双库 | `Host_api_keys_follow_contract` SQL Server/MySQL **2/2**，含认证前 Host 目录查询与委托管理员权限上限 |
| Unit | **359/359**（含权限目录快照、SQL 作用域、审计写入作用域、配置校验与提交后取消隔离回归） |
| 四处 canonical 门槛 | **359/7/40/172** |

## 本地验证

- Release 构建：通过
- Unit：**359/359**
- client-contracts：**69/69**
- API Key SQL Server/MySQL 聚焦 Integration：**2/2**

## 已知限制

- 仅 Host 作用域；无 Vue/Layui 管理 UI
- API Key 权限在创建时固定，并同时受操作者当前有效权限与目标用户权限快照上限约束
- `/api/v1/me` 等依赖会话 Claim 的端点不支持 ApiKey 主体
