# OpenAPI 离线夹具覆盖收口（2026-07-26）

## 摘要

为 OpenAPI 静态夹具补齐可持续的离线覆盖门禁。所有
`contracts/openapi/*.json` 现在都必须由 `tests/openapi/*-contract.test.mjs`
显式引用，避免“夹具和运行时 Integration 断言已存在，但 `pnpm test:openapi`
未执行该夹具”的静默缺口。

## RED / GREEN

| 阶段 | 结果 |
| --- | --- |
| RED | 新增覆盖完整性测试后稳定列出 4 个遗漏：Identity API Key、Jobs、平台工作台、平台接口文档 |
| GREEN | 为 4 个夹具各补 2 项离线契约；聚焦 **9/9**，OpenAPI 全量 **50/50** |

四组契约分别锁定：

- 夹具结构、路径/操作唯一性、权限码和 schema 引用；
- C# DTO、权限常量、Endpoint 路由或 Hosting OpenAPI/Scalar 常量的一致性；
- 新增夹具若未接入离线契约测试，覆盖完整性门禁立即失败。

## 边界

- 未修改 C# 生产代码、HTTP 路径、JSON schema、权限码或数据库对象。
- 运行时 `/openapi/v1.json` 仍由既有 SQL Server/MySQL Integration 断言覆盖；
  本收口补的是快速、无容器的源码与冻结夹具漂移检测。
- OpenAPI 离线测试发现数由 **41 → 50**；不影响 .NET 四套 canonical 门槛。

## 规则与 Skill 复盘

- `fullnet-module-delivery` 交付地图已经要求外部 HTTP 端点同时落地静态夹具、
  Node 离线门禁和 Integration 运行时断言，不新增重复 Skill 条目。
- 本次同类遗漏横跨四个既有切片，升级为可执行覆盖守卫比追加近义文字规则更有效；
  后续新增夹具若未接入 `pnpm test:openapi` 会直接失败。
- 未发现需要修改 `rules/` 的新架构、安全、命名或数据库不变量。
