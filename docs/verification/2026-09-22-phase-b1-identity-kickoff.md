# Phase B1 启动：Identity 超级管理员收口（2026-09-22）

## 背景

Document 队列 #1 功能已关闭；Verified 升档门禁见 [`2026-09-22-document-verified-gates.md`](./2026-09-22-document-verified-gates.md)。按 [`adminnet-feature-parity.md`](../roadmap/adminnet-feature-parity.md) **B1** 波次，优先启动 **B1-1 Identity**（相对 CodeGeneration 阻塞面更大）。

## 选定首切片

来源：[`2026-07-18-super-administrator.md`](../superpowers/plans/2026-07-18-super-administrator.md) 未完成项。

1. **双端 MFA UI**：Vue `ui/admin` 与（如仍适用）Layui 冻结线外的超级管理员高风险写操作 TOTP 确认流。
2. **真实栈浏览器 E2E**：`tests/e2e/admin-real-stack` 覆盖超级管理员授予/撤销、最后一名保护与 MFA 闸门（非仅 admin-parity Mock）。
3. **fresh program merge**：Identity 相关 Host 路由在 main CI `test:e2e:real` 矩阵中稳定绿。

## 非本切片

- CodeGeneration「选表→生成→Release 编译→E2E」闭环（**B1-2**，见 [`2026-07-30-codegeneration-database-import-cli.md`](../superpowers/plans/2026-07-30-codegeneration-database-import-cli.md)）。
- Workflow 队列 #2（依赖 Notifications/Jobs 恢复路径）。

## 下一动作

1. 在 super-administrator 计划 Task 5 下拆 PR：MFA UI + real-stack spec。
2. Docker/Testcontainers 可用后先跑 Identity 子集 `pnpm test:e2e:real -- --grep super-admin`（或等价 spec 文件名）再扩全量。
