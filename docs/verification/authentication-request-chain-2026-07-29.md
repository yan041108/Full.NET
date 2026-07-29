# 认证请求链数据库往返基线

## 范围

- 计划：Task 25 Step 1，以及 Step 2 的安全语义冻结输入
- 代码基线：`98622614e932822ebdf40f84e6496a5e19054b79`
- 性能工件：`BenchmarkDotNet.Artifacts/mixed-load/formal-20260728-v3/summary.json`
- 环境：Windows、.NET 10 Preview、SQL Server 2022 CU14、MySQL 8.0.46
- 场景：正式混合负载 V3，双库并发 `1/4/16/32`，每档预热 30 秒、稳态 600 秒

本记录复用已经完成的正式 V3 工件。当前切片没有代码、SQL、配置或脚本行为变化，
因此按本地性能规则不重复刷新样本，也不运行完整测试。

## 成功请求的数据库往返

| Provider | 并发 | JWT 请求 | Session 查询 | API Key 请求 | API Key 查询 | `LastUsed` 更新 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| SQL Server | 1 | 20,336 | 20,336 | 13,447 | 13,447 | 2 |
| SQL Server | 4 | 54,809 | 54,809 | 36,635 | 36,635 | 3 |
| SQL Server | 16 | 87,578 | 87,578 | 58,378 | 58,378 | 5 |
| SQL Server | 32 | 104,015 | 104,015 | 69,111 | 69,111 | 2 |
| MySQL | 1 | 12,538 | 12,538 | 8,289 | 8,289 | 2 |
| MySQL | 4 | 38,057 | 38,057 | 25,330 | 25,330 | 3 |
| MySQL | 16 | 76,223 | 76,223 | 50,644 | 50,644 | 10 |
| MySQL | 32 | 73,940 | 73,940 | 48,933 | 48,933 | 9 |

JWT 请求数由 `jwt-read`、`jwt-write-outbox`、`audit-list` 和
`validation-failure` 相加；每档都与
`identity.find_refresh_session_by_explicit_session_id` 次数完全相等。
API Key 请求数由 `api-key-read` 和 `api-key-write-outbox` 相加；每档都与
`identity.find_api_key_for_authentication` 次数完全相等。

结论是两种认证当前都固定为每请求一次数据库读取。API Key 的五分钟应用判断和 SQL
条件更新已把 `identity.touch_api_key_last_used` 压到每个十分钟档仅 2–10 次，
它不是下一候选的主要往返来源。

## 只读场景尾延迟

| Provider | 并发 | JWT P95 ms | JWT P99 ms | API Key P95 ms | API Key P99 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| SQL Server | 1 | 24.7 | 30.0 | 14.7 | 18.2 |
| SQL Server | 4 | 57.0 | 80.4 | 20.2 | 26.8 |
| SQL Server | 16 | 214.8 | 338.2 | 53.3 | 116.1 |
| SQL Server | 32 | 441.5 | 658.1 | 74.1 | 144.3 |
| MySQL | 1 | 36.2 | 60.1 | 24.9 | 37.8 |
| MySQL | 4 | 64.6 | 87.5 | 30.9 | 50.3 |
| MySQL | 16 | 194.3 | 275.6 | 76.0 | 139.2 |
| MySQL | 32 | 495.2 | 896.8 | 298.6 | 595.2 |

这些数值包含真实 Endpoint、授权、业务 SQL 和 Audit，不把场景总延迟误写成单条认证
SQL 耗时。候选 A/B 只运行发生变化的 `jwt-read` 和 `api-key-read`，以并发 16 为主要
参考档、并发 32 为压力退化档，每个候选保留 2–4 个可比较样本。

## 当前安全语义

1. JWT 在密码学验证后读取 Session、账号启用状态、锁定、安全戳和上下文；数据库提交
   撤销、密码/角色安全戳轮换或账号禁用后，下一个请求立即读取新状态。
2. API Key 每次联表读取 Key 与 Host 用户状态；Key 禁用、过期、用户禁用或锁定在下一个
   请求立即生效。
3. 租户上下文由 `TenantResolutionMiddleware` 再校验租户存在与启用状态。租户解析已有
   FusionCache、请求提交后本机失效和事务 Outbox 跨节点失效，不应重复塞入 Identity
   认证缓存。
4. Redis 当前不参与 JWT/API Key 认证；Redis 不可用不会放宽授权。数据库读取异常会使
   认证请求失败，不会使用旧状态继续授权。
5. 当前最大认证撤销传播窗口是数据库提交到下一个请求读取之间的时间，没有人为 TTL。

## 候选停止条件

任何能减少数据库读取的缓存都会引入非零撤销传播窗口，或者以 Redis 往返替换数据库
往返。进入 Task 25 Step 3 前必须明确批准最大撤销传播窗口；在此之前禁止以任意 TTL
静默改变即时撤销语义。

若批准非零窗口，候选仍必须满足：

- FusionCache Fail-Safe 关闭；
- 本机缓存上限不超过批准的撤销窗口；
- Key/Session/用户状态写后本机同步失效；
- 跨节点失效失败可观测并进入可靠重试；
- Redis、分布式缓存或 Backplane 故障时 fail-closed，不能只依赖陈旧 L1；
- 撤销、禁用、安全戳轮换、租户停用和并发撤销在 SQL Server/MySQL 各一个聚焦 smoke
  中保持拒绝；
- 双库 `jwt-read`、`api-key-read` A/B 均取得可重复收益，否则回退候选。

## 未完成项

- 撤销、账号禁用、API Key 禁用和租户停用的独立 Statement 次数与请求结果需要随
  Step 3 RED 一起锁定；现有 Unit/Integration 已覆盖拒绝行为，但未形成缓存候选的
  故障矩阵。
- 最大撤销传播窗口尚未批准，因此没有实现认证缓存，也没有调整 Identity 注册。
