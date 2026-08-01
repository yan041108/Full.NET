# CodeGeneration 多实例工作区互斥首切片设计

**状态：** Approved for implementation
**日期：** 2026-08-02
**基线：** `main` @ `73aeb30`

## 1. 决策摘要

在保留单进程 `SemaphoreSlim` 的前提下，可选启用 **数据库会话锁**（SQL Server `sp_getapplock` / MySQL `GET_LOCK`）对同一规范化 `WorkspaceRoot` 跨 API 实例互斥 Apply/Rollback。获取失败仍映射 `apply_busy` / `rollback_busy`（非阻塞，超时 0）。

未决项默认：DB 会话锁（非 Redis）；Worker 检查点清理不抢锁；锁随 Gate `Release` 释放。

## 2. 配置

`CodeGeneration:Apply:DistributedGateEnabled` 默认 `false`。

## 3. 锁资源

`fn:codegeneration:workspace:{sha256(normalized WorkspaceRoot)}`（小写 hex）。

## 4. 排除

Redis/RedLock、检查点清理加锁、HTTP/双端变更、链式 Rollback。
