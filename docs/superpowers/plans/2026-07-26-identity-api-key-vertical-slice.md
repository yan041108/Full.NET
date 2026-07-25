# 2026-07-26 Identity API Key 认证纵向切片

## 目标

交付 Host 作用域 API Key 认证与管理最小闭环，对标 Admin.NET「API Key 认证」M2 Core 能力。

## 范围

- [x] 迁移 `031_IdentityApiKey`（SQL Server + MySQL）
- [x] 表 `fn_identity_api_key`：仅存哈希、前缀与权限 JSON
- [x] `Authorization: ApiKey {secret}` 智能认证转发（JWT / ApiKey）
- [x] Host API：`GET/POST /api/v1/identity/api-keys`、`POST .../disable`
- [x] 权限：`identity.api_keys.read` / `identity.api_keys.write`
- [x] OpenAPI 契约 + Scalar ApiKey 安全方案
- [x] client-contracts `host-api-keys.ts`
- [x] Integration **168 → 170**（SQL Server/MySQL）
- [ ] 双管理端 UI（后续切片）

## 非目标

- 租户作用域 API Key
- 超级管理员能力通过 API Key 授予
- 管理端页面与菜单

## 验收

1. 创建 API Key 返回一次性明文 `fnk_*` 密钥
2. ApiKey 认证可访问已授权端点，未授权返回 403
3. 禁用后 ApiKey 返回 401
4. 双库 Integration 与 OpenAPI 契约测试通过
