# Host 全局限流验证（2026-07-26）

## 摘要

交付 Hosting 级全局限流与统一 429 ProblemDetails；Identity 登录/会话变更策略保持不变。

| 维度 | 结果 |
| --- | --- |
| 配置 | `RateLimiting:EnableGlobalApiLimit`、`GlobalApiPermitLimitPerMinute` |
| 错误码 | `hosting.rate_limit.exceeded`（端点级策略仍映射模块错误码） |
| Integration 双库 | `Global_api_rate_limit` SQL Server/MySQL **2/2** → **166 → 168** |
| Unit | `RateLimitPolicyErrorCodes` **1/1** → **351 → 352** |
| 四处 canonical 门槛 | **352/7/40/168** |

## 关联

- [实施计划](../superpowers/plans/2026-07-26-hosting-global-api-rate-limit-vertical-slice.md)
- [Admin.NET 对标矩阵](../roadmap/adminnet-feature-parity.md)
