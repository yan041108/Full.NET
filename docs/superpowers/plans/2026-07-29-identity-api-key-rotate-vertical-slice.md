# Identity API Key 轮换纵向切片

- 建立日期：2026-07-29
- 状态：Build-verified

## 交付

- `POST /api/v1/identity/api-keys/{apiKeyId}/rotate`（`identity.api_keys.write`）
- 单事务内禁用旧钥并签发新钥；权限、用户、显示名与过期时间沿用原记录
- 返回 `CreateHostApiKeyResponse`（一次性明文）
- Vue/Layui 轮换按钮 + 明文展示；OpenAPI 夹具与双库 Integration 续跑

## 非目标

- 使用审计、租户作用域 API Key
