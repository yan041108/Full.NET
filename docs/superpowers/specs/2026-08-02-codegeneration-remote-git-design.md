# CodeGeneration 远程 Git 工作区首切片设计

**状态：** Approved for implementation
**日期：** 2026-08-02
**基线：** `main` @ `a8e60dd`

## 1. 决策摘要

在 `WorkspaceRoot` 已是本地 Git 克隆的前提下，opt-in 启用 `CodeGeneration:Git`：Apply/Rollback 写盘前在 Gate 内 `fetch` + `reset --hard` 到 `origin/{DefaultBranch}`；成功后若 `PushEnabled` 则 `add`/`commit`/`push`（失败只记日志，不改变已成功的运行终态）。凭据从环境变量读取，不进配置库。

未决默认：仅 API 进程；不自动 clone；Rollback 以新提交恢复 Manifest；多实例依赖既有分布式 Gate。

## 2. 配置 `CodeGeneration:Git`

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `Enabled` | `false` | 与 `Apply:Enabled` 同时为 true 才激活 |
| `PushEnabled` | `false` | 成功后 push |
| `DefaultBranch` | `main` | 跟踪分支 |
| `RemoteName` | `origin` | 远程名 |
| `AuthorName` / `AuthorEmail` | 空 | `PushEnabled` 时必填 |
| `CredentialEnvironmentVariable` | `FULLNET_CODEGENERATION_GIT_TOKEN` | Bearer 注入 `http.extraHeader` |

## 3. 错误码

- `codegen.run.git_sync_failed` — 写盘前同步失败
- `codegen.run.git_publish_failed` — 成功后 push 失败（运行仍 succeeded，HTTP 仍 200）

## 4. 排除

自动 clone、Worker 专职、PR、生产默认启用、HTTP 契约变更。