# Host 全局限流纵向切片（2026-07-26）

## 目标

将接口限流从 Identity 专用策略提升为 Hosting 级全局限流，并保留认证端点的更严格分区策略。

## 清单

1. [x] `AddFullNetRateLimiter` + `RateLimiting` 配置节
2. [x] 全局 `GlobalLimiter`（按用户 `sub` 或来源 IP 分区）
3. [x] `hosting.rate_limited` 稳定错误码与 ProblemDetails 429
4. [x] Identity 策略迁移至 `IdentityRateLimiterPolicyConfigurator`
5. [x] Integration **166 → 168**（SQL Server/MySQL）
6. [x] Unit **351 → 352**
7. [x] 路线图与验证记录

## 范围外

- 可信代理与 `ForwardedHeaders` 后的真实客户端地址
- 管理端限流配置 UI
- Redis 分布式限流
