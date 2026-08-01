# 请求签名认证与开放访问审计设计

**状态：** Approved for implementation  
**日期：** 2026-07-30  
**适用范围：** Identity 模块开放 API、Host/Tenant 作用域 API Key、双库 Nonce 防重放、认证审计与 OpenAPI

## 1. 决策摘要

Admin.NET.Pro 的请求签名认证证明了“开放接口 + HMAC + 时间戳 + Nonce”对企业集成有价值，但其规范化细节、租户边界与日志脱敏未形成可审查的 wire-contract。Full.NET 只吸收安全语义，不复制实现。

本设计冻结请求签名 wire-contract，复用现有 `fn_identity_api_key` 哈希凭据（不新增第二套密钥存储、不保存明文 Secret），通过独立认证 Scheme 仅服务于**显式声明**的开放 Endpoint。Nonce 必须由数据库唯一约束原子拒绝；缓存只能加速，不能代替持久化防重放。

## 2. Header 与版本

| Header | 必填 | 说明 |
| --- | --- | --- |
| `X-FullNET-Access-Key-Id` | 是 | 公开标识，等于 API Key 的 `KeyPrefix`（最长 16 字符，ASCII） |
| `X-FullNET-Timestamp` | 是 | Unix 秒级 UTC 时间戳，十进制字符串，不含前导零填充要求 |
| `X-FullNET-Nonce` | 是 | 见 §6 |
| `X-FullNET-Signature` | 是 | HMAC-SHA256 十六进制小写输出，见 §7 |
| `X-FullNET-Signature-Version` | 是 | 固定 `1`；未知版本失败关闭 |
| `X-FullNET-Tenant-Id` | 否 | 租户开放接口可选绑定；Host 作用域 Key 禁止携带 |

任一签名 Header 出现但另一必填 Header 缺失时，按 `identity.signature.missing_headers` 拒绝，不得部分解析。

认证 Scheme 名称：`FullNET.Signature`（OpenAPI 安全方案名：`Signature`）。

## 3. HTTP Method 规范化

- 使用请求实际 Method，规范化为 **全大写** ASCII（`GET`、`POST` 等）。
- 不允许客户端覆盖 Method；代理不得改写 Method 后仍使用旧签名。

## 4. Path 规范化

签名输入 Path 为 ASP.NET Core 在可信代理处理后的逻辑路径：

```
canonicalPath = PathBase.Value + Path.Value
```

规则：

1. `PathBase` 与 `Path` 使用 `PathString` 原始值（保留大小写）。
2. 空 Path 视为 `/`；禁止除根路径外的尾部 `/`（`/api/v1/users/` 拒绝，`/api/v1/users` 合法）。
3. Path 不含 Query；不二次 Percent-Encoding。
4. 不可信代理未启用时，以 Kestrel 直接接收的路径为准；启用 `TrustedProxy` 后，以 Forwarded 处理后的 `Request.Path`/`PathBase` 为准。客户端必须对**服务端将用于验签的同一路径**签名。

## 5. Query 规范化

1. 解析 `Request.QueryString.Value`（不含前导 `?`）；空 Query 用空字符串。
2. 拆分为 `name=value` 对；无 `=` 的参数视为 `name` + 空值。
3. 对 name 与 value 分别做 RFC 3986 Percent-Encoding（`Uri.EscapeDataString` 语义）；空格编码为 `%20`，`+` 不当作空格。
4. 按 `(encodedName, encodedValue)` 字典序升序排序；主键相同再比 value。
5. 重复参数名保留多对，全部参与排序，不得折叠。
6. 连接为 `name=value`，多对以 `&` 连接，得到 `canonicalQuery`。
7. 客户端 Query 编码与规范化结果不一致时，按 `identity.signature.invalid_encoding` 拒绝。

## 6. Body 摘要与时间窗口

### 6.1 Body SHA-256

- 读取原始请求体字节（未解压、未解析）。
- `contentHash = lowercase_hex(SHA256(bodyBytes))`。
- 空 Body：`SHA256([])` → `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`。
- Body 被修改导致摘要不匹配时，按 `identity.signature.invalid_signature` 拒绝（不单独暴露中间状态）。

### 6.2 时间戳

- `X-FullNET-Timestamp` 为 Unix 秒（UTC）。
- 默认允许时钟偏差 **±300 秒**（可通过 `Identity:SignatureClockSkewSeconds` 配置，下限 30、上限 900）。
- 早于窗口：`identity.signature.timestamp_expired`。
- 晚于窗口：`identity.signature.timestamp_in_future`。
- 非十进制整数：`identity.signature.invalid_timestamp`。

### 6.3 Nonce

- 字符集：`[A-Za-z0-9]`。
- 长度：16–64（含端点）。
- 有效期：与 Nonce 记录 `ExpiresAtUtc` 一致，默认 `timestamp + clockSkew + 300s`。
- 规范化：原样参与签名字符串；持久化保存 `SHA256(UTF8(nonce))` 十六进制小写摘要（`NonceDigest`），禁止保存原始 Nonce 到日志。

## 7. HMAC-SHA256

### 7.1 待签名字符串

各行以单个 `\n`（LF）分隔，末尾无换行：

```
{METHOD}
{canonicalPath}
{canonicalQuery}
{contentHash}
{accessKeyId}
{timestamp}
{nonce}
```

### 7.2 密钥材料

- 服务端不保存明文 Secret。
- `signingKey = SHA256(UTF8(secret))` 的 32 字节二进制（与 `fn_identity_api_key.KeyHash` 一致）。
- `signature = lowercase_hex(HMAC-SHA256(signingKey, UTF8(canonicalString)))`。
- 比较使用 `CryptographicOperations.FixedTimeEquals`。

### 7.3 Access Key 生命周期

复用 `fn_identity_api_key`：

| 状态 | 行为 |
| --- | --- |
| 不存在 | `identity.signature.access_key_not_found` |
| `IsActive = 0` 或已轮换禁用 | `identity.signature.access_key_disabled` |
| `ExpiresAtUtc` 已过 | `identity.signature.access_key_expired` |
| 绑定用户禁用/锁定 | 同 API Key 认证，拒绝且不枚举细节 |

轮换后旧 KeyHash 失效；客户端必须使用新 Secret。AccessKeyId（KeyPrefix）可变化（轮换生成新记录）。

## 8. Host / 租户作用域绑定

从 API Key 关联用户读取 `ScopeKey` 与 `TenantId`：

| 用户作用域 | 允许场景 |
| --- | --- |
| `host`（`TenantId IS NULL`） | 仅 Host 开放 Endpoint；**禁止**携带 `X-FullNET-Tenant-Id`；禁止访问 TenantRequired SQL 作用域 |
| `tenant:{TenantId:N}` | 必须携带匹配的 `X-FullNET-Tenant-Id`；禁止跨租户 |

违反时返回 `identity.signature.tenant_scope_mismatch`（HTTP 403）。

认证、权限、租户解析与限流保持独立：签名只建立身份，Endpoint 仍执行权限策略与 `SqlDataScope` 守卫。

## 9. Nonce 防重放（迁移 042）

表 `fn_identity_signature_nonce`：

| 列 | 类型 | 说明 |
| --- | --- | --- |
| `Id` | UUID v7 | 主键 |
| `AccessKeyId` | `varchar(16)` | KeyPrefix |
| `NonceDigest` | `char(64)` | SHA256(nonce) hex |
| `CreatedAtUtc` | `datetimeoffset` | 插入时间 |
| `ExpiresAtUtc` | `datetimeoffset` | 过期时间 |

约束与索引：

- `UNIQUE (AccessKeyId, NonceDigest)` — 并发插入仅一条成功，其余 `identity.signature.replay_detected`。
- 过期清理索引：`(ExpiresAtUtc, Id)`。
- SQL Server：高写入表使用 **NONCLUSTERED** 主键 + 时间聚集索引；MySQL：`BINARY(16)` UUID。

插入与验签在同一逻辑事务：先验签，后 `INSERT`；唯一冲突即重放。可选 FusionCache 只做“已见 Nonce”短路，**不得**在缓存未命中时跳过数据库插入。

## 10. ProblemDetails 与 HTTP 状态码

| 场景 | HTTP | 稳定错误码 |
| --- | --- | --- |
| Header 缺失/版本错误 | 401 | `identity.signature.missing_headers` / `identity.signature.invalid_version` |
| 时间戳无效/过期/未来 | 401 | `identity.signature.invalid_timestamp` / `timestamp_expired` / `timestamp_in_future` |
| Nonce 非法 | 401 | `identity.signature.invalid_nonce` |
| 重放 | 401 | `identity.signature.replay_detected` |
| 编码/签名无效 | 401 | `identity.signature.invalid_encoding` / `identity.signature.invalid_signature` |
| Key 不存在/禁用/过期 | 401 | `identity.signature.access_key_*` |
| 作用域不匹配 | 403 | `identity.signature.tenant_scope_mismatch` |
| 权限不足 | 403 | `authorization.permission_denied` |
| 限流 | 429 | `identity.authentication.rate_limited` |

## 11. 日志与审计脱敏

### 11.1 禁止持久化或写入日志

- API Key **明文 Secret**
- `X-FullNET-Signature` 全值
- `Authorization` 全值
- `Cookie` 全值
- 请求/响应 **Body** 原文

### 11.2 允许字段

认证审计 `fn_identity_auth_audit` 事件类型 `signature_authentication`：

- `AccessKeyId`（KeyPrefix）
- `UserId`（如已解析）
- 稳定 `ResultCode`
- `Succeeded`
- 客户端 IP（可信代理后的）
- `UserAgent`（长度受限）
- `ContextTenantId`（如适用）

失败日志仅记录错误码与 AccessKeyId；调试日志不得包含签名字符串、Secret 或 Body 片段。

## 12. 开放 Endpoint 接入规则

1. 不得将 `FullNET.Signature` 设为全局默认 Scheme。
2. 开放 Endpoint 通过 `RequireOpenAccessAuthentication()` 同时接受 `FullNET.Smart`（Bearer/ApiKey）与 `FullNET.Signature`。
3. OpenAPI 为声明签名的操作添加 `Signature` 安全要求。
4. 限流策略 `identity-signature-auth` 以 `AccessKeyId` 分区。

## 13. 验收

必须证明：Query 顺序无关、Percent-Encoding 严格、空/非空 Body、Body 篡改拒绝、时间戳过期/未来、Nonce 重放与并发重放、Key 轮换/禁用/过期、Host/Tenant 作用域失败关闭、固定时间签名比较、失败日志无 Secret/签名/Body、SQL Server/MySQL 迁移与半完成恢复、DbUp 未记账重跑。

## 14. 非目标

- 不实现出站调用审计（Task 9）
- 不新增 API Key 管理 UI 字段（KeyPrefix 已足够标识 AccessKeyId）
- 不支持 WebSocket/SignalR 签名
- 不把签名当作业务幂等键（Nonce 只防重放，不承诺业务语义）
